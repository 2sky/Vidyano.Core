# Vidyano Script (.visc) — Visual Studio extension

Language support for `.visc` files in **Visual Studio 2022** (the Windows IDE), the sibling of the
[VS Code extension](../vscode). Like that extension, it ships **no language logic of its own** — it
locates the `vidyano` CLI tool and runs `vidyano lsp`, then bridges that stdio language server into
Visual Studio. Diagnostics and hover therefore come from the same server every other editor uses, and
the experience tracks the installed tool's version, not this extension's.

Built on the out-of-process **[VisualStudio.Extensibility](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/visualstudio.extensibility)**
SDK and its `LanguageServerProvider` API (the modern, recommended path — no MEF, no legacy `ILanguageClient`).

## Layout

| File | Purpose |
|---|---|
| `Vidyano.Visc.VS.csproj` | Project file (net8.0-windows, VisualStudio.Extensibility SDK). Builds a `.vsix`. |
| `ViscExtension.cs` | Extension entry point / identity. |
| `ViscLanguageServerProvider.cs` | Registers the `.visc` document type, locates `vidyano`, launches `vidyano lsp` over stdio. |
| `.vsextension/string-resources.json` | Localizable display-name tokens. |
| `visc.tmLanguage.json` | TextMate grammar for syntax coloring (copied verbatim from the VS Code extension). |

## Prerequisites

- **Visual Studio 2022 17.14+** with the **"Visual Studio extension development"** workload.
- The `vidyano` tool installed and reachable:
  ```
  dotnet tool install -g Vidyano.Script.Tool
  ```

## Build & debug

1. Open `Vidyano.Visc.VS.csproj` (or the solution) in Visual Studio.
2. Set it as the startup project and press **F5**. This builds the extension, deploys it to the
   **experimental instance**, and attaches the debugger.
3. In the experimental instance, open any `.visc` file. The language server starts and you get
   diagnostics + hover.

Trace the server handshake via **View → Output → "Extensions"** (the `TraceSource` logs land there).

## How the tool is located

`ResolveVidyano()` mirrors the VS Code extension's resolution order (Windows semantics):

1. `VIDYANO_PATH` environment variable, if set (full path to the executable).
2. `PATH` scan, honoring `PATHEXT`.
3. The canonical global-tools dir: `%USERPROFILE%\.dotnet\tools\vidyano.exe` (the editor's inherited
   `PATH` often lags a fresh `dotnet tool install`).

If none resolve, `.visc` files still open (with grammar coloring) but without LSP features; the remedy
is logged to the Output window.

## Packaging & publishing

```
dotnet build -c Release
```

produces the `.vsix` under `bin\Release\`. Publish it to the
[Visual Studio Marketplace](https://learn.microsoft.com/visualstudio/extensibility/walkthrough-publishing-a-visual-studio-extension)
(the `publisherName` in `ViscExtension.cs` must match your registered Marketplace publisher).

## Known gaps vs. the VS Code extension

These are deliberate scaffold cut-lines, not oversights:

- **Syntax coloring is not yet wired.** `visc.tmLanguage.json` ships in the repo but is not registered.
  The out-of-process VisualStudio.Extensibility model has no first-class TextMate-grammar contribution
  today. The **preferred** fix is to add `textDocument/semanticTokens` to the C# language server — one
  change that colors `.visc` in *every* LSP editor (VS, Rider, …), no per-editor grammar. The fallback
  is the classic VSIX TextMate `Grammars\` drop-in. Pick one before shipping.
- **No version-skew check.** The VS Code client compares `initialize`'s `serverInfo.version` against a
  minimum and warns when the tool is too old. The Extensibility SDK doesn't surface `serverInfo`, so the
  check is omitted (see the note in `OnServerInitializationResultAsync`).
- **Missing-tool UX is log-only.** A not-found `vidyano` is traced to the Output window rather than shown
  as a prompt with an install action (the VS Code extension offers "copy install command").

## Notes

- `LanguageServerProvider` is a **preview** API (`VSEXTPREVIEW_LSP`), suppressed in the provider file as
  Microsoft's own samples do. It is the documented forward path and is not expected to be removed.
- The server subcommand is `vidyano lsp`, which speaks LSP over stdio and ignores extra args (so VS's
  launcher flags don't corrupt the protocol stream).
