# Vidyano Script (.visc) — Visual Studio extension

Language support for `.visc` files in **Visual Studio 2022 (17.14+) and 2026** (the Windows IDE), the sibling of the
[VS Code extension](../vscode). Like that extension, it ships **no language logic of its own** — it
locates the `vidyano` CLI tool and runs `vidyano lsp`, then bridges that stdio language server into
Visual Studio. Diagnostics, hover, and semantic-token syntax coloring therefore come from the same server every other editor uses, and
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
| `visc.tmLanguage.json` | TextMate grammar (verbatim copy from the VS Code extension). **Not** registered in VS — coloring comes from the server's semantic tokens (see below); kept as the cross-editor reference. |

## Prerequisites

- **Visual Studio 2022 (17.14+) or 2026** with the **"Visual Studio extension development"** workload.
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

If none resolve, `.visc` files still open but as **plain text** — no coloring, diagnostics, or hover,
since all of those come from the server. The remedy is logged to the Output window.

## Packaging & publishing

```
dotnet build -c Release
```

produces the `.vsix` under `bin\Release\`. Publish it to the
[Visual Studio Marketplace](https://learn.microsoft.com/visualstudio/extensibility/walkthrough-publishing-a-visual-studio-extension)
(the `publisherName` in `ViscExtension.cs` must match your registered Marketplace publisher).

## Syntax coloring

Coloring is driven by the language server's **`textDocument/semanticTokens`** — the same server every
LSP editor uses — so VS, VS Code, and any future LSP host color `.visc` identically from one place, with
no per-editor grammar to maintain. Like diagnostics and hover, it needs the server running: no resolved
`vidyano` tool → no coloring. If colors don't appear, check **Output → Extensions** for the server
handshake. The bundled `visc.tmLanguage.json` is **not** registered in VS (the out-of-process model has
no TextMate-grammar contribution); it's kept only as the cross-editor reference grammar and for VS Code's
base highlighting.

## Known gaps vs. the VS Code extension

These are deliberate scaffold cut-lines, not oversights:

- **No version-skew check.** The VS Code client compares `initialize`'s `serverInfo.version` against a
  minimum and warns when the tool is too old. The Extensibility SDK doesn't surface `serverInfo`, so the
  check is omitted (see the note in `OnServerInitializationResultAsync`).
- **Missing-tool UX is log-only.** A not-found `vidyano` is traced to the Output window rather than shown
  as a prompt with an install action (the VS Code extension offers "copy install command").

## The ⓘ ".NET runtime" icon in Extension Manager

Extension Manager shows an info icon (ⓘ) on this extension — *"running on .NET 8, which is nearing
end-of-life. Supported runtimes are .NET 8, .NET 10."* This is **expected and benign**, not a defect:

- The icon appears **only on out-of-process VisualStudio.Extensibility extensions** — the ones that run
  in the managed `Microsoft.ServiceHub.Host.Extensibility` host, whose .NET runtime VS tracks against EOL.
  Classic in-process VSIX extensions (most of the Marketplace, e.g. Mads Kristensen's, which target .NET
  Framework and run inside `devenv.exe`) never show it. Ours is likely the only *modern* out-of-proc
  extension installed — it's ahead of the pack, not behind it.
- As of **VS 2026 / March 2026**, the host still runs out-of-proc extensions on **.NET 8 only**. The
  VisualStudio.Extensibility team's guidance is to keep the target at `net8.0-windows8.0` and **not**
  target higher ([microsoft/VSExtensibility#544](https://github.com/microsoft/VSExtensibility/issues/544)).
  Targeting `net10.0` makes VS try a runtime the host can't load (`Could not load file or assembly
  'System.Runtime, Version=10.0.0.0'`) and the extension fails to activate.
- **Don't "fix" the icon** by bumping `<TargetFramework>` or adding a net10 `DotnetTargetVersions` — that
  trades a cosmetic nudge for a non-loading extension.

**Revisit when** the extension host ships .NET 10 (announced as coming to VS 2026; no date). Then bump
`<TargetFramework>` to `net10.0-windows…` and the icon clears.

## Notes

- `LanguageServerProvider` is a **preview** API (`VSEXTPREVIEW_LSP`), suppressed in the provider file as
  Microsoft's own samples do. It is the documented forward path and is not expected to be removed.
- The server subcommand is `vidyano lsp`, which speaks LSP over stdio and ignores extra args (so VS's
  launcher flags don't corrupt the protocol stream).
