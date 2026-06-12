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

## Running actions

```csharp
var po = await client.GetPersistentObjectAsync("Order", "order-id");
var approve = po.GetAction("Approve");

if (approve is { CanExecute: true })
    await approve.Execute(null);
```

## Demo application

The [`Demo`](https://github.com/2sky/Vidyano.Core/tree/main/Demo) console app connects to the public demo service at `https://demo.vidyano.com`:

```bash
cd Demo
dotnet run
```

## Scripting

To script sessions — for regression tests, smoke tests, or agent automation — see the [`.visc` language](./visc-language.md), the [`vidyano` CLI](./cli.md), and the [embedding guide](./embedding.md).
