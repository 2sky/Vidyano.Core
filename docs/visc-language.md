# The `.visc` language

A `.visc` file is a short, declarative script that drives a **real** Vidyano session — sign in, open queries, edit rows, run actions — and asserts on the observable state at each step. Every verb maps 1:1 to something a user could do in the web client, so a `.visc` script reads like a faithful transcript of a session. There is no mocking: whatever a script does, a frontend could have done.

Use it to regress a customer flow, script a smoke test, reproduce a bug from a small file, or let an agent exercise an app.

This page is the **complete language reference**. For how to *run* scripts see the [CLI guide](./cli.md) (`vidyano`) or the [embedding guide](./embedding.md) (`Vidyano.Script` in your own .NET process).

---

## A first script

```visc
@app = "https://demo.vidyano.com/"
SIGN-IN admin / vidyano

OPEN MenuItem Home/Customers
SEARCH ""
EXPECT TotalItems >= 1

OPEN-ROW 0
EXPECT NavStack.Top.Kind = "PersistentObject"
```

Two kinds of lines do the work:

- **Verbs** (`SIGN-IN`, `OPEN`, `SEARCH`, …) perform an action against the session.
- **`EXPECT`** assertions check observable state. A failed `EXPECT` fails the run; everything else is a verb that either succeeds or raises a diagnostic.

Comments start with `##` (inline) or `###` (a *step header* that groups following lines in the run report). Directives like `@app` / `@mode` configure the run.

## The execution model

A few concepts the whole language is built on:

- **The navigation stack.** `OPEN`/`OPEN-ROW`/`FOLLOW` push frames; `GO-BACK`/`SAVE` pop them. The top frame is the "current" Query or PersistentObject (PO) — the implicit target of `SEARCH`, `EDIT`, `SET`, `ACTION`, and most `EXPECT`s. This mirrors the browser's back-stack.
- **Sessions.** A script has one **default** session and any number of **named** ones, each with its own cookie jar / identity. `USE` switches which is active; all observable state swaps with it.
- **Totality.** `.visc` is **total** — every script provably halts. The only control flow is gates (`REQUIRES`/`CLEANUP`) and *bounded* loops (`REPEAT`, `FOR-EACH ROW`) whose bound is fixed before they run. There is no `WHILE`, no recursion, no arithmetic. Genuine computation belongs in a [`TOOL`](#tool) or the host. This is a deliberate constraint, not a missing feature.

---

## Sessions & authentication

### `SIGN-IN`

```visc
SIGN-IN admin / vidyano                       ## inline credentials
SIGN-IN admin / vidyano LANGUAGE fr-FR        ## pin the session language
SIGN-IN FROM ENV                              ## VIDYANO_USER / VIDYANO_PASSWORD from the environment
SIGN-IN @admin = admin / vidyano              ## a NAMED session (the `=` is required)
```

- **`FROM ENV`** reads `VIDYANO_USER` / `VIDYANO_PASSWORD` and **loud-fails if either is unset** — credentials never appear on the command line or in the script.
- **Named sessions** (`@name = …`) mint their *own* cookie jar and identity, so an admin and a tenant never share auth. Re-running `SIGN-IN @name` re-authenticates that slot **in place** (no nav-state reset); for a clean slate, `SIGN-OUT @name` then `SIGN-IN @name`.

### `USE @name`

Switch the active session. The nav stack, current PO/Query, client operations, and `@session` all swap atomically. Only **named** sessions are addressable — the default session is unreachable by name, so name every session you switch between. An unknown name fails with a `resolve-session` diagnostic and a "did you mean" suggestion.

### `SIGN-OUT` / `SIGN-OUT @name`

A faithful `viSignOut` — a real server action plus an auth clear — against the current (bare) or a named session. A named session is then disposed and removed; the default session is left present-but-disconnected. If the signed-out session was active, the active session falls back to the default slot.

### Reserved variables: `@session`, `@initial`

`Client.Session` is reachable as `@session.<attr>` in any position — `SET` target, value, `EXPECT`, `{{…}}` interpolation — without leaving the current nav frame:

```visc
SET @session.Customer = LOOKUP "Name:Smith"   ## auto-enters edit on the Session PO
SET Year = @session.CurrentYear
EXPECT @session.Customer CONTAINS "Smith"
```

The names `session`, `user`, and `application` are reserved; `@session = …` is a parse error. `@initial` surfaces the server's login-gate PO (license terms, forced 2FA, password reset) when present; until it is satisfied and cleared, non-initial verbs error with `state-initial-pending` (escape via `@mode = direct`).

---

## Navigating

| Verb | Effect |
|---|---|
| `OPEN MenuItem <path>` | Push a Query frame (e.g. `OPEN MenuItem Home/Customers`). |
| `OPEN-ROW <i>` | Push a PO frame from row `i` of the current Query. |
| `OPEN-ROW WHERE <col> = <value>` | Push a PO from the single row whose `<col>` equals `<value>`. **Strict** — 0 or >1 matches fail. Addresses a fixture by reference, not a brittle index. |
| `OPEN-ROW Detail "<name>" <i\|WHERE …>` | Select from the named detail query on the current PO instead of the current Query. The `Detail` clause is orthogonal to the index/`WHERE` choice. |
| `FOLLOW <attr> [AS @h]` | Navigate from a **reference** attribute on the current PO to the PO it points at, pushing a PO frame — the equivalent of the web client's "open" affordance next to a reference field. Honors the same `CanOpen` gate the UI uses. It does **not** change the reference (that's `SET`). |
| `GO-BACK` | Pop the top frame (the browser back button). Refuses when the top is a PO in edit (`SAVE`/`CANCEL` first) and when already at the root. |

`<value>` in a `WHERE` is in **service-string form** — the same convention as `SET`. Only `=` is supported.

```visc
OPEN MenuItem Sales/Orders
OPEN-ROW WHERE Number = "SO-1001"
FOLLOW Customer AS @cust       ## jump to the referenced Customer PO
```

## Searching

```visc
SEARCH "Acme"                  ## text-search the current Query in place (no stack change)
SEARCH Detail "OrderLines"     ## load a named detail query's rows (empty filter)
SEARCH "Detail"                ## quoted -> searches the current query for the literal word "Detail"
```

`SEARCH Detail "<name>"` retargets a detail query on the current PO, searching it in place to **load** its rows — so a following `EXPECT Detail … TotalItems` sees server-created state. A detail is lazy; load it before asserting on it.

## Selecting rows

`SELECT-ROWS` sets the current query's selection so a selection-gated `ACTION` (e.g. `Delete`, whose `SelectionRule` is `>=1`) can run. It **replaces** the selection (never accumulates) and never pushes a frame; `CanExecute` flips automatically.

```visc
SELECT-ROWS ALL                          ## server-side select-all (see below)
SELECT-ROWS ALL EXCEPT WHERE Status = "Locked"   ## inverse: the matched rows become the exclusion set
SELECT-ROWS 0                            ## one row by index
SELECT-ROWS WHERE Status = "Open"        ## by predicate; may match many (non-strict)
SELECT-ROWS NONE                         ## clear
SELECT-ROWS Detail "Lines" ALL           ## optional leading Detail clause, orthogonal to the target
```

- **`ALL` is server-side select-all** — it sets `Query.AllSelected` (serialized as `allSelected`) so the action operates on every matching row on the backend, regardless of what's loaded. `SelectedItems` stays empty; assert it with `EXPECT Selection.AllSelected = true`.
- **`ALL EXCEPT <i|WHERE>`** is inverse selection — the addressed rows become the server-side exclusion set.
- **`<i>` / `WHERE` / `NONE`** set explicit rows and clear the flag. A zero-match `WHERE` is not an error — the selection just becomes empty.

## Editing & saving

```visc
EDIT
SET Name  = "Acme Corp"
SET Owner = LOOKUP "Email:alice@example.com"   ## reference SET resolves through a lookup
SAVE
```

`EDIT` / `CANCEL` / `SAVE` are the standard PO edit lifecycle. `SAVE` pops the PO frame and lets owner-driven refresh fire (the underlying Query re-counts). `SET <attr> = <value>` changes an attribute; a reference attribute resolves its value through a lookup.

## Running actions

```visc
ACTION Approve                       ## by name
ACTION ChangeStatus = "Shipped"      ## pick an action option by label
ACTION Export (format=xlsx, all=true)   ## with named parameters
ACTION Detail "Lines" Delete         ## target a detail query on the current PO
```

An optional leading `Detail "<name>"` clause targets a detail query (`PersistentObject.Queries`) instead of the nav-stack query — the action resolves from and executes against that detail query (the parent stays the master PO), so a `SELECT-ROWS Detail "<name>"` selection has a verb to act on.

### Asserting the negative path — `EXPECTING ERROR`

```visc
SAVE EXPECTING ERROR
ACTION Delete EXPECTING ERROR
```

This trailing suffix flips the verb's polarity: it **passes only if the server returns an error notification**, and **fails if the verb unexpectedly succeeds**. Only the server's error notification is absorbed — a client-side guard (e.g. SAVE before EDIT) or a transport fault still fails normally. The notification stays on the current PO, so a following `EXPECT Notification …` pins the exact message. Composes with every `ACTION` form.

### Server retry dialogs — `CONFIRM`

When an action handler calls `Manager.Current.RetryAction(...)` mid-execution (the web client's `onRetryAction`), the paused `ACTION`/`SAVE` surfaces as a modal nav frame:

```visc
ACTION Delete
EXPECT NavStack.Top.Kind = "RetryDialog"
EXPECT RetryDialog.Title   = "Confirm"
EXPECT RetryDialog.Options CONTAINS "Yes"
CONFIRM "Yes"                         ## or: CONFIRM ID 0
```

While a dialog is open the script is **frozen** to `CONFIRM` / `SET` / `EXPECT` (anything else trips `state-retry-pending`). `CONFIRM` picks an option by label or `ID <index>` and **resumes the action**. If the retry carried a PO for extra input, `SET` its attributes first — `CurrentPo` is the retry PO while the dialog is open, so the edits ride back with the confirmation.

---

## Asserting state — `EXPECT`

`EXPECT <subject> <op> <value>` is the assertion verb. Operators: `=`, `!=`, `>`, `>=`, `<`, `<=`, `CONTAINS`, `NOT CONTAINS`, `IS NULL`, `IS NOT NULL`, and `MATCHES "<regex>"` (1s ReDoS-guard timeout; null never matches).

**Navigation & query state**

```visc
EXPECT NavStack.Depth = 2
EXPECT NavStack.Top.Kind = "PersistentObject"   ## or "Query" / "RetryDialog"
EXPECT NavStack.Top.Name = "Customer"
EXPECT TotalItems >= 1
EXPECT Selection.Count = 3
EXPECT Selection.AllSelected = true              ## the server-side select-all flag
EXPECT IsInEdit = true
```

**Notifications & client operations**

```visc
EXPECT NotificationType = "Error"
EXPECT Notification MATCHES "already exists"
EXPECT ClientOperation ShowMessageBox
EXPECT ClientOperation ShowMessageBox CONTAINS "saved"
EXPECT ClientOperation Refresh IS NULL
```

**Retry dialog** (the open server retry; `IS NULL` when none)

```visc
EXPECT RetryDialog.Title
EXPECT RetryDialog.Message MATCHES "are you sure"
EXPECT RetryDialog.Options CONTAINS "Cancel"
```

**Attributes & round-tripped metadata** — `EXPECT` reaches the server metadata (`Tag`, `Metadata`, `NavigationHints`, `TypeHints`) a browser would see:

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

Missing bag keys produce `null` — assert with `IS NULL` / `IS NOT NULL`.

**Detail redirection** — query-family subjects (`TotalItems`, `Selection.*`, `Query.*`) accept a leading `Detail "<name>"` to target a detail query on the current PO. It reads what the detail holds in memory (no forced search), so load it first with `SEARCH Detail "<name>"` if needed:

```visc
EXPECT Detail "OrderLines" TotalItems = 4
EXPECT Detail "OrderLines" Selection.Count = 1
```

---

## Control flow (and why it stays bounded)

### `REQUIRES` / `CLEANUP`

```visc
REQUIRES TotalItems >= 1        ## reuses the full EXPECT grammar
REQUIRES TOOL seed-db           ## gate on a registered tool being available
## … body …
CLEANUP                         ## everything below runs even if the body was skipped
ACTION Delete
```

`REQUIRES` is a **precondition gate**: holds → continue; unmet or unevaluable → **skip the rest of the body** with a `state-requires-unmet` diagnostic — a skip, **not** a failure. So a checked-in script can self-disable on a machine that lacks its fixtures instead of failing spuriously. `CLEANUP` is a marker; statements after it always run, so teardown is never stranded by a skip.

### `REPEAT … END`

```visc
REPEAT 5 AS @i
  ACTION New
  SET Name = "Load Test {{i}}"       ## read the index as {{i}} (no @)
  SAVE
END
```

Runs the block `<n>` times. `<n>` resolves **once at entry** to a non-negative int (`REPEAT 0` runs zero times; negative/non-int → `state-invalid-bound`). `AS @i` binds the zero-based index as a loop-scoped variable, read in the body as `{{i}}`. Each iteration restores the nav stack to its entry depth (loud-fail `state-loop-edit-left-open` if a PO is left in edit).

### `FOR-EACH ROW … END`

```visc
OPEN MenuItem Sales/Customers
SEARCH ""
FOR-EACH ROW WHERE Status = "Inactive" AS @c
  OPEN-ROW @c                        ## opens by snapshotted identity, not a live index
  ACTION Delete = "Yes, delete"
END                                  ## nav stack auto-restored to the query frame each row
```

Iterates the **currently-loaded** rows of the current query (or a named detail), optionally filtered by an equality `WHERE` (non-strict; 0 matches → zero iterations). The matching set is **snapshotted at entry by row identity**, so body mutations (e.g. `Delete`) can't shift the iteration. `AS @row` binds a loop-scoped row handle: read a cell with `@row.<col>` (or `{{@row.<col>}}`), push its PO with `OPEN-ROW @row`. The row is also mirrored into the variable table, so a `TOOL` in the body reads the whole `QueryResultItem` as `ctx.Variables["row"]`. If the server holds more rows than were loaded, a warning names the gap (no silent truncation).

Both loops nest arbitrarily. `REQUIRES`/`CLEANUP`/`###` inside a block is a parse error (gates and step headers are top-level only).

---

## Values: variables & interpolation

```visc
@id = {{@uuid}}                       ## assign a variable (capture a built-in to freeze it)
SET Name = "Acme {{id}}"              ## read with {{name}} — resolves inside "..." too
SET Code = "ACME-{{@random}}"
```

- **User variables** are assigned `@name = …` and read `{{name}}` (no `@`). Loop indices/rows (`@i`, `@row`) follow the same read form.
- **Built-ins** `{{@today}} {{@now}} {{@uuid}} {{@random}}` are evaluated **on each reference** (like `DateTime.Now` / `rng.Next()`), so capture into a variable to freeze a value for reuse. `--seed`/`Seed` fixes the `@uuid`/`@random` sequence (independent streams); `--now`/`Now` anchors the clock, which then flows by real elapsed time.
- **In-string interpolation** — `{{…}}` holes resolve inside `"…"` literals using the same machinery, so values compose. Escape a literal brace as `\{`.

### Environment values

```visc
SIGN-IN {{env:VIDYANO_USER}} / {{env:SVC_PW}}   ## loud-fail if unset
SET Owner = {{env:OWNER ?? "unassigned"}}       ## ?? = optional with fallback
```

`{{env:NAME}}` is **loud-on-missing** (`resolve-env`) — never a silent empty value; `?? <fallback>` (a quoted string or bare token) supplies a default. The [CLI](./cli.md#environment) `--env-file` and `--env-prefix` flags, and the embedding [`EnvLookup`](./embedding.md) seam, back these.

---

<a id="tool"></a>
## `TOOL` — calling into the host

`TOOL <name> [k=v, …] [-> @var]` calls a host-registered C# delegate — for the bits that don't fit the verb grammar (DB lookups, setup/teardown, environment probes) without embedding C# in the script.

```visc
TOOL warmup
TOOL lookup-customer email="alice@example.com" -> @cust
SEARCH "CustomerId:{{cust}}"
EXPECT TotalItems >= 1
```

Arguments are named only and participate in the regular value grammar (literals, `{{vars}}`, `@session.X`). A throw becomes a `tool-error` diagnostic at the call site. Register handlers on `VidyanoScriptOptions.Tools` in-process (see [embedding](./embedding.md#tool)) or load them from a DLL with `vidyano run … --tools <path.dll>` (see [CLI / tool packs](./cli.md#tool-packs)).

`REQUIRES TOOL <name>` gates a body on a tool being registered, so a script degrades to a skip rather than a failure where the tool isn't available.

<a id="guard-modes"></a>
## Guard modes — `@mode`

A `@mode` directive (or `VidyanoScriptOptions.Mode`) selects how strictly the engine guards observable state:

- **`navigation`** *(default)* — verbs walk the UI the way a user would; nav-stack and dialog rules are enforced.
- **`audit`** — every observable side-effect is checked against the previous snapshot; useful for regression scripts.
- **`direct`** — guards relaxed; lets scripts poke state directly. Reserved for setup/teardown.

---

## Verb quick reference

| Verb | One-liner |
|---|---|
| `SIGN-IN <user> / <pwd> [LANGUAGE xx-XX]` | Authenticate the default session. |
| `SIGN-IN FROM ENV` | Authenticate from `VIDYANO_USER` / `VIDYANO_PASSWORD`. |
| `SIGN-IN @name = <user> / <pwd>` | Open / re-auth a named session (own identity). |
| `USE @name` | Switch the active session. |
| `SIGN-OUT [@name]` | Faithful sign-out; named sessions are disposed. |
| `OPEN MenuItem <path>` | Push a Query frame. |
| `OPEN-ROW <i \| WHERE … \| @row> [Detail "<n>"]` | Push a PO frame from a row. |
| `FOLLOW <attr> [AS @h]` | Open the PO a reference attribute points at. |
| `GO-BACK` | Pop the top nav frame. |
| `SEARCH <text> [Detail "<n>"]` | Text-search the current (or detail) query in place. |
| `SELECT-ROWS <ALL \| ALL EXCEPT … \| NONE \| <i> \| WHERE …>` | Set the selection for a selection-gated action. |
| `EDIT` / `CANCEL` / `SAVE` | PO edit lifecycle. |
| `SET <attr> = <value>` | Change an attribute (references resolve via lookup). |
| `ACTION <action> [= opt] [(params)] [Detail "<n>"]` | Invoke an action. |
| `SAVE \| ACTION … EXPECTING ERROR` | Assert the negative (error-notification) path. |
| `CONFIRM "<label>" \| CONFIRM ID <i>` | Answer an open server retry dialog. |
| `EXPECT <subject> <op> <value>` | Assert observable state (see above). |
| `REQUIRES <expect> \| REQUIRES TOOL <n>` | Precondition gate (unmet → skip the body). |
| `CLEANUP` | Marker; statements after it always run. |
| `REPEAT <n> [AS @i] … END` | Bounded repetition. |
| `FOR-EACH ROW [Detail "<n>"] [WHERE …] [AS @row] … END` | Iterate snapshotted rows. |
| `TOOL <name> [k=v …] [-> @var]` | Call a host-registered delegate. |

---

## See also

- **[CLI guide](./cli.md)** — install and run `vidyano`, flags, exit codes, tool packs.
- **[Embedding guide](./embedding.md)** — run scripts from your own .NET process, register tools, capture run artifacts.
- **Runnable samples** — [`Vidyano.Script.Tool/samples/*.visc`](https://github.com/2sky/Vidyano.Core/tree/main/Vidyano.Script.Tool/samples) double as regression scripts (`nav-stack.visc`, `client-ops.visc`, `loops.visc`, …).
