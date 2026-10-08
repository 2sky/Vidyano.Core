using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Vidyano.Script.Runtime;
using Vidyano.Service;
using Vidyano.Service.Repository;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// The in-process adapter the seam in <see cref="IBackendAdapter"/> was designed for ("a remote URL
/// and an in-process host are both just adapters"). Boots a full Vidyano Minimal app on an in-memory
/// <see cref="TestServer"/> — no Kestrel, no ports, no TLS — and hands the script runner a
/// cookie-aware <see cref="HttpClient"/> bound to it. Vidyano auth is cookie-based, so the client
/// MUST carry a <see cref="CookieContainerHandler"/> or sign-in state is dropped between requests.
/// </summary>
public sealed class InProcessVidyanoBackend : IBackendAdapter
{
    // Schema name is derived from the context type ("ShopContext" -> "Shop"); a custom action's user
    // right is also its attach point to a query's PO type (see the Minimal API sample).
    private const string Schema = "Shop";

    private readonly SemaphoreSlim _bootLock = new(1, 1);
    private WebApplication? _app;
    private TestServer? _server;
    private string? _baseUri;

    /// <summary>Boots the app once and reuses it; every call returns a FRESH cookie-isolated client over
    /// the same server, so successive script runs share the booted server + data but each gets its own
    /// auth/cookie jar (a clean sign-in per run). Booting the Minimal API twice in one process is unsafe
    /// (static source holders), so a single long-lived server is also the only correct shape.</summary>
    public async ValueTask<BackendConnection> StartAsync(CancellationToken cancellationToken = default)
    {
        // Serialize the one-time boot. WaitAsync is ~free when uncontended (and StartAsync is called
        // only a handful of times), so a plain lock is simpler than double-checked locking and sidesteps
        // any field-visibility concern.
        await _bootLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_server is null)
                await BootAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _bootLock.Release();
        }

        return new BackendConnection(MintClient(_server!), _baseUri!);
    }

    private async Task BootAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddVidyanoMinimal<ShopContext>(vidyano => vidyano
            .WithDefaultAdmin()
            .WithSchemaRights()
            // Three languages so the TranslatedString tests can set/assert per-language values; the seed
            // titles (ShopContext._L) use exactly this en/nl/de set.
            .WithLanguage("en", new Dictionary<string, string> { ["en"] = "English", ["nl"] = "Engels", ["de"] = "Englisch" })
            .WithLanguage("nl", new Dictionary<string, string> { ["en"] = "Dutch", ["nl"] = "Nederlands", ["de"] = "Niederländisch" })
            .WithLanguage("de", new Dictionary<string, string> { ["en"] = "German", ["nl"] = "Duits", ["de"] = "Deutsch" })
            .WithMenuItem("Products")
            .WithMenuItem("ProductCategories")
            .WithMenuItem("Documents")
            .WithModel(model =>
            {
                // Detail query: ProductCategory -> its Products, surfaced as the "Products" detail panel.
                var productsDetail = model.AddDetailQuery(nameof(ProductCategory), nameof(ProductActions.ProductCategory_Products));
                model.GetPersistentObject(nameof(ProductCategory))!
                    .GetOrCreateAttributeAsDetail("Products").Details = productsDetail;

                // A detail query with a lookup source: the server then offers its built-in AddReference action,
                // whose picker lists the lookup source (products outside the category). Confirming it reaches
                // ProductActions.OnAddReference without an AddAction parameter.
                var membersDetail = model.AddDetailQuery(nameof(ProductCategory), nameof(ProductActions.ProductCategory_Members));
                membersDetail.LookupSource = "Custom." + nameof(ProductActions.ProductCategory_Candidates);

                // Mark Product.Trigger as TriggersRefresh: a SET of it round-trips through
                // ProductActions.OnRefresh, which mirrors the value into Product.Echo — exercises the
                // client SetValueAsync -> RefreshAttributesAsync path end-to-end.
                model.GetPersistentObject(nameof(Product))!
                    .GetOrCreateAttribute(nameof(Product.Trigger)).TriggersRefresh = true;

                // Query-level custom actions. The user right both authorizes the action and attaches it
                // to the Product query's PO type.
                var hello = model.GetOrCreateCustomAction(nameof(HelloWorld));
                hello.ShowedOn = ShowedOn.PersistentObject;

                var ask = model.GetOrCreateCustomAction(nameof(AskFirst));
                ask.ShowedOn = ShowedOn.PersistentObject;

                // PO-level action that returns a Notification(..., Error) — the rejection shape the runner
                // used to drop silently (see RejectWithNotification / HelloWorld's comment).
                var reject = model.GetOrCreateCustomAction(nameof(RejectWithNotification));
                reject.ShowedOn = ShowedOn.PersistentObject;

                // PO-level action that returns a RegisteredStream (download) — the fixture for the .visc
                // stream auto-fetch (EXPECT Stream.*). ProductActions.OnGetStream serves the bytes.
                var download = model.GetOrCreateCustomAction(nameof(DownloadSpec));
                download.ShowedOn = ShowedOn.PersistentObject;

                var downloadMissing = model.GetOrCreateCustomAction(nameof(DownloadMissing));
                downloadMissing.ShowedOn = ShowedOn.PersistentObject;

                // PO-level action that queues a Navigate(path) client operation — the fixture for FOLLOW-NAVIGATE.
                var navigateTo = model.GetOrCreateCustomAction(nameof(NavigateTo));
                navigateTo.ShowedOn = ShowedOn.PersistentObject;

                // Query-level actions (toolbar, no row selection) — exercise the empty-selection payload and
                // query-action error surfacing.
                var import = model.GetOrCreateCustomAction(nameof(ImportProducts));
                import.ShowedOn = ShowedOn.Query;

                var fail = model.GetOrCreateCustomAction(nameof(FailOnServer));
                fail.ShowedOn = ShowedOn.Query;

                // PO-level action on ProductCategory that opens an Add-Reference picker over the Products
                // query — the live fixture for the .visc ADD-REFERENCE round-trip. The right both authorizes
                // the action and attaches it to the ProductCategory PO type (the parent the add runs on).
                var link = model.GetOrCreateCustomAction(nameof(LinkProducts));
                link.ShowedOn = ShowedOn.PersistentObject;

                // Query-level actions that add a product and return nothing — the fixtures for the post-action
                // refresh: the definition's RefreshQueryOnCompleted / KeepSelectionOnRefresh decide whether (and how)
                // the client re-searches the grid.
                var addSample = model.GetOrCreateCustomAction(nameof(AddSample));
                addSample.ShowedOn = ShowedOn.Query;
                addSample.RefreshQueryOnCompleted = true;

                var addSampleKeep = model.GetOrCreateCustomAction(nameof(AddSampleKeepSelection));
                addSampleKeep.ShowedOn = ShowedOn.Query;
                addSampleKeep.RefreshQueryOnCompleted = true;
                addSampleKeep.KeepSelectionOnRefresh = true;

                var addSampleNoRefresh = model.GetOrCreateCustomAction(nameof(AddSampleNoRefresh));
                addSampleNoRefresh.ShowedOn = ShowedOn.Query;
                addSampleNoRefresh.RefreshQueryOnCompleted = false;

                // PO-level action on ProductCategory that changes data and queues the Refresh client operations its
                // Refresh parameter names — the fixture for applying them.
                var reshuffle = model.GetOrCreateCustomAction(nameof(Reshuffle));
                reshuffle.ShowedOn = ShowedOn.PersistentObject;

                // Query-level custom AddReference: its picker's confirm re-searches the action's query.
                var linkIntoQuery = model.GetOrCreateCustomAction(nameof(LinkIntoQuery));
                linkIntoQuery.ShowedOn = ShowedOn.Query;

                var administrators = model.GetOrCreateGroup("Administrators");
                administrators.AddUserRight($"{nameof(HelloWorld)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(AskFirst)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(RejectWithNotification)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(DownloadSpec)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(DownloadMissing)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(NavigateTo)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(ImportProducts)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(FailOnServer)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(LinkProducts)}/{Schema}.{nameof(ProductCategory)}");
                administrators.AddUserRight($"{nameof(AddSample)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(AddSampleKeepSelection)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(AddSampleNoRefresh)}/{Schema}.{nameof(Product)}");
                administrators.AddUserRight($"{nameof(Reshuffle)}/{Schema}.{nameof(ProductCategory)}");
                administrators.AddUserRight($"{nameof(LinkIntoQuery)}/{Schema}.{nameof(Product)}");
            }));

        var app = builder.Build();
        app.UseVidyano(app.Environment, app.Configuration);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;
        _server = app.GetTestServer();
        _baseUri = _server.BaseAddress.ToString();
    }

    /// <summary>A cookie-isolated client over the same in-memory server. One per identity — this is the
    /// per-session cookie-jar isolation the named-session (<c>SIGN-IN @name</c>) path mints through the
    /// inherited <see cref="IBackendAdapter.MintIsolatedAsync"/> default (which calls back into
    /// <see cref="StartAsync"/>).</summary>
    internal static HttpClient MintClient(TestServer server) =>
        new(new CookieContainerHandler { InnerHandler = server.CreateHandler() })
        {
            BaseAddress = server.BaseAddress,
        };

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);

        _bootLock.Dispose();
    }
}
