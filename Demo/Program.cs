using Demo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vidyano;
using Vidyano.Service;
using Vidyano.ViewModel;

Console.WriteLine("Vidyano.Core Demo Application");
Console.WriteLine("==============================");
Console.WriteLine("Booting a full Vidyano app in-process (Minimal API) — no config, no database, no demo.vidyano.com...\n");

// 1) Stand up the Vidyano app on Kestrel, bound to a free localhost port. This is a real server
//    process the client below reaches over real HTTP — exactly how a frontend would hit a deployment.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");           // 0 → the OS assigns a free port
builder.Logging.SetMinimumLevel(LogLevel.Warning);       // keep the demo's own output legible
builder.AddVidyanoMinimal<DemoContext>(vidyano => vidyano
    .WithDefaultAdmin()                                  // an "admin" / "admin" user to sign in as
    .WithSchemaRights()                                  // grant that user rights to the model
    .WithMenuItem("Products")
    .WithMenuItem("ProductCategories"));

await using var app = builder.Build();
app.UseVidyano(app.Environment, app.Configuration);
await app.StartAsync();

var baseUri = app.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()!.Addresses.First();
Console.WriteLine($"Vidyano Minimal app listening at {baseUri}\n");

try
{
    // 2) Drive that in-process app with an ordinary Vidyano.Core client. `new Client()` builds its own
    //    cookie-aware HttpClient, so this is identical to talking to any remote Vidyano service.
    var client = new Client { Uri = baseUri.TrimEnd('/') + "/" };

    await client.SignInUsingCredentialsAsync("admin", "admin");
    Console.WriteLine("Signed in to the in-process Vidyano service!");
    Console.WriteLine($"User: {client.User ?? "Guest"}\n");

    // The navigation menu the model defined via WithMenuItem(...) — program units → query items.
    Console.WriteLine("Navigation menu:");
    Console.WriteLine("----------------");
    var queryItems = new List<ProgramUnitItemQuery>();
    void Collect(IReadOnlyList<ProgramUnitItem> items, int depth)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case ProgramUnitItemQuery q:
                    queryItems.Add(q);
                    Console.WriteLine($"{new string(' ', depth * 2)}- {q.Title ?? q.QueryName}");
                    break;
                case ProgramUnitItemGroup g:
                    Console.WriteLine($"{new string(' ', depth * 2)}{g.Title ?? g.Name}");
                    Collect(g.Items, depth + 1);
                    break;
            }
        }
    }
    foreach (var unit in client.ProgramUnits)
    {
        Console.WriteLine($"  {unit.Title ?? unit.Name}");
        Collect(unit.Items, depth: 2);
    }

    // Open the Products query (by the name the server emitted) and list its rows.
    var productsItem = queryItems.FirstOrDefault(q =>
        string.Equals(q.QueryName, "Products", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(q.Title, "Products", StringComparison.OrdinalIgnoreCase));
    var products = await client.GetQueryAsync(productsItem?.QueryName ?? productsItem?.QueryId ?? "Products");
    if (products is not null)
    {
        if (!products.HasSearched)
            await products.SearchTextAsync(string.Empty);

        Console.WriteLine($"\nQuery '{products.Name}': {products.TotalItems} item(s)");
        Console.WriteLine("------------------");
        for (var i = 0; i < products.Count; i++)
        {
            var item = products[i];
            if (item is not null)
                Console.WriteLine($"  [{item.Id}] {item["Name"]} — Color: {item["Color"]}, Category: {item["Category"]}");
        }
    }

    Console.WriteLine("\n✅ Demo completed successfully!");
    Console.WriteLine("This demo boots a full Vidyano app from code and drives it with a Vidyano.Core client — no external server required.");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
}

// `await using var app` above gracefully stops Kestrel and disposes the host at scope exit.
