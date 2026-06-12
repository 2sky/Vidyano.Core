# Vidyano.Script

[![NuGet](https://img.shields.io/nuget/v/Vidyano.Script.svg)](https://www.nuget.org/packages/Vidyano.Script/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Engine for the `.visc` Vidyano scripting format. Parses, interprets, and drives a real `Vidyano.Client` session against a backend — for tests, automation, agents, and reproducing customer flows from a small script file. There is no mocking: whatever a `.visc` script does, a frontend could have done.

If you just want to run scripts from the command line, install the companion tool [`Vidyano.Script.Tool`](https://www.nuget.org/packages/Vidyano.Script.Tool/) instead. This package is for **embedding** the engine in your own .NET process.

## Installation

```bash
dotnet add package Vidyano.Script
```

Targets `net8.0` and `net10.0`. Pulls in `Vidyano.Core` transitively.

## Quick example

```csharp
using Vidyano.Script;

var script = """
    @app = "https://demo.vidyano.com/"
    SIGN-IN admin / vidyano

    OPEN MenuItem Home/Customers
    SEARCH ""
    EXPECT TotalItems >= 1
    """;

var result = await VidyanoScript.RunAsync(script);

// result.Ok is the pass/fail bit; result.Describe() renders a plain-text report
// (source + pass/fail/skip tally + each failed statement's diagnostic) for a log or
// an assertion message, e.g. Assert.That(result.Ok, Is.True, result.Describe()).
Console.WriteLine(result.Ok ? "PASS" : result.Describe());
```

`RunFileAsync(path)` is the file-based equivalent; `Lint(body)` parses without executing and returns diagnostics only.

## Documentation

- 📖 **[The `.visc` language](https://github.com/2sky/Vidyano.Core/blob/main/docs/visc-language.md)** — the complete reference: every verb, `EXPECT` subject, control flow, and the determinism model.
- 🔌 **[Embedding guide](https://github.com/2sky/Vidyano.Core/blob/main/docs/embedding.md)** — options, registering `TOOL` handlers, and capturing live run artifacts for verification.
- 🖥️ **[CLI guide](https://github.com/2sky/Vidyano.Core/blob/main/docs/cli.md)** — the `vidyano` command-line runner.

## License

MIT — see [LICENSE](https://github.com/2sky/Vidyano.Core/blob/main/LICENSE).
