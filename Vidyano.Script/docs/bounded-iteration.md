# Bounded iteration for `.visc` — design

> Status: **design only** (not implemented). Captured 2026-06.
>
> This is a proposal for adding looping/retry to `.visc` *without* making the language
> Turing complete. Read §0 first — it is the constraint everything else serves.

## 0. The invariant this design must not break

`.visc` is **total**: every script provably halts. The only control flow today is `REQUIRES`
(skip-rest-of-body gate) and `CLEANUP` (always-run marker) — there are no loops, no recursion,
no general `if/else`, no arithmetic. The single missing primitive that keeps the language total is
**unbounded iteration**.

The three constructs below stay total by construction — each has a bound fixed *before* the loop
runs, so none can introduce unbounded search. That is the line separating this from Turing
completeness. **No `WHILE`, no recursion, no computed/extendable bounds.**

| Construct | Bound source | Evaluated |
|---|---|---|
| `REPEAT <n>` | literal/interp int | once, at entry |
| `FOR-EACH ROW` | snapshot of the row set | once, at entry |
| `EXPECT … EVENTUALLY` | attempt cap | static |

Why bother instead of just dropping to C#? Computation still belongs in a `TOOL`/the NUnit host.
These constructs cover the *navigational* repetition a browser user actually performs (process each
matching row, repeat an action, wait for async server state) — things that read as a faithful
transcript and that you do not want to leave the declarative layer to express.

---

## 1. Grammar (EBNF additions)

```ebnf
statement      := … | repeat | forEachRow ;        (* added to the existing alternation *)

repeat         := "REPEAT" intValue [ "AS" AT ] NL
                     block
                  "END" ;

forEachRow     := "FOR-EACH" "ROW" [ detailClause ] [ whereClause ] [ "AS" AT ] NL
                     block
                  "END" ;

block          := { statement NL } ;               (* zero or more; same statements, one level deeper *)

detailClause   := "Detail" STRING ;                (* orthogonal — mirrors OPEN-ROW/SELECT-ROWS/EXPECT *)
whereClause    := "WHERE" IDENT "=" value ;        (* reuses the existing MatchColumn/=/MatchValue grammar *)
intValue       := INTEGER | INTERP ;               (* {{@n}} allowed; must resolve to a non-negative int *)

(* EXPECT gains a trailing, optional modifier — NOT a new statement *)
expect         := "EXPECT" subject op value [ eventually ] ;
eventually     := "EVENTUALLY" [ "WITHIN" INTEGER "ATTEMPTS" ] [ "EVERY" INTEGER "ms" ] ;
```

Notes that fall out of the existing lexer (`Parsing/Lexer.cs`):

- `FOR-EACH`, `END`, `REPEAT`, `EVENTUALLY`, `ATTEMPTS`, `WITHIN`, `EVERY` are **contextual keywords**
  — plain `Identifier` tokens the parser matches by position, exactly like today's
  `ALL`/`NONE`/`WHERE`/`Detail`/`LOOKUP`. **No lexer change.** (`FOR-EACH` tokenizes as a single
  identifier because `-` is `IsIdentCont`, the same reason `SIGN-IN`/`OPEN-ROW` are one token.)
- Blocks are **`END`-terminated, not indentation-based** — `Newline` is already a real token and
  indentation is whitespace-skipped. `END` is unambiguous because no current verb starts with it.
- `AS @x` reuses the existing `At` token and the `AS @handle` parse path.

---

## 2. AST additions (`Parsing/Ast.cs`, codebase style)

Two new `Statement` records; `ExpectStmt` gains two optional fields (the same additive pattern as
`ExpectError` on `SaveStmt`/`ActionStmt`).

```csharp
/// <summary><c>REPEAT &lt;n&gt; [AS @i] … END</c> — run <see cref="Body"/> a fixed number of times.
/// <see cref="Count"/> resolves once at entry to a non-negative int; a negative or non-integer value is a
/// runtime error (never an unbounded loop). <see cref="IndexVar"/>, when set, binds the zero-based iteration
/// index as a loop-scoped variable readable via <c>{{@i}}</c>. Each iteration restores the navigation stack
/// to the depth it had at the loop's entry (see ForEachRowStmt remarks).</summary>
public sealed record RepeatStmt(
    Expression Count, string? IndexVar, IReadOnlyList<Statement> Body, SourceLocation Location) : Statement(Location);

/// <summary><c>FOR-EACH ROW [Detail "&lt;name&gt;"] [WHERE &lt;col&gt; = &lt;value&gt;] [AS @row] … END</c> —
/// iterate the rows of the current query (or the named detail query), optionally filtered by an equality
/// match. The matching set is <b>snapshotted at entry</b> by row identity, so body mutations (e.g. Delete)
/// don't shift the iteration. <see cref="RowVar"/>, when set, binds a loop-scoped row reference: read cells
/// with <c>@row.&lt;col&gt;</c> and push its PO with <c>OPEN-ROW @row</c>.
/// <para>At the end of every iteration the navigation stack is popped back to the depth it had at the loop's
/// entry, so the body may drill in (OPEN-ROW / FOLLOW) without manual GO-BACK bookkeeping. Restoration
/// refuses (loud fail) if a PO is left in edit — the same rule GO-BACK enforces.</para>
/// The WHERE fields reuse the OpenRow/SelectRows convention; <see cref="MatchOp"/> is whitelisted to
/// <see cref="ExpectOp.Eq"/> in this build.</summary>
public sealed record ForEachRowStmt(
    string? MatchColumn, ExpectOp? MatchOp, Expression? MatchValue, string? DetailName,
    string? RowVar, IReadOnlyList<Statement> Body, SourceLocation Location) : Statement(Location);

// ExpectStmt gains two optional trailing fields (additive, defaulted):
public sealed record ExpectStmt(
    ExpectSubject Subject, ExpectOp Op, Expression? Value, SourceLocation Location,
    int? EventuallyAttempts = null, int? EventuallyEveryMs = null) : Statement(Location);
```

**Reused, not reinvented:** `WHERE` matching (`MatchColumn`/`MatchOp`/`MatchValue`), `Detail "<name>"`
redirection, `AS @x` binding. The only genuinely new AST shape is `IReadOnlyList<Statement> Body` —
the first nesting in the language.

---

## 3. Parser changes (`Parsing/Parser.cs`)

- `ParseStatement` dispatch gains `REPEAT` and `FOR-EACH` arms; both call a new `ParseBlock()` that
  loops `ParseStatement` over `Newline`-separated lines until it sees `END` (or `Eof` →
  `parse-missing-block-end`). A `### StepHeader` inside a block is a parse error (steps are top-level
  reporting groups, not nestable).
- `END` with no open block → `parse-unexpected-token`.
- EXPECT parser: after the value, optionally consume `EVENTUALLY [WITHIN n ATTEMPTS] [EVERY n ms]`.
  Defaults for bare `EVENTUALLY`: **10 attempts / 250 ms** (named constants, documented).
- `ScriptAst`/`Step` are unchanged — `Body` lives inside the new statement records, so the top-level
  shape ("steps are reporting-only, execution is sequential") is preserved.

---

## 4. Interpreter semantics (`Runtime/Interpreter.cs` — the load-bearing part)

**`RepeatStmt`**
1. Resolve `Count` → `long n`; `n < 0` or non-integer → `state-invalid-bound`. `n == 0` runs the body
   zero times (empty is legal, not an error — define the error out of existence).
2. For `i` in `0..n-1`: bind loop-scoped `@i = i` (if `IndexVar` set), record
   `entryDepth = navStack.Depth`, run `Body`, then **restore to `entryDepth`** (pop extras; loud-fail
   with `state-loop-edit-left-open` if the top is a PO in edit).

**`ForEachRowStmt`**
1. Resolve the target query (current, or `Detail "<name>"` on `CurrentPo`). No current query →
   `state-no-current-query`.
2. Build the row set from the **currently loaded** `Query.Items`, applying WHERE (non-strict: 0 matches
   → body runs zero times). **Snapshot row identity** (key, not index) so deletes/inserts in the body
   don't corrupt iteration.
3. **No silent truncation:** if `TotalItems > loaded count`, emit a warning diagnostic naming the gap
   ("iterating N of M loaded; SEARCH/page to cover the rest") — honors the no-silent-caps rule.
   Full server-side paging is explicit future scope, not v1.
4. Per row: bind `@row` (if set) to a row reference, record `entryDepth`, run `Body`, restore depth.

**`@row` reads** — generalize the existing `@scope.Member` path (`VariableAttributeExpr`, today only
`@session`) so a loop-bound row variable resolves `@row.Column` to a cell value (string-service form,
same convention as `SET`/`WHERE`). `OPEN-ROW @row` is a new `OpenRowStmt` form (a row-ref alongside the
existing index/WHERE modes) that opens by the snapshotted identity, not a live index.

**`EXPECT … EVENTUALLY`** — wrap the existing single-shot assertion in a bounded retry: evaluate; on
failure, **refresh the relevant subject** (current PO `Refresh`, or current/detail query re-search) and
re-evaluate, up to the attempt cap, sleeping the interval between. Pass on first success; fail with
`assert-failed` after the cap (message records attempts made). This is the **one construct that touches
wall-clock** — see open decision (e).

**Bounds are unconditional.** REPEAT count fixed at entry; FOR-EACH set fixed at entry; EVENTUALLY
capped. No body can extend its own bound → totality preserved.

---

## 5. Diagnostics (new `ErrorKind`s — `Diagnostics/ErrorKind.cs`)

```csharp
public const string ParseMissingBlockEnd   = "parse-missing-block-end";   // FOR-EACH/REPEAT with no END
public const string StateInvalidBound       = "state-invalid-bound";       // REPEAT count < 0 / non-int
public const string StateLoopEditLeftOpen   = "state-loop-edit-left-open"; // iteration ended with PO in edit
```

Follows the existing prefix scheme (`parse-*` / `state-*`). `FOR-EACH` over zero matches and `REPEAT 0`
are **not** errors — they are empty, by design.

---

## 6. Integration touchpoints (don't let these drift)

- **`Diagnostics/VerbCatalog.cs`** — add `REPEAT` and `FOR-EACH` entries (category `control`, next to
  `REQUIRES`/`CLEANUP`); document `EVENTUALLY` under the `EXPECT` entry as a modifier. The
  catalog-reconciliation guard test asserts `Parser.KnownVerbs ⊆ VerbCatalog.Names`, so both must land
  here or the test fails.
- **`lint`** — block balance (`END` matching) and `REPEAT` bound-shape checks are static, so `lint`
  stays meaningful (exit code 2 still catches malformed loops without a server).
- **`CLAUDE.md`** — add the three rows to the `.visc` quick-reference table; add a `loops.visc` sample
  under `Vidyano.Script.Tool/samples/` doubling as a regression script.
- **Determinism** — REPEAT/FOR-EACH stay reproducible under `--seed`/`--now`; only `EVENTUALLY`
  introduces real-time, like the existing `@now` flow-by-elapsed-time behavior.

---

## 7. Explicitly out of scope (to stay total)

`WHILE` / `UNTIL` (unbounded), `IF/ELSE` (use `REQUIRES`), arithmetic/expression operators,
user-defined functions/macros, `BREAK`/`CONTINUE` (early-exit complicates the nav-depth-restore
guarantee — add later only if a real need appears). Anything genuinely computational still belongs in a
`TOOL` or the C# host.

---

## 8. Worked examples

```visc
# Delete every inactive customer — snapshot protects against index shift
OPEN MenuItem Sales/Customers
SEARCH ""
FOR-EACH ROW WHERE Status = "Inactive" AS @c
  OPEN-ROW @c
  ACTION Delete = "Yes, delete"
END                                  # nav stack auto-restored to the query frame each row

# Seed N fixtures
OPEN MenuItem Sales/Customers
REPEAT 5 AS @i
  ACTION New
  SET Name = "Load Test {{@i}}"
  SAVE
END

# Wait for async server-side processing to land
ACTION GenerateReport
EXPECT Status = "Completed" EVENTUALLY WITHIN 20 ATTEMPTS EVERY 500 ms
```

---

## 9. Open decisions (to settle before implementing)

| # | Decision | Options | Lean |
|---|---|---|---|
| a | Row binding | (a) explicit `AS @row` + `OPEN-ROW @row` · (b) implicit "current iteration row" that bare `OPEN-ROW` targets | **(a)** — obvious, composable, lets you read cells without opening |
| b | `REPEAT` index base | 0-based vs 1-based | 0-based (conventional); revisit if test-naming readability matters |
| c | FOR-EACH source | loaded `Query.Items` + warn-on-truncate vs require full paging in v1 | loaded + warn now; paging later |
| d | Nesting depth | allow arbitrary nesting vs cap at 2 | allow; it's free in the AST |
| e | `EVENTUALLY` | include in v1 vs defer | **defer if unsure** — it's the only construct touching wall-clock/determinism; REPEAT + FOR-EACH deliver most of the value cleanly |
| f | `REQUIRES`/`CLEANUP` inside a loop body | allow (define nested-skip semantics) vs restrict to top level | **restrict to top level** — keeps the gate model exactly as-is |

**Net:** `REPEAT` + `FOR-EACH` are the high-value, low-risk core and stay strictly total. `EVENTUALLY`
is worth it but is the one piece that bends the determinism property — easy to add later.
