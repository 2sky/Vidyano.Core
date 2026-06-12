using Spectre.Console;
using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Tool;

/// <summary>
/// Renders the canonical <see cref="VerbCatalog"/> as a Spectre table for <c>vidyano help verbs</c>.
/// The table holds no verb knowledge of its own — it is a pure view over the catalog, so it can never
/// drift from the parser's known-verb set again.
/// </summary>
public static class VerbReference
{
    public static void Print()
    {
        var t = new Table().Border(TableBorder.Rounded).Title("[bold].visc verb reference[/]");
        t.AddColumn("Verb");
        t.AddColumn("Example");
        t.AddColumn("Summary");

        foreach (var v in VerbCatalog.All)
        {
            var example = v.Examples.Count > 0 ? v.Examples[0] : v.Syntax;
            t.AddRow(Markup.Escape(v.Name), Markup.Escape(example), Markup.Escape(v.Summary));
        }

        AnsiConsole.Write(t);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Comments start with # — they're ignored, but `### label` starts a new step.[/]");
        AnsiConsole.MarkupLine("[grey]Strings: \"...\" with \\\" \\n \\t escapes; {{...}} interpolates inside strings (escape a literal brace as \\{).[/]");
        AnsiConsole.MarkupLine("[grey]Built-in vars: {{@today}} {{@now}} {{@uuid}} {{@random}} — evaluated per reference (capture into a var to freeze); --seed fixes the @uuid/@random sequence, --now anchors the clock.[/]");
        AnsiConsole.MarkupLine("[grey]Env values: {{env:NAME}} loud-fails when unset; {{env:NAME ?? \"fallback\"}} supplies a default. --env-file loads a .env (shadows process env); --env-prefix bulk-binds matching env vars (--var wins).[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Full reference:[/] [blue]https://github.com/2sky/Vidyano.Core/blob/main/docs/visc-language.md[/]");
    }
}
