# Vidyano.Script

[![NuGet](https://img.shields.io/nuget/v/Vidyano.Script.svg)](https://www.nuget.org/packages/Vidyano.Script/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Engine for the `.visc` Vidyano scripting format. Parses, interprets, and drives a real `Vidyano.Client` session against a backend — for tests, automation, agents, and reproducing customer flows from a small script file.

If you just want to run scripts from the command line, install the companion tool [`Vidyano.Script.Tool`](https://www.nuget.org/packages/Vidyano.Script.Tool/) instead. This package is for embedding the engine in your own .NET process.

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

    OPEN-ROW 0
    EXPECT NavStack.Top.Kind = "PersistentObject"
    """;

var result = await VidyanoScript.RunAsync(script);

Console.WriteLine($"{(result.Success ? "PASS" : "FAIL")}: " +
                  $"{result.Steps.Count} step(s), " +
                  $"{result.Diagnostics.Count} diagnostic(s)");
```

`RunFileAsync(path)` is the file-based equivalent. `Lint(body)` parses without executing and returns diagnostics only.

## What's in a `.visc`?

A `.visc` script is a sequence of **verbs** that drive a Vidyano session, with **EXPECT** assertions checking observable state at each step. Verbs map 1:1 to user actions a frontend would perform:

- `SIGN-IN <user> / <password>` — authenticate.
- `OPEN MenuItem <path>` — navigate to a query.
- `OPEN-ROW <index>` — drill into a row.
- `SEARCH <text>` — text-search the current query.
- `EDIT` / `CANCEL` / `SAVE` — standard PO edit lifecycle.
- `SET <attribute> = <value>` — change an attribute (incl. reference SET semantics).
- `EXECUTE <action>` — invoke an action by name.

EXPECT supports nav-stack state (`NavStack.Depth`, `NavStack.Top.Kind`, `NavStack.Top.Name`, `NavStack.Top.IsDialog`), query state (`TotalItems`, `IsInEdit`), notification state, and the `ClientOperation` queue:

```visc
EXPECT NavStack.Depth = 2
EXPECT NavStack.Top.Kind = "PersistentObject"
EXPECT IsInEdit = true

EXPECT ClientOperation ShowMessageBox
EXPECT ClientOperation ShowMessageBox CONTAINS "saved"
EXPECT ClientOperation Refresh IS NULL
```

## Modes

A `@mode` directive (or `VidyanoScriptOptions.Mode`) selects how strictly the engine guards observable state:

- **`navigation`** (default) — verbs walk the UI the way a user would; nav-stack and dialog rules are enforced.
- **`audit`** — every observable side-effect is checked against the previous snapshot; useful for regression scripts.
- **`direct`** — guards relaxed; lets scripts poke state directly. Reserved for setup/teardown.

## Programmatic options

```csharp
var options = new VidyanoScriptOptions
{
    RemoteUri = "https://localhost:44353/",   // overrides script's @app
    Mode = ScriptMode.Audit,
    AcceptAnyServerCertificate = true,        // dev certs only
    Variables = { ["customerId"] = "abc-123" }, // pre-seed @vars
};

var result = await VidyanoScript.RunFileAsync("regression.visc", options);
```

## License

MIT — see [LICENSE](https://github.com/2sky/Vidyano.Core/blob/main/LICENSE).
