# Vidyano Script (.visc) — VS Code extension

Syntax highlighting and language-server support (live diagnostics, verb hover) for `.visc`
Vidyano.Script files.

The extension is a thin client. The actual language server is the `vidyano` .NET tool, launched as
`vidyano lsp` over stdio (vanilla LSP).

## Prerequisites

Install the Vidyano.Script tool (provides the `vidyano` command on your `PATH`):

```bash
dotnet tool install -g Vidyano.Script.Tool
```

Requires the .NET 10 runtime. Update with `dotnet tool update -g Vidyano.Script.Tool`.

If `vidyano` is not on your `PATH`, set the absolute path in settings:

```jsonc
// settings.json
"vidyano.path": "/absolute/path/to/vidyano"
```

## Build the .vsix (sideload)

```bash
cd editors/vscode
npm install
npm run package      # produces vscode-visc-0.1.0.vsix
```

## Install the .vsix

```bash
code --install-extension vscode-visc-0.1.0.vsix
```

Or in VS Code: Extensions view → `…` menu → **Install from VSIX…**.

## Settings

| Setting | Default | Description |
|---|---|---|
| `vidyano.path` | `""` | Absolute path to the `vidyano` executable. Empty = locate on `PATH`. |
| `vidyano.trace.server` | `"off"` | LSP trace level (`off` / `messages` / `verbose`). |

## What you get

- Syntax highlighting — TextMate grammar plus semantic tokens from `vidyano lsp`.
- Live parse/lint diagnostics from `vidyano lsp`.
- Hover docs for `.visc` verbs.

This is a v1 sideload build — not published to the Marketplace.
