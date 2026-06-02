# Vidyano.Script.Tool

[![NuGet](https://img.shields.io/nuget/v/Vidyano.Script.Tool.svg)](https://www.nuget.org/packages/Vidyano.Script.Tool/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

`vidyano` — a .NET global tool for running `.visc` scripts against a Vidyano backend.

A `.visc` file is a short, declarative script that drives a real Vidyano session — sign in, open queries, edit rows, run actions — and asserts on the observable state at each step. It's the smallest possible unit for regressing a customer flow, scripting a smoke test, or letting an agent exercise an app.

To embed the engine in your own .NET code instead, use [`Vidyano.Script`](https://www.nuget.org/packages/Vidyano.Script/).

## Install

```bash
dotnet tool install -g Vidyano.Script.Tool
```

Requires the .NET 10 runtime. Updates: `dotnet tool update -g Vidyano.Script.Tool`.

## Hello, Vidyano

```bash
cat > hello.visc <<'EOF'
@app = "https://demo.vidyano.com/"
SIGN-IN admin / vidyano

OPEN MenuItem Home/Customers
SEARCH ""
EXPECT TotalItems >= 1

OPEN-ROW 0
EXPECT NavStack.Top.Kind = "PersistentObject"
EOF

vidyano run hello.visc
```

Expected: `8/8 ok` (or similar — one tick per verb plus each EXPECT).

## Commands

```
vidyano run   <file.visc> [options]   Execute a script.
vidyano lint  <file.visc>             Parse-check without executing.
vidyano repl  [options]               Start an interactive .visc REPL.
vidyano help  [verbs]                 Show help. 'verbs' lists every .visc verb.
```

## Options (shared by `run` and `repl`)

| Flag | Purpose |
|---|---|
| `--app <uri>` | Base URI of the Vidyano service. Overrides `@app` in the script. |
| `--var key=value` | Pre-seed a script variable. Repeatable. |
| `--mode navigation\|audit\|direct` | Guard mode. Overrides `@mode` in the script. |
| `--tools <path.dll>` | Load an external tool pack. Repeatable — see [Tool packs](#tool-packs-external-c-logic) below. |
| `--now <iso>` | Pin `{{@today}}` / `{{@now}}` to a fixed instant (parsed as UTC). |
| `--seed <int>` | Pin `{{@uuid}}` / `{{@random}}` for reproducible runs. |
| `--json` | NDJSON output — one event per line. Pipe-friendly for CI / agents. |
| `--verbose` | Print per-statement snapshot detail. |
| `--insecure` | Bypass TLS validation. **Local dev certs only.** |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Everything passed. |
| `1` | One or more assertions or guard checks failed. |
| `2` | Parse error — the script didn't make it past the lexer/parser. |
| `3` | Connection / sign-in failed before any script work happened. |
| `64` | Bad CLI usage (unknown subcommand, missing argument). |

## What can a `.visc` actually do?

The full verb reference is one command away:

```bash
vidyano help verbs
```

A flavour sample — navigation stack semantics around SAVE:

```visc
@app = "https://localhost:44353/"
SIGN-IN admin / anything

OPEN MenuItem Home/Orders
EXPECT NavStack.Depth = 1

OPEN-ROW 0
EXPECT NavStack.Depth = 2
EXPECT NavStack.Top.Kind = "PersistentObject"

EDIT
SAVE
### SAVE pops the PO and auto-refreshes the underlying Query (driven by Vidyano.Core's
### OwnerQuery side-effect). EXPECT picks up the fresh row count.
EXPECT NavStack.Depth = 1
EXPECT TotalItems = 6
```

EXPECT supports nav-stack state, notification state, the `ClientOperation` queue, and arbitrary attributes on the current PO. CONTAINS / IS NULL / IS NOT NULL / NOT CONTAINS make negative assertions readable.

EXPECT also reaches round-tripped server metadata — `Tag`, `Metadata`, `NavigationHints`, `TypeHints`:

```visc
EXPECT Attribute FirstName TYPE = "String"
EXPECT Attribute FirstName TYPEHINT maxLength = "50"
EXPECT PO.Metadata.brand = "vidyano"
EXPECT Query.Columns[FirstName].Label = "First name"
```

### Multiple identities in one script

Naming a session opts into multi-identity — each named session mints its **own** cookie jar, so an admin and a tenant never share auth. The default (unnamed) session stays the zero-cost single-session convenience.

```visc
SIGN-IN @admin = admin / pass            ## the `=` is required for a named session
SIGN-IN @tenant = guest / pass           ## own cookie jar = distinct identity
USE @admin                               ## flip the active session; all state swaps atomically
OPEN MenuItem Home/Customers
ACTION Delete                            ## runs with @admin's permissions
USE @tenant
ACTION Delete EXPECTING ERROR            ## @tenant can't — its permissions never leaked
SIGN-OUT @tenant                         ## faithful viSignOut, disposed; active falls back to default
```

- Only **named** sessions are addressable by `USE` — name every session you switch between.
- Re-`SIGN-IN @name` re-authenticates the existing session in place (no nav-state reset). For a clean slate, `SIGN-OUT @name` then `SIGN-IN @name`.
- An unknown `USE @name` / `SIGN-OUT @name` is a `resolve-session` diagnostic with a "did you mean" suggestion — it never throws.

### Deterministic regression scripts

So a checked-in script passes on any machine, it can gate itself and pin its own randomness:

```visc
REQUIRES TotalItems >= 1                 ## unmet -> skip the rest (not a failure)
EDIT
SET Name = "Acme {{@uuid}}"              ## {{...}} resolves inside "..." too
EXPECT Name MATCHES "^Acme [0-9a-fA-F-]{36}$"
CLEANUP                                  ## runs even if the body was skipped
ACTION Delete
```

- `REQUIRES <assertion>` / `REQUIRES TOOL <name>` — precondition gates; an unmet gate skips the rest of the body (`state-requires-unmet`) rather than failing.
- `CLEANUP` — statements after it always run, so teardown isn't stranded by a skip.
- Built-in vars `{{@today}} {{@now}} {{@uuid}} {{@random}}` — evaluated on each reference, like calling `DateTime.Now` / `rng.Next()` in C#. `--seed` fixes the `@uuid`/`@random` sequence (each reference draws the next value); `--now` anchors the clock, which then flows by real elapsed time. To freeze a value for reuse, capture it: `@id = {{@uuid}}`.
- `EXPECT … MATCHES "<regex>"` — regex assertion (1s ReDoS guard; a bad pattern is a clean failure).

Run `vidyano help verbs` for the full grammar.

### TOOL — host-registered logic

The `TOOL` verb calls a C# delegate registered on `VidyanoScriptOptions.Tools`, with named arguments and an optional return binding:

```visc
TOOL warmup
TOOL lookup-customer email="alice@example.com" -> @cust
SEARCH "CustomerId:{{cust}}"
```

Host processes (xUnit fixture, custom CLI) register handlers directly on `options.Tools`. The `vidyano` CLI loads them from an external DLL via `--tools` — see below.

### Tool packs (external C# logic)

`--tools <path.dll>` loads an external assembly and discovers every public, non-abstract type that implements `Vidyano.Script.Runtime.IVidyanoScriptToolPack`. Each pack's `Register` is called with the same dictionary backing `VidyanoScriptOptions.Tools`, so anything a host process could register is reachable from the CLI.

Minimal plugin:

```csharp
// MyTools.csproj — net10.0, references Vidyano.Script
using Vidyano.Script.Runtime;

public sealed class MyTools : IVidyanoScriptToolPack
{
    public void Register(IDictionary<string, ScriptToolHandler> tools)
    {
        tools["lookup-customer"] = async (ctx, args, ct) =>
        {
            var email = (string?)args["email"];
            var id    = await MyDb.FindCustomerIdAsync(email, ct);
            return ScriptToolResult.Value(id);
        };
    }
}
```

```bash
dotnet build MyTools.csproj
vidyano run regression.visc --tools ./bin/Debug/net10.0/MyTools.dll
```

`--tools` is repeatable — pass multiple flags to merge several DLLs. The loader uses the default load context, so plugins must build against a Vidyano.Script version compatible with the installed CLI (drift is the plugin author's responsibility, not the CLI's).

### Reserved `@session`

`Client.Session` (the built-in Vidyano session PO) is reachable as `@session.<attr>` — write with `SET @session.X = …` (auto-enters edit on the Session PO), read with `SET Y = @session.X` or `EXPECT @session.X = …`, interpolate with `{{@session.X}}`. The names `session`, `user`, and `application` are reserved by the engine — `@session = …` is a parse error.

## CI / agent use

`--json` emits NDJSON suitable for piping into a log collector. Each verb produces one event with its result, timing, and any guard violation. Pair with an exit-code check:

```bash
vidyano run regression.visc --json > run.ndjson || echo "regression failed: $?"
```

## License

MIT — see [LICENSE](https://github.com/2sky/Vidyano.Core/blob/main/LICENSE).
