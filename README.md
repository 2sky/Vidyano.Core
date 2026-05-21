# Vidyano.Core

[![NuGet](https://img.shields.io/nuget/v/Vidyano.Core.svg)](https://www.nuget.org/packages/Vidyano.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Vidyano.Core.svg)](https://www.nuget.org/packages/Vidyano.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET%20Standard-2.0-512BD4)](https://dotnet.microsoft.com/download)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download)
[![GitHub last commit](https://img.shields.io/github/last-commit/2sky/Vidyano.Core)](https://github.com/2sky/Vidyano.Core/commits/main)

Official .NET client library for Vidyano applications. This library provides a comprehensive client-side SDK for connecting to Vidyano backend services.

## Installation

Install the Vidyano.Core NuGet package:

```bash
dotnet add package Vidyano.Core
```

Or via Package Manager Console:

```powershell
Install-Package Vidyano.Core
```

## Quick Start

```csharp
using Vidyano;

// Initialize the client
var client = new Client()
{
    Uri = "https://your-vidyano-service.com"
};

// Connect with credentials
await client.SignInUsingCredentialsAsync("username", "password");

// Execute a query
var query = await client.GetQueryAsync("YourQueryName");
await query.SearchTextAsync(string.Empty);

// Access results
foreach (var item in query)
{
    Console.WriteLine(item["PropertyName"]);
}
```

## Demo Application

Check out the [Demo](./Demo) folder for a complete console application that connects to our public demo service at https://demo.vidyano.com.

To run the demo:

```bash
cd Demo
dotnet run
```

## Features

- **Cross-platform support** - Works on Windows, Linux, and macOS
- **Multiple .NET targets** - Supports .NET Standard 2.0 and .NET 8.0
- **Async/await patterns** - Modern asynchronous programming model
- **MVVM architecture** - Built-in support for data binding and property change notifications
- **Comprehensive action system** - Execute backend actions with ease
- **Multi-language support** - Internationalization for 30+ languages
- **Type-safe operations** - Generic implementations for compile-time safety

## Basic Usage

### Connecting to a Service

```csharp
var client = new Client()
{
    Uri = "https://your-service-url"
};
await client.SignInUsingCredentialsAsync("username", "password");
```

### Working with Persistent Objects

```csharp
// Get a persistent object
var po = await client.GetPersistentObjectAsync("Customer", "customer-id");

// Update attributes
po["Name"].Value = "New Name";
po["Email"].Value = "email@example.com";

// Save changes using the Save action
var saveAction = po.GetAction("Save");
if (saveAction != null && saveAction.CanExecute)
    await saveAction.Execute(null);
```

### Executing Queries

```csharp
// Get and execute a query
var query = await client.GetQueryAsync("Customers");
await query.SearchTextAsync(string.Empty);

// Access results (Query implements IReadOnlyList<QueryResultItem>)
foreach (var item in query)
{
    Console.WriteLine($"Customer: {item["Name"]}");
}

// Get total count
Console.WriteLine($"Total items: {query.TotalItems}");

// Paging through results
for (int i = 0; i < query.Count; i++)
{
    var item = query[i];
    Console.WriteLine($"Item {i}: {item.Id}");
}
```

### Working with Actions

```csharp
// Execute an action on a persistent object
var po = await client.GetPersistentObjectAsync("Order", "order-id");
var approveAction = po.GetAction("Approve");

if (approveAction != null && approveAction.CanExecute)
{
    await approveAction.Execute(null);
}
```

## Companion packages

The same repository ships two .NET packages built on top of `Vidyano.Core` for **scripting** Vidyano sessions — useful for regression tests, agent automation, and reproducing customer flows from a small file.

| Package | Use it when… |
|---|---|
| [`Vidyano.Script`](https://www.nuget.org/packages/Vidyano.Script/) | You want to **embed** the `.visc` engine in your own .NET process (test fixtures, agents, custom runners). |
| [`Vidyano.Script.Tool`](https://www.nuget.org/packages/Vidyano.Script.Tool/) | You want a **CLI** — `dotnet tool install -g Vidyano.Script.Tool` gives you `vidyano run script.visc`. |

A `.visc` file is a short declarative script — sign in, open queries, edit rows, run actions — with `EXPECT` assertions on the observable state at each step:

```visc
@app = "https://demo.vidyano.com/"
SIGN-IN admin / vidyano

OPEN MenuItem Home/Customers
SEARCH ""
EXPECT TotalItems >= 1

OPEN-ROW 0
EXPECT NavStack.Top.Kind = "PersistentObject"
```

See each package's README for the full verb reference and examples.

## TypeScript/JavaScript Client

For TypeScript and JavaScript applications, we also provide an npm package:

[![npm version](https://img.shields.io/npm/v/@vidyano/core.svg)](https://www.npmjs.com/package/@vidyano/core)

```bash
npm install @vidyano/core
```

Learn more about the TypeScript client at [@vidyano/core](https://www.npmjs.com/package/@vidyano/core).

## Documentation

For comprehensive documentation, visit [www.vidyano.com](https://www.vidyano.com/).

## Contributing

We welcome contributions! Please feel free to submit pull requests or open issues on our [GitHub repository](https://github.com/2sky/Vidyano.Core).

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

For support and questions:
- Open an issue on [GitHub](https://github.com/2sky/Vidyano.Core/issues)
- Visit our website at [www.vidyano.com](https://www.vidyano.com/)
- Contact us at support@vidyano.com

## About Vidyano

Vidyano is a comprehensive application platform that enables rapid development of data-driven applications. Learn more at [www.vidyano.com](https://www.vidyano.com/).

---

Copyright © 2025 2sky NV. All rights reserved.