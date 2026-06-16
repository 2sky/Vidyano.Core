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
vidyano run   <file.visc> [options]   Execute a single script.
vidyano test  <path...> [options]     Run a suite (files/dirs/globs); aggregate exit code + reports.
vidyano lint  <file.visc>             Parse-check without executing.
vidyano repl  [options]               Start an interactive .visc REPL.
vidyano lsp                           Run the .visc language server over stdio (for editors).
vidyano help  [verbs]                 Show help. 'verbs' lists every .visc verb.
```

`vidyano help verbs` prints the full verb grammar straight from the engine's catalog — the always-current companion to [the language reference](./visc-language.md).

## Running a suite (`test`)

`vidyano run` executes one file; `vidyano test` runs **many** and rolls them up into a single pass/fail with machine-readable reports — the shape CI wants.

```bash
vidyano test tests/ --app https://demo.vidyano.com/ --report junit:results.xml
```

**Discovery.** Each positional argument is a file, a directory (recursed for `*.visc`), or a glob (`tests/**/*.visc`, `smoke/*.visc`). Matches are de-duplicated and ordered, so a run — and its reports — are deterministic. Discovering **zero** files is exit `64`, not a green run of nothing.

**Per-file outcome.** Each file ends as exactly one of: passed, failed (an assertion/guard didn't hold), skipped (every statement gated out by `REQUIRES`), timed out (`--timeout`), connection (no base URI, or a transport/sign-in failure), or parse. Each file runs in its **own session** (own cookie jar), so files don't share sign-in state.

**Aggregate exit code** is the most-blocking outcome across the suite — see [Exit codes](#exit-codes).

### `test` options

| Flag | Purpose |
|---|---|
| `--report <fmt>[:<path>]` | Emit a report. `<fmt>` is `junit`, `tap`, or `sarif`. Repeatable (emit several at once). With `:<path>` it's written to that file (parent dirs created); bare, it goes to stdout. |
| `--timeout <dur>` | Per-file wall-clock budget: `30s`, `2m`, `1h`, or `0` (off). A file that exceeds it is cancelled and recorded as a timeout. **Default: off** — the transport already caps each request at 90s, so a hung backend still surfaces; set this only to bound legitimately slow scripts. Also honored by `run`. |
| `--jobs <n>` | Run up to `n` files concurrently. **Default: 1 (serial)** — safe for suites whose scripts touch shared server fixtures. Raise it when your scripts are independent. Report order is unaffected by `--jobs`. |

All the shared options below (`--app`, `--var`, `--mode`, `--tools`, `--seed`, `--now`, `--env-file`, `--env-prefix`, `--json`, `--verbose`, `--insecure`) apply to `test` too; `--seed` / `--now` are applied per file, so a seeded suite stays reproducible regardless of `--jobs`.

### Report formats

| `--report` | Format | Granularity | For |
|---|---|---|---|
| `junit` | JUnit XML | one `<testsuite>` per file, one `<testcase>` per `###` step | GitLab/Jenkins/GitHub test dashboards |
| `tap` | TAP v13 | one test point per file | TAP consumers, simple CI |
| `sarif` | SARIF 2.1.0 | one result per failure diagnostic (file + line) | GitHub code scanning |

```bash
# Two reports at once + parallelism, fail the build on any non-green file:
vidyano test tests/ --jobs 4 --report junit:out/visc.xml --report sarif:out/visc.sarif
echo $?   # 0 ok · 1 failed/timeout · 2 parse · 3 connection · 64 no files
```

## Options (shared by `run`, `test` and `repl`)

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
| `--file-root <dir>` | Root that `SET attr = FILE "<path>"` reads resolve against and are confined to (`..`, absolute, drive-qualified paths rejected). Default: the script's directory. |
| `--json` | NDJSON output — one event per line. Pipe-friendly for CI / agents. `test` wraps each file in `file.start`/`file.end` and ends with a `suite.summary`; `lint` emits `lint.diagnostic`/`lint.summary` (previously `lint` ignored `--json`). |
| `--verbose` | Print per-statement snapshot detail. |
| `--insecure` | Bypass TLS validation. **Local dev certs only.** |

<a id="environment"></a>
### Environment

`{{env:NAME}}` reads a variable and **loud-fails if unset** (`{{env:NAME ?? "fallback"}}` makes it optional); `SIGN-IN FROM ENV` reads `VIDYANO_USER` / `VIDYANO_PASSWORD`. By default these come from the process environment. `--env-file` layers a `.env` on top (literal `KEY=VALUE`, full-line `#` comments and an optional `export ` prefix; no quote stripping or `${VAR}` expansion), **shadowing** the process environment — repeatable, last file wins per key. `--env-prefix` bulk-binds matching *process* env vars into plain `{{NAME}}` variables (not fed by `--env-file`).

<a id="exit-codes"></a>
## Exit codes

| Code | Meaning |
|---|---|
| `0` | Everything passed (skipped files are not failures). |
| `1` | One or more assertions or guard checks failed — or a file timed out. |
| `2` | Parse error — a script didn't make it past the lexer/parser. |
| `3` | Connection / sign-in failed — no base URI, or a transport failure. |
| `64` | Bad CLI usage (unknown subcommand, missing argument, no files discovered). |

For `test`, the code is the **most-blocking** outcome in the suite: any connection failure ⇒ `3`, else any parse error ⇒ `2`, else any failure or timeout ⇒ `1`, else `0`. `run` classifies its single file the same way — so a script with no base URI now correctly exits `3` (it used to report `2`).

## CI / agent use

For a whole regression suite, `vidyano test` is the entry point: point it at a directory, emit JUnit for the dashboard, and let the exit code gate the build.

```bash
vidyano test tests/ --app "$VIDYANO_URL" --report junit:results.xml
# upload results.xml as the test report; non-zero exit fails the job
```

For a single script (or streaming to a log collector), `--json` emits NDJSON — each verb produces one event with its result, timing, and any guard violation:

```bash
vidyano run regression.visc --json > run.ndjson || echo "regression failed: $?"
```

`lint` (exit code `2` on malformed scripts) runs without a server, so it's a cheap pre-commit / PR gate — it statically catches block-balance (`END` matching) and bound-shape errors in loops. Add `--json` for machine-readable lint output.

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
