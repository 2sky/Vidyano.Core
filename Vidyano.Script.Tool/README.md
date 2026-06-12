# Vidyano.Script.Tool

[![NuGet](https://img.shields.io/nuget/v/Vidyano.Script.Tool.svg)](https://www.nuget.org/packages/Vidyano.Script.Tool/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

`vidyano` — a .NET global tool for running `.visc` scripts against a Vidyano backend.

A `.visc` file is a short, declarative script that drives a real Vidyano session — sign in, open queries, edit rows, run actions — and asserts on the observable state at each step. It's the smallest unit for regressing a customer flow, scripting a smoke test, or letting an agent exercise an app.

To embed the engine in your own .NET code instead, use [`Vidyano.Script`](https://www.nuget.org/packages/Vidyano.Script/).

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

## Commands

```
vidyano run   <file.visc> [options]   Execute a script.
vidyano lint  <file.visc>             Parse-check without executing.
vidyano repl  [options]               Start an interactive .visc REPL.
vidyano help  [verbs]                 Show help. 'verbs' lists every .visc verb.
```

Run `vidyano help verbs` for the full grammar straight from the engine.

## Documentation

- 🖥️ **[CLI guide](https://github.com/2sky/Vidyano.Core/blob/main/docs/cli.md)** — every flag, exit codes, `--json` for CI/agents, and external tool packs (`--tools`).
- 📖 **[The `.visc` language](https://github.com/2sky/Vidyano.Core/blob/main/docs/visc-language.md)** — the complete verb and `EXPECT` reference.
- 🔌 **[Embedding guide](https://github.com/2sky/Vidyano.Core/blob/main/docs/embedding.md)** — drive scripts from your own .NET process instead of the CLI.

## License

MIT — see [LICENSE](https://github.com/2sky/Vidyano.Core/blob/main/LICENSE).
