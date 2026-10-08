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

Comments start with `##` (inline) or `###` (a *step header* that groups following lines in the run report). Directives like `@app` / `@mode` / `@expects` configure the run.

## The execution model

A few concepts the whole language is built on:

- **The navigation stack.** `OPEN`/`OPEN-ROW`/`FOLLOW`/`FOLLOW-NAVIGATE` push frames; `GO-BACK`/`SAVE` pop them. The top frame is the "current" Query or PersistentObject (PO) — the implicit target of `SEARCH`, `EDIT`, `SET`, `ACTION`, and most `EXPECT`s. This mirrors the browser's back-stack.
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
| `OPEN-ROW <…> EXPECTING ERROR` | Assert the row's PO load is **refused** server-side. Leaves the error on the still-current calling query, so `EXPECT Notification` can follow (see [Asserting the negative path](#asserting-the-negative-path--expecting-error)). |
| `FOLLOW <attr> [AS @h]` | Navigate from a **reference** attribute on the current PO to the PO it points at, pushing a PO frame — the equivalent of the web client's "open" affordance next to a reference field. Honors the same `CanOpen` gate the UI uses. It does **not** change the reference (that's `SET`). |
| `FOLLOW-NAVIGATE [AS @h]` | Open the page the previous verb's `Navigate(path)` client operation points at — what the browser does when the server navigates it. Pushes a PO or Query frame. |
| `GO-BACK` | Pop the top frame (the browser back button). Refuses when the top is a PO in edit (`SAVE`/`CANCEL` first) and when already at the root. |

`<value>` in a `WHERE` is in **service-string form** — the same convention as `SET`. Only `=` is supported.

```visc
OPEN MenuItem Sales/Orders
OPEN-ROW WHERE Number = "SO-1001"
FOLLOW Customer AS @cust       ## jump to the referenced Customer PO
```

### Following a server `Navigate` — `FOLLOW-NAVIGATE`

An action whose server code calls `Manager.Current.QueueClientOperation(ExecuteMethodOperation.Navigate("<path>"))` sends the browser to another page. `EXPECT ClientOperation Navigate = "<path>"` asserts the operation; `FOLLOW-NAVIGATE` opens the page:

```visc
ACTION ChargePointDiagnostics                ## server: Navigate("vesta-charge-point/vestaChargePoints/9001")
EXPECT ClientOperation Navigate CONTAINS "vesta-charge-point/"
FOLLOW-NAVIGATE AS @cp                       ## pushes the VestaChargePoint PO
EXPECT PO.ObjectId = "vestaChargePoints/9001"
```

- **Which Navigate.** The one queued by the **immediately preceding** verb (`EXPECT`s in between don't count as verbs). None fails with `state-no-navigate`; more than one fails with `resolve-navigate` (the paths are in the diagnostic details).
- **Route resolution** mirrors the web client, using the Application's `Routes`: a route name matches **raw or kebab-cased** (`VestaChargePoint` / `vesta-charge-point`), optionally behind a program-unit prefix (`Charging/vesta-charge-point/…`). A PersistentObject route takes everything after its first `/` as the object id, so ids containing `/` survive intact; a Query route takes no id. The raw forms `[<pu>/]persistent-object.<type-or-id>[/<objectId>]` and `[<pu>/]query.<id>` work too. A path no route matches fails with `resolve-navigate` and a "did you mean" over the route names.
- The target opens like `OPEN PersistentObject` / `OPEN Query` (no parent, as the browser does) and pushes on top of the current frame.

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

## Detail-attribute rows

A **detail attribute** (`AsDetail`) inlines a list of rows on a persistent object — distinct from a detail *query* (`Detail "<name>"`). Address it with `Detail Attribute "<name>"` (the string literal after `Attribute` is what tells it apart from a detail query named `Attribute`).

```visc
EXPECT Detail Attribute "Certificates" TotalItems = 2
EXPECT Detail Attribute "Certificates" ROW 0 Name = "root"     ## a cell of row 0
EDIT
DELETE-ROW Detail Attribute "Certificates" WHERE SerialNumber = "0A1B"   ## or an index
ADD-ROW Detail Attribute "Certificates" AS @new              ## appended as the last row; @new = its index
SET Detail Attribute "Certificates" ROW {{new}} Name = "intermediate"    ## fill a cell of any row
SAVE                                                          ## deleted rows arrive in DeletedObjects, new rows in Objects
```

- **Rows** are the ones not deleted; indexes and `TotalItems` count only those, so they shift after a `DELETE-ROW`.
- **`DELETE-ROW`** marks the row `IsDeleted` (as the web client's row delete button does); nothing reaches the server until `SAVE`. A `WHERE` match must be unique (strict, like `OPEN-ROW WHERE`); zero/many matches and out-of-range indexes fail.
- **Gating** mirrors the web client's delete button: the PO must be in edit (`guard-edit-mode-required`), the attribute not read-only (`guard-attribute-read-only`), and — unless the PO is new — the attribute's details query must offer a `Delete` action (`guard-action-not-available`).
- **`ADD-ROW`** runs the details query's `New` action (the web client's add button) and appends the returned row; it needs a `New` action on the details query. `AS @i` binds the new row's index (read `{{i}}`, like `REPEAT … AS @i`) — it is a plain index, so a later `DELETE-ROW` of an earlier row shifts it. The web client's picker for a `LookupAttribute` and its dialog for an `OpenAsDialog` row are not simulated — set the lookup/cells with `SET … ROW` instead.
- **`SET Detail Attribute "<name>" ROW <i> <col> = …`** takes every `SET` value form (`LOOKUP` / `ID` / `FILE` / `LANGUAGE` / `null`) and the same hidden/read-only guards, applied to the row's attribute; changing a row marks the parent dirty.
- `DELETE-ROW` on a row added in this edit drops it instead of flagging it, as the web client does.

## Editing & saving

```visc
EDIT
SET Name  = "Acme Corp"
SET Owner = LOOKUP "Email:alice@example.com"   ## reference SET resolves through a lookup
SET Logo  = FILE "fixtures/acme-logo.png"      ## attach a file to a BinaryFile/Image attribute
SAVE
```

`EDIT` / `CANCEL` / `SAVE` are the standard PO edit lifecycle. `SAVE` pops the PO frame and lets owner-driven refresh fire (the underlying Query re-counts). `SET <attr> = <value>` changes an attribute; a reference attribute resolves its value through a lookup.

### Attaching a file — `SET <attr> = FILE "<path>"`

`SET <attr> = FILE "<path>"` reads a file off disk and assigns it to a `BinaryFile` or `Image` attribute, so a script author never hand-builds the wire format. The bytes are formatted for the attribute's data type automatically: a `BinaryFile` gets the `"<filename>|<base64>"` service string, an `Image` gets the bare base64 (no filename). Setting `FILE` on any other attribute type is a loud error.

The path is **relative to the script's directory** (or to `--file-root` / `VidyanoScriptOptions.FileRoot` when set) and is **confined to that root** — `..` traversal, absolute, and drive-qualified paths are rejected with `resolve-file`, so a script can never read outside its root. Point `--file-root` at a shared fixtures directory when test data lives outside the script tree.

```visc
EDIT
SET Photo = FILE "fixtures/avatar.png"   ## Image attr  → base64
SET Doc   = FILE "fixtures/contract.pdf" ## BinaryFile  → "contract.pdf|<base64>"
SAVE
```

### Multi-lingual attributes — `SET <attr> [LANGUAGE <lang>]`

A `TranslatedString` attribute holds a per-language map of strings. The **set of supported languages is decided by the server, per attribute** — a client connected to a different deployment may see a different set — so `.visc` never carries a global language list; you just name the language you want to write.

- `SET <attr> = "<value>"` sets the **session's current language** (the common case — same syntax as any other attribute).
- `SET <attr> LANGUAGE <lang> = "<value>"` sets **one specific language**, merged over the rest — languages you don't touch keep their server value.

```visc
EDIT
SET Title = "Widget"                   ## current language
SET Title LANGUAGE nl = "Hulpmiddel"   ## a specific language
SET Title LANGUAGE de = "Werkzeug"
SAVE
```

`LANGUAGE` only applies to a `TranslatedString` attribute (using it on any other type is a loud error) and can't combine with `LOOKUP` / `ID` / `FILE`.

## Running actions

```visc
ACTION Approve                       ## by name
ACTION ChangeStatus = "Shipped"      ## pick an action option by label
ACTION Export (format=xlsx, all=true)   ## with named parameters
ACTION Detail "Lines" Delete         ## target a detail query on the current PO
```

An optional leading `Detail "<name>"` clause targets a detail query (`PersistentObject.Queries`) instead of the nav-stack query — the action resolves from and executes against that detail query (the parent stays the master PO), so a `SELECT-ROWS Detail "<name>"` selection has a verb to act on.

A query action invoked with **no selection** posts an empty selection (matching the web client), so a server action that only needs an open query — e.g. one that calls `EnsureQuery()` — runs without a meaningless `SELECT-ROWS`. If a query action **errors** server-side, the error rides on the current Query (there's no PO to carry it); `ACTION` surfaces it as a failure (it does not pass silently), and a following `EXPECT Notification …` can read it.

A custom action can fail two ways, and both surface as an `ACTION` failure with the message copied onto the current frame for `EXPECT Notification …` to read: the server sets an error notification on the PO/query and returns null, **or** the action *returns* a `Notification(message, Error)` result (the toast shape — `return Notification(...)`). A returned **non-error** notification (info/warning) is copied onto the frame too — so `EXPECT Notification …` can read it — but does **not** fail the verb, faithful to the toast a browser shows.

An action that **returns a stream** (server: `Manager.Current.RegisterStream(...)`, e.g. a "download PDF" button) is handled like the web client: the runner **auto-fetches** the stream — no extra verb — and buffers it for the `EXPECT Stream.*` subjects (see [Asserting state](#asserting-state--expect)). No navigation frame is pushed; the capture is cleared by the next verb, like the per-verb `ClientOperation` buffer. The stream is fetched exactly once, whichever `ACTION` form ran it (an `= "option"` action is fetched by Core itself and handed to the runner).

The built-in query **exports** — `ACTION ExportToExcel` / `ACTION ExportToCsv` — download the same way. Like the web client, the runner runs them as a single `GetStream` request (the action's parent and query, never `ExecuteAction`), so the generated file lands in `EXPECT Stream.*` with no extra verb and no frame pushed. The whole query is exported as the client holds it — the selection is not sent, exactly as in the browser — and a `Detail "<name>"` clause exports a detail query. An export that fails server-side is served as an `Error.txt` stream whose `Stream.Text` carries the message.

```visc
OPEN MenuItem Home/Products
ACTION ExportToCsv
EXPECT Stream.Name = "Products.csv"
EXPECT Stream.Text CONTAINS "Widget"
ACTION ExportToExcel
EXPECT Stream.Text MATCHES "^PK"                ## an .xlsx is a zip package
```

### Asserting the negative path — `EXPECTING ERROR`

```visc
SAVE EXPECTING ERROR
ACTION Delete EXPECTING ERROR
CONFIRM "Cancel" EXPECTING ERROR
OPEN PersistentObject "Customer" "deleted-id" EXPECTING ERROR
OPEN Query "RestrictedOrders" EXPECTING ERROR
OPEN MenuItem Admin/Users EXPECTING ERROR
OPEN-ROW WHERE Name = "Faulty" EXPECTING ERROR
```

This trailing suffix flips the verb's polarity: it **passes only if the verb fails as expected**, and **fails if the verb unexpectedly succeeds**. A client-side authoring guard (e.g. SAVE before EDIT, or OPEN before SIGN-IN) still fails normally — only the verb's *expected* failure is absorbed. For `SAVE` / `ACTION` / `CONFIRM`, that expected failure is the server's error notification — whether the server set it on the PO/query and returned null, or the action *returned* it as a `Notification(…, Error)` result — which stays on the current PO (or, for a query action, on the current Query), so a following `EXPECT Notification …` pins the exact message; it composes with every `ACTION` form. For `CONFIRM` it asserts that **answering a server retry dialog** resumes an action that then fails (a retry option that throws / returns an error — the archetypal "Cancel" branch); see [Server retry dialogs](#server-retry-dialogs--confirm).

All three `OPEN` forms take the suffix to assert the open is **refused** — the `.visc` equivalent of "this should not open":

- **`OPEN PersistentObject <type> <id>`** — a refused point-load (not-found, access-denied, no PO returned).
- **`OPEN Query <id>`** — a refused query-load (no such query, or access-denied).
- **`OPEN MenuItem <path>`** — a path that does not resolve in this user's menu (the natural way to assert a permission/visibility boundary), or a refused load of the entry it points at.

A refused open pushes **no frame**, yet the server's refusal message is still readable: until the next verb, `EXPECT Notification` / `EXPECT Notification.Type` read **the refusal** (type `Error`) — the error the browser shows for a record it can't open — even when an earlier frame is still on top:

```visc
OPEN PersistentObject "ChargePointConnector" "{{connectorId}}" EXPECTING ERROR
EXPECT NavStack.Depth = 0
EXPECT Notification CONTAINS "card is not accepted on this connector"
```

One caveat applies to those three OPEN forms: because Core collapses every open failure into one error channel, a refused open is **indistinguishable from a transport fault** here (unlike SAVE/ACTION, which a transport fault still fails) — pin the message with `EXPECT Notification` to tell them apart. To assert a row is gone *and* read state afterwards, prefer a query re-search (`SELECT-ROWS WHERE … → EXPECT Selection.Count = 0`).

**`OPEN-ROW … EXPECTING ERROR`** asserts a row whose PO **load** is refused server-side (e.g. its `OnLoad` ends in an error). A refused row-open sets the error on the **still-current calling query** (no PO frame is pushed, so that query stays the top frame), mirroring the web client, so `EXPECT Notification` / `EXPECT Notification.Type = "Error"` read it there. It only works when a query lists the record; to open a record by id, use `OPEN PersistentObject … EXPECTING ERROR`. Only the refused *load* (a `server-error`) is absorbed — a bad row *selection* (index out of range, or a `WHERE` matching no/many rows) is a client-side authoring fault that still fails loudly.

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

Answering a dialog can itself resume an action that **fails** — the archetypal "Cancel" branch that throws or returns an error notification server-side. Assert that negative path with the `EXPECTING ERROR` suffix (see [Asserting the negative path](#asserting-the-negative-path--expecting-error)): the `CONFIRM` then passes only if the resumed action surfaces a server error notification, and fails if it succeeds (or merely parks a *further* retry, which you'd answer with another `CONFIRM`). The notification stays on the current PO/query, so `EXPECT Notification …` still pins the message:

```visc
ACTION AppNotificationSend
CONFIRM "Cancel" EXPECTING ERROR
EXPECT Notification.Type = "Error"
EXPECT Notification CONTAINS "Cancelled"
```

### Add-Reference pickers — `ADD-REFERENCE`

Some custom actions return an **Add-Reference picker** instead of opening a record: server-side the handler returns `AddReference("<query>")`, the affordance for *linking existing rows* to the current record (the web client shows it as the toolbar **Add** dialog). In `.visc` such an `ACTION` opens the picker as a modal frame, which you drive like any query and confirm with `ADD-REFERENCE`:

```visc
OPEN MenuItem Home/ProductCategories
OPEN-ROW WHERE Name = "Tools"
ACTION LinkProducts                       ## server result is an AddReference → opens a picker frame
EXPECT NavStack.Top.Kind = "AddReferenceDialog"
SEARCH "Gadget"                           ## the picker is a query — search / select / assert it
EXPECT TotalItems >= 1
SELECT-ROWS WHERE Name = "Gadget"
ADD-REFERENCE                             ## confirm the selection → reaches the server's OnAddReference
```

`ADD-REFERENCE` also takes an **inline selector** as sugar — it selects on the picker, then confirms, in one line:

```visc
ACTION LinkProducts
ADD-REFERENCE WHERE Name = "Gadget"       ## or: ADD-REFERENCE <index>
```

The **built-in Add button of a detail query** (its `AddReference` action — offered when the query has a lookup source) works the same way. As in the web client, the `ACTION` posts nothing: it opens a **lookup clone** of the detail query (rows from its lookup source) as the picker, and `ADD-REFERENCE` posts `Query.AddReference` against the detail query itself (no `AddAction`), reaching its `OnAddReference`:

```visc
OPEN-ROW WHERE Name = "Tools"
ACTION Detail "Members" AddReference      ## opens the lookup picker — nothing is posted yet
EXPECT TotalItems = 2                     ## the picker lists the lookup source
ADD-REFERENCE WHERE Name = "Gadget"       ## posts Query.AddReference on the Members detail
SEARCH Detail "Members"                   ## reload the detail to see the new row
```

When the query also offers `New`, the client folds both into one `AddReference` action with options (`New …`, `Existing`); `ACTION Detail "Members" AddReference = "Existing"` (or `= ID <last>`) opens the same picker, while the `New` option runs `New`.

While the picker is open the script is **frozen** to the verbs that drive, inspect, confirm, or dismiss it — `SEARCH` / `SELECT-ROWS` / `EXPECT` / `REQUIRES`, plus `ADD-REFERENCE` (confirm) and `GO-BACK` (dismiss without linking); anything else trips `state-add-reference-pending`. Confirming with **no selection** fails loudly (an add that adds nothing is always a mistake), and `ADD-REFERENCE` with **no picker open** fails with `state-no-add-reference-pending`. On success the picker frame pops, revealing the record beneath; reload its detail (`SEARCH Detail "<name>"`) to see the new link.

> **Removing a reference** has no dedicated verb — it is an ordinary selection-gated action on the *already-linked* rows. Select them on the relevant (detail) query and run the server's remove action:
>
> ```visc
> SELECT-ROWS Detail "ChargeCards" WHERE Name = "Card-007"
> ACTION Detail "ChargeCards" Remove
> ```

---

## Running charts — `CHART`

```visc
OPEN MenuItem Home/Products
CHART "ByColor"                       ## run a named chart of the current query
EXPECT Chart.Data CONTAINS "Blue"     ## assert on the returned chart JSON
CHART Detail "Sessions" "History"     ## or run a chart of a detail query
```

`CHART "<name>"` executes the server's `QueryFilter.Chart` system action for the named chart of the current query — the same call the web client's dashboard makes — and **captures** the returned chart JSON for `EXPECT Chart` / `EXPECT Chart.Data`. Unlike `ACTION`, a chart is a read-only observable of the query, **not** a navigation destination: no frame is pushed (the current query stays current), and the capture is cleared by the next executable verb, exactly like the per-verb `ClientOperation` buffer. An optional leading `Detail "<name>"` clause runs the chart against a named detail query on the current PO, mirroring `ACTION Detail "<name>"`.

A chart name the server doesn't know fails the verb loudly with the server's `Missing chart <name>` notification (an `assert-notification-error`), so a typo can't pass silently.

Read the result with `EXPECT Chart.Data` (the aggregated chart JSON string — assert with `CONTAINS` / `MATCHES` / `=`) or the bare `EXPECT Chart` for a presence check (`IS NULL` / `IS NOT NULL`); both spellings resolve to the same value.

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
EXPECT Notification.Type = "Error"
EXPECT Notification MATCHES "already exists"
EXPECT ClientOperation ShowMessageBox
EXPECT ClientOperation ShowMessageBox CONTAINS "saved"
EXPECT ClientOperation Refresh IS NULL
```

`Notification` / `Notification.Type` read the current PO's notification, or the current Query's when no PO is open — so a query action's notification (e.g. an error) is assertable.

**Streams** (the stream the previous action auto-fetched; `IS NULL` when none)

```visc
ACTION DownloadInvoice
EXPECT Stream.Name = "invoice.pdf"
EXPECT Stream.Length > 0
EXPECT Stream.Text CONTAINS "%PDF"
EXPECT Stream IS NULL                            ## after another verb — the capture is per-verb
```

When an action returns a stream — or is one of the built-in exports (`ExportToExcel` / `ExportToCsv`) — the runner auto-fetches it (like the web client) and buffers `Stream.Name` (file name), `Stream.Length` (byte length), and `Stream.Text` (UTF-8 decode); bare `Stream` is a presence check. A server-side download fault is served as the stream *body*, so it's assertable via `Stream.Text` too.

**Charts** (the last `CHART` result; `IS NULL` when none was captured)

```visc
EXPECT Chart.Data CONTAINS "barchart"
EXPECT Chart.Data MATCHES "\"name\":\"Blue\",\"value\":2"
EXPECT Chart IS NULL                             ## no chart captured (e.g. after another verb)
```

`Chart` / `Chart.Data` read the JSON of the chart the previous `CHART` verb ran (both spellings resolve to the same value; bare `Chart` reads naturally with `IS NULL`). See [Running charts](#running-charts--chart).

**Retry dialog** (the open server retry; `IS NULL` when none)

```visc
EXPECT RetryDialog.Title
EXPECT RetryDialog.Message MATCHES "are you sure"
EXPECT RetryDialog.Options CONTAINS "Cancel"
```

**References by document id** — `EXPECT <ref> = ID "<id>"` (and `!= ID`) asserts a reference attribute by the document it points at (its `ObjectId`), symmetric with `SET <ref> = ID "<id>"`. A plain `EXPECT <ref> = "..."` still compares the display value; `= ID` compares the underlying id, the stable identifier. Only `=` / `!=` accept `ID` (a document id has no ordering), and only reference attributes — `= ID` on a non-reference is a loud error, never a silent fall-through to the display value.

```visc
EXPECT Customer = ID "people/acme"     ## the reference points at exactly this document
EXPECT Owner   != ID "people/old-rep"
```

**Translations by language** — `EXPECT <attr> LANGUAGE <lang> = "..."` asserts one translation of a `TranslatedString` attribute, symmetric with `SET <attr> LANGUAGE <lang>`. A plain `EXPECT <attr> = "..."` compares the current-language value.

```visc
EXPECT Title = "Widget"                ## current-language value
EXPECT Title LANGUAGE nl = "Hulpmiddel"
```

**Dates, times & numbers compare by value** — for a numeric or date/time attribute, `=` / `!=` / `<` / `>` / `<=` / `>=` compare **by value**, not by formatted string, so an assertion matches regardless of the host machine's locale. Write the literal in the same invariant service-string form `SET` uses — `dd-MM-yyyy` (optionally `… HH:mm:ss.FFFFFFF`, with a trailing ` K` for an offset) for a date, `HH:mm[:ss]` for a time. A literal that can't be parsed as the attribute's type falls back to a plain string compare.

```visc
EXPECT ReleaseDate = "15-03-2024"      ## by value — locale-independent
EXPECT ReleaseDate < "01-01-2025"
EXPECT ReleaseTime = "14:30"
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

**Action availability** — `EXPECT Action <name> IS [NOT] AVAILABLE | VISIBLE` asserts whether a named action is executable (`AVAILABLE` → `CanExecute`) or shown (`VISIBLE` → `IsVisible`) on the current PO / nav-stack query. An action filtered out server-side (e.g. via `DisableActions`) reads as `IS NOT AVAILABLE`:

```visc
EXPECT Action Delete IS NOT AVAILABLE   ## gated out (e.g. server DisableActions)
EXPECT Action Export IS VISIBLE
```

**Absent attributes & columns** — `EXPECT Attribute <name> IS [NOT] PRESENT` and `EXPECT Query.Columns[<name>] IS [NOT] PRESENT` assert whether an attribute is on the PO (`PO.Attributes`) or a column on the query — e.g. one the server removed with `RemoveAttribute` / `RemoveColumns`. Presence ignores visibility (a hidden attribute is present; use `IS [NOT] VISIBLE` for that). It is the **only** assertion a missing name satisfies: every other `EXPECT` on it still fails with `resolve-attribute`, so a typo is never mistaken for an absent field.

```visc
EXPECT Attribute DefaultPublicKwhPriceEuro IS NOT PRESENT
EXPECT Query.Columns[DefaultPublicKwhPriceEuro] IS NOT PRESENT
EXPECT Detail "Prices" Query.Columns[Price] IS PRESENT    ## Detail-redirectable
```

A query row's cells are its query's columns, so a column that isn't present has no cell in any row; a present-but-empty cell is `EXPECT {{@row.<col>}} IS NULL` inside `FOR-EACH ROW … AS @row`.

**Detail-attribute rows** — `EXPECT Detail Attribute "<name>" TotalItems <op> <n>` and `EXPECT Detail Attribute "<name>" ROW <i> <col> <op> <value>` read the rows of an `AsDetail` attribute (see [Detail-attribute rows](#detail-attribute-rows)). Both work under `REQUIRES`.

**Detail redirection** — query-family subjects (`TotalItems`, `Selection.*`, `Query.*`) accept a leading `Detail "<name>"` to target a detail query on the current PO. It reads what the detail holds in memory (no forced search), so load it first with `SEARCH Detail "<name>"` if needed. The same clause also targets a named **action** on that detail — symmetric with `ACTION Detail "<name>" <X>` — resolving the action against the detail's own actions alone (never the master PO, which may carry a same-named action):

```visc
EXPECT Detail "OrderLines" TotalItems = 4
EXPECT Detail "OrderLines" Selection.Count = 1
EXPECT Detail "OrderLines" Action Delete IS NOT AVAILABLE   ## action gated on the sub-query
EXPECT Detail "OrderLines" Action ExportToExcel IS AVAILABLE
```

Only the `AVAILABLE` / `VISIBLE` flags compose with `Detail` (the `DISPLAY-NAME` form does not).

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

- **User variables** are assigned `@name = …` and read `{{name}}` (no `@`). A loop index (`AS @i`) reads the same — `{{i}}` — but a loop **row** keeps the `@`: read a cell as `{{@row.<col>}}` (or use the bare handle `@row` for `OPEN-ROW @row`).
- **Built-ins** `{{@today}} {{@now}} {{@uuid}} {{@random}}` are evaluated **on each reference** (like `DateTime.Now` / `rng.Next()`), so capture into a variable to freeze a value for reuse. `--seed`/`Seed` fixes the `@uuid`/`@random` sequence (independent streams); `--now`/`Now` anchors the clock, which then flows by real elapsed time.
- **In-string interpolation** — `{{…}}` holes resolve inside `"…"` literals using the same machinery, so values compose. Escape a literal brace as `\{`.

### Values of the current record — `{{PO.…}}`

`{{PO.<prop>}}` and `{{PO.Attr.<name>}}` read the current PersistentObject (the top PO frame), so a script can keep a value it saw on a record — typically the id of a record it created — and reuse it later:

```visc
@name = "CP-{{@uuid}}"
OPEN MenuItem Home/ChargePoints
ACTION New
SET Name = "{{name}}"
SAVE                                   ## the saved frame pops …
OPEN-ROW WHERE Name = "{{name}}"       ## … so re-open it to read its id
@cpId = {{PO.ObjectId}}
@vendor = {{PO.Attr.Vendor}}
OPEN PersistentObject "ChargePoint" "{{cpId}}" EXPECTING ERROR
```

Each form yields exactly what the matching `EXPECT` compares: `{{PO.Attr.<name>}}` is `EXPECT <name>` (the attribute value, with the same hidden-attribute guard — navigation mode rejects a hidden attribute), `{{PO.Metadata.<key>}}` / `{{PO.NavigationHints.<key>}}` are the bag lookups, and `{{PO.<prop>}}` is `EXPECT PO.<prop>` (`ObjectId`, `Type`, `FullTypeName`, `Label`, `Breadcrumb`, `IsNew`, `IsHidden`, `Tag`). Attributes sit under `Attr.` so one named `Type` or `Label` never shadows the PO property. With no PO frame on top it fails with `state-no-current-po`; capture into a variable to keep the value once you navigate away.

### Declaring host-supplied variables — `@expects`

```visc
@expects region, tenant              ## the host supplies {{region}} / {{tenant}} at run time
OPEN MenuItem Shop/{{region}}/Products
SEARCH "{{tenant}}"
```

Some variables aren't assigned in the script — the host injects them through `VidyanoScriptOptions.Variables`, the CLI `--var`, or `--env-prefix` (this is how a test harness feeds per-test values). The static lint can't see those bindings, so an editor would flag every such `{{x}}` as undefined. `@expects a, b` declares them: the [lint](./cli.md) counts the names as declared (so the reads are clean), while the interpreter treats the line as a **no-op** — it never binds the variables, so a host value is still required and is *never overwritten*.

This makes the directive runtime-safe, which a workaround assignment is not: `@a = "{{a}}"` would silence the lint but also overwrite the host-injected value (and crash if it isn't set). With `@expects`, an unsupplied declared variable still loud-fails (`resolve-variable`) the moment it's first read — the declaration silences the *static* check without weakening the runtime backstop.

`@expects` is an `@`-directive (like `@mode` / `@app`), not a verb; conventionally it goes at the top of the file, but because it has no runtime effect it's accepted anywhere and the lint is order-insensitive. Names are bare (no `@`, no `{{}}`) and comma-separated.

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

### Hidden attributes and the mode tier

A **hidden** attribute (`AttributeVisibility.Never` — the default editor never renders it) is treated as a *reachability* concern, not a hard constraint: the standard UI can't touch it, but a custom web component can, and Core itself only ever blocks a **read-only** write (visibility never blocks a set). So `SET`/`EXPECT` on a hidden attribute tier with the mode:

| | `navigation` | `audit` | `direct` |
|---|---|---|---|
| `SET <hidden>` | rejected (`guard-attribute-hidden`) | allowed **+ warning** | allowed silently |
| `EXPECT <hidden>` (read) | rejected (`guard-attribute-hidden`) | allowed silently | allowed silently |

Use `@mode = direct` (or `audit`) to script the custom-component path. **Read-only stays a hard guard in every mode** (`guard-attribute-read-only`) — a read-only attribute is genuinely not settable, even by a custom component, so no mode bypasses it.

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
| `FOLLOW-NAVIGATE [AS @h]` | Open the page the previous verb's `Navigate(path)` points at. |
| `GO-BACK` | Pop the top nav frame. |
| `SEARCH <text> [Detail "<n>"]` | Text-search the current (or detail) query in place. |
| `SELECT-ROWS <ALL \| ALL EXCEPT … \| NONE \| <i> \| WHERE …>` | Set the selection for a selection-gated action. |
| `ADD-ROW Detail Attribute "<n>" [AS @i]` | Append a new row (details query `New`) to a detail attribute. |
| `SET Detail Attribute "<n>" ROW <i> <col> = <value>` | Change a cell of a detail-attribute row. |
| `DELETE-ROW Detail Attribute "<n>" <i \| WHERE …>` | Remove a row of a detail (`AsDetail`) attribute; `SAVE` sends it as deleted. |
| `EDIT` / `CANCEL` / `SAVE` | PO edit lifecycle. |
| `SET <attr> = <value> \| LOOKUP "…" \| ID "…" \| FILE "<path>" \| null` | Change an attribute. `FILE` attaches a file (root-confined) to a BinaryFile/Image. |
| `SET <attr> LANGUAGE <lang> = <value>` | Set one translation of a TranslatedString attribute (bare `SET` = current language). |
| `ACTION <action> [= opt] [(params)] [Detail "<n>"]` | Invoke an action. |
| `CHART "<name>" [Detail "<n>"]` | Run a named query chart; capture its JSON for `EXPECT Chart`. |
| `SAVE \| ACTION \| CONFIRM … EXPECTING ERROR` | Assert the negative (error-notification) path. |
| `OPEN PersistentObject \| Query \| MenuItem … EXPECTING ERROR` | Assert the open is refused (no frame pushed; `EXPECT Notification` reads the refusal until the next verb). |
| `OPEN-ROW … EXPECTING ERROR` | Assert the row's PO load is refused; error stays on the calling query (`EXPECT Notification` **can** follow). |
| `CONFIRM "<label>" \| CONFIRM ID <i> [EXPECTING ERROR]` | Answer an open server retry dialog (`EXPECTING ERROR` asserts the resumed action fails). |
| `ADD-REFERENCE [<i> \| WHERE <col> = <value>]` | Confirm an Add-Reference picker an `ACTION` opened (a custom `AddReference(...)` result or a query's built-in `AddReference`), linking the selected (or inline-selected) rows. |
| `EXPECT <subject> <op> <value>` | Assert observable state (see above). |
| `EXPECT <ref> = ID "<id>"` | Assert a reference by its document id (`ObjectId`). |
| `EXPECT <attr> LANGUAGE <lang> = "…"` | Assert one translation of a TranslatedString attribute. |
| `EXPECT Stream[.Name\|.Length\|.Text]` | Assert the stream an action auto-fetched (returned via `RegisterStream`). |
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
