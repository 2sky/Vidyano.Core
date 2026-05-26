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
        var opts = new VidyanoScriptOptions { RemoteUri = AppUri, AcceptAnyServerCertificate = Insecure };
        if (Mode is { } m) opts.Mode = m;
        foreach (var kv in Vars) opts.Variables[kv.Key] = kv.Value;
        return opts;
    }
}
