# Vidyano.Core documentation

This repository ships three .NET packages: the **client library** and two **scripting** packages built on top of it. Start here to find the right one, then follow the links into the deep docs.

## The packages

| Package | What it is | Use it when… |
|---|---|---|
| [**Vidyano.Core**](https://www.nuget.org/packages/Vidyano.Core/) | The .NET client SDK for Vidyano backends — sessions, persistent objects, queries, actions, MVVM notifications. | You're building a .NET app (desktop, service, CLI) that talks to a Vidyano backend. |
| [**Vidyano.Script**](https://www.nuget.org/packages/Vidyano.Script/) | The engine for the `.visc` scripting format — parse, lint, and run scripts that drive a real `Vidyano.Core` session. | You want to **embed** scripting in your own .NET process — test fixtures, agents, custom runners. |
| [**Vidyano.Script.Tool**](https://www.nuget.org/packages/Vidyano.Script.Tool/) | The `vidyano` .NET global tool — a CLI that runs `.visc` scripts. | You want a **command-line** runner for smoke tests, regressions, or agent-driven flows. |

`Vidyano.Script` and `Vidyano.Script.Tool` are the same engine reached two ways: in-process vs. CLI. Both drive a real session — there is no mocking, so whatever a `.visc` script does, a frontend could have done.

## Documentation

- **[Vidyano.Core usage](./core.md)** — connect a client, work with persistent objects, run queries and actions.
- **[The `.visc` language](./visc-language.md)** — the complete reference: every verb, `EXPECT` subject, control-flow construct, and the determinism model. **The single source of truth for the scripting language.**
- **[CLI guide](./cli.md)** — install and run `vidyano`; commands, flags, exit codes, tool packs.
- **[Embedding guide](./embedding.md)** — run scripts from your own .NET process; register tools, capture run artifacts.

## What a `.visc` looks like

```visc
@app = "https://demo.vidyano.com/"
SIGN-IN admin / vidyano

OPEN MenuItem Home/Customers
SEARCH ""
EXPECT TotalItems >= 1

OPEN-ROW 0
EXPECT NavStack.Top.Kind = "PersistentObject"
```

Verbs map 1:1 to user actions; `EXPECT` assertions check observable state at each step. See the [language reference](./visc-language.md) for the full story.

## Design notes

Historical design RFCs (the interface-design phase behind several features) live under [`docs/design/`](./design/) for reference — they record *why* a feature looks the way it does, and may predate later refinements.

## Other resources

- **TypeScript / JavaScript client** — [`@vidyano/core`](https://www.npmjs.com/package/@vidyano/core) on npm.
- **Editor support** — `.visc` syntax highlighting and language-server integration for [VS Code](https://github.com/2sky/Vidyano.Core/tree/main/editors/vscode) and [Visual Studio](https://github.com/2sky/Vidyano.Core/tree/main/editors/visualstudio).
- **Website** — [www.vidyano.com](https://www.vidyano.com/).
