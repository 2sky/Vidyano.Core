using System;
using System.Collections.Generic;
using Vidyano.Script;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>One <c>--report &lt;format&gt;[:&lt;target&gt;]</c> request. <see cref="Target"/> is null when the
/// format was given bare (write to stdout); otherwise it is the file path to write.</summary>
public readonly record struct ReportSpec(string Format, string? Target);

/// <summary>Parsed CLI options shared by run, test and repl. Hand-rolled to keep <c>--help</c> honest.</summary>
public sealed class Args
{
    public string? File { get; set; }
    /// <summary>All positional arguments, in order. <c>run</c>/<c>lint</c> accept exactly one (they read
    /// <see cref="File"/> and reject extras); <c>test</c> reads the whole list as files/dirs/globs.</summary>
    public List<string> Paths { get; } = new();
    public string? AppUri { get; set; }
    public Dictionary<string, object?> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public GuardMode? Mode { get; set; }
    public bool Json { get; set; }
    public bool Verbose { get; set; }
    public bool Insecure { get; set; }
    public List<string> ToolPaths { get; } = new();
    public int? Seed { get; set; }
    public DateTimeOffset? Now { get; set; }
    public string? EnvironmentPrefix { get; set; }
    public string? EnvFile { get; set; }
    public Dictionary<string, string?> EnvFileValues { get; } = new(StringComparer.Ordinal);
    /// <summary><c>--report</c> requests (repeatable). Formats: junit | tap | sarif.</summary>
    public List<ReportSpec> Reports { get; } = new();
    /// <summary>Per-file timeout. Null = not set; <see cref="TimeSpan.Zero"/> = explicitly disabled.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Suite parallelism (<c>--jobs</c>). Null = default (serial).</summary>
    public int? Jobs { get; set; }
    public List<string> Unknown { get; } = new();

    private static readonly string[] ReportFormats = { "junit", "tap", "sarif" };

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
                case "--env-prefix":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--env-prefix requires a prefix"); break; }
                    result.EnvironmentPrefix = args[++i]; break;
                case "--env-file":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--env-file requires a path"); break; }
                    var envPath = args[++i];
                    // Parse here (the single arg-validation gate both run and repl check via Unknown) so a
                    // missing/unreadable file surfaces as a usage error instead of an exception later.
                    // Repeatable: later files merge in, last value wins per key.
                    if (!System.IO.File.Exists(envPath))
                    {
                        result.Unknown.Add(System.IO.Directory.Exists(envPath)
                            ? $"--env-file '{envPath}' is a directory, not a file"
                            : $"--env-file '{envPath}' not found");
                        break;
                    }
                    try
                    {
                        foreach (var pair in DotEnv.Parse(System.IO.File.ReadAllText(envPath)))
                            result.EnvFileValues[pair.Key] = pair.Value;
                        result.EnvFile = envPath;
                    }
                    catch (Exception ex) { result.Unknown.Add($"--env-file '{envPath}' could not be read: {ex.Message}"); }
                    break;
                case "--report":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--report requires a format (junit|tap|sarif)[:path]"); break; }
                    var spec = args[++i];
                    var colon = spec.IndexOf(':');
                    var fmt = (colon < 0 ? spec : spec.Substring(0, colon)).ToLowerInvariant();
                    var target = colon < 0 ? null : spec.Substring(colon + 1);
                    if (Array.IndexOf(ReportFormats, fmt) < 0)
                    {
                        result.Unknown.Add($"--report '{fmt}' is not junit, tap, or sarif");
                        break;
                    }
                    result.Reports.Add(new ReportSpec(fmt, string.IsNullOrEmpty(target) ? null : target));
                    break;
                case "--timeout":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--timeout requires a duration (e.g. 30s, 2m, 0)"); break; }
                    if (!TryParseDuration(args[++i], out var to)) { result.Unknown.Add($"--timeout '{args[i]}' is not a duration (try 30s, 2m, 1h, or 0)"); break; }
                    result.Timeout = to; break;
                case "--jobs":
                    if (i + 1 >= args.Length) { result.Unknown.Add("--jobs requires a positive integer"); break; }
                    if (!int.TryParse(args[++i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var jobs) || jobs < 1)
                    {
                        result.Unknown.Add($"--jobs '{args[i]}' is not a positive integer");
                        break;
                    }
                    result.Jobs = jobs; break;
                case "--json":     result.Json = true; break;
                case "--verbose":  result.Verbose = true; break;
                case "--insecure": result.Insecure = true; break;
                default:
                    if (a.StartsWith('-')) result.Unknown.Add($"Unknown flag: {a}");
                    else { result.Paths.Add(a); result.File ??= a; }
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
            EnvironmentPrefix = EnvironmentPrefix,
        };
        if (Mode is { } m) opts.Mode = m;
        foreach (var kv in Vars) opts.Variables[kv.Key] = kv.Value;
        if (EnvFile != null)
        {
            // Back {{env:NAME}} and SIGN-IN FROM ENV with the .env values, shadowing the process env; keys
            // absent from the file fall through to it. (--env-prefix and --var use a separate variable
            // namespace and are unaffected.)
            var envValues = EnvFileValues;
            opts.EnvLookup = name => envValues.TryGetValue(name, out var v) ? v : Environment.GetEnvironmentVariable(name);
        }
        return opts;
    }

    /// <summary>Parses a duration: a bare number (seconds) or a number with an <c>s</c>/<c>m</c>/<c>h</c>
    /// suffix. <c>0</c> yields <see cref="TimeSpan.Zero"/> (the caller treats non-positive as "no limit").</summary>
    internal static bool TryParseDuration(string text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var unit = char.ToLowerInvariant(text[text.Length - 1]);
        var hasSuffix = unit is 's' or 'm' or 'h';
        var number = hasSuffix ? text.Substring(0, text.Length - 1) : text;
        if (!double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 0)
            return false;
        value = unit switch
        {
            'm' when hasSuffix => TimeSpan.FromMinutes(n),
            'h' when hasSuffix => TimeSpan.FromHours(n),
            _ => TimeSpan.FromSeconds(n),
        };
        return true;
    }
}
