using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using Vidyano.Script;

namespace Vidyano.Script.Tool;

/// <summary><c>vidyano lint &lt;file&gt;</c> — parse-check a .visc without executing it, plus a
/// presence-only check for <c>{{x}}</c> reads of undeclared variables. <c>--var</c> / <c>--env-prefix</c>
/// names are forwarded as "expected" so a parameterized script isn't flagged for values it gets at run time.</summary>
public static class LintCommand
{
    public static Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0 || Cli.IsHelpFlag(args[0]))
        {
            AnsiConsole.MarkupLine("Usage: [yellow]vidyano lint <file.visc> [--var k=v] [--env-prefix PREFIX][/]");
            return Task.FromResult(Cli.ExitUsage);
        }

        var parsed = Args.Parse(args);
        if (parsed.Unknown.Count > 0)
        {
            foreach (var u in parsed.Unknown)
                AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(u)}");
            return Task.FromResult(Cli.ExitUsage);
        }
        if (parsed.File is null)
        {
            AnsiConsole.MarkupLine("[red]error:[/] no .visc file given");
            return Task.FromResult(Cli.ExitUsage);
        }

        var path = parsed.File;
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]error:[/] file not found: [yellow]{Markup.Escape(path)}[/]");
            return Task.FromResult(Cli.ExitUsage);
        }

        var diags = VidyanoScript.Lint(File.ReadAllText(path), path, ExpectedVariables(parsed));
        if (diags.Count == 0)
        {
            AnsiConsole.MarkupLine($"[green]ok[/] {Markup.Escape(path)} (no problems)");
            return Task.FromResult(Cli.ExitOk);
        }

        foreach (var d in diags)
            ConsoleReporter.WriteDiagnostic(d);
        AnsiConsole.MarkupLine($"[red]{diags.Count} problem(s)[/] in {Markup.Escape(path)}");
        return Task.FromResult(Cli.ExitLint);
    }

    /// <summary>The variable names the run would supply externally: explicit <c>--var</c> keys plus the
    /// process-env names <c>--env-prefix</c> would bulk-bind (prefix stripped), mirroring the engine's
    /// own binding so a script that reads <c>{{REGION}}</c> off <c>VIDYANO_REGION</c> isn't flagged.</summary>
    private static IEnumerable<string> ExpectedVariables(Args parsed)
    {
        var names = new List<string>(parsed.Vars.Keys);
        if (parsed.EnvironmentPrefix is { Length: > 0 } prefix)
            foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
                if (e.Key is string k && k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.Length > prefix.Length)
                    names.Add(k.Substring(prefix.Length));
        return names;
    }
}
