# `vidyano` — the CLI

`Vidyano.Script.Tool` installs a .NET global tool named `vidyano` for running [`.visc` scripts](./visc-language.md) against a Vidyano backend from the command line. It's the smallest unit for regressing a customer flow, scripting a smoke test, or letting an agent exercise an app.

To embed the engine in your own .NET code instead, use [`Vidyano.Script`](./embedding.md).

## Install

```bash
dotnet tool install -g Vidyano.Script.Tool
```

Requires the .NET 10 runtime. Update with `dotnet tool update -g Vidyano.Script.Tool`.

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

Expected: `7/7 ok` — a tick for the `@app` assignment, one per verb, and each `EXPECT`.

## Commands

```
vidyano run   <file.visc> [options]   Execute a script.
vidyano lint  <file.visc>             Parse-check without executing.
vidyano repl  [options]               Start an interactive .visc REPL.
vidyano lsp                           Run the .visc language server over stdio (for editors).
vidyano help  [verbs]                 Show help. 'verbs' lists every .visc verb.
```

`vidyano help verbs` prints the full verb grammar straight from the engine's catalog — the always-current companion to [the language reference](./visc-language.md).

## Options (shared by `run` and `repl`)

| Flag | Purpose |
|---|---|
| `--app <uri>` | Base URI of the Vidyano service. Overrides `@app` in the script. |
| `--var key=value` | Pre-seed a script variable. Repeatable. |
| `--mode navigation\|audit\|direct` | Guard mode. Overrides `@mode` in the script. |
| `--tools <path.dll>` | Load an external [tool pack](#tool-packs). Repeatable. |
| `--now <iso>` | Pin `{{@today}}` / `{{@now}}` to a fixed instant (parsed as UTC). |
| `--seed <int>` | Pin `{{@uuid}}` / `{{@random}}` for reproducible runs. |
| `--env-file <path>` | Load `KEY=VALUE` pairs from a `.env`, backing `{{env:NAME}}` and `SIGN-IN FROM ENV`. Repeatable; last wins. |
| `--env-prefix <prefix>` | Bind matching process env vars into the variable table, prefix stripped (`VIDYANO_REGION` → `{{REGION}}`). An explicit `--var` wins. |
| `--json` | NDJSON output — one event per line. Pipe-friendly for CI / agents. |
| `--verbose` | Print per-statement snapshot detail. |
| `--insecure` | Bypass TLS validation. **Local dev certs only.** |

<a id="environment"></a>
### Environment

`{{env:NAME}}` reads a variable and **loud-fails if unset** (`{{env:NAME ?? "fallback"}}` makes it optional); `SIGN-IN FROM ENV` reads `VIDYANO_USER` / `VIDYANO_PASSWORD`. By default these come from the process environment. `--env-file` layers a `.env` on top (literal `KEY=VALUE`, full-line `#` comments and an optional `export ` prefix; no quote stripping or `${VAR}` expansion), **shadowing** the process environment — repeatable, last file wins per key. `--env-prefix` bulk-binds matching *process* env vars into plain `{{NAME}}` variables (not fed by `--env-file`).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Everything passed. |
| `1` | One or more assertions or guard checks failed. |
| `2` | Parse error — the script didn't make it past the lexer/parser. |
| `3` | Connection / sign-in failed before any script work happened. |
| `64` | Bad CLI usage (unknown subcommand, missing argument). |

## CI / agent use

`--json` emits NDJSON suitable for piping into a log collector. Each verb produces one event with its result, timing, and any guard violation. Pair it with an exit-code check:

```bash
vidyano run regression.visc --json > run.ndjson || echo "regression failed: $?"
```

`lint` (exit code `2` on malformed scripts) runs without a server, so it's a cheap pre-commit / PR gate — it statically catches block-balance (`END` matching) and bound-shape errors in loops.

<a id="tool-packs"></a>
## Tool packs (external C# logic)

The [`TOOL`](./visc-language.md#tool) verb calls a host-registered C# delegate. From the CLI, `--tools <path.dll>` loads an external assembly and discovers every public, non-abstract type implementing `Vidyano.Script.Runtime.IVidyanoScriptToolPack`. Each pack's `Register` is called with the same dictionary backing `VidyanoScriptOptions.Tools`, so anything a host process could register is reachable from the CLI.

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

`--tools` is repeatable — pass multiple flags to merge several DLLs. The loader uses the default load context, so plugins must build against a `Vidyano.Script` version compatible with the installed CLI (drift is the plugin author's responsibility).

## See also

- **[The `.visc` language](./visc-language.md)** — the complete verb and `EXPECT` reference.
- **[Embedding guide](./embedding.md)** — drive scripts from your own .NET process instead of the CLI.
