using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.ViewModel;

namespace Vidyano.Script.Runtime;

/// <summary>
/// Executes a parsed <see cref="Script"/> against a <see cref="VidyanoSession"/>. Holds the variable
/// table, expands <c>{{interpolations}}</c>, and turns the session's <see cref="OpResult"/>s into
/// <see cref="StatementResult"/>s that the CLI/MCP layers can render.
/// </summary>
public sealed class Interpreter
{
    private readonly VidyanoSession _session;
    private readonly Dictionary<string, object?> _vars;
    private readonly Func<string, string?> _envLookup;
    private readonly IReadOnlyDictionary<string, ScriptToolHandler> _tools;
    private readonly CancellationToken _cancellationToken;
    private readonly DateTimeOffset? _now;
    private readonly int? _seed;
    private GuardMode _mode = GuardMode.Navigation;
    private bool _statementsExecuted;

    // Per-run state. Reset at the top of every RunAsync so a REPL — which reuses one Interpreter
    // across typed lines — never carries a skip, a cleanup phase, a clock anchor, or an RNG stream
    // position from one line into the next.
    private bool _skipped;
    private bool _inCleanup;
    private DateTimeOffset _clockAnchor;
    private Stopwatch _stopwatch = null!;
    private Random _uuidRng = null!;
    private Random _randomRng = null!;

    public Interpreter(
        VidyanoSession session,
        IReadOnlyDictionary<string, object?>? initialVars = null,
        GuardMode mode = GuardMode.Navigation,
        IReadOnlyDictionary<string, ScriptToolHandler>? tools = null,
        CancellationToken cancellationToken = default,
        DateTimeOffset? now = null,
        int? seed = null,
        Func<string, string?>? envLookup = null,
        string? envPrefix = null)
    {
        _session = session;
        _vars = initialVars is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(initialVars, StringComparer.OrdinalIgnoreCase);
        // Default to the process environment. Hosts that need hermetic runs (NUnit / VidyanoTestDriver)
        // inject a closure here; --env-prefix bulk-binding below uses the *process* env directly.
        _envLookup = envLookup ?? Environment.GetEnvironmentVariable;
        _mode = mode;
        _tools = tools ?? new Dictionary<string, ScriptToolHandler>(StringComparer.OrdinalIgnoreCase);
        _cancellationToken = cancellationToken;
        _now = now;
        _seed = seed;
        BindEnvPrefix(envPrefix);
        ResetRunState();
    }

    /// <summary>Bulk-binds process environment variables whose names start with <paramref name="envPrefix"/>
    /// into the variable table, stripping the prefix (IConfiguration convention: <c>VIDYANO_REGION</c> →
    /// <c>{{REGION}}</c>). An explicit <c>--var</c> / <c>initialVars</c> binding always wins — entries
    /// already present are left untouched.</summary>
    private void BindEnvPrefix(string? envPrefix)
    {
        if (string.IsNullOrEmpty(envPrefix)) return;
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key) continue;
            if (!key.StartsWith(envPrefix, StringComparison.Ordinal)) continue;
            var stripped = key.Substring(envPrefix!.Length);
            if (stripped.Length == 0 || _vars.ContainsKey(stripped)) continue;
            _vars[stripped] = entry.Value as string;
        }
    }

    /// <summary>Effective guard mode after any <c>@mode</c> directives have been processed.</summary>
    public GuardMode Mode => _mode;

    /// <summary>Read-only view of the variable table after execution.</summary>
    public IReadOnlyDictionary<string, object?> Variables => _vars;

    public async Task<ScriptResult> RunAsync(ScriptAst script)
    {
        // Reset per-run state (D3): the REPL reuses one Interpreter across lines, so skip/cleanup
        // flags, the clock anchor, and the RNG streams must start clean for every run.
        ResetRunState();

        var steps = new List<StepResult>();
        var anyFail = false;
        foreach (var step in script.Steps)
        {
            var (stepRes, ok) = await RunStepAsync(step).ConfigureAwait(false);
            steps.Add(stepRes);
            if (!ok) anyFail = true;
        }
        return new ScriptResult(script.Location.SourcePath, !anyFail, steps, Array.Empty<Diagnostic>());
    }

    private async Task<(StepResult Result, bool Ok)> RunStepAsync(Step step)
    {
        var stmtResults = new List<StatementResult>(step.Statements.Count);
        var anyFail = false;
        foreach (var stmt in step.Statements)
        {
            var r = await RunStatementAsync(stmt).ConfigureAwait(false);
            stmtResults.Add(r);
            if (!r.Ok) anyFail = true;
        }
        var skipped = stmtResults.Count > 0 && stmtResults.All(s => s.Skipped);
        return (new StepResult(step.Label, step.Location, !anyFail, stmtResults, skipped), !anyFail);
    }

    private async Task<StatementResult> RunStatementAsync(Statement stmt)
    {
        // CLEANUP opens the cleanup phase: every statement after it runs even when the body was
        // skipped by an unmet REQUIRES. The marker itself is recorded as a normal pass.
        if (stmt is CleanupMarker)
        {
            _inCleanup = true;
            return Ok(stmt);
        }

        // Once an unmet REQUIRES has set the skip flag, body statements are recorded as skipped
        // without executing. Cleanup statements are exempt — they always run.
        if (_skipped && !_inCleanup)
            return Skip(stmt, null);

        // Track whether real work has happened, so @mode can be rejected after the first execution.
        var isMetaStmt = stmt is VariableAssignment or ModeDirective;
        if (!isMetaStmt) _statementsExecuted = true;

        // Reset the per-verb ClientOperations buffer before every executable verb (but NOT before
        // EXPECT — assertions consume what the *previous* verb produced). Meta statements
        // (@var, @mode) don't talk to the server, so they leave the buffer alone too.
        if (!isMetaStmt && stmt is not ExpectStmt)
            _session.ResetLastOperations();

        // Initial-PO gate: while Client.Initial is non-null the script is "frozen" against the
        // gate, matching the spec ("Until @initial == null, non-initial verbs error with
        // state-initial-pending"). Only meta statements, SAVE @initial, and EXPECTs that
        // observe the @initial scope are allowed through. `@mode = direct` is the documented
        // escape hatch. Lint never sees this — the guard requires a live Client.Initial.
        if (!isMetaStmt && _mode != GuardMode.Direct && _session.Client.Initial is not null
            && !IsInitialScoped(stmt))
        {
            return Fail(stmt, new Diagnostic(
                ErrorKind.StateInitialPending,
                "An Initial PO is pending — the server returned a gate (license terms, 2FA enrol, password reset) that must be satisfied first.",
                stmt.Location,
                Hint: "Drive @initial to a clean SAVE (SAVE @initial) or set `@mode = direct` at the top of the script to bypass the gate."));
        }

        switch (stmt)
        {
            case VariableAssignment va:
                {
                    var v = EvaluateExpression(va.Value);
                    if (!v.Ok) return Fail(stmt, v.Error!);
                    _vars[va.Name] = v.Value;
                    return Ok(stmt);
                }
            case ModeDirective md:
                {
                    if (_statementsExecuted)
                        return Fail(stmt, new Diagnostic(ErrorKind.ParseInvalidMode,
                            "@mode must be set before any executable statement runs.",
                            md.Location,
                            Hint: "Move the @mode line above the first SIGN-IN / OPEN / ... in the script."));
                    _mode = md.Mode;
                    return Ok(stmt);
                }
            case SignInStmt si:                return await DoSignIn(si).ConfigureAwait(false);
            case UseSessionStmt us:            return Fail(stmt, new Diagnostic(ErrorKind.ResolveSession, "Multi-session is not implemented in this build.", us.Location));
            case SignOutStmt so:               return Fail(stmt, new Diagnostic(ErrorKind.ResolveSession, "SIGN-OUT is not implemented in this build.", so.Location));
            case OpenPersistentObjectStmt op:  return await DoOpenPo(op).ConfigureAwait(false);
            case OpenQueryStmt oq:             return await DoOpenQuery(oq).ConfigureAwait(false);
            case OpenMenuItemStmt om:          return await DoOpenMenu(om).ConfigureAwait(false);
            case OpenRowStmt or:               return await DoOpenRow(or).ConfigureAwait(false);
            case SelectRowsStmt sr:            return await DoSelectRows(sr).ConfigureAwait(false);
            case GoBackStmt gb:                return Wrap(stmt, _session.GoBack(gb.Location));
            case EditStmt e:                   return Wrap(stmt, _session.Edit(e.Location));
            case CancelStmt c:                 return Wrap(stmt, _session.Cancel(c.Location));
            case SaveStmt sv:
                {
                    var saveRes = string.Equals(sv.Scope, "initial", StringComparison.OrdinalIgnoreCase)
                        ? await _session.SaveInitialAsync(sv.Location).ConfigureAwait(false)
                        : await _session.SaveAsync(sv.Location).ConfigureAwait(false);
                    return Wrap(stmt, saveRes);
                }
            case RefreshStmt rf:               return Wrap(stmt, await _session.RefreshAsync(rf.Location).ConfigureAwait(false));
            case SetStmt s:                    return await DoSet(s).ConfigureAwait(false);
            case ActionStmt a:                 return await DoAction(a).ConfigureAwait(false);
            case SearchStmt q:                 return await DoSearch(q).ConfigureAwait(false);
            case ExpectStmt ex:                return DoExpect(ex);
            case ToolCallStmt tc:              return await DoTool(tc).ConfigureAwait(false);
            case RequiresStmt rq:              return DoRequires(rq);
            case RequiresToolStmt rqt:         return DoRequiresTool(rqt);
        }
        return Fail(stmt, new Diagnostic(ErrorKind.ParseUnexpectedToken, $"Statement type {stmt.GetType().Name} is not supported.", stmt.Location));
    }

    /// <summary>Returns <c>true</c> when a statement is permitted while
    /// <see cref="Vidyano.Client.Initial"/> is pending. Two categories are allowed: statements
    /// that drive the gate (<c>SAVE @initial</c>, <c>SET @initial.X = …</c>), and any
    /// <c>EXPECT</c> — assertions are read-only, so blocking them only hurts observability
    /// without preventing mutation. All other statements trip the <c>state-initial-pending</c>
    /// guard.</summary>
    private static bool IsInitialScoped(Statement stmt) =>
        stmt switch
        {
            SaveStmt sv  => string.Equals(sv.Scope, "initial", StringComparison.OrdinalIgnoreCase),
            SetStmt set  => string.Equals(set.Scope, "initial", StringComparison.OrdinalIgnoreCase),
            ExpectStmt   => true,
            // REQUIRES is a read-only precondition gate; let it evaluate (an unevaluable gate skips,
            // per D8) rather than tripping the initial-pending guard.
            RequiresStmt or RequiresToolStmt => true,
            _ => false,
        };

    // --- statement handlers -------------------------------------------------------------------

    private async Task<StatementResult> DoSignIn(SignInStmt si)
    {
        string userName;
        string? password;
        if (si.FromEnv)
        {
            // SIGN-IN FROM ENV — credentials from the environment, loud-fail when unset (an empty
            // credential posted to the server is the footgun this form exists to close).
            var u = _envLookup("VIDYANO_USER");
            if (string.IsNullOrEmpty(u))
                return Fail(si, EnvMissing("VIDYANO_USER", si.Location));
            var p = _envLookup("VIDYANO_PASSWORD");
            if (string.IsNullOrEmpty(p))
                return Fail(si, EnvMissing("VIDYANO_PASSWORD", si.Location));
            userName = u!;
            password = p;
        }
        else
        {
            var user = EvaluateExpression(si.UserName!);
            if (!user.Ok) return Fail(si, user.Error!);
            var pwd = si.Password is null ? OpResult<object?>.Success(null) : EvaluateExpression(si.Password);
            if (!pwd.Ok) return Fail(si, pwd.Error!);
            userName = AsString(user.Value);
            password = pwd.Value as string;
        }
        string? language = null;
        if (si.Language is not null)
        {
            var lang = EvaluateExpression(si.Language);
            if (!lang.Ok) return Fail(si, lang.Error!);
            language = AsString(lang.Value);
        }
        var res = await _session.SignInAsync(userName, password, language, si.Location).ConfigureAwait(false);
        return Wrap(si, res);
    }

    private static Diagnostic EnvMissing(string name, SourceLocation loc) =>
        new(ErrorKind.ResolveEnv,
            $"Environment variable '{name}' is not set.",
            loc,
            Hint: "Set it in the shell, pass --var, or add a ?? fallback.");

    private async Task<StatementResult> DoOpenPo(OpenPersistentObjectStmt op)
    {
        var t = EvaluateExpression(op.Type);
        if (!t.Ok) return Fail(op, t.Error!);
        string? oid = null;
        if (op.ObjectId != null)
        {
            var o = EvaluateExpression(op.ObjectId);
            if (!o.Ok) return Fail(op, o.Error!);
            oid = AsString(o.Value);
        }
        var res = await _session.OpenPersistentObjectAsync(AsString(t.Value), oid, op.AsHandle, op.Location).ConfigureAwait(false);
        return Wrap(op, res);
    }

    private async Task<StatementResult> DoOpenQuery(OpenQueryStmt oq)
    {
        var t = EvaluateExpression(oq.Id);
        if (!t.Ok) return Fail(oq, t.Error!);
        var res = await _session.OpenQueryAsync(AsString(t.Value), oq.AsHandle, oq.Location).ConfigureAwait(false);
        return Wrap(oq, res);
    }

    private async Task<StatementResult> DoOpenMenu(OpenMenuItemStmt om)
    {
        var segments = new List<string>(om.PathSegments.Count);
        foreach (var seg in om.PathSegments)
        {
            var v = EvaluateExpression(seg);
            if (!v.Ok) return Fail(om, v.Error!);
            segments.Add(AsString(v.Value));
        }
        var res = await _session.OpenMenuItemAsync(segments, om.AsHandle, om.Location).ConfigureAwait(false);
        return Wrap(om, res);
    }

    private async Task<StatementResult> DoOpenRow(OpenRowStmt or)
    {
        if (or.MatchColumn != null)
        {
            var mv = EvaluateExpression(or.MatchValue!);
            if (!mv.Ok) return Fail(or, mv.Error!);
            var whereRes = await _session.OpenRowWhereAsync(or.MatchColumn, mv.Value, or.AsHandle, or.Location, or.DetailName).ConfigureAwait(false);
            return Wrap(or, whereRes);
        }

        var v = EvaluateExpression(or.Index!);
        if (!v.Ok) return Fail(or, v.Error!);
        if (!TryCoerceInt(v.Value, out var index))
            return Fail(or, new Diagnostic(ErrorKind.ParseInvalidValue, "OPEN-ROW needs an integer index.", or.Location));
        var res = await _session.OpenRowAsync(index, or.AsHandle, or.Location, or.DetailName).ConfigureAwait(false);
        return Wrap(or, res);
    }

    private async Task<StatementResult> DoSelectRows(SelectRowsStmt sr)
    {
        int? index = null;
        object? matchValue = null;
        if (sr.MatchColumn != null)
        {
            var mv = EvaluateExpression(sr.MatchValue!);
            if (!mv.Ok) return Fail(sr, mv.Error!);
            matchValue = mv.Value;
        }
        else if (sr.Index != null)
        {
            // Positional index — the positive selection (`SELECT-ROWS <i>`) or, when All is set, the
            // EXCEPT exclusion row (`SELECT-ROWS ALL EXCEPT <i>`). Coerce in the interpreter layer (mirrors
            // DoOpenRow) so the "needs an integer index" diagnostic comes from the same place for both verbs.
            var v = EvaluateExpression(sr.Index);
            if (!v.Ok) return Fail(sr, v.Error!);
            if (!TryCoerceInt(v.Value, out var idx))
                return Fail(sr, new Diagnostic(ErrorKind.ParseInvalidValue, "SELECT-ROWS needs an integer index.", sr.Location));
            index = idx;
        }
        var res = await _session.SelectRowsAsync(sr.All, sr.None, index, sr.MatchColumn, matchValue, sr.DetailName, sr.Location).ConfigureAwait(false);
        return Wrap(sr, res);
    }

    private async Task<StatementResult> DoSet(SetStmt s)
    {
        var v = EvaluateExpression(s.Value);
        if (!v.Ok) return Fail(s, v.Error!);
        ReferenceHint? hint = s.Hint is null ? null : new ReferenceHint(s.Hint.Value, AsString(v.Value));
        var res = s.Scope is null
            ? await _session.SetAttributeAsync(s.Attribute, v.Value, s.Location, hint).ConfigureAwait(false)
            : await _session.SetScopedAttributeAsync(s.Scope, s.Attribute, v.Value, hint, s.Location).ConfigureAwait(false);
        return Wrap(s, res);
    }

    private async Task<StatementResult> DoAction(ActionStmt a)
    {
        Dictionary<string, string>? parameters = null;
        if (a.Parameters != null)
        {
            parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, expr) in a.Parameters)
            {
                var v = EvaluateExpression(expr);
                if (!v.Ok) return Fail(a, v.Error!);
                parameters[k] = AsString(v.Value);
            }
        }
        object? option = null;
        if (a.Option != null)
        {
            var ov = EvaluateExpression(a.Option);
            if (!ov.Ok) return Fail(a, ov.Error!);
            option = ov.Value;
            // ACTION X = null and ACTION X = ID null are footguns: the option clause was written
            // (so the author meant *something*), but a null value silently degrades to bare ACTION X.
            // Catch it here while we still know `a.Option != null` — the session-level overload only
            // sees the post-evaluation value and can't tell "no clause" from "null-valued clause".
            if (option is null)
                return Fail(a, new Diagnostic(
                    ErrorKind.ParseInvalidValue,
                    $"ACTION {a.ActionName} = null is not a valid option.",
                    a.Location,
                    Hint: "Omit the `= …` clause to invoke without an option, or pass a label string / `ID <index>`."));
        }
        var res = await _session.ExecuteActionAsync(a.ActionName, parameters, option, a.OptionHint, a.Location, a.DetailName).ConfigureAwait(false);
        return Wrap(a, res);
    }

    private async Task<StatementResult> DoSearch(SearchStmt q)
    {
        var v = EvaluateExpression(q.Text);
        if (!v.Ok) return Fail(q, v.Error!);
        var res = await _session.SearchAsync(AsString(v.Value), q.Location).ConfigureAwait(false);
        return Wrap(q, res);
    }

    // --- TOOL ---------------------------------------------------------------------------------

    private async Task<StatementResult> DoTool(ToolCallStmt tc)
    {
        if (!_tools.TryGetValue(tc.Name, out var handler))
        {
            return Fail(tc, new Diagnostic(
                ErrorKind.ToolUnknown,
                $"No tool named '{tc.Name}' was registered.",
                tc.Location,
                Hint: Suggester.Hint(tc.Name, _tools.Keys, "tool")
                      ?? "Register one via VidyanoScriptOptions.Tools[\"name\"] = (ctx, args, ct) => …"));
        }

        // Evaluate all arguments up-front so a bad value surfaces before the tool side-effects.
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, expr) in tc.Args)
        {
            var v = EvaluateExpression(expr);
            if (!v.Ok) return Fail(tc, v.Error!);
            args[k] = v.Value;
        }

        var ctx = new ToolContext(_session, _vars, tc.Location, tc.Name);
        ScriptToolResult result;
        try
        {
            result = await handler(ctx, args, _cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            return Fail(tc, new Diagnostic(
                ErrorKind.ToolError,
                $"[tool:{tc.Name}] cancelled.",
                tc.Location));
        }
        catch (Exception ex)
        {
            return Fail(tc, new Diagnostic(
                ErrorKind.ToolError,
                $"[tool:{tc.Name}] {ex.Message}",
                tc.Location));
        }

        if (result is null)
        {
            return Fail(tc, new Diagnostic(
                ErrorKind.ToolError,
                $"[tool:{tc.Name}] returned null. Use ScriptToolResult.Ok or ScriptToolResult.Value(...).",
                tc.Location));
        }

        if (tc.ResultVariable is not null)
        {
            if (!result.HasValue)
                return Fail(tc, new Diagnostic(
                    ErrorKind.ToolNoValue,
                    $"[tool:{tc.Name}] returned ScriptToolResult.Ok but the call binds to @{tc.ResultVariable}.",
                    tc.Location,
                    Hint: "Either drop the `-> @var` or return ScriptToolResult.Value(...) from the handler."));
            _vars[tc.ResultVariable] = result.ValueOrNull;
        }

        return Ok(tc);
    }

    /// <summary>Implementation of <see cref="IScriptToolContext"/> handed to a
    /// <see cref="ScriptToolHandler"/>. <see cref="Variables"/> aliases the interpreter's own
    /// variable table — writes are immediately visible to subsequent <c>{{var}}</c> reads.</summary>
    private sealed class ToolContext(VidyanoSession session, Dictionary<string, object?> variables, SourceLocation location, string toolName) : IScriptToolContext
    {
        public VidyanoSession Session => session;
        public IDictionary<string, object?> Variables => variables;
        public SourceLocation Location => location;
        public string ToolName => toolName;
    }

    // --- EXPECT -------------------------------------------------------------------------------

    private StatementResult DoExpect(ExpectStmt ex)
    {
        // ClientOperation needs a custom shape: "exists an op of type X (optionally matching value V)?"
        // rather than the regular "lhs op rhs" comparison.
        if (ex.Subject.Kind == ExpectSubjectKind.ClientOperation)
            return DoExpectClientOperation(ex);

        // Resolve subject value (lhs)
        var lhs = ResolveExpectSubject(ex.Subject, ex.Location);
        if (!lhs.Ok) return Fail(ex, lhs.Error!);

        // Handle IS-shaped operators
        if (ex.Op == ExpectOp.IsNull)
            return lhs.Value is null
                ? Ok(ex)
                : Fail(ex, AssertDiag(ex, "null", lhs.Value));
        if (ex.Op == ExpectOp.IsNotNull)
            return lhs.Value is not null
                ? Ok(ex)
                : Fail(ex, AssertDiag(ex, "non-null", null));

        // IS [NOT] AVAILABLE / VISIBLE / READONLY / REQUIRED
        if (ex.Op is ExpectOp.Is or ExpectOp.IsNot)
        {
            if (lhs.Value is not bool flag)
                return Fail(ex, new Diagnostic(ErrorKind.ParseUnexpectedToken,
                    "IS <flag> can only be used with boolean subjects (Action IS AVAILABLE, Attribute IS VISIBLE, etc.).",
                    ex.Location));
            var expected = ex.Op == ExpectOp.Is;
            return flag == expected
                ? Ok(ex)
                : Fail(ex, AssertDiag(ex, expected, !expected));
        }

        // Comparison form
        var rhs = EvaluateExpression(ex.Value!);
        if (!rhs.Ok) return Fail(ex, rhs.Error!);

        // MATCHES is split out from the generic comparison so a malformed pattern surfaces as a
        // clean authoring diagnostic rather than letting RegexParseException escape and abort the run.
        if (ex.Op == ExpectOp.Matches)
        {
            if (!TryRegexMatches(lhs.Value, rhs.Value, out var matched, out var patternError))
                return Fail(ex, new Diagnostic(ErrorKind.ParseInvalidValue,
                    $"MATCHES pattern is not a valid regular expression: {patternError}", ex.Location,
                    Hint: "Fix the regex literal on the right-hand side of MATCHES."));
            return matched ? Ok(ex) : Fail(ex, AssertDiag(ex, rhs.Value, lhs.Value));
        }

        var ok = Compare(lhs.Value, rhs.Value, ex.Op);
        return ok ? Ok(ex) : Fail(ex, AssertDiag(ex, rhs.Value, lhs.Value));
    }

    /// <summary>Evaluates a <c>REQUIRES &lt;assertion&gt;</c> gate using the exact same logic as
    /// <c>EXPECT</c>. When the gate holds the statement is a normal pass and execution continues.
    /// When it does not hold — or evaluating it raises a resolution error (D8) — the skip flag is set
    /// and the statement is recorded as a skipped pass with an informational diagnostic.</summary>
    private StatementResult DoRequires(RequiresStmt rq)
    {
        var probe = new ExpectStmt(rq.Subject, rq.Op, rq.Value, rq.Location);
        var result = DoExpect(probe);
        if (result.Ok)
            return Ok(rq);

        var reason = result.Diagnostics.Count > 0 ? result.Diagnostics[0].Message : "precondition not met";
        _skipped = true;
        return Skip(rq, new Diagnostic(
            ErrorKind.StateRequiresUnmet,
            $"Precondition not met ({reason}) — skipping the rest of the body.",
            rq.Location,
            Hint: "Statements after CLEANUP still run. REQUIRES gates the body, it does not fail the run."));
    }

    /// <summary>Evaluates a <c>REQUIRES TOOL &lt;name&gt;</c> capability gate: satisfied iff a tool of
    /// that name is registered. Unsatisfied gates skip the rest of the body.</summary>
    private StatementResult DoRequiresTool(RequiresToolStmt rqt)
    {
        if (_tools.ContainsKey(rqt.ToolName))
            return Ok(rqt);

        _skipped = true;
        return Skip(rqt, new Diagnostic(
            ErrorKind.StateRequiresUnmet,
            $"Required tool '{rqt.ToolName}' is not registered — skipping the rest of the body.",
            rqt.Location,
            Hint: "Register it via VidyanoScriptOptions.Tools or pass --tools <pack.dll>. Statements after CLEANUP still run."));
    }

    private StatementResult DoExpectClientOperation(ExpectStmt ex)
    {
        var opType = ex.Subject.Name!;
        var matches = _session.LastOperations
            .Where(o => string.Equals(o.Type, opType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // IS NULL → no op of that type fired since the previous verb.
        if (ex.Op == ExpectOp.IsNull)
        {
            if (matches.Count == 0) return Ok(ex);
            return Fail(ex, new Diagnostic(
                ErrorKind.AssertFailed,
                $"Expected no {opType} operation but saw {matches.Count}.",
                ex.Location,
                Details: new Dictionary<string, object?>
                {
                    ["expected"] = "none",
                    ["actual"] = matches.Select(m => m.PrimaryValue).ToArray(),
                }));
        }

        // Bare / IS NOT NULL → at least one op of that type fired.
        if (ex.Op == ExpectOp.IsNotNull || ex.Value is null)
        {
            if (matches.Count > 0) return Ok(ex);
            var seen = _session.LastOperations.Select(o => o.Type).Distinct().ToArray();
            return Fail(ex, new Diagnostic(
                ErrorKind.AssertFailed,
                $"Expected a '{opType}' client operation but saw {(seen.Length == 0 ? "none" : string.Join(", ", seen))}.",
                ex.Location,
                Hint: seen.Length > 0 ? Suggester.Hint(opType, seen) : "The previous verb didn't return any operations — check whether it ran successfully."));
        }

        // Value-matching forms: =, !=, CONTAINS, NOT CONTAINS — all compare against the
        // canonical field for that op type (PrimaryValue), so callers don't need to know the
        // protocol shape.
        if (ex.Op is not (ExpectOp.Eq or ExpectOp.NotEq or ExpectOp.Contains or ExpectOp.NotContains))
            return Fail(ex, new Diagnostic(
                ErrorKind.ParseUnexpectedToken,
                "EXPECT ClientOperation supports '=', '!=', CONTAINS, NOT CONTAINS, IS NULL, IS NOT NULL.",
                ex.Location));

        var rhs = EvaluateExpression(ex.Value!);
        if (!rhs.Ok) return Fail(ex, rhs.Error!);
        var expected = AsString(rhs.Value);

        // Predicate per op: positive forms need at least one match; negative forms need zero.
        var positive = ex.Op is ExpectOp.Eq or ExpectOp.Contains;
        var partial  = ex.Op is ExpectOp.Contains or ExpectOp.NotContains;

        bool Matches(ClientOperation m) => partial
            ? ContainsSubstring(m.PrimaryValue, expected)
            : string.Equals(m.PrimaryValue, expected, StringComparison.Ordinal);

        var matchedAny = matches.Any(Matches);
        if (positive ? matchedAny : !matchedAny)
            return Ok(ex);

        var opWord = ex.Op switch
        {
            ExpectOp.Eq          => "=",
            ExpectOp.NotEq       => "!=",
            ExpectOp.Contains    => "CONTAINS",
            ExpectOp.NotContains => "NOT CONTAINS",
            _ => "?",
        };

        if (positive)
        {
            var seenValues = matches.Select(m => m.PrimaryValue ?? "<null>").ToArray();
            return Fail(ex, new Diagnostic(
                ErrorKind.AssertFailed,
                matches.Count == 0
                    ? $"Expected {opType} {opWord} \"{expected}\" but no {opType} operation fired."
                    : $"Expected {opType} {opWord} \"{expected}\" but saw {opType} for {string.Join(", ", seenValues.Select(v => $"\"{v}\""))}.",
                ex.Location,
                Hint: matches.Count == 0 ? null : Suggester.Hint(expected, seenValues!),
                Details: new Dictionary<string, object?>
                {
                    ["expected"] = expected,
                    ["operator"] = opWord,
                    ["actual"]   = seenValues,
                }));
        }

        return Fail(ex, new Diagnostic(
            ErrorKind.AssertFailed,
            $"Expected no {opType} {opWord} \"{expected}\" but a matching one fired.",
            ex.Location));
    }

    private OpResult<object?> ResolveExpectSubject(ExpectSubject subj, SourceLocation loc)
    {
        var po = _session.CurrentPo;
        var query = _session.CurrentQuery;

        // EXPECT Detail "<name>" IS [NOT] AVAILABLE | VISIBLE — flag check against PO.Queries.
        // Handled before the DetailName-as-query-redirect path because IS AVAILABLE specifically
        // means "does the detail exist?" so a missing detail must not surface as a resolve error.
        if (subj.Kind == ExpectSubjectKind.DetailQueryFlag)
        {
            if (po is null)
                return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo,
                    "EXPECT Detail needs a current PersistentObject.", loc));
            var present = po.Queries.TryGetValue(subj.DetailName!, out var dq);
            return subj.Flag switch
            {
                // IS [NOT] AVAILABLE — presence in PO.Queries.
                AttributeFlagKind.Available => OpResult<object?>.Success((object?)present),
                // IS [NOT] VISIBLE — !IsHidden when present; missing detail surfaces a diagnostic
                // with a suggester hint over the available detail names.
                AttributeFlagKind.Visible   => present
                    ? OpResult<object?>.Success((object?)!dq!.IsHidden)
                    : Fail<object?>(new Diagnostic(ErrorKind.ResolveQuery,
                        $"PersistentObject '{po.Type}' has no detail query '{subj.DetailName}'.", loc,
                        Hint: Suggester.Hint(subj.DetailName!, po.Queries.Keys))),
                _ => Fail<object?>(new Diagnostic(ErrorKind.ParseUnexpectedToken,
                        "EXPECT Detail flag must be AVAILABLE or VISIBLE.", loc)),
            };
        }

        // EXPECT Detail "<name>" <query-subject>: reroute all query-family subjects to the named detail
        // query on the current PO. Read whatever the detail Query has in memory (no forced search) —
        // identical to the current-query EXPECT TotalItems contract.
        if (subj.DetailName is not null)
        {
            var detail = _session.ResolveDetail(subj.DetailName, loc);
            if (!detail.Ok) return Fail<object?>(detail.Error!);
            query = detail.Value;
        }

        switch (subj.Kind)
        {
            case ExpectSubjectKind.Attribute:
                {
                    if (subj.Scope is not null)
                    {
                        var scopedAttr = _session.GetScopedAttributeValue(subj.Scope, subj.Name!, loc);
                        if (scopedAttr.Ok) return scopedAttr;
                        // `@scope.Prop` may be a PO scalar (FullTypeName, Type, IsNew, …) rather
                        // than an attribute — common for @initial where the script wants to
                        // identify the gate by FullTypeName. Fall back to PO-property lookup,
                        // but only when the attribute genuinely doesn't exist; visibility errors
                        // still surface as-is.
                        if (scopedAttr.Error!.Kind == ErrorKind.ResolveAttribute)
                        {
                            var scopePo = _session.ResolveScopePo(subj.Scope, loc);
                            if (scopePo.Ok)
                            {
                                var poProp = ReadPoProperty(scopePo.Value!, subj.Name!, loc);
                                if (poProp.Ok) return poProp;
                                // Neither attribute nor PO scalar — reshape the diagnostic so the
                                // hint reflects both search spaces. The original attribute-only
                                // hint (e.g. "did you mean Customer?") is misleading when the user
                                // typed `FullTypeName`.
                                var attrCandidates = scopePo.Value!.Attributes.Select(a => a.Name);
                                var poScalars = new[] { "Type", "FullTypeName", "Breadcrumb", "IsNew", "IsHidden", "ObjectId", "Label", "Tag" };
                                return Fail<object?>(new Diagnostic(
                                    ErrorKind.ResolveAttribute,
                                    $"`@{subj.Scope}` has no attribute or PO property named '{subj.Name}'.",
                                    loc,
                                    Hint: Suggester.Hint(subj.Name!, attrCandidates.Concat(poScalars))
                                          ?? "PO scalars: Type, FullTypeName, Breadcrumb, IsNew, IsHidden, ObjectId, Label, Tag."));
                            }
                        }
                        return scopedAttr;
                    }
                    if (po is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo, "EXPECT on an attribute needs a current PersistentObject.", loc));
                    var attr = po.GetAttribute(subj.Name!);
                    if (attr is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.ResolveAttribute,
                            $"Attribute '{subj.Name}' does not exist on {po.Type}.",
                            loc,
                            Hint: Suggester.Hint(subj.Name!, po.Attributes.Select(a => a.Name))));
                    if (!attr.IsVisible)
                        return Fail<object?>(new Diagnostic(ErrorKind.GuardAttributeHidden,
                            $"Attribute '{subj.Name}' exists on {po.Type} but is hidden — the UI cannot read it.",
                            loc));
                    return OpResult<object?>.Success(attr.Value);
                }
            case ExpectSubjectKind.Action:
                {
                    if (po is null && query is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo, "EXPECT Action needs a current PO or Query.", loc));
                    var action = po?.GetAction(subj.Name!) ?? query?.GetAction(subj.Name!);
                    var candidates = (po?.Actions ?? Array.Empty<Vidyano.ViewModel.Actions.ActionBase>())
                        .Concat(po?.PinnedActions ?? Array.Empty<Vidyano.ViewModel.Actions.ActionBase>())
                        .Concat(query?.Actions ?? Array.Empty<Vidyano.ViewModel.Actions.QueryAction>())
                        .Select(a => a.Name);
                    if (action is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.ResolveAction,
                            $"Action '{subj.Name}' does not exist here.", loc,
                            Hint: Suggester.Hint(subj.Name!, candidates)));
                    // IS VISIBLE → IsVisible; IS AVAILABLE → CanExecute. None (no flag yet) defaults
                    // to CanExecute — preserves the bare `EXPECT Action X` historical sense.
                    return subj.Flag switch
                    {
                        AttributeFlagKind.Visible   => OpResult<object?>.Success((object?)action.IsVisible),
                        AttributeFlagKind.Available => OpResult<object?>.Success((object?)action.CanExecute),
                        _                           => OpResult<object?>.Success((object?)action.CanExecute),
                    };
                }
            case ExpectSubjectKind.AttributeFlag:
                {
                    PersistentObjectAttribute? attr;
                    if (subj.Scope is not null)
                    {
                        var scoped = _session.ResolveScopedAttribute(subj.Scope, subj.Name!, loc);
                        if (!scoped.Ok) return Fail<object?>(scoped.Error!);
                        attr = scoped.Value!.Attribute;
                    }
                    else
                    {
                        if (po is null)
                            return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo, "EXPECT Attribute needs a current PersistentObject.", loc));
                        attr = po.GetAttribute(subj.Name!);
                        if (attr is null)
                            return Fail<object?>(new Diagnostic(ErrorKind.ResolveAttribute,
                                $"Attribute '{subj.Name}' does not exist on {po.Type}.", loc,
                                Hint: Suggester.Hint(subj.Name!, po.Attributes.Select(a => a.Name))));
                    }
                    return subj.Flag switch
                    {
                        AttributeFlagKind.Visible   => OpResult<object?>.Success((object?)attr.IsVisible),
                        AttributeFlagKind.ReadOnly  => OpResult<object?>.Success((object?)attr.IsReadOnly),
                        AttributeFlagKind.Required  => OpResult<object?>.Success((object?)attr.IsRequired),
                        // IS AVAILABLE mirrors SetAttributeOn's guard: settable means visible AND not read-only.
                        AttributeFlagKind.Available => OpResult<object?>.Success((object?)(attr.IsVisible && !attr.IsReadOnly)),
                        _                           => OpResult<object?>.Success((object?)attr.IsVisible),
                    };
                }
            case ExpectSubjectKind.Notification:
                return OpResult<object?>.Success(po?.Notification);
            case ExpectSubjectKind.NotificationType:
                return OpResult<object?>.Success(po?.HasNotification == true ? po.NotificationType.ToString() : null);
            case ExpectSubjectKind.IsDirty:
                return OpResult<object?>.Success((object?)(po?.IsDirty ?? false));
            case ExpectSubjectKind.IsInEdit:
                return OpResult<object?>.Success((object?)(po?.IsInEdit ?? false));
            case ExpectSubjectKind.TotalItems:
                if (query is null)
                    return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, "EXPECT TotalItems needs a current Query.", loc));
                return OpResult<object?>.Success((object?)query.TotalItems);
            case ExpectSubjectKind.SelectionCount:
                if (query is null)
                    return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, "EXPECT Selection.Count needs a current Query.", loc));
                return OpResult<object?>.Success((object?)query.SelectedItems.Count);
            case ExpectSubjectKind.SelectionAllSelected:
                if (query is null)
                    return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, "EXPECT Selection.AllSelected needs a current Query.", loc));
                return OpResult<object?>.Success((object?)query.AllSelected);
            case ExpectSubjectKind.NavStackDepth:
                return OpResult<object?>.Success((object?)_session.NavStackDepth);
            case ExpectSubjectKind.NavStackTopKind:
                return OpResult<object?>.Success((object?)_session.NavStackTop?.Kind);
            case ExpectSubjectKind.NavStackTopName:
                return OpResult<object?>.Success((object?)_session.NavStackTop?.Name);
            case ExpectSubjectKind.NavStackTopIsDialog:
                return OpResult<object?>.Success((object?)(_session.NavStackTop?.IsDialog ?? false));
            case ExpectSubjectKind.AttributeLabel:
                {
                    if (subj.Scope is not null)
                    {
                        var scoped = _session.ResolveScopedAttribute(subj.Scope, subj.Name!, loc);
                        if (!scoped.Ok) return Fail<object?>(scoped.Error!);
                        return OpResult<object?>.Success((object?)scoped.Value!.Attribute.Label);
                    }
                    if (po is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo, "EXPECT Attribute LABEL needs a current PersistentObject.", loc));
                    var attr = po.GetAttribute(subj.Name!);
                    if (attr is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.ResolveAttribute,
                            $"Attribute '{subj.Name}' does not exist on {po.Type}.", loc,
                            Hint: Suggester.Hint(subj.Name!, po.Attributes.Select(a => a.Name))));
                    return OpResult<object?>.Success((object?)attr.Label);
                }
            case ExpectSubjectKind.ActionDisplayName:
                {
                    if (po is null && query is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo, "EXPECT Action DISPLAY-NAME needs a current PO or Query.", loc));
                    var action = po?.GetAction(subj.Name!) ?? query?.GetAction(subj.Name!);
                    if (action is null)
                    {
                        var candidates = (po?.Actions ?? Array.Empty<Vidyano.ViewModel.Actions.ActionBase>())
                            .Concat(po?.PinnedActions ?? Array.Empty<Vidyano.ViewModel.Actions.ActionBase>())
                            .Concat(query?.Actions ?? Array.Empty<Vidyano.ViewModel.Actions.QueryAction>())
                            .Select(a => a.Name);
                        return Fail<object?>(new Diagnostic(ErrorKind.ResolveAction,
                            $"Action '{subj.Name}' does not exist here.", loc,
                            Hint: Suggester.Hint(subj.Name!, candidates)));
                    }
                    return OpResult<object?>.Success((object?)action.DisplayName);
                }
            case ExpectSubjectKind.QueryLabel:
                {
                    if (query is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, "EXPECT Query LABEL needs a current Query.", loc));
                    return OpResult<object?>.Success((object?)query.Label);
                }
            case ExpectSubjectKind.Expression:
                return EvaluateExpression(subj.Lhs!);
            case ExpectSubjectKind.AttributeType:
                {
                    var ar = ResolveAttributeForMetadata(subj, loc);
                    if (!ar.Ok) return Fail<object?>(ar.Error!);
                    return OpResult<object?>.Success((object?)ar.Value!.Type);
                }
            case ExpectSubjectKind.AttributeTag:
                {
                    var ar = ResolveAttributeForMetadata(subj, loc);
                    if (!ar.Ok) return Fail<object?>(ar.Error!);
                    return OpResult<object?>.Success(ar.Value!.Tag);
                }
            case ExpectSubjectKind.AttributeTypeHint:
                {
                    var ar = ResolveAttributeForMetadata(subj, loc);
                    if (!ar.Ok) return Fail<object?>(ar.Error!);
                    var hints = ar.Value!.TypeHints;
                    if (hints is null)
                        return OpResult<object?>.Success((object?)null);
                    // KeyValueList is case-insensitive in its TryGetValue; do a manual scan so any
                    // dictionary impl (including read-only) works the same way.
                    foreach (var kv in hints)
                        if (string.Equals(kv.Key, subj.MetadataKey, StringComparison.OrdinalIgnoreCase))
                            return OpResult<object?>.Success((object?)kv.Value);
                    return OpResult<object?>.Success((object?)null);
                }
            case ExpectSubjectKind.PoProperty:
                {
                    if (po is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo, $"EXPECT PO.{subj.Name} needs a current PersistentObject.", loc));
                    return ReadPoProperty(po, subj.Name!, loc);
                }
            case ExpectSubjectKind.PoMetadata:
                {
                    if (po is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo, "EXPECT PO.Metadata needs a current PersistentObject.", loc));
                    return OpResult<object?>.Success(BagLookup(po.Metadata, subj.MetadataKey!));
                }
            case ExpectSubjectKind.PoNavigationHints:
                {
                    if (po is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo, "EXPECT PO.NavigationHints needs a current PersistentObject.", loc));
                    return OpResult<object?>.Success(BagLookup(po.NavigationHints, subj.MetadataKey!));
                }
            case ExpectSubjectKind.QueryProperty:
                {
                    if (query is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, $"EXPECT Query.{subj.Name} needs a current Query.", loc));
                    return ReadQueryProperty(query, subj.Name!, loc);
                }
            case ExpectSubjectKind.QueryMetadata:
                {
                    if (query is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, "EXPECT Query.Metadata needs a current Query.", loc));
                    return OpResult<object?>.Success(BagLookup(query.Metadata, subj.MetadataKey!));
                }
            case ExpectSubjectKind.QueryNavigationHints:
                {
                    if (query is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, "EXPECT Query.NavigationHints needs a current Query.", loc));
                    return OpResult<object?>.Success(BagLookup(query.NavigationHints, subj.MetadataKey!));
                }
            case ExpectSubjectKind.QueryPoProperty:
                {
                    if (query is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, $"EXPECT Query.PersistentObject.{subj.Name} needs a current Query.", loc));
                    var qpo = query.PersistentObject;
                    if (qpo is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentPo,
                            $"Query '{query.Name}' has no associated PersistentObject.", loc));
                    return ReadPoProperty(qpo, subj.Name!, loc);
                }
            case ExpectSubjectKind.ScopedRoot:
                {
                    // `EXPECT @initial IS NULL` / `EXPECT @session IS NOT NULL` — surface the PO
                    // directly. Going through ResolveScopePo here would turn an unbound scope
                    // into a `state-no-session` diagnostic, but the entire point of this subject
                    // shape is to *observe* presence/absence, not error on it. Bypass the
                    // diagnostic by reading Client.Initial / Client.Session straight.
                    if (string.Equals(subj.Scope, "initial", StringComparison.OrdinalIgnoreCase))
                        return OpResult<object?>.Success((object?)_session.Client.Initial);
                    if (string.Equals(subj.Scope, "session", StringComparison.OrdinalIgnoreCase))
                        return OpResult<object?>.Success((object?)_session.Client.Session);
                    return Fail<object?>(new Diagnostic(ErrorKind.ResolveVariable,
                        $"Unknown variable scope '@{subj.Scope}'.", loc,
                        Hint: "Valid scopes: @session, @initial."));
                }
            case ExpectSubjectKind.QueryColumn:
                {
                    if (query is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.StateNoCurrentQuery, "EXPECT Query.Columns needs a current Query.", loc));
                    var col = (query.Columns ?? Array.Empty<Vidyano.ViewModel.QueryColumn>())
                        .FirstOrDefault(c => string.Equals(c.Name, subj.Name, StringComparison.OrdinalIgnoreCase));
                    if (col is null)
                        return Fail<object?>(new Diagnostic(ErrorKind.ResolveAttribute,
                            $"Query '{query.Name}' has no column named '{subj.Name}'.", loc,
                            Hint: Suggester.Hint(subj.Name!, (query.Columns ?? Array.Empty<Vidyano.ViewModel.QueryColumn>()).Select(c => c.Name))));
                    return ReadColumnProperty(col, subj.MetadataKey!, loc);
                }
        }
        return Fail<object?>(new Diagnostic(ErrorKind.ParseUnexpectedToken, "Unhandled EXPECT subject.", loc));
    }

    /// <summary>Resolves the target attribute for the metadata-shaped EXPECTs (Type / Tag /
    /// TypeHint). Handles scoped <c>@session</c> attributes uniformly with bare ones.</summary>
    private OpResult<PersistentObjectAttribute> ResolveAttributeForMetadata(ExpectSubject subj, SourceLocation loc)
    {
        if (subj.Scope is not null)
        {
            var scoped = _session.ResolveScopedAttribute(subj.Scope, subj.Name!, loc);
            if (!scoped.Ok) return OpResult<PersistentObjectAttribute>.Fail(scoped.Error!);
            return OpResult<PersistentObjectAttribute>.Success(scoped.Value!.Attribute);
        }
        var po = _session.CurrentPo;
        if (po is null)
            return OpResult<PersistentObjectAttribute>.Fail(new Diagnostic(ErrorKind.StateNoCurrentPo,
                $"EXPECT Attribute {subj.Name} needs a current PersistentObject.", loc));
        var attr = po.GetAttribute(subj.Name!);
        if (attr is null)
            return OpResult<PersistentObjectAttribute>.Fail(new Diagnostic(ErrorKind.ResolveAttribute,
                $"Attribute '{subj.Name}' does not exist on {po.Type}.", loc,
                Hint: Suggester.Hint(subj.Name!, po.Attributes.Select(a => a.Name))));
        return OpResult<PersistentObjectAttribute>.Success(attr);
    }

    /// <summary>Maps an <c>EXPECT PO.&lt;prop&gt;</c> identifier to the corresponding CLR property
    /// on the live <see cref="PersistentObject"/>. Recognized names are case-insensitive.</summary>
    private static OpResult<object?> ReadPoProperty(PersistentObject po, string name, SourceLocation loc)
    {
        return name.ToUpperInvariant() switch
        {
            "TYPE"          => OpResult<object?>.Success((object?)po.Type),
            "FULLTYPENAME"  => OpResult<object?>.Success((object?)po.FullTypeName),
            "BREADCRUMB"    => OpResult<object?>.Success((object?)po.Breadcrumb),
            "ISNEW"         => OpResult<object?>.Success((object?)po.IsNew),
            "ISHIDDEN"      => OpResult<object?>.Success((object?)po.IsHidden),
            "OBJECTID"      => OpResult<object?>.Success((object?)po.ObjectId),
            "LABEL"         => OpResult<object?>.Success((object?)po.Label),
            "TAG"           => OpResult<object?>.Success(po.Tag),
            _ => Fail<object?>(new Diagnostic(ErrorKind.ParseUnexpectedToken,
                $"PO has no property '{name}' for EXPECT.", loc,
                Hint: "Supported: Type, FullTypeName, Breadcrumb, IsNew, IsHidden, ObjectId, Label, Tag, Metadata.<key>, NavigationHints.<key>.")),
        };
    }

    private static OpResult<object?> ReadQueryProperty(Query query, string name, SourceLocation loc)
    {
        return name.ToUpperInvariant() switch
        {
            "NAME"        => OpResult<object?>.Success((object?)query.Name),
            "LABEL"       => OpResult<object?>.Success((object?)query.Label),
            "ID"          => OpResult<object?>.Success((object?)query.Id),
            "TAG"         => OpResult<object?>.Success(query.Tag),
            "HASSEARCHED" => OpResult<object?>.Success((object?)query.HasSearched),
            "TEXTSEARCH"  => OpResult<object?>.Success((object?)query.TextSearch),
            "TOTALITEMS"  => OpResult<object?>.Success((object?)query.TotalItems),
            "ISHIDDEN"    => OpResult<object?>.Success((object?)query.IsHidden),
            _ => Fail<object?>(new Diagnostic(ErrorKind.ParseUnexpectedToken,
                $"Query has no property '{name}' for EXPECT.", loc,
                Hint: "Supported: Name, Label, Id, Tag, HasSearched, TextSearch, TotalItems, IsHidden, Metadata.<key>, NavigationHints.<key>, PersistentObject.<prop>, Columns[name].<prop>.")),
        };
    }

    private static OpResult<object?> ReadColumnProperty(Vidyano.ViewModel.QueryColumn col, string name, SourceLocation loc)
    {
        return name.ToUpperInvariant() switch
        {
            "LABEL"  => OpResult<object?>.Success((object?)col.Label),
            "NAME"   => OpResult<object?>.Success((object?)col.Name),
            "TYPE"   => OpResult<object?>.Success((object?)col.Type),
            "OFFSET" => OpResult<object?>.Success((object?)col.Offset),
            _ => Fail<object?>(new Diagnostic(ErrorKind.ParseUnexpectedToken,
                $"Column has no property '{name}' for EXPECT.", loc,
                Hint: "Supported: Label, Name, Type, Offset.")),
        };
    }

    /// <summary>Case-insensitive lookup against an optional string-keyed metadata bag. Returns
    /// <c>null</c> when the bag is null or missing the key — that surfaces as
    /// <c>EXPECT … IS NULL</c> in the script.</summary>
    private static object? BagLookup(IReadOnlyDictionary<string, string>? bag, string key)
    {
        if (bag is null) return null;
        foreach (var kv in bag)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    private static OpResult<T> Fail<T>(Diagnostic d) => OpResult<T>.Fail(d);

    private static Diagnostic AssertDiag(ExpectStmt ex, object? expected, object? actual) =>
        new(ErrorKind.AssertFailed,
            $"Expected {FormatValue(expected)} but got {FormatValue(actual)}.",
            ex.Location,
            Details: new Dictionary<string, object?>
            {
                ["expected"] = expected,
                ["actual"] = actual,
            });

    // --- expression evaluation ---------------------------------------------------------------

    private OpResult<object?> EvaluateExpression(Expression expr)
    {
        switch (expr)
        {
            case LiteralExpr l: return OpResult<object?>.Success(l.Value);
            case IdentifierExpr i: return OpResult<object?>.Success(i.Name);
            case InterpExpr interp: return EvaluateInterpolation(interp);
            case StringInterpExpr si: return EvaluateStringInterp(si);
            case VariableAttributeExpr v: return _session.GetScopedAttributeValue(v.Scope, v.AttributeName, v.Location);
        }
        return Fail<object?>(new Diagnostic(ErrorKind.ParseUnexpectedToken, "Unhandled expression.", expr.Location));
    }

    /// <summary>Evaluates a <c>"..."</c> literal that carried <c>{{...}}</c> holes: literal runs pass
    /// through verbatim, holes resolve via <see cref="EvaluateInterpolation"/>, and every piece is
    /// coerced with <see cref="AsString"/> before concatenation. A failing hole (e.g. an undefined
    /// variable) surfaces its diagnostic and aborts the statement — the same loud failure a
    /// standalone <c>{{...}}</c> would produce.</summary>
    private OpResult<object?> EvaluateStringInterp(StringInterpExpr si)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in si.Parts)
        {
            if (part is InterpExpr hole)
            {
                var v = EvaluateInterpolation(hole);
                if (!v.Ok) return v;
                sb.Append(AsString(v.Value));
            }
            else
            {
                sb.Append((string)part);
            }
        }
        return OpResult<object?>.Success(sb.ToString());
    }

    private OpResult<object?> EvaluateInterpolation(InterpExpr interp)
    {
        var inner = interp.Inner.Trim();
        // Built-in deterministic variables (D5): dotless @-names resolved before the @scope.attr
        // path. Each is evaluated on every reference, not memoized: {{@now}} flows from a per-run
        // anchor, and {{@uuid}}/{{@random}} draw the next value from a seeded stream. To freeze a
        // value for reuse, capture it into a variable (@id = {{@uuid}}) — same idiom as C#.
        if (string.Equals(inner, "@today", StringComparison.OrdinalIgnoreCase))
            return OpResult<object?>.Success((object?)BuiltinToday());
        if (string.Equals(inner, "@now", StringComparison.OrdinalIgnoreCase))
            return OpResult<object?>.Success((object?)BuiltinNow());
        if (string.Equals(inner, "@uuid", StringComparison.OrdinalIgnoreCase))
            return OpResult<object?>.Success((object?)BuiltinUuid());
        if (string.Equals(inner, "@random", StringComparison.OrdinalIgnoreCase))
            return OpResult<object?>.Success((object?)BuiltinRandom());

        // {{@session.Attr}} — read an attribute from a reserved scoped PO. Mirrors the SET shape
        // so authors don't need to remember a separate interpolation syntax. Scope and attribute
        // parts are trimmed individually so {{ @session . X }} reads the same as {{@session.X}}.
        if (inner.Length > 0 && inner[0] == '@')
        {
            var dot = inner.IndexOf('.');
            if (dot < 0)
            {
                var bare = inner.Substring(1).Trim();
                if (bare.Length == 0)
                    return Fail<object?>(new Diagnostic(
                        ErrorKind.ResolveVariable,
                        "Empty scope in interpolation — use `{{@session.<attr>}}`.",
                        interp.Location));
                return Fail<object?>(new Diagnostic(
                    ErrorKind.ResolveVariable,
                    $"`@{bare}` is a PO reference, not a value — use `@{bare}.<attr>`.",
                    interp.Location));
            }
            var scope = inner.Substring(1, dot - 1).Trim();
            var attr = inner.Substring(dot + 1).Trim();
            if (scope.Length == 0)
                return Fail<object?>(new Diagnostic(
                    ErrorKind.ResolveVariable,
                    "Empty scope in interpolation — use `{{@session.<attr>}}`.",
                    interp.Location));
            if (attr.Length == 0)
                return Fail<object?>(new Diagnostic(
                    ErrorKind.ResolveVariable,
                    $"`@{scope}` is a PO reference, not a value — use `@{scope}.<attr>`.",
                    interp.Location));
            return _session.GetScopedAttributeValue(scope, attr, interp.Location);
        }
        // {{Messages.Saved}} — look up the server-localized client message by key. Surfaces the
        // same strings the UI uses, so assertions can compare against "what the user would read"
        // without hard-coding translations into the script.
        if (inner.StartsWith("Messages.", StringComparison.Ordinal))
        {
            var key = inner.Substring("Messages.".Length).Trim();
            if (key.Length == 0)
                return Fail<object?>(new Diagnostic(ErrorKind.ResolveVariable,
                    "Empty Messages key.", interp.Location,
                    Hint: "Use {{Messages.<key>}} — e.g. {{Messages.Save}}."));
            if (_session.Client.Messages.TryGetValue(key, out var msg))
                return OpResult<object?>.Success(msg);
            return Fail<object?>(new Diagnostic(ErrorKind.ResolveVariable,
                $"No client message named '{key}'.",
                interp.Location,
                Hint: Suggester.Hint(key, _session.Client.Messages.Keys)));
        }
        // {{env:NAME}} — loud-on-missing environment lookup (a missing var never silently becomes an empty
        // value). Resolves through the injectable EnvLookup, so `--env-file` / hermetic test hosts feed it.
        // Optional `?? <fallback>` makes a value optional: a quoted string or bare token used verbatim as a
        // literal string when NAME is unset.
        if (inner.StartsWith("env:", StringComparison.Ordinal))
        {
            var spec = inner.Substring("env:".Length);
            string? fallback = null;
            var fbIdx = spec.IndexOf("??", StringComparison.Ordinal);
            if (fbIdx >= 0)
            {
                fallback = StripQuotes(spec.Substring(fbIdx + 2).Trim());
                spec = spec.Substring(0, fbIdx);
            }
            var name = spec.Trim();
            if (name.Length == 0)
                return Fail<object?>(new Diagnostic(ErrorKind.ResolveEnv,
                    "Empty environment-variable name.", interp.Location,
                    Hint: "Use {{env:NAME}} — e.g. {{env:VIDYANO_USER}}."));
            var envVal = _envLookup(name);
            // Treat empty as missing (matches SIGN-IN FROM ENV) so an env var set to "" still falls
            // through to the ?? fallback or the loud failure — closing the empty-credential footgun.
            if (!string.IsNullOrEmpty(envVal)) return OpResult<object?>.Success((object?)envVal);
            if (fallback is not null) return OpResult<object?>.Success((object?)fallback);
            return Fail<object?>(new Diagnostic(ErrorKind.ResolveEnv,
                $"Environment variable '{name}' is not set.",
                interp.Location,
                Hint: "Set it in the shell, pass --var, or add a ?? fallback."));
        }
        if (_vars.TryGetValue(inner, out var v))
            return OpResult<object?>.Success(v);
        return Fail<object?>(new Diagnostic(
            ErrorKind.ResolveVariable,
            $"Variable '{inner}' is not defined.",
            interp.Location,
            Hint: Suggester.Hint(inner, _vars.Keys)));
    }

    // --- built-in deterministic variables -----------------------------------------------------

    /// <summary>Resets everything scoped to a single RunAsync: a fresh clock anchor + stopwatch and
    /// fresh RNG streams. Called from the constructor and the top of every run so a reused
    /// Interpreter (the REPL) never leaks per-run state across lines.</summary>
    private void ResetRunState()
    {
        _skipped = false;
        _inCleanup = false;
        // Anchor the clock once per run; each {{@now}} read = anchor + real elapsed. A pinned --now
        // fixes the origin, but time still flows the way a live session's would (the delta is real,
        // so it is NOT bit-reproducible — capture into a variable when an exact value is needed).
        _clockAnchor = _now ?? DateTimeOffset.Now;
        _stopwatch = Stopwatch.StartNew();
        // Independent seeded streams: adding a {{@random}} draw never perturbs the {{@uuid}}
        // sequence (and vice versa). Same seed -> same sequence, distinct value per reference.
        _uuidRng = _seed is { } us ? new Random(us) : new Random();
        _randomRng = _seed is { } rs ? new Random(rs ^ unchecked((int)0x9E3779B9)) : new Random();
    }

    private DateTimeOffset Clock => _clockAnchor + _stopwatch.Elapsed;

    private string BuiltinToday() =>
        Clock.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string BuiltinNow() =>
        Clock.ToString("o", CultureInfo.InvariantCulture);

    private string BuiltinUuid()
    {
        var bytes = new byte[16];
        _uuidRng.NextBytes(bytes);
        return new Guid(bytes).ToString();
    }

    private long BuiltinRandom() =>
        // Random.NextInt64 isn't on netstandard2.0, so compose a non-negative 62-bit value from two
        // 31-bit draws — the long return then spans well beyond int.MaxValue.
        ((long)_randomRng.Next() << 31) | (long)_randomRng.Next();

    // --- comparison / coercion ----------------------------------------------------------------

    private static bool Compare(object? left, object? right, ExpectOp op)
    {
        if (op is ExpectOp.Eq or ExpectOp.NotEq)
        {
            var eq = ValuesEqual(left, right);
            return op == ExpectOp.Eq ? eq : !eq;
        }

        if (op is ExpectOp.Contains or ExpectOp.NotContains)
        {
            // Case-insensitive substring on the string form of both sides — partial assertions
            // typically target human text where case shouldn't matter. Null haystack never contains.
            var contained = ContainsSubstring(left, right);
            return op == ExpectOp.Contains ? contained : !contained;
        }

        // ExpectOp.Matches is handled in DoExpect (it needs to distinguish a malformed pattern from a
        // non-match), so it never reaches Compare.

        // Numeric comparisons coerce both sides to decimal when possible.
        if (!TryCoerceDecimal(left, out var l) || !TryCoerceDecimal(right, out var r))
            return false;

        return op switch
        {
            ExpectOp.Lt   => l < r,
            ExpectOp.LtEq => l <= r,
            ExpectOp.Gt   => l > r,
            ExpectOp.GtEq => l >= r,
            _ => false,
        };
    }

    /// <summary><c>EXPECT … MATCHES "pattern"</c> — regex test of the left side's string form. Returns
    /// <c>true</c> when the pattern is valid and was evaluated (a null subject is a clean non-match, and
    /// a 1s ReDoS-guard timeout is also treated as a non-match — never a crash); <paramref name="matched"/>
    /// carries the outcome. Returns <c>false</c> with <paramref name="error"/> set when the pattern is not
    /// a valid regex, so the caller can surface a clean authoring diagnostic instead of letting the
    /// exception abort the run.</summary>
    private static bool TryRegexMatches(object? left, object? pattern, out bool matched, out string? error)
    {
        matched = false;
        error = null;
        if (left is null || pattern is null) return true;
        var subject = left as string ?? left.ToString();
        var pat = pattern as string ?? pattern.ToString();
        if (subject is null || pat is null) return true;
        try
        {
            matched = System.Text.RegularExpressions.Regex.IsMatch(
                subject, pat,
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1));
            return true;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool ContainsSubstring(object? haystack, object? needle)
    {
        if (haystack is null || needle is null) return false;
        var h = haystack as string ?? haystack.ToString();
        var n = needle as string ?? needle.ToString();
        if (h is null || n is null) return false;
        if (n.Length == 0) return true; // empty needle matches anything non-null.
        return h.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null || b is null) return ReferenceEquals(a, b);
        if (a is bool ab && b is bool bb) return ab == bb;
        if (TryCoerceDecimal(a, out var ad) && TryCoerceDecimal(b, out var bd))
            return ad == bd;
        var sa = a is string s1 ? s1 : a.ToString();
        var sb = b is string s2 ? s2 : b.ToString();
        return string.Equals(sa, sb, StringComparison.Ordinal);
    }

    private static bool TryCoerceInt(object? v, out int result)
    {
        switch (v)
        {
            case int i: result = i; return true;
            case long l: result = (int)l; return true;
            case decimal d: result = (int)d; return true;
            case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed): result = parsed; return true;
        }
        result = 0;
        return false;
    }

    private static bool TryCoerceDecimal(object? v, out decimal result)
    {
        switch (v)
        {
            case decimal d: result = d; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case double db: result = (decimal)db; return true;
            case string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed): result = parsed; return true;
        }
        result = 0;
        return false;
    }

    private static string FormatValue(object? v) =>
        v switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "null",
        };

    private static string AsString(object? v) => v switch { null => "", string s => s, IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };

    /// <summary>Strips one matching pair of surrounding double or single quotes from a <c>??</c> fallback
    /// token, so <c>?? "x"</c> and <c>?? x</c> both yield the literal <c>x</c>. Unbalanced or absent quotes
    /// pass through unchanged.</summary>
    private static string StripQuotes(string s)
    {
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s.Substring(1, s.Length - 2);
        return s;
    }

    // --- statement result wrappers ------------------------------------------------------------

    private StatementResult Wrap(Statement stmt, OpResult res) =>
        res.Ok ? Ok(stmt) : Fail(stmt, res.Error!);

    private StatementResult Ok(Statement stmt) =>
        new(stmt, true, _session.TakeSnapshot(), Array.Empty<Diagnostic>());

    private StatementResult Fail(Statement stmt, Diagnostic d) =>
        new(stmt, false, _session.TakeSnapshot(), new[] { d });

    /// <summary>A skipped statement: a non-failing pass that did not execute. <paramref name="d"/>
    /// carries an informational diagnostic explaining the skip (e.g. an unmet REQUIRES).</summary>
    private StatementResult Skip(Statement stmt, Diagnostic? d) =>
        new(stmt, true, _session.TakeSnapshot(),
            d is null ? Array.Empty<Diagnostic>() : new[] { d },
            Skipped: true);
}
