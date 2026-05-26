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

## CI / agent use

`--json` emits NDJSON suitable for piping into a log collector. Each verb produces one event with its result, timing, and any guard violation. Pair with an exit-code check:

```bash
vidyano run regression.visc --json > run.ndjson || echo "regression failed: $?"
```

## License

MIT — see [LICENSE](https://github.com/2sky/Vidyano.Core/blob/main/LICENSE).
