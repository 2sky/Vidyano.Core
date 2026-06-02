using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>
/// Renders a <see cref="ScriptResult"/> as ANSI-coloured human-readable output. The shape of the
/// output is intentionally compact: one line per statement when things pass, an expanded block when
/// they don't. Snapshots are shown only on <c>--verbose</c> so a green run stays readable.
/// </summary>
public static class ConsoleReporter
{
    public static void Write(ScriptResult result, bool verbose)
    {
        if (result.ParseDiagnostics.Count > 0)
        {
            AnsiConsole.MarkupLine("[red]parse failed[/]");
            foreach (var d in result.ParseDiagnostics) WriteDiagnostic(d);
            return;
        }

        var passed = 0; var failed = 0; var skipped = 0; var total = 0;
        foreach (var step in result.Steps)
        {
            if (!string.IsNullOrEmpty(step.Label))
            {
                var color = step.Ok ? "green" : "red";
                AnsiConsole.MarkupLine($"[{color}]###[/] [bold]{Markup.Escape(step.Label)}[/]");
            }

            foreach (var s in step.Statements)
            {
                total++;
                if (s.Skipped)
                {
                    skipped++;
                    AnsiConsole.MarkupLine($"  [yellow]↓[/] [grey]{Describe(s.Statement)}[/]");
                    foreach (var d in s.Diagnostics) WriteDiagnostic(d, indent: "    ");
                }
                else if (s.Ok)
                {
                    passed++;
                    AnsiConsole.MarkupLine($"  [green]✓[/] {Describe(s.Statement)}");
                    if (verbose && s.Snapshot is not null) RenderSnapshot(s.Snapshot);
                }
                else
                {
                    failed++;
                    AnsiConsole.MarkupLine($"  [red]✗[/] {Describe(s.Statement)}");
                    foreach (var d in s.Diagnostics) WriteDiagnostic(d, indent: "    ");
                    if (verbose && s.Snapshot is not null) RenderSnapshot(s.Snapshot);
                }
            }
        }

        AnsiConsole.WriteLine();
        var summary = $"[bold]{passed}[/]/{total} ok";
        if (failed > 0) summary += $", [red]{failed} failed[/]";
        if (skipped > 0) summary += $", [yellow]{skipped} skipped[/]";
        AnsiConsole.MarkupLine(summary);
    }

    public static void WriteDiagnostic(Diagnostic d, string indent = "")
    {
        var locColor = d.Location.Line > 0 ? "grey" : "grey46";
        AnsiConsole.MarkupLine($"{indent}[red]{Markup.Escape(d.Kind)}[/]  [{locColor}]{Markup.Escape(d.Location.ToString())}[/]");
        AnsiConsole.MarkupLine($"{indent}{Markup.Escape(d.Message)}");
        if (!string.IsNullOrEmpty(d.Hint))
            AnsiConsole.MarkupLine($"{indent}  [yellow]hint:[/] {Markup.Escape(d.Hint!)}");
    }

    /// <summary>Pretty-prints a one-line description of a statement (verb plus the parts that matter).</summary>
    public static string Describe(Statement stmt) =>
        stmt switch
        {
            VariableAssignment v       => $"[grey]@{v.Name} = ...[/]",
            ModeDirective md           => $"[grey]@mode = {md.Mode.ToString().ToLowerInvariant()}[/]",
            SignInStmt si              => $"SIGN-IN {(si.SessionName is null ? "" : "@" + si.SessionName + " = ")}…",
            OpenPersistentObjectStmt o => $"OPEN PersistentObject …",
            OpenQueryStmt o            => $"OPEN Query …",
            OpenMenuItemStmt o         => $"OPEN MenuItem …",
            OpenRowStmt o              => $"OPEN-ROW …",
            EditStmt                   => "EDIT",
            CancelStmt                 => "CANCEL",
            SaveStmt sv                => sv.ExpectError ? "SAVE EXPECTING ERROR" : "SAVE",
            RefreshStmt                => "REFRESH",
            SetStmt s                  => $"SET {Markup.Escape(s.Attribute)} = …",
            ActionStmt a               => $"ACTION {Markup.Escape(a.ActionName)}{(a.ExpectError ? " EXPECTING ERROR" : "")}",
            SearchStmt q               => q.DetailName is null ? "SEARCH …" : $"SEARCH Detail \"{Markup.Escape(q.DetailName)}\" …",
            ExpectStmt e               => $"EXPECT {DescribeSubject(e.Subject)}",
            ToolCallStmt t             => $"TOOL {Markup.Escape(t.Name)}{(t.ResultVariable is null ? "" : $" -> @{Markup.Escape(t.ResultVariable)}")}",
            RequiresStmt r             => $"REQUIRES {DescribeSubject(r.Subject)}",
            RequiresToolStmt rt        => $"REQUIRES TOOL {Markup.Escape(rt.ToolName)}",
            CleanupMarker              => "CLEANUP",
            UseSessionStmt u           => $"USE @{u.SessionName}",
            SignOutStmt so             => so.SessionName is null ? "SIGN-OUT" : $"SIGN-OUT @{so.SessionName}",
            GoBackStmt                 => "GO-BACK",
            SelectRowsStmt sr          => $"SELECT-ROWS {DescribeSelectTarget(sr)}",
            _                          => stmt.GetType().Name,
        };

    private static string DescribeSelectTarget(SelectRowsStmt sr)
    {
        var detail = sr.DetailName is null ? "" : $"Detail \"{Markup.Escape(sr.DetailName)}\" ";
        var target = sr.None ? "NONE"
            : sr.All ? (sr.Index is not null || sr.MatchColumn is not null ? "ALL EXCEPT …" : "ALL")
            : sr.MatchColumn is not null ? $"WHERE {Markup.Escape(sr.MatchColumn)} = …"
            : "…";
        return detail + target;
    }

    private static string DescribeSubject(ExpectSubject s) =>
        s.Kind switch
        {
            ExpectSubjectKind.Attribute            => Markup.Escape(s.Name ?? "?"),
            ExpectSubjectKind.Action               => $"Action {Markup.Escape(s.Name ?? "?")}",
            ExpectSubjectKind.AttributeFlag        => $"Attribute {Markup.Escape(s.Name ?? "?")}",
            ExpectSubjectKind.Notification         => "Notification",
            ExpectSubjectKind.NotificationType     => "Notification.Type",
            ExpectSubjectKind.IsDirty              => "IsDirty",
            ExpectSubjectKind.IsInEdit             => "IsInEdit",
            ExpectSubjectKind.TotalItems           => "TotalItems",
            ExpectSubjectKind.AttributeType        => $"Attribute {Markup.Escape(s.Name ?? "?")} TYPE",
            ExpectSubjectKind.AttributeTag         => $"Attribute {Markup.Escape(s.Name ?? "?")} TAG",
            ExpectSubjectKind.AttributeTypeHint    => $"Attribute {Markup.Escape(s.Name ?? "?")} TYPEHINT {Markup.Escape(s.MetadataKey ?? "?")}",
            ExpectSubjectKind.PoProperty           => $"PO.{Markup.Escape(s.Name ?? "?")}",
            ExpectSubjectKind.PoMetadata           => $"PO.Metadata.{Markup.Escape(s.MetadataKey ?? "?")}",
            ExpectSubjectKind.PoNavigationHints    => $"PO.NavigationHints.{Markup.Escape(s.MetadataKey ?? "?")}",
            ExpectSubjectKind.QueryProperty        => $"Query.{Markup.Escape(s.Name ?? "?")}",
            ExpectSubjectKind.QueryMetadata        => $"Query.Metadata.{Markup.Escape(s.MetadataKey ?? "?")}",
            ExpectSubjectKind.QueryNavigationHints => $"Query.NavigationHints.{Markup.Escape(s.MetadataKey ?? "?")}",
            ExpectSubjectKind.QueryPoProperty      => $"Query.PersistentObject.{Markup.Escape(s.Name ?? "?")}",
            ExpectSubjectKind.QueryColumn          => $"Query.Columns[{Markup.Escape(s.Name ?? "?")}].{Markup.Escape(s.MetadataKey ?? "?")}",
            _                                      => "?",
        };

    private static void RenderSnapshot(Snapshot snap)
    {
        if (snap.Po is { } po)
            RenderPoTable(po, $"[bold]{Markup.Escape(po.Type)}[/] / {Markup.Escape(po.ObjectId ?? "<new>")}");
        if (snap.SessionPo is { } sess)
            RenderPoTable(sess, $"[bold]@session[/] [grey]({Markup.Escape(sess.Type)})[/]");
        if (snap.Query is { } q)
            AnsiConsole.MarkupLine($"    [grey]Query[/] {Markup.Escape(q.Name)} — {q.TotalItems} item(s)");
    }

    private static void RenderPoTable(PoSnapshot po, string title)
    {
        var t = new Table().Border(TableBorder.Rounded).Title(title);
        t.AddColumn("Attribute"); t.AddColumn("Value"); t.AddColumn("R/O"); t.AddColumn("Req'd"); t.AddColumn("Vis.");
        foreach (var a in po.Attributes)
            t.AddRow(Markup.Escape(a.Name), Markup.Escape(a.DisplayValue ?? a.Value ?? ""), a.IsReadOnly ? "✓" : "", a.IsRequired ? "✓" : "", a.IsVisible ? "" : "hidden");
        AnsiConsole.Write(t);
    }

    // --- interactive REPL rendering -----------------------------------------------------------

    /// <summary>Renders one just-executed statement for the interactive REPL. Unlike the batch
    /// <see cref="Write"/> path, this distinguishes pass / skip / fail and — on a pass — shows the
    /// resulting session state (see <see cref="DescribeOutcome"/>) rather than echoing the typed verb,
    /// so the prompt reflects where you now are. A skipped (unmet <c>REQUIRES</c>) statement is shown
    /// as <c>skip</c>, not a misleading green <c>ok</c>; a failure prints its diagnostics as before.</summary>
    public static void WriteReplLine(StatementResult s)
    {
        if (s.Skipped)
            AnsiConsole.MarkupLine($"[yellow]skip[/] {DescribeOutcome(s)}");
        else if (s.Ok)
            AnsiConsole.MarkupLine($"[green]ok[/]   {DescribeOutcome(s)}");
        else
            foreach (var d in s.Diagnostics) WriteDiagnostic(d);
    }

    /// <summary>
    /// A one-line, outcome-oriented summary of a just-executed statement: what changed and where you
    /// now are, read from the post-statement <see cref="StatementResult.Snapshot"/>. Distinct from
    /// <see cref="Describe"/> (a static echo of the parsed verb, used by batch <c>run</c> output): an
    /// <c>OPEN Query Modules</c> renders as <c>Query "Modules" · 52 items</c>, and an EXPECT/REQUIRES
    /// shows the assertion that held — not a bare <c>ok</c>. Returns Spectre markup.
    /// </summary>
    public static string DescribeOutcome(StatementResult r)
    {
        var snap = r.Snapshot;
        switch (r.Statement)
        {
            case SignInStmt si:
                return $"signed in as {Markup.Escape(snap?.Session?.User ?? "?")}"
                     + (si.SessionName is null ? "" : $" [grey](@{Markup.Escape(si.SessionName)})[/]");
            case SignOutStmt so:
                return so.SessionName is null ? "signed out" : $"signed out [grey]@{Markup.Escape(so.SessionName)}[/]";
            case UseSessionStmt u:
                return $"using [yellow]@{Markup.Escape(u.SessionName)}[/]  →  {Frame(snap)}";
            case OpenQueryStmt or OpenMenuItemStmt:
                return snap?.Query is { } q ? QueryLine(q) : "opened query";
            case OpenPersistentObjectStmt or OpenRowStmt:
                return snap?.Po is { } po ? $"opened {PoLine(po)}" : "opened object";
            case GoBackStmt:
                return $"[grey]←[/] back to {Frame(snap)}";
            case EditStmt:
                return snap?.Po is { } poe ? $"editing {PoLine(poe)}" : "editing";
            case CancelStmt:
                return "cancelled edit";
            case SaveStmt { ExpectError: true }:
                return $"error as expected{Notif(snap)}";
            case SaveStmt:
                return $"saved{Notif(snap)}";
            case RefreshStmt:
                return snap?.Po is { } por ? $"refreshed {PoLine(por)}" : "refreshed";
            case SetStmt set:
                return SetLine(set, snap);
            case ActionStmt { ExpectError: true }:
                return $"error as expected{Notif(snap)}";
            case ActionStmt a:
                return $"ran [yellow]{Markup.Escape(a.ActionName)}[/]{Notif(snap)}";
            case SearchStmt sq:
                return sq.DetailName is not null
                    ? $"detail [bold]\"{Markup.Escape(sq.DetailName)}\"[/] loaded"
                    : snap?.Query is { } qq ? QueryLine(qq) : "searched";
            case SelectRowsStmt { DetailName: { } detailName }:
                // The selection lands on the detail query, which the snapshot doesn't surface — so
                // report the target rather than the (parent) current-query selection.
                return $"selection set on detail [bold]\"{Markup.Escape(detailName)}\"[/]";
            case SelectRowsStmt:
                return $"selection: {(snap?.Query is { } qs ? SelectionText(qs) : "none")}";
            case ExpectStmt e:
                return $"{ExpectAssertion(e)}  [green]✓[/]";
            case RequiresStmt rq:
                return $"requires {ExpectAssertion(new ExpectStmt(rq.Subject, rq.Op, rq.Value, rq.Location))} — "
                     + (r.Skipped ? "[yellow]unmet[/]" : "[green]met[/]");
            case RequiresToolStmt rt:
                return $"requires tool [yellow]{Markup.Escape(rt.ToolName)}[/] — "
                     + (r.Skipped ? "[yellow]missing[/]" : "[green]present[/]");
            case ToolCallStmt t:
                return $"tool [yellow]{Markup.Escape(t.Name)}[/] ran"
                     + (t.ResultVariable is null ? "" : $"  →  [grey]@{Markup.Escape(t.ResultVariable)}[/]");
            case VariableAssignment v:
                return $"[grey]@{Markup.Escape(v.Name)} set[/]";
            case ModeDirective md:
                return $"[grey]@mode = {md.Mode.ToString().ToLowerInvariant()}[/]";
            case CleanupMarker:
                return "[grey]cleanup[/]";
            default:
                return Describe(r.Statement);
        }
    }

    private static string PoLine(PoSnapshot po)
    {
        var id = string.IsNullOrEmpty(po.ObjectId) ? "new" : po.ObjectId!;
        var s = $"[bold]{Markup.Escape(po.Type)}[/] · {Markup.Escape(id)}";
        if (po.IsInEdit) s += " [grey][[edit]][/]";
        if (po.IsDirty) s += " [yellow][[dirty]][/]";
        return s;
    }

    private static string QueryLine(QuerySnapshot q)
    {
        var s = $"Query [bold]\"{Markup.Escape(q.Name)}\"[/] · {q.TotalItems} {Plural(q.TotalItems, "item")}";
        if (!string.IsNullOrEmpty(q.TextSearch)) s += $"  [grey](search: \"{Markup.Escape(q.TextSearch!)}\")[/]";
        return s;
    }

    private static string Frame(Snapshot? snap) =>
        snap?.Po is { } po ? PoLine(po)
        : snap?.Query is { } q ? QueryLine(q)
        : "[grey](empty)[/]";

    private static string Notif(Snapshot? snap)
    {
        var n = snap?.Po?.Notification;
        return string.IsNullOrEmpty(n) ? "" : $"  [grey]·[/] \"{Markup.Escape(n!)}\"";
    }

    private static string SetLine(SetStmt set, Snapshot? snap)
    {
        // A scoped SET (@session.X) writes the session PO, which the snapshot carries separately —
        // read from there so the outcome shows the value rather than the "value-unavailable" sentinel.
        var po = string.Equals(set.Scope, "session", StringComparison.OrdinalIgnoreCase) ? snap?.SessionPo : snap?.Po;
        var attr = po?.Attributes.FirstOrDefault(a => string.Equals(a.Name, set.Attribute, StringComparison.OrdinalIgnoreCase));
        var val = attr is null ? "…" : attr.DisplayValue ?? attr.Value ?? "null";
        var dirty = po?.IsDirty == true ? "  [yellow](dirty)[/]" : "";
        return $"{Markup.Escape(set.Attribute)} = {Markup.Escape(val)}{dirty}";
    }

    private static string SelectionText(QuerySnapshot q)
    {
        if (q.AllSelected) return q.SelectedCount > 0 ? $"ALL except {q.SelectedCount}" : "ALL";
        return q.SelectedCount == 0 ? "none" : $"{q.SelectedCount} {Plural(q.SelectedCount, "row")}";
    }

    private static string ExpectAssertion(ExpectStmt e)
    {
        var detail = e.Subject.DetailName is null ? "" : $"Detail \"{Markup.Escape(e.Subject.DetailName)}\" ";
        var subj = detail + DescribeSubject(e.Subject);
        return e.Op switch
        {
            ExpectOp.IsNull    => $"{subj} IS NULL",
            ExpectOp.IsNotNull => $"{subj} IS NOT NULL",
            ExpectOp.Is        => $"{subj} IS {FlagOrValue(e)}",
            ExpectOp.IsNot     => $"{subj} IS NOT {FlagOrValue(e)}",
            _                  => $"{subj} {OpSymbol(e.Op)} {DescribeValue(e.Value)}",
        };
    }

    private static string FlagOrValue(ExpectStmt e) =>
        e.Subject.Flag != AttributeFlagKind.None
            ? e.Subject.Flag.ToString().ToUpperInvariant()
            : DescribeValue(e.Value);

    private static string OpSymbol(ExpectOp op) => op switch
    {
        ExpectOp.Eq => "=", ExpectOp.NotEq => "!=", ExpectOp.Lt => "<", ExpectOp.LtEq => "<=",
        ExpectOp.Gt => ">", ExpectOp.GtEq => ">=", ExpectOp.Contains => "CONTAINS",
        ExpectOp.NotContains => "NOT CONTAINS", ExpectOp.Matches => "MATCHES",
        _ => op.ToString(),
    };

    private static string DescribeValue(Expression? e) => e switch
    {
        null => "",
        LiteralExpr { Value: null } => "null",
        LiteralExpr { Value: string sv } => $"\"{Markup.Escape(sv)}\"",
        LiteralExpr lit => Markup.Escape(lit.Value?.ToString() ?? ""),
        IdentifierExpr id => Markup.Escape(id.Name),
        InterpExpr ie => "{{" + Markup.Escape(ie.Inner) + "}}",
        StringInterpExpr si => "\"" + Markup.Escape(string.Concat(si.Parts.Select(
            p => p is InterpExpr hole ? "{{" + hole.Inner + "}}" : p?.ToString() ?? ""))) + "\"",
        VariableAttributeExpr va => Markup.Escape($"@{va.Scope}.{va.AttributeName}"),
        _ => "…",
    };

    private static string Plural(int n, string word) => n == 1 ? word : word + "s";

    // --- :state — focused current-frame view --------------------------------------------------

    /// <summary>Renders a focused, human-readable view of the current frame for the REPL's
    /// <c>:state</c>: the active sign-in, the navigation breadcrumb, and either the current PO
    /// (attributes, actions, detail-query names, notification) or the current Query (totals, search,
    /// actions, a head of rows). The deliberately small counterpart to <c>:snapshot</c>'s raw JSON.</summary>
    public static void RenderState(Snapshot snap)
    {
        if (snap.Session is { } s)
            AnsiConsole.MarkupLine($"[grey]session:[/] {Markup.Escape(s.User ?? "?")} [grey]@[/] {Markup.Escape(s.Uri ?? "?")}");
        else
            AnsiConsole.MarkupLine("[grey]session:[/] [yellow]not signed in[/]");

        if (snap.NavStack is { Count: > 0 } nav)
        {
            var crumbs = nav.Select(f => f.IsDialog ? $"[grey][[[/]{Markup.Escape(f.Name)}[grey]]][/]" : Markup.Escape(f.Name));
            AnsiConsole.MarkupLine($"[grey]nav:[/] {string.Join(" [grey]›[/] ", crumbs)}");
        }
        else
            AnsiConsole.MarkupLine("[grey]nav:[/] [grey](empty)[/]");

        if (snap.Po is { } po)
            RenderPoState(po);
        else if (snap.Query is { } q)
            RenderQueryState(q);
        else
            AnsiConsole.MarkupLine("[grey](no current frame)[/]");
    }

    private static void RenderPoState(PoSnapshot po)
    {
        var id = string.IsNullOrEmpty(po.ObjectId) ? "<new>" : po.ObjectId!;
        var title = $"[bold]{Markup.Escape(po.Type)}[/] / {Markup.Escape(id)}";
        if (po.IsInEdit) title += " [grey][[edit]][/]";
        if (po.IsDirty) title += " [yellow][[dirty]][/]";
        RenderPoTable(po, title);

        if (!string.IsNullOrEmpty(po.Notification))
            AnsiConsole.MarkupLine($"  [red]![/] [grey]{Markup.Escape(po.NotificationType ?? "")}:[/] {Markup.Escape(po.Notification!)}");

        var actions = po.Actions.Where(a => a.IsVisible).ToList();
        if (actions.Count > 0)
            AnsiConsole.MarkupLine($"  [grey]actions:[/] {string.Join(", ", actions.Select(ActionChip))}");
        if (po.DetailQueries is { Count: > 0 } details)
            AnsiConsole.MarkupLine($"  [grey]details:[/] {string.Join(", ", details.Select(d => Markup.Escape(d)))}");
    }

    private static void RenderQueryState(QuerySnapshot q)
    {
        var head = $"[bold]Query \"{Markup.Escape(q.Name)}\"[/] · {q.TotalItems} {Plural(q.TotalItems, "item")}, {q.Count} loaded";
        if (!string.IsNullOrEmpty(q.TextSearch)) head += $"  [grey](search: \"{Markup.Escape(q.TextSearch!)}\")[/]";
        if (q.AllSelected || q.SelectedCount > 0) head += $"  [grey](selection: {SelectionText(q)})[/]";
        AnsiConsole.MarkupLine(head);

        if (q.Actions is { Count: > 0 } qActions)
        {
            var vis = qActions.Where(a => a.IsVisible).ToList();
            if (vis.Count > 0)
                AnsiConsole.MarkupLine($"  [grey]actions:[/] {string.Join(", ", vis.Select(ActionChip))}");
        }

        if (q.Rows.Count > 0)
        {
            // Cap the column count so a wide query doesn't overflow the terminal; the full shape is in :snapshot.
            const int maxCols = 6;
            var cols = q.Rows[0].Keys.Take(maxCols).ToList();
            var t = new Table().Border(TableBorder.Rounded);
            foreach (var c in cols) t.AddColumn(Markup.Escape(c));
            foreach (var row in q.Rows)
                t.AddRow(cols.Select(c => Markup.Escape(row.TryGetValue(c, out var v) ? v ?? "" : "")).ToArray());
            AnsiConsole.Write(t);
            var extra = q.Rows[0].Count - cols.Count;
            if (extra > 0) AnsiConsole.MarkupLine($"  [grey](+{extra} more column(s) — see :snapshot)[/]");
        }
    }

    private static string ActionChip(ActionSnapshot a) =>
        a.CanExecute ? $"{Markup.Escape(a.Name)} [green]✓[/]" : $"[grey]{Markup.Escape(a.Name)} ✗[/]";
}
