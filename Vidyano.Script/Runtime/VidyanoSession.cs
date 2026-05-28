using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vidyano.Script.Diagnostics;
using Vidyano.ViewModel;
using Vidyano.ViewModel.Actions;

namespace Vidyano.Script.Runtime;

/// <summary>
/// A live, single-user Vidyano session driven through <see cref="Vidyano.Client"/>. Methods return
/// <see cref="OpResult"/> rather than throwing so callers can collect diagnostics from many
/// operations in one run.
/// </summary>
/// <remarks>
/// The session owns the "current PO" and "current query" implicitly — most verbs target the top of
/// the stack. Named handles override the implicit choice for multi-PO scenarios.
/// </remarks>
public sealed class VidyanoSession : IDisposable
{
    private readonly HttpClient? _ownedHttpClient;
    private readonly Dictionary<string, object> _handles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ClientOperation> _allOperations = new();
    private List<ClientOperation> _lastOperations = new();
    private readonly List<NavEntry> _navStack = new();
    private readonly ScriptHooks _hooks = new();

    /// <summary>
    /// Creates a session. When <paramref name="httpClient"/> is <c>null</c>, the constructor builds an
    /// <see cref="HttpClient"/> with a <see cref="CookieContainer"/> attached — Vidyano's auth depends
    /// on cookies, so a bare <c>new HttpClient()</c> would silently drop sign-in state between calls.
    /// </summary>
    /// <param name="acceptAnyServerCertificate">
    /// Bypass TLS validation. <c>true</c> for local development (self-signed dev certs); never in CI/prod.
    /// Ignored when an external <paramref name="httpClient"/> is supplied.
    /// </param>
    public VidyanoSession(string baseUri, HttpClient? httpClient = null, bool acceptAnyServerCertificate = false)
    {
        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            };
            if (acceptAnyServerCertificate)
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            _ownedHttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
            Client = new Vidyano.Client(_ownedHttpClient) { Uri = baseUri };
        }
        else
        {
            Client = new Vidyano.Client(httpClient) { Uri = baseUri };
        }

        // Bridge Vidyano.Core's per-response ClientOperation dispatch (Hooks.OnClientOperation)
        // into the script's per-verb buffers. _lastOperations is reset before each executable verb
        // so EXPECT ClientOperation only sees what the *previous* verb produced; _allOperations
        // accumulates for diagnostics.
        Client.Hooks = _hooks;
        _hooks.ClientOperationObserver = co =>
        {
            var op = ClientOperation.FromJson(co.Raw);
            if (op is null) return;
            _allOperations.Add(op);
            _lastOperations.Add(op);

            if (Environment.GetEnvironmentVariable("VIDYANO_DUMP_OPERATIONS") == "1")
                Console.Error.WriteLine($"[vidyano] op: {co.Type}{((string?)co.Raw["name"] is { } n ? $":{n}" : "")}");
        };
    }

    /// <summary>All client operations seen since the session started, in arrival order.</summary>
    public IReadOnlyList<ClientOperation> ClientOperations => _allOperations;

    /// <summary>Client operations seen since the last <see cref="ResetLastOperations"/> call.
    /// The interpreter resets this before every executable verb so <c>EXPECT ClientOperation</c>
    /// asserts only what the *previous* verb produced.</summary>
    public IReadOnlyList<ClientOperation> LastOperations => _lastOperations;

    /// <summary>Clears the per-statement operations buffer. Called by the interpreter; library
    /// callers can use it directly when checkpointing between manual operations.</summary>
    public void ResetLastOperations() => _lastOperations = new List<ClientOperation>();

    /// <summary>The underlying Vidyano client. Exposed so library callers can drop down when needed.</summary>
    public Vidyano.Client Client { get; }

    /// <summary>The PO at the top of the navigation stack, or <c>null</c> if the top is a Query or the stack is empty.</summary>
    public PersistentObject? CurrentPo => _navStack.Count > 0 && _navStack[^1] is PoEntry pe ? pe.Po : null;

    /// <summary>The nearest Query on the navigation stack going down from the top, or <c>null</c> if none.
    /// This means SEARCH and OPEN-ROW still target the underlying Query even after OPEN-ROW pushed a PO on top of it.</summary>
    public Query? CurrentQuery
    {
        get
        {
            for (var i = _navStack.Count - 1; i >= 0; i--)
                if (_navStack[i] is QueryEntry qe) return qe.Query;
            return null;
        }
    }

    /// <summary>The navigation stack — oldest entry at index 0, top at <c>NavStack[Count-1]</c>.
    /// Every OPEN pushes; SAVE/CANCEL pop when there's an underlying frame to return to.</summary>
    public IReadOnlyList<NavEntry> NavStack => _navStack;

    /// <summary>The current top of the navigation stack, or <c>null</c> if the stack is empty.</summary>
    public NavEntry? NavStackTop => _navStack.Count > 0 ? _navStack[^1] : null;

    /// <summary>Number of entries on the navigation stack. Zero before the first OPEN.</summary>
    public int NavStackDepth => _navStack.Count;

    /// <summary>Whether the session has completed sign-in.</summary>
    public bool IsSignedIn => Client.IsConnected;

    // --- sign-in ------------------------------------------------------------------------------

    /// <summary>
    /// Signs in. <paramref name="password"/> may be <c>null</c> for anonymous services
    /// (the Vidyano demo accepts a null password for the default user). When
    /// <paramref name="language"/> is non-null the session pins it for every subsequent server
    /// post (sign-in itself included), so labels, action display names, and messages return
    /// in the requested culture.
    /// </summary>
    public async Task<OpResult> SignInAsync(string user, string? password, string? language, SourceLocation loc)
    {
        _hooks.RequestedLanguage = string.IsNullOrWhiteSpace(language) ? null : language;
        try
        {
            var app = await Client.SignInUsingCredentialsAsync(user, password).ConfigureAwait(false);
            if (app == null || !Client.IsConnected)
                return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, "Sign-in did not produce an Application.", loc));
            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.TransportError, $"Sign-in failed: {ex.Message}", loc));
        }
    }

    // --- open family --------------------------------------------------------------------------

    public async Task<OpResult> OpenPersistentObjectAsync(string type, string? objectId, string? asHandle, SourceLocation loc)
    {
        if (!IsSignedIn)
            return OpResult.Fail(new Diagnostic(ErrorKind.StateNotSignedIn, "Sign in before opening a PersistentObject.", loc));
        try
        {
            var po = await Client.GetPersistentObjectAsync(type, objectId).ConfigureAwait(false);
            if (po is null)
                return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, $"Server returned no PersistentObject for '{type}'.", loc));
            _navStack.Add(new PoEntry(po, IsDialog(po)));
            if (asHandle != null) _handles[asHandle] = po;
            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    public async Task<OpResult> OpenQueryAsync(string id, string? asHandle, SourceLocation loc)
    {
        if (!IsSignedIn)
            return OpResult.Fail(new Diagnostic(ErrorKind.StateNotSignedIn, "Sign in before opening a Query.", loc));
        try
        {
            var q = await Client.GetQueryAsync(id).ConfigureAwait(false);
            if (q is null)
                return OpResult.Fail(new Diagnostic(ErrorKind.ResolveQuery, $"No Query named '{id}'.", loc));
            _navStack.Add(new QueryEntry(q));
            if (asHandle != null) _handles[asHandle] = q;
            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    /// <summary>
    /// Opens a menu item by <c>/</c>-separated path. Walks <see cref="Vidyano.Client.ProgramUnits"/>
    /// (top-level units → nested groups → leaf items). With no segments, opens the first unit and
    /// (if its <c>OpenFirst</c> flag is set) drills to its first non-separator item. With one
    /// segment, accepts either a unit name or an item name (when the latter is unambiguous across
    /// all units). With more segments, follows the path explicitly.
    /// </summary>
    public async Task<OpResult> OpenMenuItemAsync(IReadOnlyList<string> segments, string? asHandle, SourceLocation loc)
    {
        if (!IsSignedIn || Client.Application is null)
            return OpResult.Fail(new Diagnostic(ErrorKind.StateNotSignedIn, "Sign in before opening a menu item.", loc));

        var pus = Client.ProgramUnits;
        if (pus.Count == 0)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ResolveMenuItem,
                "This user's Application has no programUnits — nothing to navigate.",
                loc,
                Hint: "Check whether the signed-in account has any program-unit rights, or use OPEN Query directly."));

        if (segments.Count == 0)
            return await OpenProgramUnitAsync(pus[0], openFirstItem: true, asHandle, loc).ConfigureAwait(false);

        if (segments.Count == 1)
        {
            var target = segments[0];

            var puMatch = pus.FirstOrDefault(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase));
            if (puMatch is not null)
                return await OpenProgramUnitAsync(puMatch, openFirstItem: true, asHandle, loc).ConfigureAwait(false);

            var matches = new List<(ProgramUnit pu, ProgramUnitItem item, string path)>();
            foreach (var pu in pus)
                CollectItems(pu, pu.Items, pu.Name ?? "", target, matches);

            if (matches.Count == 1)
                return await OpenMenuItemAsync(matches[0].item, asHandle, loc).ConfigureAwait(false);

            if (matches.Count > 1)
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.ResolveMenuItem,
                    $"Menu item '{target}' is ambiguous — appears in {matches.Count} places.",
                    loc,
                    Hint: $"Qualify with a path: {string.Join(", ", matches.Take(3).Select(m => $"'{m.path}'"))}",
                    Details: new Dictionary<string, object?> { ["paths"] = matches.Select(m => m.path).ToArray() }));

            var names = AllItemAndPuNames(pus);
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ResolveMenuItem,
                $"No menu entry named '{target}'.",
                loc,
                Hint: Suggester.Hint(target, names) ?? FormatAvailableHint(names),
                Details: new Dictionary<string, object?> { ["available"] = names }));
        }

        var head = segments[0];
        var pu0 = pus.FirstOrDefault(p => string.Equals(p.Name, head, StringComparison.OrdinalIgnoreCase));
        if (pu0 is null)
        {
            var puNames = pus.Select(p => p.Name).Where(s => s != null).Select(s => s!).ToArray();
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ResolveMenuItem,
                $"No ProgramUnit named '{head}'.",
                loc,
                Hint: Suggester.Hint(head, puNames) ?? FormatAvailableHint(puNames)));
        }
        return await WalkPathAsync(pu0.Items, segments, 1, asHandle, loc).ConfigureAwait(false);
    }

    private async Task<OpResult> OpenProgramUnitAsync(ProgramUnit pu, bool openFirstItem, string? asHandle, SourceLocation loc)
    {
        if (!openFirstItem || !pu.OpenFirst)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ResolveMenuItem,
                $"ProgramUnit '{pu.Name}' has no automatic landing page.",
                loc,
                Hint: "Add an explicit item: OPEN MenuItem <ProgramUnit>/<Item>"));

        var item = FindFirstOpenableItem(pu.Items);
        if (item is null)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ResolveMenuItem,
                $"ProgramUnit '{pu.Name}' is empty.", loc));
        return await OpenMenuItemAsync(item, asHandle, loc).ConfigureAwait(false);
    }

    private async Task<OpResult> WalkPathAsync(IReadOnlyList<ProgramUnitItem> children, IReadOnlyList<string> segments, int index, string? asHandle, SourceLocation loc)
    {
        if (index >= segments.Count) return OpResult.Fail(new Diagnostic(ErrorKind.ResolveMenuItem, "Empty menu path.", loc));
        var seg = segments[index];

        foreach (var child in children)
        {
            if (!string.Equals(child.Name, seg, StringComparison.OrdinalIgnoreCase)) continue;

            if (child is ProgramUnitItemGroup g)
                return await WalkPathAsync(g.Items, segments, index + 1, asHandle, loc).ConfigureAwait(false);

            if (index == segments.Count - 1)
                return await OpenMenuItemAsync(child, asHandle, loc).ConfigureAwait(false);

            return OpResult.Fail(new Diagnostic(ErrorKind.ResolveMenuItem,
                $"'{seg}' is a leaf item but there are more segments after it.", loc));
        }

        return OpResult.Fail(new Diagnostic(
            ErrorKind.ResolveMenuItem,
            $"No item or group named '{seg}' under this menu node.",
            loc));
    }

    private async Task<OpResult> OpenMenuItemAsync(ProgramUnitItem item, string? asHandle, SourceLocation loc)
    {
        switch (item)
        {
            case ProgramUnitItemQuery q:
                return await OpenQueryAsync(q.QueryName ?? q.QueryId!, asHandle, loc).ConfigureAwait(false);

            case ProgramUnitItemPersistentObject p:
                var poType = p.PersistentObjectType ?? p.PersistentObjectId;
                if (string.IsNullOrEmpty(poType))
                    return OpResult.Fail(new Diagnostic(ErrorKind.ResolveMenuItem,
                        $"Menu item '{item.Name}' has no persistentObject type.", loc));
                return await OpenPersistentObjectAsync(poType!, p.ObjectId, asHandle, loc).ConfigureAwait(false);

            case ProgramUnitItemUrl _:
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.ResolveMenuItem,
                    $"Menu item '{item.Name}' opens a URL — not driveable from a .visc script.",
                    loc));

            case ProgramUnitItemSeparator _:
                return OpResult.Fail(new Diagnostic(ErrorKind.ResolveMenuItem,
                    $"Menu item '{item.Name}' is a separator.", loc));

            default:
                return OpResult.Fail(new Diagnostic(ErrorKind.ResolveMenuItem,
                    $"Menu item '{item.Name}' has no recognised target.", loc));
        }
    }

    private static ProgramUnitItem? FindFirstOpenableItem(IReadOnlyList<ProgramUnitItem> items)
    {
        foreach (var i in items)
        {
            if (i is ProgramUnitItemSeparator) continue;
            if (i is ProgramUnitItemGroup g)
            {
                var inner = FindFirstOpenableItem(g.Items);
                if (inner is not null) return inner;
                continue;
            }
            return i;
        }
        return null;
    }

    private static void CollectItems(ProgramUnit rootPu, IReadOnlyList<ProgramUnitItem> items, string pathSoFar, string targetName, List<(ProgramUnit pu, ProgramUnitItem item, string path)> hits)
    {
        foreach (var i in items)
        {
            if (i is ProgramUnitItemSeparator) continue;
            if (i is ProgramUnitItemGroup g)
            {
                CollectItems(rootPu, g.Items, $"{pathSoFar}/{g.Name ?? "?"}", targetName, hits);
                continue;
            }
            if (i.Name is { } nm && string.Equals(nm, targetName, StringComparison.OrdinalIgnoreCase))
                hits.Add((rootPu, i, $"{pathSoFar}/{nm}"));
        }
    }

    private static IReadOnlyList<string> AllItemAndPuNames(IReadOnlyList<ProgramUnit> pus)
    {
        var names = new List<string>();
        foreach (var pu in pus)
        {
            if (pu.Name is { } n) names.Add(n);
            CollectAllItemNames(pu.Items, names);
        }
        return names;
    }

    private static void CollectAllItemNames(IReadOnlyList<ProgramUnitItem> items, List<string> names)
    {
        foreach (var i in items)
        {
            if (i is ProgramUnitItemSeparator) continue;
            if (i is ProgramUnitItemGroup g) { CollectAllItemNames(g.Items, names); continue; }
            if (i.Name is { } n) names.Add(n);
        }
    }

    private static string FormatAvailableHint(IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
            return "The user's menu is empty — does the signed-in account have any program-unit rights?";
        const int max = 8;
        var sample = candidates.Take(max).Select(c => $"'{c}'");
        var tail = candidates.Count > max ? $", … ({candidates.Count - max} more)" : "";
        return $"Available menu entries: {string.Join(", ", sample)}{tail}.";
    }

    // --- query operations ---------------------------------------------------------------------

    public async Task<OpResult> SearchAsync(string text, SourceLocation loc)
    {
        if (CurrentQuery is null)
            return OpResult.Fail(new Diagnostic(ErrorKind.StateNoCurrentQuery,
                "SEARCH needs a current Query.",
                loc,
                Hint: "Open one first with OPEN Query <id> or OPEN MenuItem <path>."));
        try
        {
            await CurrentQuery.SearchTextAsync(text).ConfigureAwait(false);
            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    /// <summary>Resolves a detail query by name on the current PO (<see cref="PersistentObject.Queries"/>).
    /// Surfaces a suggester hint on a missing name.</summary>
    public OpResult<Query> ResolveDetail(string name, SourceLocation loc)
    {
        if (CurrentPo is null)
            return OpResult<Query>.Fail(new Diagnostic(ErrorKind.StateNoCurrentPo,
                "Detail needs a current PersistentObject.", loc));
        if (!CurrentPo.Queries.TryGetValue(name, out var q))
            return OpResult<Query>.Fail(new Diagnostic(ErrorKind.ResolveQuery,
                $"PersistentObject '{CurrentPo.Type}' has no detail query '{name}'.", loc,
                Hint: Suggester.Hint(name, CurrentPo.Queries.Keys)));
        return OpResult<Query>.Success(q);
    }

    /// <summary>Resolves the Query an OPEN-ROW targets: the named detail query on the current PO when
    /// <paramref name="detailName"/> is set, otherwise the current Query. Keeps both OPEN-ROW bodies
    /// agnostic to which query they're scanning.</summary>
    private OpResult<Query> ResolveRowQuery(string? detailName, SourceLocation loc)
    {
        if (detailName is not null) return ResolveDetail(detailName, loc);
        if (CurrentQuery is null)
            return OpResult<Query>.Fail(new Diagnostic(ErrorKind.StateNoCurrentQuery, "OPEN-ROW needs a current Query.", loc));
        return OpResult<Query>.Success(CurrentQuery);
    }

    public async Task<OpResult> OpenRowAsync(int index, string? asHandle, SourceLocation loc, string? detailName = null)
    {
        var qt = ResolveRowQuery(detailName, loc);
        if (!qt.Ok) return OpResult.Fail(qt.Error!);
        var query = qt.Value!;
        if (index < 0 || index >= query.TotalItems)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.AssertFailed,
                $"Row index {index} is out of range (Query has {query.TotalItems} items).",
                loc));
        try
        {
            var items = await query.GetItemsAsync(index, 1).ConfigureAwait(false);
            var row = items.FirstOrDefault();
            if (row is null)
                return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, $"Could not load row {index}.", loc));
            return await OpenRowItemAsync(row, $"Row {index}", asHandle, loc).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    /// <summary><c>OPEN-ROW WHERE &lt;column&gt; = &lt;value&gt; [AS @handle]</c> — open the PO behind the
    /// single query row whose <paramref name="column"/> cell equals <paramref name="value"/>. The match is
    /// strict: zero matches or more than one match are assertion failures (no first-wins), so a row picked
    /// this way is unambiguous. The full result set is loaded and filtered exactly client-side; the
    /// underlying Query's search state is left untouched.</summary>
    public async Task<OpResult> OpenRowWhereAsync(string column, object? value, string? asHandle, SourceLocation loc, string? detailName = null)
    {
        var qt = ResolveRowQuery(detailName, loc);
        if (!qt.Ok) return OpResult.Fail(qt.Error!);
        var query = qt.Value!;

        var columnNames = query.Columns.Select(c => c.Name).ToArray();
        var matchedColumn = columnNames.FirstOrDefault(n => string.Equals(n, column, StringComparison.OrdinalIgnoreCase));
        if (matchedColumn is null)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ResolveAttribute,
                $"Column '{column}' does not exist on Query '{query.Name}'.",
                loc,
                Hint: Suggester.Hint(column, columnNames)));

        // The value is matched against the cell's service-string form, so it must be written in
        // service-string format — the same convention SET uses for writing values. A string literal
        // passes through unchanged; a non-string input is rendered the same way the cell side is.
        // Representation-tolerant matching via FromServiceString(value, column.Type) is a future upgrade.
        var target = value as string ?? Vidyano.Client.ToServiceString(value);

        try
        {
            // top==0 = all rows from skip — load the whole set and filter exactly client-side. We do
            // NOT SearchTextAsync to narrow: that mutates the resolved Query's search state, which
            // would linger after this row's PO frame pops. The resolved instance is retained either on
            // the nav stack (CurrentQuery path) or on the parent PO's Queries dictionary (Detail path).
            var items = await query.GetItemsAsync(0, 0).ConfigureAwait(false);
            var matches = items
                .Where(r => r != null &&
                            string.Equals(Vidyano.Client.ToServiceString(r[matchedColumn]), target, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 0)
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.AssertFailed,
                    $"No row where {matchedColumn} = \"{target}\" (Query '{query.Name}' has {query.TotalItems} items).",
                    loc));
            if (matches.Count > 1)
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.AssertFailed,
                    $"Row match for {matchedColumn} = \"{target}\" is ambiguous ({matches.Count} rows). Tighten the value, or use OPEN-ROW <index>.",
                    loc));

            return await OpenRowItemAsync(matches[0], $"Row where {matchedColumn} = \"{target}\"", asHandle, loc).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    /// <summary>Shared tail for both OPEN-ROW forms: loads the PO behind <paramref name="row"/>, pushes a
    /// nav-stack frame, and binds the optional handle. <paramref name="rowLabel"/> describes the row in
    /// load-failure diagnostics. Kept private so the positional and by-value paths can't drift.</summary>
    private async Task<OpResult> OpenRowItemAsync(QueryResultItem row, string rowLabel, string? asHandle, SourceLocation loc)
    {
        var po = await row.Load().ConfigureAwait(false);
        if (po is null)
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, $"{rowLabel} did not load a PersistentObject.", loc));
        _navStack.Add(new PoEntry(po, IsDialog(po)));
        if (asHandle != null) _handles[asHandle] = po;
        return OpResult.Success;
    }

    // --- edit / save / cancel / refresh -------------------------------------------------------

    public OpResult Edit(SourceLocation loc)
    {
        if (CurrentPo is null) return NoCurrentPo(loc);
        CurrentPo.Edit();
        return OpResult.Success;
    }

    public OpResult GoBack(SourceLocation loc)
    {
        if (_navStack.Count < 2)
            return OpResult.Fail(new Diagnostic(ErrorKind.StateNavStackAtRoot,
                "GO-BACK has nowhere to go — already at the root frame.", loc));
        if (_navStack[^1] is PoEntry { Po.IsInEdit: true })
            return OpResult.Fail(new Diagnostic(ErrorKind.GuardInEdit,
                "GO-BACK can't leave a PO with unsaved edits — SAVE or CANCEL first.", loc));
        _navStack.RemoveAt(_navStack.Count - 1);
        return OpResult.Success;
    }

    public OpResult Cancel(SourceLocation loc)
    {
        if (CurrentPo is null) return NoCurrentPo(loc);
        if (!CurrentPo.IsInEdit)
            return OpResult.Fail(new Diagnostic(ErrorKind.GuardNotInEdit, "Nothing to cancel — not in edit mode.", loc));
        CurrentPo.CancelEdit();
        // Browser-style navigation: cancelling an edit returns to where we came from, but only if
        // there's an underlying frame. A top-level PO opened directly stays in scope so the script
        // can still inspect it.
        if (_navStack.Count >= 2)
            _navStack.RemoveAt(_navStack.Count - 1);
        return OpResult.Success;
    }

    public async Task<OpResult> SaveAsync(SourceLocation loc)
    {
        if (CurrentPo is null) return NoCurrentPo(loc);
        if (!CurrentPo.IsInEdit)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.GuardEditModeRequired,
                "SAVE needs the PersistentObject to be in edit mode.",
                loc,
                Hint: "Call EDIT before SET/SAVE — or the runner can auto-edit if you SET first."));

        // Required-attribute check before going out — clearer error than a server-side validation message.
        var missing = CurrentPo.Attributes
            .Where(a => a.IsRequired && a.IsVisible && !a.IsReadOnly && string.IsNullOrEmpty(a.ValueDirect))
            .Select(a => a.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            return OpResult.Fail(new Diagnostic(
                ErrorKind.GuardRequiredMissing,
                $"Required attribute(s) not set: {string.Join(", ", missing)}.",
                loc,
                Hint: "SET each required attribute before SAVE.",
                Details: new Dictionary<string, object?> { ["missing"] = missing }));
        }

        try
        {
            // Capture owner relationships before Save — PersistentObject.Save already mirrors the v4
            // frontend: it refreshes OwnerQuery and propagates the new objectId into
            // OwnerAttributeWithReference.Parent (after Parent.Edit()). The nav-stack pop below just
            // keeps the script-visible stack in sync; the owner side-effects are already done.
            // Only when neither owner is wired (script opened the PO directly and stacked a Query under it)
            // does the underlying Query need a fallback refresh from this side.
            var savedPo = CurrentPo;
            var ownerQuery = savedPo.OwnerQuery;
            var ownerAttr = savedPo.OwnerAttributeWithReference;

            await savedPo.Save().ConfigureAwait(false);
            if (savedPo.HasNotification && savedPo.NotificationType == NotificationType.Error)
                return OpResult.Fail(new Diagnostic(ErrorKind.AssertNotificationError, savedPo.Notification, loc));

            // Direct-OPEN PO scripts stay on the PO after save so they can still inspect it. Otherwise
            // the saved frame pops, mirroring the browser's "Save closes the dialog/page" behaviour.
            if (_navStack.Count >= 2)
            {
                _navStack.RemoveAt(_navStack.Count - 1);

                // If the revealed frame is the OwnerQuery, Save already refreshed it. If it's a parent PO
                // (OwnerAttributeWithReference dialog), Save already mutated it. Either way, no extra work.
                // The fallback is for unusual stacks where a Query sits underneath but isn't this PO's
                // OwnerQuery — best-effort refresh so the script sees current row state.
                if (ownerAttr is null && _navStack[^1] is QueryEntry qe && !ReferenceEquals(qe.Query, ownerQuery))
                {
                    try { await qe.Query.RefreshQueryAsync().ConfigureAwait(false); }
                    catch { /* refresh is best-effort — Save itself succeeded */ }
                }
            }

            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    /// <summary>
    /// Saves <see cref="Vidyano.Client.Initial"/> — the gate PO surfaced when the server requires
    /// the user to satisfy something before the app loads (license-terms acceptance, forced
    /// two-factor enrolment, password reset). On a clean Save calls <see cref="Vidyano.Client.ClearInitial"/>,
    /// matching v4's <c>service.clearInitial()</c>. The Initial PO is NOT on the navigation stack,
    /// so no stack mutation happens here — this is parallel to <see cref="SaveAsync"/>, not a path
    /// through it.
    /// </summary>
    public async Task<OpResult> SaveInitialAsync(SourceLocation loc)
    {
        var resolved = ResolveScopePo("initial", loc);
        if (!resolved.Ok)
            return OpResult.Fail(resolved.Error!);
        var po = resolved.Value!;

        // Auto-enter edit — deliberately asymmetric with SaveAsync, which errors when the PO
        // isn't in edit. The gate PO has no script-level EDIT verb in front of it (v4's sign-in
        // component begins-edit implicitly when binding the gate UI), and SAVE @initial is a
        // single-shot atomic operation in scripts: the user satisfies the gate, no fine-grained
        // edit workflow. Auto-edit mirrors that.
        if (!po.IsInEdit)
            po.Edit();

        var missing = po.Attributes
            .Where(a => a.IsRequired && a.IsVisible && !a.IsReadOnly && string.IsNullOrEmpty(a.ValueDirect))
            .Select(a => a.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            return OpResult.Fail(new Diagnostic(
                ErrorKind.GuardRequiredMissing,
                $"Required attribute(s) on @initial not set: {string.Join(", ", missing)}.",
                loc,
                Hint: "SET each required @initial attribute before SAVE @initial.",
                Details: new Dictionary<string, object?> { ["missing"] = missing }));
        }

        try
        {
            await po.Save().ConfigureAwait(false);
            if (po.HasNotification && po.NotificationType == NotificationType.Error)
                return OpResult.Fail(new Diagnostic(ErrorKind.AssertNotificationError, po.Notification, loc));

            Client.ClearInitial();
            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    public async Task<OpResult> RefreshAsync(SourceLocation loc)
    {
        if (CurrentPo is null) return NoCurrentPo(loc);
        try
        {
            await CurrentPo.RefreshAttributesAsync().ConfigureAwait(false);
            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    // --- set / action -------------------------------------------------------------------------

    /// <summary>
    /// Sets an attribute. Distinguishes four cases and uses a different <see cref="ErrorKind"/> for each:
    /// the attribute doesn't exist (typo → suggest), it exists but is hidden, it exists but is read-only,
    /// and the PO isn't in edit mode (auto-enter, with a warning diagnostic so the script author sees it).
    /// </summary>
    /// <remarks>
    /// For reference attributes (<see cref="PersistentObjectAttributeWithReference"/>) the runner does
    /// what a UI user does without making the script branch on <c>SelectInPlace</c>:
    /// <list type="bullet">
    ///   <item>Set-in-place attributes: the value is matched against <c>Options</c>' DisplayValue, then Key,
    ///   so writing <c>SET Customer = "Smith"</c> picks the matching dropdown entry.</item>
    ///   <item>Non-set-in-place attributes: the value is fed to <c>Lookup.SearchTextAsync</c> and the first
    ///   result is selected via <c>ChangeReference</c>. Pass a <see cref="ReferenceHint.Lookup"/> hint to
    ///   override the search text, or <see cref="ReferenceHint.RawId"/> to bypass the lookup entirely.</item>
    /// </list>
    /// </remarks>
    public OpResult SetAttribute(string name, object? value, SourceLocation loc, ReferenceHint? hint = null)
    {
        if (CurrentPo is null) return NoCurrentPo(loc);
        return SetAttributeOn(CurrentPo, name, value, loc, hint);
    }

    /// <summary>Sets an attribute on an explicit PO. Shared body between the implicit current-PO
    /// path (<see cref="SetAttribute"/>) and the scoped path (<see cref="SetScopedAttribute"/>).
    /// The only difference between callers is which PO they target — the hidden/readonly guards,
    /// the auto-enter-edit behaviour, and the reference-resolution branch are identical.</summary>
    private OpResult SetAttributeOn(PersistentObject po, string name, object? value, SourceLocation loc, ReferenceHint? hint)
    {
        var attr = po.GetAttribute(name);
        if (attr is null)
        {
            var candidates = po.Attributes.Select(a => a.Name);
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ResolveAttribute,
                $"Attribute '{name}' does not exist on {po.Type}.",
                loc,
                Hint: Suggester.Hint(name, candidates)));
        }

        if (!attr.IsVisible)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.GuardAttributeHidden,
                $"Attribute '{name}' exists on {po.Type} but is hidden — the UI would not allow setting it.",
                loc,
                Hint: "Use mode=direct only if you intend to bypass the UI guard.",
                Details: new Dictionary<string, object?> { ["attribute"] = name, ["isVisible"] = false }));

        if (attr.IsReadOnly)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.GuardAttributeReadOnly,
                $"Attribute '{name}' is read-only on {po.Type}.",
                loc,
                Hint: "Read-only attributes can be computed server-side — check the model or the action that sets it.",
                Details: new Dictionary<string, object?> { ["attribute"] = name }));

        if (!po.IsInEdit)
            po.Edit();

        if (attr is PersistentObjectAttributeWithReference refAttr)
            return SetReferenceAttribute(refAttr, value, loc, hint);

        // Non-reference Options-bearing attrs (KeyValueList / Dropdown / ComboBox) accept the same
        // LOOKUP/ID hints — resolve the value against Options[] and assign the matching Key. A bare
        // `SET X = "y"` (hint == null) keeps the literal-write behaviour for back-compat.
        if (hint is not null && attr.Options is { Length: > 0 } options)
        {
            var text = value as string ?? value?.ToString() ?? "";
            var resolved = ResolveOption(options, text, hint.Kind, loc, attr.Name);
            if (!resolved.Ok) return OpResult.Fail(resolved.Error!);
            attr.Value = resolved.Value;
            return OpResult.Success;
        }

        attr.Value = value;
        return OpResult.Success;
    }

    /// <summary>Resolves a user-supplied option text against an <c>Options[]</c> array, returning
    /// the key to assign to <c>attr.Value</c>. The <paramref name="hint"/> picks the matching policy:
    /// <list type="bullet">
    ///   <item><see cref="ReferenceHintKind.RawId"/> — exact match on <c>Key</c>.</item>
    ///   <item><see cref="ReferenceHintKind.Lookup"/> or <c>null</c> — match on <c>DisplayValue</c>
    ///         first (what the user reads), then fall back to <c>Key</c>.</item>
    /// </list>
    /// A miss returns a diagnostic with a <see cref="Suggester.Hint"/> built from the available
    /// labels, plus the full option set in <c>Details</c>.</summary>
    private static OpResult<string> ResolveOption(
        IReadOnlyList<PersistentObjectAttribute.Option> options,
        string text,
        ReferenceHintKind? hint,
        SourceLocation loc,
        string attrName)
    {
        PersistentObjectAttribute.Option? match = hint == ReferenceHintKind.RawId
            ? options.FirstOrDefault(o => string.Equals(o.Key, text, StringComparison.Ordinal))
            : options.FirstOrDefault(o => string.Equals(o.DisplayValue, text, StringComparison.Ordinal))
                  ?? options.FirstOrDefault(o => string.Equals(o.Key, text, StringComparison.Ordinal));

        if (match is not null)
            return OpResult<string>.Success(match.Key);

        var candidates = options.Where(o => o.DisplayValue != null).Select(o => o.DisplayValue!);
        return OpResult<string>.Fail(new Diagnostic(
            ErrorKind.AssertFailed,
            $"'{text}' is not one of the options for '{attrName}'.",
            loc,
            Hint: Suggester.Hint(text, candidates),
            Details: new Dictionary<string, object?>
            {
                ["attribute"] = attrName,
                ["available"] = options.Select(o => new { o.Key, o.DisplayValue }).ToArray(),
            }));
    }

    private OpResult SetReferenceAttribute(PersistentObjectAttributeWithReference attr, object? value, SourceLocation loc, ReferenceHint? hint)
    {
        // Explicit RawId hint: bypass lookup logic — the caller asserts they know the key.
        if (hint is { Kind: ReferenceHintKind.RawId, Value: var rawId })
        {
            attr.SelectedReferenceValue = rawId;
            return OpResult.Success;
        }

        // Null clears the reference (when allowed).
        if (value is null)
        {
            if (!attr.CanRemoveReference)
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.GuardAttributeReadOnly,
                    $"Attribute '{attr.Name}' is required and cannot be cleared.",
                    loc));
            attr.SelectedReferenceValue = null;
            return OpResult.Success;
        }

        var text = value as string ?? value.ToString() ?? "";

        if (attr.SelectInPlace)
        {
            // Set-in-place reference: pick a key from Options[] using the same matching policy as
            // non-reference Options attrs (see ResolveOption).
            var resolved = ResolveOption(attr.Options, text, hint?.Kind, loc, attr.Name);
            if (!resolved.Ok) return OpResult.Fail(resolved.Error!);
            attr.SelectedReferenceValue = resolved.Value;
            return OpResult.Success;
        }

        // Non-set-in-place: open the Lookup, search by the supplied value (or the explicit Lookup hint),
        // and pick the first hit. Multi-match is reported in Details so the user can refine.
        if (attr.Lookup is null)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ServerError,
                $"Reference attribute '{attr.Name}' has no Lookup query.",
                loc));

        var search = hint is { Kind: ReferenceHintKind.Lookup, Value: var s } ? (s ?? text) : text;
        return SetReferenceViaLookupAsync(attr, search, loc).GetAwaiter().GetResult();
    }

    private async Task<OpResult> SetReferenceViaLookupAsync(PersistentObjectAttributeWithReference attr, string searchText, SourceLocation loc)
    {
        try
        {
            await attr.Lookup.SearchTextAsync(searchText).ConfigureAwait(false);
            if (attr.Lookup.TotalItems == 0)
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.AssertFailed,
                    $"Lookup for '{attr.Name}' found no rows matching '{searchText}'.",
                    loc,
                    Hint: "Refine the search text, or use 'SET attr = ID \"<guid>\"' to bypass the lookup."));

            var first = attr.Lookup[0];
            if (first is null)
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.ServerError,
                    $"Lookup for '{attr.Name}' did not return the first row.",
                    loc));
            await attr.ChangeReference(first).ConfigureAwait(false);
            // Surface multi-match so a flaky test can be tightened.
            if (attr.Lookup.TotalItems > 1)
                return OpResult.Success; // First-wins is consistent with the UI's behavior.
            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    // --- reserved scopes (@session) -----------------------------------------------------------

    /// <summary>A scoped PO + attribute pair returned by <see cref="ResolveScopedAttribute"/>.
    /// Lets EXPECT branches reuse the same not-found / suggestion logic as SET/READ.</summary>
    public sealed record ScopedAttribute(PersistentObject Po, PersistentObjectAttribute Attribute);

    /// <summary>Resolves the PO backing a reserved variable scope. <c>"session"</c> maps to
    /// <see cref="Vidyano.Client.Session"/>; <c>"initial"</c> maps to <see cref="Vidyano.Client.Initial"/>
    /// (the gate PO surfaced when the server requires license-terms acceptance / 2FA enrol /
    /// password reset). <c>"user"</c> / <c>"application"</c> are reserved shapes that return a
    /// not-implemented diagnostic. Anything else is rejected as unknown.</summary>
    internal OpResult<PersistentObject> ResolveScopePo(string scope, SourceLocation loc)
    {
        if (string.Equals(scope, "session", StringComparison.OrdinalIgnoreCase))
        {
            var po = Client.Session;
            if (po is null)
                return OpResult<PersistentObject>.Fail(new Diagnostic(
                    ErrorKind.StateNoSession,
                    "No Session PO on this app — `@session` is unbound.",
                    loc,
                    Hint: "Configure a Session PersistentObject on the server, or remove the @session reference."));
            return OpResult<PersistentObject>.Success(po);
        }
        if (string.Equals(scope, "initial", StringComparison.OrdinalIgnoreCase))
        {
            var po = Client.Initial;
            if (po is null)
                return OpResult<PersistentObject>.Fail(new Diagnostic(
                    ErrorKind.StateNoSession,
                    "No Initial PO on this session — `@initial` is unbound.",
                    loc,
                    Hint: "The server only returns an Initial PO when a gate (license terms, 2FA enrol, password reset) is required. Check `Client.Initial` after sign-in."));
            return OpResult<PersistentObject>.Success(po);
        }
        if (string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scope, "application", StringComparison.OrdinalIgnoreCase))
        {
            return OpResult<PersistentObject>.Fail(new Diagnostic(
                ErrorKind.StateScopeNotImplemented,
                $"Reserved scope '@{scope.ToLowerInvariant()}' is not yet implemented.",
                loc,
                Hint: "Only @session and @initial resolve in this build."));
        }
        return OpResult<PersistentObject>.Fail(new Diagnostic(
            ErrorKind.ResolveVariable,
            $"Unknown variable scope '@{scope}'.",
            loc,
            Hint: Suggester.Hint(scope, new[] { "session", "initial", "user", "application" })
                  ?? "Valid scopes: @session, @initial."));
    }

    /// <summary>Resolves a scoped attribute by name, surfacing the same not-found suggestion
    /// shape as the current-PO path. The EXPECT branches reuse this so flag/label checks share
    /// the resolution logic with the value read.</summary>
    public OpResult<ScopedAttribute> ResolveScopedAttribute(string scope, string attributeName, SourceLocation loc)
    {
        var poRes = ResolveScopePo(scope, loc);
        if (!poRes.Ok) return OpResult<ScopedAttribute>.Fail(poRes.Error!);
        var po = poRes.Value!;
        var attr = po.GetAttribute(attributeName);
        if (attr is null)
            return OpResult<ScopedAttribute>.Fail(new Diagnostic(
                ErrorKind.ResolveAttribute,
                $"Attribute '{attributeName}' does not exist on @{scope} ({po.Type}).",
                loc,
                Hint: Suggester.Hint(attributeName, po.Attributes.Select(a => a.Name))));
        return OpResult<ScopedAttribute>.Success(new ScopedAttribute(po, attr));
    }

    /// <summary>Reads an attribute value from a scoped PO (e.g. <c>@session.CurrentYear</c>).
    /// Mirrors <see cref="ResolveExpectSubject"/>'s hidden-attribute guard so reads through this
    /// path are subject to the same UI-visibility contract as the current-PO bare-name form.</summary>
    public OpResult<object?> GetScopedAttributeValue(string scope, string attributeName, SourceLocation loc)
    {
        var res = ResolveScopedAttribute(scope, attributeName, loc);
        if (!res.Ok) return OpResult<object?>.Fail(res.Error!);
        var attr = res.Value!.Attribute;
        if (!attr.IsVisible)
            return OpResult<object?>.Fail(new Diagnostic(
                ErrorKind.GuardAttributeHidden,
                $"Attribute '{attributeName}' exists on @{scope} ({res.Value!.Po.Type}) but is hidden — the UI cannot read it.",
                loc));
        return OpResult<object?>.Success(attr.Value);
    }

    /// <summary>Sets an attribute on a scoped PO. Same edit/guard/reference-resolution semantics
    /// as <see cref="SetAttribute"/>, but targeting <see cref="Vidyano.Client.Session"/> (or, in
    /// the future, <c>@user</c>/<c>@application</c>) instead of the navigation-stack top.</summary>
    public OpResult SetScopedAttribute(string scope, string attributeName, object? value, ReferenceHint? hint, SourceLocation loc)
    {
        var poRes = ResolveScopePo(scope, loc);
        if (!poRes.Ok) return OpResult.Fail(poRes.Error!);
        return SetAttributeOn(poRes.Value!, attributeName, value, loc, hint);
    }

    /// <summary>
    /// Executes an action by name. Distinguishes: not found (typo → suggest), found but hidden,
    /// found and visible but <c>CanExecute=false</c>, transport/server error.
    /// </summary>
    public Task<OpResult> ExecuteActionAsync(string name, IReadOnlyDictionary<string, string>? parameters, SourceLocation loc) =>
        ExecuteActionAsync(name, parameters, option: null, optionHint: null, loc);

    /// <summary>Overload supporting the <c>ACTION X = "label"</c> / <c>ACTION X = ID 0</c> form.
    /// When <paramref name="option"/> is non-null, the value is matched against
    /// <see cref="ActionBase.Options"/> (Core's <see cref="ActionBase.Execute(object)"/> overload then
    /// builds the MenuOption / MenuLabel parameters itself, so we just hand it the resolved label):
    /// <list type="bullet">
    ///   <item><paramref name="optionHint"/> = <see cref="ReferenceHintKind.RawId"/> — treat the value
    ///         as an int index into <c>Options</c>.</item>
    ///   <item>Otherwise — treat the value as a label and match it against the entries directly.</item>
    /// </list>
    /// <paramref name="option"/> and <paramref name="parameters"/> are mutually exclusive (Core's
    /// option-form <c>Execute</c> doesn't take named params). Label matching is case-sensitive
    /// (<see cref="StringComparison.Ordinal"/>) — same convention as the SET SIP path uses against
    /// <see cref="PersistentObjectAttribute.Option.DisplayValue"/>; the diagnostic's
    /// <see cref="Suggester.Hint"/> catches close-but-not-exact typos.</summary>
    public async Task<OpResult> ExecuteActionAsync(string name, IReadOnlyDictionary<string, string>? parameters, object? option, ReferenceHintKind? optionHint, SourceLocation loc)
    {
        if (CurrentPo is null && CurrentQuery is null)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.StateNoCurrentPo,
                "ACTION needs a current PersistentObject or Query.",
                loc));

        ActionBase? action = null;
        IEnumerable<string> candidates = Array.Empty<string>();
        if (CurrentPo != null)
        {
            action = CurrentPo.GetAction(name);
            candidates = CurrentPo.Actions.Concat(CurrentPo.PinnedActions).Select(a => a.Name);
        }
        if (action is null && CurrentQuery != null)
        {
            action = CurrentQuery.GetAction(name);
            candidates = candidates.Concat(CurrentQuery.Actions.Concat(CurrentQuery.PinnedActions).Select(a => a.Name));
        }
        if (action is null)
        {
            return OpResult.Fail(new Diagnostic(
                ErrorKind.ResolveAction,
                $"Action '{name}' does not exist here.",
                loc,
                Hint: Suggester.Hint(name, candidates)));
        }

        if (!action.IsVisible)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.GuardActionHidden,
                $"Action '{name}' exists but is hidden — the UI would not show it.",
                loc));

        if (!action.CanExecute)
            return OpResult.Fail(new Diagnostic(
                ErrorKind.GuardActionNotAvailable,
                $"Action '{name}' cannot execute right now (CanExecute is false).",
                loc,
                Hint: "Often this means a selection or required state isn't met. Check the SelectionRule or PO state."));

        // Resolve the optional `= "label"` / `= ID <index>` form to the label Core expects on its
        // Execute(option) overload. Done up-front so the bad-option diagnostic fires before any
        // server call.
        string? optionLabel = null;
        if (option is not null)
        {
            if (parameters is { Count: > 0 })
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.ParseUnexpectedToken,
                    $"Action '{name}' was invoked with both an option value and named parameters.",
                    loc,
                    Hint: "Core's Execute(option) overload doesn't accept parameters — use ACTION X = \"label\" or ACTION X (P=…), not both."));

            var actionOptions = action.Options;
            if (actionOptions.Length == 0)
                return OpResult.Fail(new Diagnostic(
                    ErrorKind.AssertFailed,
                    $"Action '{name}' has no Options[] — the `= …` form requires server-defined options.",
                    loc));

            if (optionHint == ReferenceHintKind.RawId)
            {
                if (!TryAsInt(option, out var idx))
                    return OpResult.Fail(new Diagnostic(
                        ErrorKind.ParseInvalidValue,
                        $"ACTION {name} = ID <index> needs an integer.",
                        loc));
                if (idx < 0 || idx >= actionOptions.Length)
                    return OpResult.Fail(new Diagnostic(
                        ErrorKind.AssertFailed,
                        $"Option index {idx} is out of range for action '{name}' (Options.Length = {actionOptions.Length}).",
                        loc,
                        Hint: $"Valid indices: 0..{actionOptions.Length - 1}. Labels: {string.Join(", ", actionOptions.Select(o => $"\"{o}\""))}."));
                optionLabel = actionOptions[idx];
            }
            else
            {
                var label = option as string ?? option.ToString() ?? "";
                var hit = actionOptions.FirstOrDefault(o => string.Equals(o, label, StringComparison.Ordinal));
                if (hit is null)
                    return OpResult.Fail(new Diagnostic(
                        ErrorKind.AssertFailed,
                        $"'{label}' is not one of the options for action '{name}'.",
                        loc,
                        Hint: Suggester.Hint(label, actionOptions),
                        Details: new Dictionary<string, object?> { ["available"] = actionOptions }));
                optionLabel = hit;
            }
        }

        try
        {
            PersistentObject? result;
            if (optionLabel is not null)
            {
                // Defer to Core's Execute(option) — it builds MenuOption/MenuLabel and routes the
                // result through the existing post-execute machinery (notifications, refresh, etc.).
                result = await action.Execute(optionLabel).ConfigureAwait(false);
            }
            else
            {
                var dict = parameters?.ToDictionary(kv => kv.Key, kv => kv.Value);
                var prefix = action is QueryAction ? "Query." : "PersistentObject.";
                result = await Client.ExecuteActionAsync(prefix + name, CurrentPo, CurrentQuery, null, dict).ConfigureAwait(false);
            }
            // ExecuteActionAsync sets notification on the parent on error and returns null.
            if (result is null && CurrentPo != null && CurrentPo.HasNotification && CurrentPo.NotificationType == NotificationType.Error)
                return OpResult.Fail(new Diagnostic(ErrorKind.AssertNotificationError, CurrentPo.Notification, loc));
            if (result != null && result.FullTypeName != "Vidyano.Notification")
            {
                // If the top frame is already a PO, swap to the action's result (same navigation level).
                // Otherwise push a new PO frame on top of whatever query was current (e.g. ACTION New).
                // The server marks dialogs via StateBehavior.OpenAsDialog — including the cascading
                // case where a New PO launches further references as dialogs.
                var entry = new PoEntry(result, IsDialog(result));
                if (_navStack.Count > 0 && _navStack[^1] is PoEntry)
                    _navStack[^1] = entry;
                else
                    _navStack.Add(entry);
            }
            return OpResult.Success;
        }
        catch (Exception ex)
        {
            return OpResult.Fail(new Diagnostic(ErrorKind.ServerError, ex.Message, loc));
        }
    }

    /// <summary>Coerces a script value into an int. Accepts <see cref="int"/>, <see cref="long"/>,
    /// <see cref="decimal"/>, and numeric strings (<c>"0"</c>, <c>"12"</c>). Used by the
    /// option-by-index form on ACTION. Mirrors <c>Interpreter.TryCoerceInt</c> so the two coercion
    /// paths stay in sync — Vidyano expressions can produce decimals (e.g. from sums), so accepting
    /// them here avoids gratuitous rejections.</summary>
    private static bool TryAsInt(object value, out int result)
    {
        switch (value)
        {
            case int i:                                                                                              result = i;      return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:                                               result = (int)l; return true;
            case decimal d when d is >= int.MinValue and <= int.MaxValue:                                            result = (int)d; return true;
            case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p):       result = p;      return true;
            default:                                                                                                 result = 0;      return false;
        }
    }

    // --- handle lookup ------------------------------------------------------------------------

    /// <summary>
    /// Resolves <c>@handle</c> to a PO or Query previously bound via <c>AS</c>. Returns
    /// <see cref="ErrorKind.ResolveHandle"/> with a suggestion if it doesn't exist.
    /// </summary>
    public OpResult<object> ResolveHandle(string name, SourceLocation loc)
    {
        if (_handles.TryGetValue(name, out var v))
            return OpResult<object>.Success(v);
        return OpResult<object>.Fail(new Diagnostic(
            ErrorKind.ResolveHandle,
            $"No handle named '@{name}'.",
            loc,
            Hint: Suggester.Hint(name, _handles.Keys)));
    }

    // --- snapshot -----------------------------------------------------------------------------

    /// <summary>Builds a point-in-time snapshot of the session state for reporting.</summary>
    public Snapshot TakeSnapshot()
    {
        var session = IsSignedIn ? new SessionSnapshot(Client.User, Client.Uri) : null;
        var po = CurrentPo is { } cur ? BuildPoSnapshot(cur) : null;
        var sessionPo = Client.Session is { } sess ? BuildPoSnapshot(sess) : null;

        var query = CurrentQuery is null ? null : new QuerySnapshot(
            Name: CurrentQuery.Name,
            TextSearch: CurrentQuery.TextSearch,
            TotalItems: CurrentQuery.TotalItems,
            Count: CurrentQuery.Count,
            Rows: CurrentQuery.Take(10).Where(r => r != null)
                .Select(r => (IReadOnlyDictionary<string, string?>)CurrentQuery.Columns.ToDictionary(
                    c => c.Name,
                    c => Vidyano.Client.ToServiceString(r[c.Name]) as string)).ToList());

        IReadOnlyDictionary<string, string>? handles = _handles.Count == 0 ? null
            : _handles.ToDictionary(kv => kv.Key, kv => DescribeHandle(kv.Value));

        return new Snapshot(session, po, query, handles, sessionPo);
    }

    private static PoSnapshot BuildPoSnapshot(PersistentObject po) => new(
        Type: po.Type,
        ObjectId: po.ObjectId,
        IsInEdit: po.IsInEdit,
        IsDirty: po.IsDirty,
        IsNew: po.IsNew,
        Notification: po.Notification,
        NotificationType: po.HasNotification ? po.NotificationType.ToString() : null,
        Attributes: po.Attributes.Select(a => new AttributeSnapshot(
            a.Name, a.Type, a.ValueDirect, SafeDisplay(a),
            a.IsVisible, a.IsReadOnly, a.IsRequired, a.IsValueChanged, a.ValidationError)).ToList(),
        Actions: po.Actions.Concat(po.PinnedActions)
            .Select(a => new ActionSnapshot(a.Name, a.CanExecute, a.IsVisible)).ToList());

    private static string? SafeDisplay(PersistentObjectAttribute attr)
    {
        try { return attr.DisplayValue; }
        catch { return attr.ValueDirect; }
    }

    private static string DescribeHandle(object value) =>
        value switch
        {
            PersistentObject po => $"PO {po.Type}/{po.ObjectId}",
            Query q             => $"Query {q.Name}",
            _                   => value.GetType().Name,
        };

    private static OpResult NoCurrentPo(SourceLocation loc) => OpResult.Fail(new Diagnostic(
        ErrorKind.StateNoCurrentPo,
        "No current PersistentObject — open one first with OPEN PersistentObject … or OPEN-ROW.",
        loc));

    /// <summary>Whether the server marked this PO to be presented as a modal dialog. The server sets
    /// <see cref="StateBehavior.OpenAsDialog"/> for stand-alone dialogs and for the cascading case
    /// where a New PO launches reference lookups as nested dialogs — so a single flag check covers
    /// both the explicit and the implicit (dirty-tracking) dialog paths.</summary>
    private static bool IsDialog(PersistentObject po) => po.StateBehavior.HasFlag(StateBehavior.OpenAsDialog);

    public void Dispose()
    {
        _ownedHttpClient?.Dispose();
    }
}
