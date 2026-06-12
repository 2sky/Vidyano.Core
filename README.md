# Vidyano.Core

[![NuGet](https://img.shields.io/nuget/v/Vidyano.Core.svg)](https://www.nuget.org/packages/Vidyano.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Vidyano.Core.svg)](https://www.nuget.org/packages/Vidyano.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET%20Standard-2.0-512BD4)](https://dotnet.microsoft.com/download)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download)
[![GitHub last commit](https://img.shields.io/github/last-commit/2sky/Vidyano.Core)](https://github.com/2sky/Vidyano.Core/commits/main)

Official .NET client library for Vidyano applications — a comprehensive client-side SDK for connecting to Vidyano backend services. Cross-platform, async-first, MVVM-friendly, and multi-target (.NET Standard 2.0, .NET 8.0, .NET 10.0).

## Installation

```bash
dotnet add package Vidyano.Core
```

## Quick start

```csharp
using Vidyano;

var client = new Client { Uri = "https://your-vidyano-service.com" };
await client.SignInUsingCredentialsAsync("username", "password");

var query = await client.GetQueryAsync("Customers");
await query.SearchTextAsync(string.Empty);

foreach (var item in query)
    Console.WriteLine(item["Name"]);
```

➡️ **[Full Vidyano.Core usage guide](https://github.com/2sky/Vidyano.Core/blob/main/docs/core.md)** — persistent objects, queries, actions, and the demo app.

## This repository also ships scripting packages

The same repo provides two packages for **scripting** Vidyano sessions — regression tests, smoke tests, agent automation, reproducing customer flows from a small file. They drive a real session: whatever a `.visc` script does, a frontend could have done.

| Package | Use it when… |
|---|---|
| [`Vidyano.Script`](https://www.nuget.org/packages/Vidyano.Script/) | You want to **embed** the `.visc` engine in your own .NET process. |
| [`Vidyano.Script.Tool`](https://www.nuget.org/packages/Vidyano.Script.Tool/) | You want a **CLI** — `dotnet tool install -g Vidyano.Script.Tool` gives you `vidyano run script.visc`. |

```visc
@app = "https://demo.vidyano.com/"
SIGN-IN admin / vidyano

OPEN MenuItem Home/Customers
SEARCH ""
EXPECT TotalItems >= 1
```

## Documentation

📖 **[Full documentation](https://github.com/2sky/Vidyano.Core/blob/main/docs/)** — the package overview, the complete [`.visc` language reference](https://github.com/2sky/Vidyano.Core/blob/main/docs/visc-language.md), the [CLI guide](https://github.com/2sky/Vidyano.Core/blob/main/docs/cli.md), and the [embedding guide](https://github.com/2sky/Vidyano.Core/blob/main/docs/embedding.md).

For the broader platform, visit [www.vidyano.com](https://www.vidyano.com/).

## TypeScript / JavaScript client

For TypeScript and JavaScript applications, we also provide an npm package:

[![npm version](https://img.shields.io/npm/v/@vidyano/core.svg)](https://www.npmjs.com/package/@vidyano/core)

```bash
npm install @vidyano/core
```

## Contributing

We welcome contributions! Please feel free to submit pull requests or open issues on our [GitHub repository](https://github.com/2sky/Vidyano.Core).

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

## Support

- Open an issue on [GitHub](https://github.com/2sky/Vidyano.Core/issues)
- Visit [www.vidyano.com](https://www.vidyano.com/)
- Contact us at support@vidyano.com

---

Copyright © 2025 2sky NV. All rights reserved.
