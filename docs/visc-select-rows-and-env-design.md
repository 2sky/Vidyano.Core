# .visc: SELECT-ROWS for selection-gated actions + env-backed value sourcing

> ✅ **Design phase complete** (`/design-interface`). Grammar and C# change-map below are hardened and ready for `/implement-issue` (point the skill at this file as the spec). GitHub Issues are disabled on this repo, so the RFC lives here as a committed doc.

> ⚠️ **Partially superseded by a follow-up (same branch).** Since this RFC: (1) `SELECT-ROWS ALL` was **redefined** from "every loaded row" to **server-side select-all** — it sets `Query.AllSelected` (serialized as `allSelected`) and `SELECT-ROWS ALL EXCEPT <index|WHERE>` expresses inverse selection (the addressed rows become the server-side exclusion set). The new `EXPECT Selection.AllSelected` subject asserts the flag. (2) The legacy silent `{{$env NAME}}` form was **removed** (not kept as an alias) — use `{{env:NAME}}`. (3) `--env-file <path>` now loads literal `KEY=VALUE` pairs that back `{{env:NAME}}` / `SIGN-IN FROM ENV` (shadowing the process env), composed onto the same `EnvLookup` seam. The notes below predate these changes.

Two `.visc` grammar gaps surfaced in conversation. Both have the Core plumbing already in place — only the script-language entry points are missing.

## Gap 1 — invoke selection-gated actions on a Query

Actions like `Delete` carry a `SelectionRule` (e.g. `>=1`), so `ActionBase.CanExecute` is `false` until rows are selected. Today **nothing in `.visc` ever populates `Query.SelectedItems`**, so these actions can't be run at all.

Worse, even once selection exists, the parameter-path of `VidyanoSession.ExecuteActionAsync` (`VidyanoSession.cs:1108`) hard-codes `selectedItems: null` — so it would drop the selection even if present. (The option-label path already reads it via `ActionBase.Execute` → `ActionBase.cs:78`.)

## Gap 2 — source values (especially credentials) from the environment

`SIGN-IN` credentials and other values should come from the environment **without pasting them on the command line**. A `{{$env NAME}}` form exists (`Interpreter.cs:1035`) but **returns `null` silently when unset** — an empty password posted to the server. We want a loud, ergonomic, paste-free story.

---

## Chosen interface

### Selection — fill the already-reserved `SELECT-ROWS` verb

`SELECT-ROWS` is already in `KnownVerbs` (`Parser.cs:24`) with no parser arm — this wires the intent that was reserved. Row-addressing is **identical to `OPEN-ROW`** (positional index, `WHERE <col> = <value>`, leading `Detail "<name>"` clause), plus the keywords `ALL` and `NONE`. Always takes an explicit target — consistent with `OPEN-ROW`, which is never argument-less.

```visc
SELECT-ROWS ALL                            # select every loaded row
SELECT-ROWS 0                              # by index (replaces current selection)
SELECT-ROWS WHERE Status = "Open"          # by predicate; may match many (non-strict)
SELECT-ROWS Detail "OrderLines" ALL        # Detail clause orthogonal, mirrors OPEN-ROW
SELECT-ROWS NONE                           # clear (keyword, not a separate verb)
ACTION Delete                              # CanExecute now true; selection forwarded
EXPECT Selection.Count = 3                 # observability (Detail-redirectable)
```

Semantics:
- **Replace, not accumulate.** Subsets are expressed by a `WHERE` predicate. Non-contiguous hand-picking (`SELECT-ROWS ADD …`) is deliberately deferred — no real script case yet.
- **`SELECT-ROWS ALL` = currently *loaded* rows**, not a server-side all-matching flag. Core's `SelectedItems` is an item collection; a phantom all-flag has no representation and would diverge from what `EXPECT Selection.Count` reports and from what a browser posts.
- **Zero-match `WHERE` is not an error** — selection becomes empty. Tolerant cleanup uses the existing primitives: `REQUIRES Selection.Count >= 1` + `CLEANUP`. The strict case surfaces naturally at `ACTION` via the existing `CanExecute` diagnostic. ("Define errors out of existence" — no new failure policy.)
- **Selection lives on the Query frame**, persists across `ACTION`, cleared by `SEARCH`/refresh/frame pop. Wiring is automatic: `Query.SelectedItems_CollectionChanged` (`Query.cs:241`) → `QueryAction.Invalidate(count)` → `CanExecute`.

### Env / value-sourcing — three complementary layers, all explicit (nothing implicit)

```visc
# 1. Inline, portable — works in ANY value position (SET, SIGN-IN, params, EXPECT):
SIGN-IN {{env:VIDYANO_USER}} / {{env:SVC_PW}}     # loud-fail if unset
SET Owner = {{env:OWNER ?? "unassigned"}}         # ?? = optional-with-fallback

# 2. Zero-ceremony, paste-nothing — the dominant credential case:
SIGN-IN FROM ENV                                   # VIDYANO_USER / VIDYANO_PASSWORD, loud if missing
```

```bash
# 3. General "make env vars available without naming each" (IConfiguration env-prefix), opt-in at invocation:
vidyano run x.visc --env-prefix VIDYANO_           # VIDYANO_REGION → {{REGION}}; an explicit --var still wins
```

- `{{env:NAME}}` is **loud-on-missing** — the whole point is closing the empty-password footgun. _(Follow-up: the legacy silent `{{$env NAME}}` form was removed rather than kept as an alias — see the banner at the top.)_
- Only `??` (fallback) is added inside holes — **no `!` (required) marker**, because missing is loud *by default*, making it redundant.
- `--env-prefix` strips the prefix (IConfiguration convention): `VIDYANO_REGION` → `{{REGION}}`. Precedence: explicit `--var` / script assignment overrides env-bound values.
- `SIGN-IN FROM ENV` is **opt-in per statement** — credentials never appear implicitly on a plain `SIGN-IN`.

---

## C# change-map

**`Parsing/Ast.cs`**
```csharp
// new — mirrors OpenRowStmt's field shape so row-addressing parsing is shared
public sealed record SelectRowsStmt(
    bool All, bool None, Expression? Index,
    string? MatchColumn, ExpectOp? MatchOp, Expression? MatchValue,
    string? DetailName, SourceLocation Location) : Statement(Location);

// new — env-sourced value usable anywhere a value/interpolation appears
public sealed record EnvExpr(string Name, Expression? Fallback, SourceLocation Location) : Expression(Location);

// SignInStmt gains: bool FromEnv  (UserName/Password null when FromEnv == true)

// ExpectSubjectKind gains: SelectionCount   // EXPECT Selection.Count <op> N (Detail-redirectable)
```

**`Parsing/Parser.cs`**
```csharp
"SELECT-ROWS" => ParseSelectRows(tok.Location),     // dispatch (~line 131); verb already in KnownVerbs (line 24)
private Statement? ParseSelectRows(SourceLocation loc);   // ALL/NONE keywords, else shared row-target parse
// Refactor the WHERE/index/Detail block of ParseOpenRow (~351-390) into:
private bool ParseRowTarget(out Expression? index, out string? col, out ExpectOp? op,
                            out Expression? val, out string? detail);   // shared by OPEN-ROW + SELECT-ROWS
// ParseSignIn (~221): detect `FROM ENV` → SignInStmt(FromEnv: true, …)
// Hole-content parsing: recognize `env:NAME` and optional `?? <expr>` → EnvExpr
```

**`Runtime/Interpreter.cs`**
```csharp
case SelectRowsStmt s: return await DoSelectRows(s);
private async Task<StatementResult> DoSelectRows(SelectRowsStmt s);   // resolve target, mutate SelectedItems
// EvaluateInterpolation (~1032): add EnvExpr handling via _envLookup —
//   value ?? evaluate(Fallback); if both null → Fail(ErrorKind.ResolveEnv, "Environment variable 'NAME' is not set.")
//   (follow-up: the legacy "$env " branch was removed, not retained)
// DoSignIn (~206): if FromEnv → read VIDYANO_USER / VIDYANO_PASSWORD via _envLookup, loud-fail each if null
// _envLookup: Func<string,string?> from options (default Environment.GetEnvironmentVariable),
//             with --env-prefix vars pre-bound into _vars
```
New `ErrorKind.ResolveEnv`.

**`Runtime/VidyanoSession.cs`**
```csharp
public OpResult SelectRows(bool all, bool none, object? index, string? col, ExpectOp? op,
                           object? val, string? detail, SourceLocation loc);   // reuses ResolveRowQuery
public OpResult<int> SelectionCount(string? detailName, SourceLocation loc);   // backs EXPECT Selection.Count

// THE MANDATORY FIX — line ~1108, parameter path:
var selected = action is QueryAction && CurrentQuery is { Count: > 0 } q ? q.SelectedItems.ToArray() : null;
result = await Client.ExecuteActionAsync(prefix + name, CurrentPo, CurrentQuery, selected, dict).ConfigureAwait(false);
```
`SelectRows` does `query.SelectedItems.Clear(); foreach (var r in chosen) query.SelectedItems.Add(r);` — `CanExecute` flips for free via the existing `CollectionChanged` hook.

**`VidyanoScriptOptions.cs`**
```csharp
public Func<string,string?>? EnvLookup { get; set; }     // default Environment.GetEnvironmentVariable; injectable for hermetic NUnit/VidyanoTestDriver runs
public string? EnvironmentPrefix { get; set; }           // --env-prefix; binds matching env vars into the variable table (prefix stripped)
// add both to the shallow-copy ctor
```

**`Vidyano.Script.Tool/Args.cs`**
```csharp
case "--env-prefix": result.EnvPrefix = args[++i]; break;   // → opts.EnvironmentPrefix in ToOptions()
// no flag needed for inline {{env:}} or SIGN-IN FROM ENV — process env flows through EnvLookup
```

**No changes to `Vidyano.Core`** — `Query.SelectedItems`, `SelectedItems_CollectionChanged`, `QueryAction.Invalidate`, `ExpressionParser` (SelectionRule), and `Client.ExecuteActionAsync(…, selectedItems, …)` are all consumed as-is.

---

## Acceptance / samples
- New regression sample (e.g. `samples/selection.visc`): `OPEN` a query → `SELECT-ROWS ALL` → `EXPECT Selection.Count = <n>` → a selection-gated `ACTION` → assert result; plus `SELECT-ROWS WHERE`, `NONE`, and a `Detail` form.
- New sample (e.g. `samples/env-signin.visc`, lint-or-env-gated): `SIGN-IN FROM ENV`, `{{env:NAME}}`, `{{env:NAME ?? "default"}}`, and a `--env-prefix` run.
- Loud-on-missing: a missing `{{env:NAME}}` / `SIGN-IN FROM ENV` produces a `ResolveEnv` diagnostic, not an empty credential.
- Update the `.visc` quick-reference table in `CLAUDE.md` (`SELECT-ROWS`, `SIGN-IN … FROM ENV`, `{{env:…}}`, `EXPECT Selection.Count`) and `help verbs`.

---

## Designs considered (and rejected)

Three designs were explored in parallel, each under a different optimization constraint.

- **A — Minimize surface** (chosen as the skeleton): fill the reserved `SELECT-ROWS`, reuse OPEN-ROW addressing, `{{env:NAME}}` loud, no auto-binding. Most consistent, smallest delta.
- **B — Maximize composability** (mostly rejected): a new `SELECT` verb with accumulating `Add`/`All`/`None`, and `!`/`??` operators parsed inside holes. **Rejected:** accumulation is a non-case for scripts; a second mini-grammar inside `{{...}}` is a learning tax; `!` is redundant once missing-is-loud-by-default. **Kept from B:** `EXPECT Selection.Count` observability and the injectable env lookup (the script projects double as NUnit regression drivers).
- **C — Optimize the common case** (partly chosen): bare `SELECT-ROWS` = all, strict singular `SELECT-ROW`, and `SIGN-IN FROM ENV`. **Kept:** `SIGN-IN FROM ENV` zero-ceremony credentials and the `??` fallback. **Rejected:** bare `SELECT-ROWS` (inconsistent with always-argumented OPEN-ROW; ambiguous "all or nothing?"); a separate strict singular verb (a `WHERE` predicate already addresses one row); auto-binding on every `SIGN-IN` (too implicit — `FROM ENV` makes it one explicit, greppable token).

**Cross-design consensus (already decided):** select-all = loaded rows (not a server flag); missing-env = loud; the `VidyanoSession.cs:1108` `selectedItems: null` fix is mandatory; selection lives on the Query frame and auto-flips `CanExecute`.
