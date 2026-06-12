# Embedding `Vidyano.Script`

`Vidyano.Script` is the engine behind [`.visc`](./visc-language.md): parser, interpreter, guard layer, and a programmatic session API on top of `Vidyano.Core`. Use this package to run scripts **inside your own .NET process** — test fixtures, agents, custom runners. If you just want to run scripts from the command line, use the [`vidyano` CLI](./cli.md) instead.

## Install

```bash
dotnet add package Vidyano.Script
```

Targets `net8.0` and `net10.0`. Pulls in `Vidyano.Core` transitively.

## Running a script

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

// result.Ok is the pass/fail bit. result.Describe() renders a plain-text report — the source,
// a pass/fail/skip tally, and each failed statement's diagnostic — ready for a log or an
// assertion message, e.g. Assert.That(result.Ok, Is.True, result.Describe()).
Console.WriteLine(result.Ok ? "PASS" : result.Describe());
```

The three entry points on `VidyanoScript`:

- **`RunAsync(body, options?)`** — execute a script from a string.
- **`RunFileAsync(path, options?)`** — execute a script from a file.
- **`Lint(body)`** — parse without executing; returns diagnostics only.

## Options

```csharp
var options = new VidyanoScriptOptions
{
    RemoteUri = "https://localhost:44353/",             // overrides the script's @app
    Mode = ScriptMode.Audit,                            // navigation | audit | direct
    AcceptAnyServerCertificate = true,                  // dev certs only
    Variables = { ["customerId"] = "abc-123" },         // pre-seed @vars
    Now  = DateTimeOffset.Parse("2026-05-26T00:00:00Z"),// pin {{@today}} / {{@now}}
    Seed = 1234,                                        // pin {{@uuid}} / {{@random}}
    EnvLookup = name => myConfig[name],                 // back {{env:NAME}} hermetically
};

var result = await VidyanoScript.RunFileAsync("regression.visc", options);
```

`EnvLookup` is the seam the CLI's `--env-file` composes onto — inject it for hermetic test runs so `{{env:NAME}}` and `SIGN-IN FROM ENV` resolve from your own config instead of the process environment. `EnvironmentPrefix` mirrors the CLI's `--env-prefix`.

<a id="tool"></a>
## Registering tools

The [`TOOL`](./visc-language.md#tool) verb calls a delegate you register on `options.Tools` — the in-process equivalent of a CLI [tool pack](./cli.md#tool-packs):

```csharp
options.Tools["lookup-customer"] = async (ctx, args, ct) =>
{
    var email = (string?)args["email"];
    var id    = await myDb.FindCustomerIdAsync(email, ct);
    ctx.Variables["lookupAt"] = DateTime.UtcNow.ToString("o");
    return ScriptToolResult.Value(id);          // bound by `TOOL … -> @var`
};
```

```visc
TOOL lookup-customer email="alice@example.com" -> @cust
SEARCH "CustomerId:{{cust}}"
```

A throw becomes a `tool-error` diagnostic with the call site; cancellation flows through the host-supplied `CancellationToken`.

## Capturing run artifacts for verification

A run drives a live session. To grab a specific `PersistentObject` or `Query` *as it existed mid-run* and assert on it afterward, a registered tool can hand the live instance back to your host through a closure:

```csharp
PersistentObject? captured = null;

var options = new VidyanoScriptOptions
{
    Tools =
    {
        ["capture"] = (ctx, args, ct) =>
        {
            captured = ctx.Session.CurrentPo;       // or ctx.Session.CurrentQuery
            return Task.FromResult(ScriptToolResult.Ok);
        },
    },
};

await VidyanoScript.RunFileAsync("flow.visc", options);
// `captured` is the live PO from the run — assert on it here.
```

```visc
OPEN MenuItem Home/Customers
OPEN-ROW 0
TOOL capture            ## stash the current PO into the host
```

`ctx.Session.CurrentPo` / `CurrentQuery` are the same instances the verbs operate on. Inside a `FOR-EACH ROW … AS @row` body the current loop row is mirrored into the variable table, so a tool reads the whole `QueryResultItem` as `ctx.Variables["row"]` — the way to collect rows host-side without enumerating every column as a `TOOL` argument.

Two limits worth knowing:

- **`ScriptResult` does not expose the variable table.** The tool closure above is how you hand an object to the host; you can't read script `@vars` off the result.
- **A live `PersistentObject` can't cross into a separate process/run.** It's an object graph bound to this session's `Client`. For a cross-run verify, capture its `Id` and re-open it by reference there (`OPEN-ROW WHERE Id = {{customerId}}`).

If your host drives `Vidyano.Core` directly (outside this engine — e.g. a test driver that owns the `Client`), a `Hooks` subclass is another capture point: override the hook that fires for the objects you care about and record them, the same way the engine's own hooks buffer client operations.

## Modes

See [guard modes](./visc-language.md#guard-modes) in the language reference. For regression fixtures, `ScriptMode.Audit` checks every observable side-effect against the previous snapshot.

## See also

- **[The `.visc` language](./visc-language.md)** — the complete verb and `EXPECT` reference.
- **[CLI guide](./cli.md)** — the `vidyano` command-line runner.
