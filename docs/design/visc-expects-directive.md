# RFC: `@expects` — declaring host-supplied `.visc` variables

> ✅ Design phase complete. Implemented: `ExpectsDirective` (AST), `Parser.ParseExpectsDirective`,
> the no-op interpreter handler, and the `VariableUseAnalyzer.CollectDeclarations` arm.

## Problem

The `.visc` variable-use lint (`VariableUseAnalyzer`, surfaced through `VidyanoScript.Lint`, shipped
5.64.0) reports `Variable '<name>' is not defined` for any `{{variable}}` whose value is injected by the
host at run time — `VidyanoScriptOptions.Variables`, the CLI `--var`, or `--env-prefix` — rather than
declared inside the script. The analyzer's "declared" set is built only from in-script bindings plus a
caller-supplied `expectedVariables` list, and a file opened in an editor has no way to convey those
caller names: the language server lints with `expectedVariables = none` (`Lint()` is called with no list),
so it flags every host-injected hole.

There was no runtime-safe way to silence this from inside the script. An `@x = …` assignment adds `x` to
the declared set **but also overwrites** the host-injected value at execution time (`Interpreter.cs`, the
`VariableAssignment` case writes straight into the variable table), so the only workaround was an
idempotent self-assign `@x = "{{x}}"` — a hack a reader can't interpret and every harness-driven script
would have to carry.

The concrete trigger: CronosCore's `VidyanoTestDriver` runs committed `.visc` regression tests and feeds
per-test values through `VidyanoScriptOptions.Variables`. Every such script showed red squiggles in the
IDE for variables that are correct and that the test relies on.

## Decision

Add an `@`-directive: **`@expects a, b`**.

```visc
@expects region, tenant
OPEN MenuItem Shop/{{region}}/Products
SEARCH "{{tenant}}"
```

- **Analyzer**: `CollectDeclarations` adds the names to `declared`, so `{{a}}` reads lint clean.
- **Interpreter**: a pure **no-op** — it never writes the variable table. A host value is still required and
  is never overwritten; an unsupplied declared variable still loud-fails (`resolve-variable`) the moment it
  is first interpolated. The directive silences the *static* check without weakening the runtime backstop.
- **Surface**: recognized in `ParseVariableOrModeAssignment` alongside `@mode` (bare, comma-separated
  names; no value grammar). Being a directive, it needs **no** `VerbCatalog`/`KeywordCatalog`/tmLanguage
  entry and **no** new `ErrorKind` — it highlights as a variable, exactly like `@mode`.
- **Placement**: conventionally top-of-file, but accepted anywhere and order-insensitive (the lint is
  presence-only). Unlike `@mode`, it is *not* header-enforced — see below.

### Why a directive, not a verb

`.visc` already splits its grammar: constructs that **configure the script's frame** are `@`-directives
(`@mode` sets guard semantics, `@app` names the target app), and constructs that **act at a step** are
verbs (`SIGN-IN`, `OPEN`, `EXPECT`). "These `{{vars}}` come from the host" declares the script's input
frame, so the `@` namespace — the same namespace as the `@name` bindings and `{{name}}` reads it talks
about — is its correct home. A 21st verb named `EXPECT-VARS`/`EXPECTS-VARS` would also read as a cousin of
the `EXPECT` *assertion* verb, which it is not.

### Why no header-only enforcement

`@mode` must precede the first executable statement because mode **changes execution semantics** and must
be known before any semantics-affecting statement runs — a real causal reason. `@expects` has *zero*
runtime effect and the analyzer is already position-insensitive, so a placement guard would be cargo-culted
(and would cost a new error kind for no benefit). Placement stays a documented convention, not a rule.

### Why no run-time validation or defaults (v1)

The stated problem is lint false-positives. Two tempting extras were deliberately excluded:

- **Fail-fast presence validation** (fail at the declaration when a var is unsupplied) would move the
  failure *off* the existing runtime backstop the verification approach wants preserved, and would couple a
  script to one host's wiring. Separable; not the ask.
- **Declared defaults** (`a ?? "x"`) are the one plausible future extension — but they require the
  interpreter to write `_vars`, breaking the clean "a declaration never touches state" rule, and
  `{{env:NAME ?? "fallback"}}` already covers env-sourced defaults. YAGNI for now.

## Designs considered (and rejected)

Four shapes were explored in parallel; each rejection is anchored on a concrete axis.

1. **Minimal verb** `EXPECTS-VARS a, b` — pure no-op, flat list, zero new error/keyword. *Adopted its
   no-op semantics; rejected its verb framing* on **consistency**: the construct is a frame declaration, so
   the `@` namespace is its home and avoids the `EXPECT` name collision.
2. **Maximal verb** `REQUIRE-VAR … END` with `OPTIONAL` / `?? default` / `AS type` / `:desc` — rejected on
   **complexity/speculation**: the annotations are inert (no tooling consumes them) and the default
   `_vars`-write muddies the no-op rule. The agent itself concluded "ship list-only now."
3. **Fail-fast verb** `EXPECT-VARS a, b [OPTIONAL]` (presence-check at the gate, hard-fail) — rejected on
   **consistency/spec-fit**: default-on validation moves failure off the runtime backstop and over-couples
   the script to host wiring.
4. **`@`-directive** `@expects a, b` — **chosen**. Lowest surface (no catalog/keyword/error burden), most
   consistent with `@mode`/`@app`, preserves the runtime backstop.

## The trap (failure mode if the directive call is wrong)

Discoverability: `@expects` won't appear in `vidyano help verbs` or editor hover (directives aren't
catalogued) — authors find it via docs only, the same blind spot `@mode`/`@app` have. If that bites, the
cheap fix is a "directives" section in help/docs, not a redesign. The deeper bet is that inputs are
declared up front (header-natural); if real scripts want mid-body, adjacent-to-use declarations, a
catalogued anywhere-verb would have been better. Judged unlikely — host inputs are script *parameters*.

## Tests

- `ExpectsDirectiveLintTests` — declared `{{x}}` lints clean (no `expectedVariables`), undeclared still
  flags `resolve-variable`, parse errors (no names / trailing comma / missing comma), AST shape,
  presence-only placement, case-insensitivity.
- `ExpectsDirectiveRuntimeTests` — the directive is a no-op pass; it does **not** populate the variable
  table; an unsupplied declared var still loud-fails at first use; a host-supplied value is never
  overwritten.
- `LintTests.ExpectsSample_LintsClean` — the shipped `samples/expects.visc` lints clean end-to-end.
