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
            SaveStmt                   => "SAVE",
            RefreshStmt                => "REFRESH",
            SetStmt s                  => $"SET {Markup.Escape(s.Attribute)} = …",
            ActionStmt a               => $"ACTION {Markup.Escape(a.ActionName)}",
            SearchStmt                 => "SEARCH …",
            ExpectStmt e               => $"EXPECT {DescribeSubject(e.Subject)}",
            ToolCallStmt t             => $"TOOL {Markup.Escape(t.Name)}{(t.ResultVariable is null ? "" : $" -> @{Markup.Escape(t.ResultVariable)}")}",
            RequiresStmt r             => $"REQUIRES {DescribeSubject(r.Subject)}",
            RequiresToolStmt rt        => $"REQUIRES TOOL {Markup.Escape(rt.ToolName)}",
            CleanupMarker              => "CLEANUP",
            UseSessionStmt u           => $"USE @{u.SessionName}",
            SignOutStmt                => "SIGN-OUT",
            _                          => stmt.GetType().Name,
        };

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
}
