# Vidyano.Script

[![NuGet](https://img.shields.io/nuget/v/Vidyano.Script.svg)](https://www.nuget.org/packages/Vidyano.Script/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Engine for the `.visc` Vidyano scripting format. Parses, interprets, and drives a real `Vidyano.Client` session against a backend — for tests, automation, agents, and reproducing customer flows from a small script file.

If you just want to run scripts from the command line, install the companion tool [`Vidyano.Script.Tool`](https://www.nuget.org/packages/Vidyano.Script.Tool/) instead. This package is for embedding the engine in your own .NET process.

## Installation

```bash
dotnet add package Vidyano.Script
```

Targets `net8.0` and `net10.0`. Pulls in `Vidyano.Core` transitively.

## Quick example

```csharp
using Vidyano.Script;

var script = """
    @app = "https://demo.vidyano.com/"
    SIGN-IN admin / vidyano

    OPEN MenuItem Home/Customers
    SEARCH ""
    EXPECT TotalItems >= 1

    OPEN-ROW 0
    EXPECT NavStack.Top.Kind = "PersistentObject"
    """;

var result = await VidyanoScript.RunAsync(script);

// `result.Ok` is the pass/fail bit. `result.Describe()` renders a plain-text report — the source,
// a pass/fail/skip tally, and each failed statement's diagnostic — ready for a log or an assertion
// message, e.g. `Assert.That(result.Ok, Is.True, result.Describe())`.
Console.WriteLine(result.Ok ? "PASS" : result.Describe());
```

`RunFileAsync(path)` is the file-based equivalent. `Lint(body)` parses without executing and returns diagnostics only.

## What's in a `.visc`?

A `.visc` script is a sequence of **verbs** that drive a Vidyano session, with **EXPECT** assertions checking observable state at each step. Verbs map 1:1 to user actions a frontend would perform:

- `SIGN-IN <user> / <password>` — authenticate. `SIGN-IN FROM ENV` reads `VIDYANO_USER` / `VIDYANO_PASSWORD` from the environment instead (loud-fails if either is unset). `SIGN-IN @name = <user> / <password>` (the `=` is required) opens a **named** session that mints its own cookie jar / identity — so admin-vs-tenant permission flows never share auth.
- `USE @name` — switch the active session to a named one. All observable state (nav stack, current PO/Query, client operations, `@session`) swaps atomically. Only named sessions are addressable — the default session is unreachable by name, so name every session you switch between. An unknown name fails with a `resolve-session` diagnostic and a suggestion.
- `SIGN-OUT` / `SIGN-OUT @name` — faithful `viSignOut` (real server action + auth clear) against the current (bare) or named session. A named (minted) session is then disposed and removed; the default session is left present-but-disconnected. If the signed-out session was active, the active session falls back to the default slot. Re-`SIGN-IN @name` re-authenticates an existing named session in place and does **not** reset its nav state — to get a clean slate, `SIGN-OUT @name` then `SIGN-IN @name`.
- `OPEN MenuItem <path>` — navigate to a query.
- `OPEN-ROW <index>` — drill into a row by position.
- `OPEN-ROW WHERE <column> = <value>` — drill into the single row matched by a column value (strict — 0 or >1 matches fail; value in service-string form, like `SET`). Addresses a fixture by reference instead of a brittle row index.
- `FOLLOW <attr> [AS @handle]` — navigate from a reference attribute on the current PO to the PO it points at, pushing a PO frame (the .visc equivalent of the web client's "open" affordance next to a reference field). The attribute must be a reference; FOLLOW honors the same `CanOpen` gate the UI uses and loads the target the same way `OPEN-ROW` does. It does *not* change the reference — that's `SET`.
- `SELECT-ROWS <ALL | ALL EXCEPT <index|WHERE …> | NONE | <index> | WHERE <column> = <value>>` — set the current query's selection so a selection-gated `ACTION` (e.g. `Delete`) can run. `ALL` is **server-side select-all** — it sets `Query.AllSelected` (serialized as `allSelected`) so the action operates on every row the query matches on the backend, regardless of what is loaded; the explicit selection stays empty. `ALL EXCEPT <index|WHERE>` is **inverse** selection — the addressed rows become the server-side exclusion set. `<index>` / `WHERE` (non-strict) / `NONE` set explicit rows and clear the flag. Replaces the selection (never accumulates), never pushes a frame; `CanExecute` flips automatically. Optional leading `Detail "<name>"` targets a detail query.
- `SEARCH <text>` — text-search the current query. `SEARCH Detail "<name>"` retargets a named detail query on the current PO instead, searching it in place (no nav frame, no selection change) to **load** its rows — so a following `EXPECT Detail … TotalItems` sees server-created state. Omit the text to load with an empty filter; quote `"Detail"` to search the current query for that literal word.
- `EDIT` / `CANCEL` / `SAVE` — standard PO edit lifecycle.
- `SET <attribute> = <value>` — change an attribute (incl. reference SET semantics).
- `ACTION <action>` — invoke an action by name. An optional leading `Detail "<name>"` clause targets a detail query on the current PO (`PersistentObject.Queries`) instead of the nav-stack query: the action resolves from — and posts against — that detail query (parent stays the master PO), so a `SELECT-ROWS Detail "<name>"` selection has a verb to act on.
- `SAVE EXPECTING ERROR` / `ACTION <action> EXPECTING ERROR` — assert the **negative path**: the verb passes only if the server returns an error notification, and **fails if it unexpectedly succeeds**. A client-side guard or transport fault still fails normally — the suffix only absorbs the server's error notification. The notification stays on the current PO, so a following `EXPECT Notification …` pins the exact message.
- `CONFIRM "<label>"` / `CONFIRM ID <index>` — answer a **server retry dialog**. When an action handler calls `Manager.Current.RetryAction(...)` mid-execution (the web client's `onRetryAction`), the paused `ACTION`/`SAVE` surfaces as a modal nav frame (`NavStack.Top.Kind = "RetryDialog"`). While it's open the script is frozen to `CONFIRM` / `SET` / `EXPECT` (anything else trips `state-retry-pending`), mirroring the `@initial` gate. `CONFIRM` picks one of the offered options by label or index and resumes the action; if the retry carried a PersistentObject for extra input, `SET` its attributes first and the edits ride back with the confirmation. Read the open dialog with `EXPECT RetryDialog.Title` / `.Message` / `.Options` (the options are comma-joined for display — use `CONTAINS "Label"` to check membership).

### Reserved `@session` variable

`Client.Session` is reachable as `@session.<attr>` in any position — SET target, value, `EXPECT`, `{{…}}` interpolation — without leaving the current nav frame:

```visc
SET @session.Customer = LOOKUP "Name:Smith"
SET Year = @session.CurrentYear
EXPECT @session.Customer CONTAINS "Smith"
```

The names `session`, `user`, `application` are reserved; `@session = …` is a parse error. `@user` / `@application` parse but produce a runtime diagnostic until wired up.

EXPECT supports nav-stack state (`NavStack.Depth`, `NavStack.Top.Kind`, `NavStack.Top.Name`, `NavStack.Top.IsDialog`), query state (`TotalItems`, `Selection.Count`, `Selection.AllSelected`, `IsInEdit`), notification state, and the `ClientOperation` queue:

```visc
EXPECT NavStack.Depth = 2
EXPECT NavStack.Top.Kind = "PersistentObject"
EXPECT IsInEdit = true

EXPECT ClientOperation ShowMessageBox
EXPECT ClientOperation ShowMessageBox CONTAINS "saved"
EXPECT ClientOperation Refresh IS NULL
```

Asserting a rejection — `EXPECTING ERROR` flips the verb's polarity, then `EXPECT Notification` pins the message:

```visc
SAVE EXPECTING ERROR
EXPECT NotificationType = "Error"
EXPECT Notification MATCHES "already exists"
```

### EXPECT on metadata

`EXPECT` also reaches the round-tripped server metadata — `Tag`, `Metadata`, `NavigationHints`, and `TypeHints` — on attributes, the current PO, the current Query, and individual Query columns:

```visc
EXPECT Attribute FirstName TYPE = "String"
EXPECT Attribute FirstName TYPEHINT maxLength = "50"
EXPECT Attribute FirstName TAG IS NULL

EXPECT PO.Type = "Customer"
EXPECT PO.Metadata.brand = "vidyano"
EXPECT PO.NavigationHints.target = "Detail"

EXPECT Query.Name = "Customers"
EXPECT Query.PersistentObject.Type = "Customer"
EXPECT Query.Columns[FirstName].Label = "First name"
```

Missing bag keys produce `null` — assert with `IS NULL` / `IS NOT NULL`. The legacy `EXPECT Query LABEL = "…"` form still works.

### TOOL — host-registered logic

`TOOL <name> [k=v, …] [-> @var]` calls a host-registered C# delegate. Use it for the bits that don't fit the verb grammar — DB lookups, startup/teardown snippets, environment probes — without embedding C# in the script:

```csharp
options.Tools["lookup-customer"] = async (ctx, args, ct) =>
{
    var email = (string?)args["email"];
    var id    = await myDb.FindCustomerIdAsync(email, ct);
    ctx.Variables["lookupAt"] = DateTime.UtcNow.ToString("o");
    return ScriptToolResult.Value(id);
};
```

```visc
TOOL warmup
TOOL lookup-customer email="alice@example.com" -> @cust
SEARCH "CustomerId:{{cust}}"
EXPECT TotalItems >= 1
```

Argument values participate in the regular expression grammar (literals, `{{vars}}`, `@session.X` reads). A throw becomes a `tool-error` diagnostic with the call site; cancellation flows through the host-supplied `CancellationToken`.

For CLI-driven runs, implement `IVidyanoScriptToolPack` and load the DLL with `vidyano run … --tools <path.dll>` — see the [Vidyano.Script.Tool README](https://www.nuget.org/packages/Vidyano.Script.Tool/) for the plugin contract.

### Capturing run artifacts for verification

A `.visc` run drives a live session; an in-process host often wants to grab a specific `PersistentObject` or `Query` *as it existed mid-run* and assert on it afterward (a separate "verify" step). The `TOOL` context exposes the live session, so a registered tool can hand the instance straight back to your host through a closure:

```csharp
PersistentObject? captured = null;

var options = new VidyanoScriptOptions
{
    Tools =
    {
        ["capture"] = (ctx, args, ct) =>
        {
            captured = ctx.Session.CurrentPo;        // or ctx.Session.CurrentQuery
            return Task.FromResult(ScriptToolResult.Ok);
        },
    },
};

await VidyanoScript.RunFileAsync("flow.visc", options);
// `captured` is the live PO from the run — assert on it here.
```

```visc
OPEN MenuItem Home/Customers
OPEN-ROW 0
TOOL capture            ## stash the current PO into the host
```

`ctx.Session.CurrentPo` / `CurrentQuery` are the same instances the verbs operate on. To pass a value *into the script* instead of the host, return `ScriptToolResult.Value(obj)` and bind it with `TOOL capture -> @snapshot`; the variable table holds arbitrary objects, so a later tool in the same run can read `ctx.Variables["snapshot"]`.

Two limits worth knowing:

- **`ScriptResult` does not expose the variable table** — the tool closure above is how you hand an object to the host; you cannot read script `@vars` off the result.
- **A live `PersistentObject` cannot cross into a separate process/run** — it is an object graph bound to this session's `Client`. For a cross-run verify, capture its `Id` and re-open it by reference there (`OPEN-ROW WHERE Id = {{customerId}}`).

If your host drives `Vidyano.Core` directly (outside this engine — e.g. a test driver that owns the `Client`), a `Hooks` subclass is another capture point: override the hook that fires for the objects you care about and record them, the same way the engine's own hooks buffer client operations.

## Deterministic regression scripts

A script you check in has to pass on a teammate's machine with different data. These features let one `.visc` gate itself, pin its own randomness, and assert with patterns instead of exact values:

```visc
### Built-ins evaluate per reference (capture to freeze); Now/Seed pin them below.
EDIT
SET Name      = "Acme {{@uuid}}"        ## {{...}} resolves inside "..." too
SET CreatedOn = "{{@today}}"
EXPECT Name MATCHES "^Acme [0-9a-fA-F-]{36}$"

### REQUIRES gates the body: if it doesn't hold, the rest is skipped (not failed).
REQUIRES TotalItems >= 1
REQUIRES TOOL seed-db

CLEANUP                                  ## statements below always run, even after a skip
ACTION Delete
```

- **`REQUIRES <assertion>`** — reuses the full `EXPECT` grammar. Holds → continue; unmet or unevaluable → skip the rest of the body with a `state-requires-unmet` diagnostic (a skip, **not** a failure). **`REQUIRES TOOL <name>`** gates on a registered tool.
- **`CLEANUP`** — a marker; everything after it runs even when the body was skipped, so teardown never gets stranded.
- **Built-in vars** `{{@today}} {{@now}} {{@uuid}} {{@random}}` — evaluated on each reference, mirroring `DateTime.Now` / `rng.Next()` in C#. `Seed` fixes the `@uuid`/`@random` sequence (independent streams; each reference draws the next value); `Now` anchors the clock, which then flows by real elapsed time. Capture into a variable (`@id = {{@uuid}}`) to freeze a value for reuse.
- **Environment values** — `{{env:NAME}}` reads an environment variable, **loud-failing if unset** (never a silent empty value); `{{env:NAME ?? "fallback"}}` makes it optional. CLI `--env-file <path>` loads literal `KEY=VALUE` pairs from a `.env` (full-line `#` comments and an optional `export ` prefix; no quote stripping or `${VAR}` expansion) that back `{{env:NAME}}` and `SIGN-IN FROM ENV`, **shadowing the process environment** (repeatable; last file wins per key). `VidyanoScriptOptions.EnvironmentPrefix` (CLI `--env-prefix VIDYANO_`) bulk-binds `VIDYANO_*` **process** env vars into plain `{{NAME}}` vars with the prefix stripped (not fed by `--env-file`), and an explicit `--var` / `Variables` entry always wins. Hosts can inject `VidyanoScriptOptions.EnvLookup` directly for hermetic runs — the seam `--env-file` composes.
- **`EXPECT … MATCHES "<regex>"`** — regex assertion on the subject's string form (1s ReDoS-guard timeout; a malformed pattern is a clean failure, null never matches).
- **In-string interpolation** — `{{…}}` holes resolve inside `"…"` literals using the same machinery as a standalone `{{…}}`, so values compose (`"Acme {{@uuid}}"`). Escape a literal brace as `\{`.

## Modes

A `@mode` directive (or `VidyanoScriptOptions.Mode`) selects how strictly the engine guards observable state:

- **`navigation`** (default) — verbs walk the UI the way a user would; nav-stack and dialog rules are enforced.
- **`audit`** — every observable side-effect is checked against the previous snapshot; useful for regression scripts.
- **`direct`** — guards relaxed; lets scripts poke state directly. Reserved for setup/teardown.

## Programmatic options

```csharp
var options = new VidyanoScriptOptions
{
    RemoteUri = "https://localhost:44353/",   // overrides script's @app
    Mode = ScriptMode.Audit,
    AcceptAnyServerCertificate = true,        // dev certs only
    Variables = { ["customerId"] = "abc-123" }, // pre-seed @vars
    Now = DateTimeOffset.Parse("2026-05-26T00:00:00Z"), // pin {{@today}}/{{@now}}
    Seed = 1234,                              // pin {{@uuid}}/{{@random}}
};

var result = await VidyanoScript.RunFileAsync("regression.visc", options);
```

## License

MIT — see [LICENSE](https://github.com/2sky/Vidyano.Core/blob/main/LICENSE).
