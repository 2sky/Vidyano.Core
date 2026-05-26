using System;
using System.Collections.Generic;
using Vidyano.Script;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>Parsed CLI options shared by run and repl. Hand-rolled to keep <c>--help</c> honest.</summary>
public sealed class Args
{
    public string? File { get; set; }
    public string? AppUri { get; set; }
    public Dictionary<string, object?> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public GuardMode? Mode { get; set; }
    public bool Json { get; set; }
    public bool Verbose { get; set; }
    public bool Insecure { get; set; }
    public List<string> ToolPaths { get; } = new();
    public int? Seed { get; set; }
    public DateTimeOffset? Now { get; set; }
    public List<string> Unknown { get; } = new();

    /// <summary>Parses positional + flag arguments. Unknown flags are collected; callers decide whether to error.</summary>
    public static Args Parse(string[] args)
    {
        var result = new Args();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--app":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--app requires a URI"); break; }
                    result.AppUri = args[++i]; break;
                case "--var":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--var requires key=value"); break; }
                    var kv = args[++i];
                    var eq = kv.IndexOf('=');
                    if (eq <= 0) { result.Unknown.Add($"--var '{kv}' must be key=value"); break; }
                    result.Vars[kv.Substring(0, eq)] = kv.Substring(eq + 1);
                    break;
                case "--mode":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--mode requires navigation|audit|direct"); break; }
                    if (!Enum.TryParse<GuardMode>(args[++i], ignoreCase: true, out var m))
                    {
                        result.Unknown.Add($"--mode '{args[i]}' is not navigation, audit, or direct");
                        break;
                    }
                    result.Mode = m; break;
                case "--tools":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--tools requires a DLL path"); break; }
                    result.ToolPaths.Add(args[++i]); break;
                case "--seed":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--seed requires an integer"); break; }
                    if (!int.TryParse(args[++i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seed))
                    {
                        result.Unknown.Add($"--seed '{args[i]}' is not an integer");
                        break;
                    }
                    result.Seed = seed; break;
                case "--now":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--now requires an ISO datetime"); break; }
                    if (!DateTimeOffset.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var now))
                    {
                        result.Unknown.Add($"--now '{args[i]}' is not a valid ISO datetime");
                        break;
                    }
                    result.Now = now; break;
                case "--json":     result.Json = true; break;
                case "--verbose":  result.Verbose = true; break;
                case "--insecure": result.Insecure = true; break;
                default:
                    if (a.StartsWith('-')) result.Unknown.Add($"Unknown flag: {a}");
                    else if (result.File is null) result.File = a;
                    else result.Unknown.Add($"Unexpected positional argument: {a}");
                    break;
            }
        }
        return result;
    }

    /// <summary>Builds the <see cref="VidyanoScriptOptions"/> the engine expects.</summary>
    public VidyanoScriptOptions ToOptions()
    {
        var opts = new VidyanoScriptOptions
        {
            RemoteUri = AppUri,
            AcceptAnyServerCertificate = Insecure,
            Seed = Seed,
            Now = Now,
        };
        if (Mode is { } m) opts.Mode = m;
        foreach (var kv in Vars) opts.Variables[kv.Key] = kv.Value;
        return opts;
    }
}
