# Vidyano.Core usage

`Vidyano.Core` is the official .NET client SDK for Vidyano backends. It connects to a service, manages the session, and exposes the server's persistent objects, queries, and actions through an MVVM-friendly, async-first API.

- **Cross-platform** — Windows, Linux, macOS.
- **Multi-target** — .NET Standard 2.0, .NET 8.0, .NET 10.0.
- **Async-first** — modern `async`/`await` throughout.
- **MVVM** — built-in `INotifyPropertyChanged` for data binding.
- **Internationalized** — 30+ languages built in.

## Install

```bash
dotnet add package Vidyano.Core
```

## Connecting

```csharp
using Vidyano;

var client = new Client
{
    Uri = "https://your-vidyano-service.com"
};

await client.SignInUsingCredentialsAsync("username", "password");
```

## Working with persistent objects

```csharp
var po = await client.GetPersistentObjectAsync("Customer", "customer-id");

po["Name"].Value  = "New Name";
po["Email"].Value = "email@example.com";

var save = po.GetAction("Save");
if (save is { CanExecute: true })
    await save.Execute(null);
```

### Multi-lingual attributes (`TranslatedString`)

A `TranslatedString` attribute carries a per-language map of strings. The supported languages are decided by the server **per attribute** (not globally on the `Client`), so you read them off the attribute itself. `attr.Value` is the current-language string; the full map is read with a cast and written with the `SetTranslation*` helpers, which update every language correctly and keep the current-language `Value` in sync.

```csharp
po.Edit();

var title = po["Title"];

// Read: the current-language value, or the full per-language map.
string current = (string)title.Value;             // e.g. "Widget"
TranslatedString all = (TranslatedString)title;   // { "en": "Widget", "nl": "Hulpmiddel", … }
string dutch = all?["nl"];

// Write: one language, the session's current language, or a whole map (merged over the rest).
await title.SetTranslationAsync("nl", "Hulpmiddel");
await title.SetCurrentTranslationAsync("Updated title");
await title.SetTranslationsAsync(new Dictionary<string, string> { ["en"] = "Widget", ["de"] = "Werkzeug" });

await po.Save();
```

## Executing queries

```csharp
var query = await client.GetQueryAsync("Customers");
await query.SearchTextAsync(string.Empty);

// Query implements IReadOnlyList<QueryResultItem>.
foreach (var item in query)
    Console.WriteLine($"Customer: {item["Name"]}");

Console.WriteLine($"Total items: {query.TotalItems}");

for (int i = 0; i < query.Count; i++)
    Console.WriteLine($"Item {i}: {query[i].Id}");
```

`query.Clone(asLookup: true)` copies a query's definition (parent, search text, sort — not its rows, selection, or notification) as a lookup, like the web client's `query.clone(true)`. Searching the clone lists the query's lookup source: the candidates a built-in `AddReference` picker offers. Post the picked rows against the original query:

```csharp
var picker = detail.Clone(asLookup: true);
await picker.RefreshQueryAsync();
await client.ExecuteActionAsync("Query.AddReference", parentPo, detail, new[] { picker[0] }, skipHooks: true);
```

## Running actions

```csharp
var po = await client.GetPersistentObjectAsync("Order", "order-id");
var approve = po.GetAction("Approve");

if (approve is { CanExecute: true })
    await approve.Execute(null);
```

`Execute` re-searches the action's query afterwards when the server's action definition says so (`action.RefreshQueryOnCompleted`). A caller that posts the action itself with `client.ExecuteActionAsync` owns that refresh; `query.RefreshQueryAsync(keepSelection: action.KeepSelectionOnRefresh)` does it the way the web client does, keeping the selection (re-selected by id) when the definition asks for it:

```csharp
var delete = query.GetAction("Delete");
await client.ExecuteActionAsync("Query.Delete", query.Parent, query, query.SelectedItems.ToArray());
if (delete.RefreshQueryOnCompleted)
    await query.RefreshQueryAsync(keepSelection: delete.KeepSelectionOnRefresh);
```

## Demo application

The [`Demo`](https://github.com/2sky/Vidyano.Core/tree/main/Demo) console app connects to the public demo service at `https://demo.vidyano.com`:

```bash
cd Demo
dotnet run
```

## Scripting

To script sessions — for regression tests, smoke tests, or agent automation — see the [`.visc` language](./visc-language.md), the [`vidyano` CLI](./cli.md), and the [embedding guide](./embedding.md).
