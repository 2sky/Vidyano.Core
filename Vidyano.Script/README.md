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

### Reserved `@session` variable

`Client.Session` is reachable as `@session.<attr>` in any position — SET target, value, `EXPECT`, `{{…}}` interpolation — without leaving the current nav frame:

```visc
SET @session.Patient = LOOKUP "Naam:Smith"
SET Year = @session.CurrentYear
EXPECT @session.Patient CONTAINS "Smith"
```

The names `session`, `user`, `application` are reserved; `@session = …` is a parse error. `@user` / `@application` parse but produce a runtime diagnostic until wired up.

EXPECT supports nav-stack state (`NavStack.Depth`, `NavStack.Top.Kind`, `NavStack.Top.Name`, `NavStack.Top.IsDialog`), query state (`TotalItems`, `IsInEdit`), notification state, and the `ClientOperation` queue:

```visc
EXPECT NavStack.Depth = 2
EXPECT NavStack.Top.Kind = "PersistentObject"
EXPECT IsInEdit = true

EXPECT ClientOperation ShowMessageBox
EXPECT ClientOperation ShowMessageBox CONTAINS "saved"
EXPECT ClientOperation Refresh IS NULL
```

### EXPECT on metadata

`EXPECT` also reaches the round-tripped server metadata — `Tag`, `Metadata`, `NavigationHints`, and `TypeHints` — on attributes, the current PO, the current Query, and individual Query columns:

```visc
EXPECT Attribute FirstName TYPE = "String"
EXPECT Attribute FirstName TYPEHINT maxLength = "50"
EXPECT Attribute FirstName TAG IS NULL

EXPECT PO.Type = "Customer"
EXPECT PO.Metadata.brand = "vidyano"
EXPECT PO.NavigationHints.target = "Detail"

EXPECT Query.Name = "Customers"
EXPECT Query.PersistentObject.Type = "Customer"
EXPECT Query.Columns[FirstName].Label = "First name"
```

Missing bag keys produce `null` — assert with `IS NULL` / `IS NOT NULL`. The legacy `EXPECT Query LABEL = "…"` form still works.

### TOOL — host-registered logic

`TOOL <name> [k=v, …] [-> @var]` calls a host-registered C# delegate. Use it for the bits that don't fit the verb grammar — DB lookups, startup/teardown snippets, environment probes — without embedding C# in the script:

```csharp
options.Tools["lookup-customer"] = async (ctx, args, ct) =>
{
    var email = (string?)args["email"];
    var id    = await myDb.FindCustomerIdAsync(email, ct);
    ctx.Variables["lookupAt"] = DateTime.UtcNow.ToString("o");
    return ScriptToolResult.Value(id);
};
```

```visc
TOOL warmup
TOOL lookup-customer email="alice@example.com" -> @cust
SEARCH "CustomerId:{{cust}}"
EXPECT TotalItems >= 1
```

Argument values participate in the regular expression grammar (literals, `{{vars}}`, `@session.X` reads). A throw becomes a `tool-error` diagnostic with the call site; cancellation flows through the host-supplied `CancellationToken`.

For CLI-driven runs, implement `IVidyanoScriptToolPack` and load the DLL with `vidyano run … --tools <path.dll>` — see the [Vidyano.Script.Tool README](https://www.nuget.org/packages/Vidyano.Script.Tool/) for the plugin contract.

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
