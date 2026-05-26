using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private GuardMode _mode = GuardMode.Navigation;
    private bool _statementsExecuted;

    public Interpreter(VidyanoSession session, IReadOnlyDictionary<string, object?>? initialVars = null, GuardMode mode = GuardMode.Navigation)
    {
        _session = session;
        _vars = initialVars is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(initialVars, StringComparer.OrdinalIgnoreCase);
        _mode = mode;
    }

    /// <summary>Effective guard mode after any <c>@mode</c> directives have been processed.</summary>
    public GuardMode Mode => _mode;

    /// <summary>Read-only view of the variable table after execution.</summary>
    public IReadOnlyDictionary<string, object?> Variables => _vars;

    public async Task<ScriptResult> RunAsync(ScriptAst script)
    {
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
        return (new StepResult(step.Label, step.Location, !anyFail, stmtResults), !anyFail);
    }

    private async Task<StatementResult> RunStatementAsync(Statement stmt)
    {
        // Track whether real work has happened, so @mode can be rejected after the first execution.
        var isMetaStmt = stmt is VariableAssignment or ModeDirective;
        if (!isMetaStmt) _statementsExecuted = true;

        // Reset the per-verb ClientOperations buffer before every executable verb (but NOT before
        // EXPECT — assertions consume what the *previous* verb produced). Meta statements
        // (@var, @mode) don't talk to the server, so they leave the buffer alone too.
        if (!isMetaStmt && stmt is not ExpectStmt)
            _session.ResetLastOperations();

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
            case EditStmt e:                   return Wrap(stmt, _session.Edit(e.Location));
            case CancelStmt c:                 return Wrap(stmt, _session.Cancel(c.Location));
            case SaveStmt sv:                  return Wrap(stmt, await _session.SaveAsync(sv.Location).ConfigureAwait(false));
            case RefreshStmt rf:               return Wrap(stmt, await _session.RefreshAsync(rf.Location).ConfigureAwait(false));
            case SetStmt s:                    return await DoSet(s).ConfigureAwait(false);
            case ActionStmt a:                 return await DoAction(a).ConfigureAwait(false);
            case SearchStmt q:                 return await DoSearch(q).ConfigureAwait(false);
            case ExpectStmt ex:                return DoExpect(ex);
        }
        return Fail(stmt, new Diagnostic(ErrorKind.ParseUnexpectedToken, $"Statement type {stmt.GetType().Name} is not supported.", stmt.Location));
    }

    // --- statement handlers -------------------------------------------------------------------

    private async Task<StatementResult> DoSignIn(SignInStmt si)
    {
        var user = EvaluateExpression(si.UserName);
        if (!user.Ok) return Fail(si, user.Error!);
        var pwd = si.Password is null ? OpResult<object?>.Success(null) : EvaluateExpression(si.Password);
        if (!pwd.Ok) return Fail(si, pwd.Error!);
        string? language = null;
        if (si.Language is not null)
        {
            var lang = EvaluateExpression(si.Language);
            if (!lang.Ok) return Fail(si, lang.Error!);
            language = AsString(lang.Value);
        }
        var res = await _session.SignInAsync(AsString(user.Value), pwd.Value as string, language, si.Location).ConfigureAwait(false);
        return Wrap(si, res);
    }

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
        var v = EvaluateExpression(or.Index);
        if (!v.Ok) return Fail(or, v.Error!);
        if (!TryCoerceInt(v.Value, out var index))
            return Fail(or, new Diagnostic(ErrorKind.ParseInvalidValue, "OPEN-ROW needs an integer index.", or.Location));
        var res = await _session.OpenRowAsync(index, or.AsHandle, or.Location).ConfigureAwait(false);
        return Wrap(or, res);
    }

    private async Task<StatementResult> DoSet(SetStmt s)
    {
        var v = EvaluateExpression(s.Value);
        if (!v.Ok) return Fail(s, v.Error!);
        ReferenceHint? hint = s.Hint is null ? null : new ReferenceHint(s.Hint.Value, AsString(v.Value));
        var res = s.Scope is null
            ? _session.SetAttribute(s.Attribute, v.Value, s.Location, hint)
            : _session.SetScopedAttribute(s.Scope, s.Attribute, v.Value, hint, s.Location);
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
        var res = await _session.ExecuteActionAsync(a.ActionName, parameters, a.Location).ConfigureAwait(false);
        return Wrap(a, res);
    }

    private async Task<StatementResult> DoSearch(SearchStmt q)
    {
        var v = EvaluateExpression(q.Text);
        if (!v.Ok) return Fail(q, v.Error!);
        var res = await _session.SearchAsync(AsString(v.Value), q.Location).ConfigureAwait(false);
        return Wrap(q, res);
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

        var ok = Compare(lhs.Value, rhs.Value, ex.Op);
        return ok ? Ok(ex) : Fail(ex, AssertDiag(ex, rhs.Value, lhs.Value));
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

        switch (subj.Kind)
        {
            case ExpectSubjectKind.Attribute:
                {
                    if (subj.Scope is not null)
                        return _session.GetScopedAttributeValue(subj.Scope, subj.Name!, loc);
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
                    // Flag is set by the parser to None on Action subjects until IS X is parsed.
                    // The interpreter receives Flag=None and operator=Is/IsNot. Treat "IS AVAILABLE"
                    // as CanExecute, "IS VISIBLE" as IsVisible. We encode that via subj.Flag if set;
                    // otherwise default to CanExecute (the AVAILABLE alias).
                    return subj.Flag switch
                    {
                        AttributeFlagKind.Visible => OpResult<object?>.Success((object?)action.IsVisible),
                        _                         => OpResult<object?>.Success((object?)action.CanExecute),
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
                        AttributeFlagKind.Visible  => OpResult<object?>.Success((object?)attr.IsVisible),
                        AttributeFlagKind.ReadOnly => OpResult<object?>.Success((object?)attr.IsReadOnly),
                        AttributeFlagKind.Required => OpResult<object?>.Success((object?)attr.IsRequired),
                        _                          => OpResult<object?>.Success((object?)attr.IsVisible),
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
        }
        return Fail<object?>(new Diagnostic(ErrorKind.ParseUnexpectedToken, "Unhandled EXPECT subject.", loc));
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
            case VariableAttributeExpr v: return _session.GetScopedAttributeValue(v.Scope, v.AttributeName, v.Location);
        }
        return Fail<object?>(new Diagnostic(ErrorKind.ParseUnexpectedToken, "Unhandled expression.", expr.Location));
    }

    private OpResult<object?> EvaluateInterpolation(InterpExpr interp)
    {
        var inner = interp.Inner.Trim();
        if (inner.StartsWith("$env ", StringComparison.OrdinalIgnoreCase))
        {
            var name = inner.Substring(5).Trim();
            var val = Environment.GetEnvironmentVariable(name);
            return OpResult<object?>.Success(val);
        }
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
        if (_vars.TryGetValue(inner, out var v))
            return OpResult<object?>.Success(v);
        return Fail<object?>(new Diagnostic(
            ErrorKind.ResolveVariable,
            $"Variable '{inner}' is not defined.",
            interp.Location,
            Hint: Suggester.Hint(inner, _vars.Keys)));
    }

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

    // --- statement result wrappers ------------------------------------------------------------

    private StatementResult Wrap(Statement stmt, OpResult res) =>
        res.Ok ? Ok(stmt) : Fail(stmt, res.Error!);

    private StatementResult Ok(Statement stmt) =>
        new(stmt, true, _session.TakeSnapshot(), Array.Empty<Diagnostic>());

    private StatementResult Fail(Statement stmt, Diagnostic d) =>
        new(stmt, false, _session.TakeSnapshot(), new[] { d });
}
