# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Restore NuGet packages
dotnet restore

# Build all target frameworks (Debug)
dotnet build

# Build with Release configuration (automatically generates NuGet package)
dotnet build --configuration Release

# Clean build artifacts
dotnet clean
```

## Architecture Overview

Vidyano.Core is a portable .NET client library for Vidyano backend applications following MVVM architecture:

### Core Components
- **Client.cs**: Central hub for backend communication and session management. Contains extensive internationalization support and handles all server interactions.
- **NotifyableBase.cs**: Foundation for property change notifications via INotifyPropertyChanged
- **ViewModelBase.cs**: Base class for view models, extending NotifyableBase with additional functionality
- **PersistentObject.cs**: Represents data entities with server synchronization capabilities
- **Query.cs**: Manages data queries and result sets from the backend

### Command System
Actions follow a hierarchical command pattern:
- **ActionBase.cs**: Abstract base for all actions
- **QueryAction.cs**: Query-specific operations
- Specialized actions in `ViewModel/Actions/` handle CRUD operations

### Key Architectural Patterns
1. **Observable Pattern**: All view models inherit from NotifyableBase for data binding
2. **Async-First Design**: Extensive use of async/await throughout Client operations
3. **Strong Typing**: Generic implementations for type safety (e.g., PersistentObject<T>)
4. **Immutable Collections**: Query results use immutable collections for thread safety

## Multi-Target Framework Support

The project targets multiple frameworks - ensure compatibility when adding features:
- .NET Standard 2.0 (for maximum compatibility)
- .NET 8.0 (modern target)

Use conditional compilation when necessary:
```csharp
#if NETSTANDARD2_0
// .NET Standard 2.0 implementation
#else
// .NET 8.0 implementation
#endif
```

## Code Standards

- **Language Version**: C# 13
- **Async/Await**: ConfigureAwait(false) is mandatory (enforced at error level)
- **Code Analysis**: Follows Microsoft.Managed.Recommended.Rules ruleset
- **Naming**: Follow existing patterns - public members use PascalCase, private fields use camelCase

## Version Management

When updating versions:
1. Update `<Version>` in Vidyano.Core.csproj
2. Version format: Major.Minor.Patch (currently 5.51.0)

## Development Notes

### Demo Application
The solution includes a Demo console application that connects to https://demo.vidyano.com. Use this to:
- Test library functionality
- Verify API changes work correctly
- Demonstrate usage patterns to developers

To run the demo:
```bash
cd Demo
dotnet run
```

### No Test Project
This codebase currently lacks automated tests. When implementing new features:
- Ensure backward compatibility across all target frameworks
- Test manually against different .NET runtimes
- Consider impact on existing client applications
- Use the Demo app to verify basic functionality

### Internationalization
Client.cs contains translations for 30+ languages. When modifying error messages or user-facing strings, maintain consistency across all language dictionaries in the Client constructor.

### NuGet Package Generation
The project automatically generates NuGet packages on Release builds. Package metadata is defined in the .csproj file.

## Companion packages: Vidyano.Script + Vidyano.Script.Tool

The repository also ships two scripting packages built on top of Vidyano.Core. They live in this solution and ship from this repo.

### Vidyano.Script (library)
- Path: `Vidyano.Script/`
- Public façade: `Vidyano.Script.VidyanoScript` — `RunFileAsync(path, options)`, `RunAsync(body, options)`, `Lint(body)`.
- Engine layers: `Parsing/` (lexer + parser), `Diagnostics/` (errors + suggester), `Runtime/` (`VidyanoSession`, `Interpreter`, guards, `ScriptHooks`).
- `VidyanoSession` drives a real `Vidyano.Client` — there is no mocking. Whatever a `.visc` script does, a frontend could have done.
- `ScriptHooks` pins the session to `Environment="Web"` + `environmentVersion=3` so the server applies default filters and emits `IncludeFilters` exactly like a v4 browser session. It also forwards `Hooks.OnClientOperation` into the session's per-verb buffer (`_lastOperations`) and full-history buffer (`_allOperations`) — this is the pattern documented in PR #6's reply: hosts that want operation history record it in their `Hooks` subclass, not on `Client`.

### Vidyano.Script.Tool (CLI)
- Path: `Vidyano.Script.Tool/`
- Packs as a dotnet tool: `<PackAsTool>true</PackAsTool>`, command name `vidyano`.
- Subcommands: `run` (execute), `lint` (parse-only), `repl` (interactive), `help` (`help verbs` lists every `.visc` verb).
- Shared options: `--app`, `--var k=v`, `--mode navigation|audit|direct`, `--tools <path.dll>` (repeatable; loads `IVidyanoScriptToolPack` plugins), `--json` (NDJSON), `--verbose`, `--insecure` (dev TLS only).
- Exit codes: `0` ok, `1` failed, `2` parse error, `3` connection error, `64` usage.

### `.visc` quick reference

| Verb | Effect |
|---|---|
| `SIGN-IN <user> / <pwd>` | Authenticate (optionally `LANGUAGE xx-XX`). |
| `SIGN-IN FROM ENV` | Authenticate using `VIDYANO_USER` / `VIDYANO_PASSWORD` from the environment (loud-fails when either is unset). Optional `LANGUAGE xx-XX`. |
| `SIGN-IN @name = <user> / <pwd>` | Open a **named** session (the `=` is required). Mints its OWN cookie jar / identity, so it never shares auth with the default or any other named session. Re-`SIGN-IN @name` re-auths the existing slot in place (no nav-state reset). |
| `USE @name` | Switch the active session to a named one — all observable state (nav stack, current PO/Query, client operations, `@session`) swaps atomically. Only named sessions are addressable (the default `""` session is unreachable by name); an unknown name fails with `resolve-session` + a suggestion. |
| `SIGN-OUT` / `SIGN-OUT @name` | Faithful `viSignOut` (real server action + auth clear) against the current (bare) or named session. A named (minted) session is then disposed + removed; the default session is left present-but-disconnected. If the signed-out session was active, the active session falls back to the default slot. |
| `OPEN MenuItem <path>` | Push a Query frame on the nav stack. |
| `OPEN-ROW <i>` | Push a PO frame from row `i` of the top Query (by index). |
| `OPEN-ROW WHERE <col> = <value>` | Push a PO frame from the single row whose `<col>` equals `<value>` — addresses a fixture by reference, not index. Strict: 0 or >1 matches fail. Value is service-string form (same convention as `SET`); only `=` is supported. |
| `OPEN-ROW Detail "<name>" <index\|WHERE …>` | Select the row from the named detail query on the current PO (`PersistentObject.Queries`) instead of the current Query. The `Detail` clause is orthogonal to the positional/`WHERE` choice. |
| `SELECT-ROWS <ALL\|ALL EXCEPT <i\|WHERE …>\|NONE\|<i>\|WHERE <col> = <value>>` | Set the current query's selection so a selection-gated `ACTION` (e.g. `Delete`) can run. `ALL` is server-side select-all: sets `Query.AllSelected` (serialized as `allSelected`) so the action operates on every matching row on the backend, regardless of what's loaded — `SelectedItems` stays empty. `ALL EXCEPT <index\|WHERE>` is inverse selection: the addressed rows become the server-side exclusion set. `<i>` / `WHERE` (non-strict) / `NONE` set explicit rows and clear the flag. Replaces the selection (never accumulates), never pushes a nav frame; `CanExecute` flips automatically (with `ALL`, the rule sees `TotalItems` minus exclusions). Optional leading `Detail "<name>"` targets a detail query, orthogonal to the target. |
| `GO-BACK` | Pop the top navigation frame, revealing the one beneath (the browser back button). Refuses when the top is a PO in edit (SAVE or CANCEL first) and when already at the root frame. |
| `FOLLOW <attr> [AS @h]` | Navigate from a reference attribute on the current PO to the PO it points at, pushing a PO frame — the .visc equivalent of the web client's "open" affordance next to a reference field. The attribute must be a reference (`PersistentObjectAttributeWithReference`); FOLLOW honors the same `CanOpen` gate (a non-empty reference the signed-in user may read) and loads the target the same way `OPEN-ROW` loads a query row. It does **not** change the reference — that's `SET`. Composes after `OPEN-ROW` (open an order row, then `FOLLOW Customer`). |
| `SEARCH <text>` / `SEARCH Detail "<name>" [text]` | Text-search the current Query in place (no stack change). An optional leading `Detail "<name>"` retargets a named detail query on the current PO — searching it in place (no nav frame, no selection change) to **load** its rows so a following `EXPECT Detail … TotalItems` sees server-created state; omit the text to load with an empty filter. A bare identifier `Detail` is the keyword; quote it (`SEARCH "Detail"`) to search the current query for the literal word. |
| `EDIT` / `CANCEL` / `SAVE` | Standard PO edit lifecycle. SAVE pops + lets owner-driven refresh fire. |
| `SET <attr> = <value>` | Change an attribute; reference SET resolves through lookup. |
| `ACTION <action>` | Invoke an action by name. Optional leading `Detail "<name>"` targets a detail query on the current PO (`PersistentObject.Queries`) instead of the nav-stack query — the action resolves from and executes against that detail query (parent stays the master PO), so a `SELECT-ROWS Detail "<name>"` selection has a verb to act on. |
| `SAVE EXPECTING ERROR` / `ACTION <action> EXPECTING ERROR` | Trailing suffix on the fallible verbs that asserts the **negative path**: the verb passes iff the server returns an error notification (`assert-notification-error`), and **fails if it unexpectedly succeeds** (`assert-expected-error`). Only that server notification is absorbed — a client-side guard (e.g. `guard-edit-mode-required` from SAVE before EDIT, or `guard-action-not-available`) or transport fault still fails. The notification stays on the current PO, so a following `EXPECT Notification …` / `EXPECT NotificationType = "Error"` pins the message. Composes with every ACTION form (`= option`, `(params)`, `Detail "<name>"`) and with `SAVE @initial`. |
| `EXPECT <state>` | Assert on `NavStack.*`, `TotalItems`, `Selection.Count`, `Selection.AllSelected`, `IsInEdit`, `ClientOperation <type>`, attributes, notifications. `Selection.AllSelected` is the server-side select-all flag (`Query.AllSelected`); `Selection.Count` stays literal (0 for pure all, the exclusion count for inverse). Metadata forms: `Attribute X TYPE/TAG/TYPEHINT <k>`, `PO.<prop>` / `PO.Metadata.<k>` / `PO.NavigationHints.<k>`, `Query.<prop>` / `Query.Metadata.<k>` / `Query.NavigationHints.<k>` / `Query.PersistentObject.<prop>` / `Query.Columns[<name>].<prop>`. `Selection.Count` / `Selection.AllSelected` (and `TotalItems` / `Query.*`) are `Detail "<name>"`-redirectable. |
| `TOOL <name> [k=v, …] [-> @var]` | Call a registered C# delegate. Named args only; throws become `tool-error` diagnostics. In-process: register on `VidyanoScriptOptions.Tools`. From the CLI: implement `IVidyanoScriptToolPack` in a DLL and pass `--tools <path.dll>`. |
| `EXPECT Detail "<name>" <query-subject>` | Target a detail query on the current PO for query-family subjects (`TotalItems` / `Query.*`). Reads what the detail Query holds in memory (no forced search) — a detail is lazy, so load it first with `SEARCH Detail "<name>"` if it hasn't been opened (e.g. to see rows a SAVE created server-side). |
| `EXPECT <lhs> MATCHES "<regex>"` | Regex assertion on the subject's string form (1s ReDoS-guard timeout; null never matches). |
| `REQUIRES <expect-subject> <op> <value>` | Precondition gate reusing the full EXPECT grammar. Holds → pass + continue. Unmet/unevaluable → skip the rest of the body (`state-requires-unmet`, not a failure). |
| `REQUIRES TOOL <name>` | Capability gate; skips the body unless a tool of that name is registered. |
| `CLEANUP` | Marker; statements after it always run, even when the body was skipped by an unmet `REQUIRES`. |
| Built-in vars `{{@today}} {{@now}} {{@uuid}} {{@random}}` | Evaluated on each reference (like `DateTime.Now` / `rng.Next()` in C#) — capture into a var to freeze (`@id = {{@uuid}}`). `--seed <int>` / `.Seed` fixes the `@uuid`/`@random` sequence (independent streams, next value per reference); `--now <iso>` / `.Now` anchors the clock, which then flows by real elapsed time (so `@now` is anchored but not bit-reproducible). |
| `"... {{x}} ..."` (in-string interpolation) | `{{...}}` holes resolve inside string literals (same forms as a standalone `{{...}}`), so values compose: `SET Name = "Acme {{@uuid}}"`. Escape a literal brace as `\{`. Hole-free strings are unchanged. |
| Env values `{{env:NAME}}` / `{{env:NAME ?? "fallback"}}` | Source a value from the environment in any value position. `{{env:NAME}}` is loud-on-missing (`resolve-env`); `?? <fallback>` (quoted string or bare token) supplies a default when unset. `--env-file <path>` loads literal `KEY=VALUE` pairs from a `.env` (full-line `#` comments + optional `export `; no quoting/`${VAR}` expansion) that back `{{env:NAME}}` / `SIGN-IN FROM ENV`, shadowing the process env; repeatable, last wins. `--env-prefix <prefix>` / `.EnvironmentPrefix` bulk-binds matching **process** env vars into the variable table with the prefix stripped (`VIDYANO_REGION` → `{{REGION}}`; not fed by `--env-file`); an explicit `--var` / `.Variables` binding wins. Hosts inject `.EnvLookup` for hermetic test runs (the seam `--env-file` composes). |

### Samples and regression scripts
`Vidyano.Script.Tool/samples/*.visc` — these double as regression tests:
- `nav-stack.visc` (37/37) — full nav stack semantics, SAVE side-effects, dialog frames.
- `client-ops.visc` (17/17) — `ClientOperation` EXPECT shapes against the RavenDB sample.
- `localization.visc` (17/17) — `SIGN-IN … LANGUAGE` round-trip.
- `env-web.visc` (4/4) — verifies `environmentVersion=3` unlocks server filter machinery.
- `tool-call.visc` — `TOOL` grammar (lint-only by default; pass `--tools <path.dll>` to a DLL implementing `IVidyanoScriptToolPack` to actually run it from the CLI).
- `metadata-expect.visc` — `EXPECT` shapes for `Tag` / `Metadata` / `NavigationHints` / `TypeHints` / column properties (server-shape-dependent assertions commented).
- `deterministic.visc` — lint-only demo of `REQUIRES` (TOOL + data gates), built-in vars (`{{@today}}`/`{{@now}}`/`{{@uuid}}`/`{{@random}}`), `EXPECT … MATCHES`, and a trailing `CLEANUP`.
- `expecting-error.visc` — lint-only demo of `SAVE EXPECTING ERROR` / `ACTION … EXPECTING ERROR` (asserting the negative path) plus the `EXPECT Notification` follow-up that pins the message.

Run a sample (requires the local RavenDB sample at `https://localhost:44353/` for the local-only ones):

```bash
dotnet run --project Vidyano.Script.Tool -c Debug -- run Vidyano.Script.Tool/samples/nav-stack.visc --insecure
```

### Maintenance notes
- The script projects pin to `Vidyano.Core` via `ProjectReference`, not `PackageReference` — they always build against the same-tree Core. After cutting a Core release, bump the script projects' versions in lockstep.
- `ScriptHooks.OnClientOperation` overrides the Core `Hooks` virtual; that virtual lives on `Hooks.cs` in Vidyano.Core. Changes to that signature need a coordinated update in `ScriptHooks.cs`.
- Round-trip metadata: `PersistentObject`/`Query`/`PersistentObjectAttribute` `GetServiceProperties` include `metadata`/`tag`/`navigationHints` so server-side action handlers see the same shape a browser would post. Don't strip these.
- `PackageReadmeFile` and `PackageIcon` are wired for both packages — the per-project `README.md` and the shared `Vidyano.png` ship inside the nupkg.