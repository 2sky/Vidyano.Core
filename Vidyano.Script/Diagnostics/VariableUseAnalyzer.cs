using System;
using System.Collections;
using System.Collections.Generic;
using Vidyano.Script.Parsing;

namespace Vidyano.Script.Diagnostics;

/// <summary>
/// Static, presence-only check for variable READS that reference a name nothing supplies — neither a
/// script declaration nor a caller-provided value. It complements the two existing layers: the parser
/// is purely syntactic and never looks at names, and the interpreter loud-fails an undeclared
/// <c>{{x}}</c> only when it actually evaluates it. Linting closes the gap by catching the typo up
/// front (before a connection is opened), while the interpreter stays the control-flow-aware backstop.
/// </summary>
/// <remarks>
/// <para><b>Presence-only.</b> Order is ignored — a <c>{{x}}</c> written above its <c>@x = …</c> is
/// accepted because the binding exists <em>somewhere</em>. The analyzer also can't see control flow, so
/// it deliberately stays advisory: a read reachable only inside a <c>REQUIRES</c>-skipped body would be
/// flagged here even though the interpreter would never evaluate it. That's why this feeds
/// <see cref="VidyanoScript.Lint"/> only and does not gate <see cref="VidyanoScript.RunAsync"/>.</para>
/// <para><b>Reads are the <c>{{x}}</c> form only.</b> The bare <c>@x</c> syntax is a reserved-scope
/// reference (<c>@session.attr</c>), which the parser already validates. The non-variable interpolation
/// forms (<c>{{@today}}</c>, <c>{{@session.X}}</c>, <c>{{env:NAME}}</c>, <c>{{Messages.X}}</c>) resolve
/// through their own machinery, not the variable table, so they are skipped here.</para>
/// </remarks>
internal static class VariableUseAnalyzer
{
    /// <summary>Returns one <see cref="ErrorKind.ResolveVariable"/> diagnostic per interpolation that
    /// reads a plain variable name nothing in <paramref name="script"/> declares and
    /// <paramref name="externalVariables"/> (caller-supplied via <c>--var</c> / options / env-prefix)
    /// doesn't provide.</summary>
    public static IReadOnlyList<Diagnostic> Analyze(ScriptAst script, IEnumerable<string>? externalVariables)
    {
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (externalVariables is not null)
            foreach (var name in externalVariables)
                declared.Add(name);

        // First pass: every name the script binds. `@x =` assignments and TOOL `-> @result` captures
        // both populate the variable table at runtime, so a later {{x}} / {{result}} is legal.
        foreach (var step in script.Steps)
            foreach (var stmt in step.Statements)
            {
                if (stmt is VariableAssignment va) declared.Add(va.Name);
                else if (stmt is ToolCallStmt { ResultVariable: { } rv }) declared.Add(rv);
            }

        // Second pass: collect every interpolation read, then flag the plain-variable ones whose name
        // nothing declares.
        var holes = new List<InterpExpr>();
        foreach (var step in script.Steps)
            foreach (var stmt in step.Statements)
                CollectInterps(stmt, holes);

        var diags = new List<Diagnostic>();
        foreach (var hole in holes)
        {
            var name = hole.Inner.Trim();
            if (!IsPlainVariable(name) || declared.Contains(name)) continue;
            diags.Add(new Diagnostic(
                ErrorKind.ResolveVariable,
                $"Variable '{name}' is not defined.",
                hole.Location,
                Hint: Suggester.Hint(name, declared)
                      ?? "Declare it with `@name = …`, pass --var name=value, or add it to the expected variables."));
        }
        return diags;
    }

    /// <summary>True when an interpolation body is a plain variable name — not a built-in (<c>@…</c>),
    /// a scoped attribute read (<c>@scope.attr</c>), an env lookup (<c>env:NAME</c>), or a client-message
    /// lookup (<c>Messages.X</c>). Mirrors the dispatch order in the interpreter's interpolation evaluator
    /// so the two never disagree on what counts as a variable.</summary>
    private static bool IsPlainVariable(string inner)
    {
        if (inner.Length == 0) return false;                                            // empty hole — runtime owns that error
        if (inner[0] == '@') return false;                                              // built-in or @scope.attr
        if (inner.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) return false; // environment lookup
        if (inner.StartsWith("Messages.", StringComparison.Ordinal)) return false;      // client-message lookup
        return true;
    }

    /// <summary>Accumulates every <see cref="InterpExpr"/> reachable from an AST node. Reflection over
    /// the parsing records keeps this complete as the grammar grows: a new statement field that can hold
    /// an expression is traversed without a change here. The AST is an immutable tree, so there are no
    /// cycles to guard against.</summary>
    private static void CollectInterps(object? node, List<InterpExpr> sink)
    {
        switch (node)
        {
            case null:
            case string:
                return;
            case InterpExpr interp:
                sink.Add(interp); // leaf: Inner is a string, nothing further to descend into
                return;
            case IDictionary dict: // ACTION params / TOOL args — IReadOnlyDictionary<string, Expression>
                foreach (var value in dict.Values) CollectInterps(value, sink);
                return;
            case IEnumerable seq: // menu segments, string-interpolation parts, etc.
                foreach (var item in seq) CollectInterps(item, sink);
                return;
        }

        // Descend only into our own AST records; enums, SourceLocation, and primitives are leaves here.
        var type = node.GetType();
        if (type.Namespace != "Vidyano.Script.Parsing") return;
        foreach (var prop in type.GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            CollectInterps(prop.GetValue(node), sink);
        }
    }
}
