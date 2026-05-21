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
- Shared options: `--app`, `--var k=v`, `--mode navigation|audit|direct`, `--json` (NDJSON), `--verbose`, `--insecure` (dev TLS only).
- Exit codes: `0` ok, `1` failed, `2` parse error, `3` connection error, `64` usage.

### `.visc` quick reference

| Verb | Effect |
|---|---|
| `SIGN-IN <user> / <pwd>` | Authenticate (optionally `LANGUAGE xx-XX`). |
| `OPEN MenuItem <path>` | Push a Query frame on the nav stack. |
| `OPEN-ROW <i>` | Push a PO frame from row `i` of the top Query. |
| `SEARCH <text>` | Text-search the current Query in place (no stack change). |
| `EDIT` / `CANCEL` / `SAVE` | Standard PO edit lifecycle. SAVE pops + lets owner-driven refresh fire. |
| `SET <attr> = <value>` | Change an attribute; reference SET resolves through lookup. |
| `EXECUTE <action>` | Invoke an action by name. |
| `EXPECT <state>` | Assert on `NavStack.*`, `TotalItems`, `IsInEdit`, `ClientOperation <type>`, attributes, notifications. |

### Samples and regression scripts
`Vidyano.Script.Tool/samples/*.visc` — these double as regression tests:
- `nav-stack.visc` (37/37) — full nav stack semantics, SAVE side-effects, dialog frames.
- `client-ops.visc` (17/17) — `ClientOperation` EXPECT shapes against the RavenDB sample.
- `localization.visc` (17/17) — `SIGN-IN … LANGUAGE` round-trip.
- `env-web.visc` (4/4) — verifies `environmentVersion=3` unlocks server filter machinery.

Run a sample (requires the local RavenDB sample at `https://localhost:44353/` for the local-only ones):

```bash
dotnet run --project Vidyano.Script.Tool -c Debug -- run Vidyano.Script.Tool/samples/nav-stack.visc --insecure
```

### Maintenance notes
- The script projects pin to `Vidyano.Core` via `ProjectReference`, not `PackageReference` — they always build against the same-tree Core. After cutting a Core release, bump the script projects' versions in lockstep.
- `ScriptHooks.OnClientOperation` overrides the Core `Hooks` virtual; that virtual lives on `Hooks.cs` in Vidyano.Core. Changes to that signature need a coordinated update in `ScriptHooks.cs`.
- Round-trip metadata: `PersistentObject`/`Query`/`PersistentObjectAttribute` `GetServiceProperties` include `metadata`/`tag`/`navigationHints` so server-side action handlers see the same shape a browser would post. Don't strip these.
- `PackageReadmeFile` and `PackageIcon` are wired for both packages — the per-project `README.md` and the shared `Vidyano.png` ship inside the nupkg.