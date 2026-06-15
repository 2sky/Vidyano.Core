using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>Turns CLI path arguments into the <see cref="ViscSource"/> set a suite runs. Each argument may be
/// a directory (recursive <c>*.visc</c>), an explicit file, or a glob (<c>tests/**/*.visc</c>, <c>*.visc</c>).
/// Results are de-duplicated and ordered so a suite run — and its reports — are deterministic.</summary>
internal static class SourceDiscovery
{
    public static IReadOnlyList<ViscSource> Discover(IEnumerable<string> paths)
    {
        // Key dedup/ordering on the full path (two args can resolve to the same file); display with forward
        // slashes so report identifiers (JUnit classname, SARIF uri) are stable across OSes.
        var seen = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
            foreach (var file in Expand(p))
            {
                var full = Path.GetFullPath(file);
                if (!seen.ContainsKey(full)) seen[full] = file.Replace('\\', '/');
            }
        return seen.Values.Select(display => new ViscSource(display, File.ReadAllText(display))).ToList();
    }

    private static IEnumerable<string> Expand(string path)
    {
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*.visc", SearchOption.AllDirectories);
        if (File.Exists(path))
            return new[] { path };

        var wild = path.IndexOfAny(new[] { '*', '?' });
        if (wild < 0)
            return Array.Empty<string>(); // a non-existent literal path — caller reports "no files found"

        // Split the glob at the last separator before the first wildcard: the prefix is a literal root to
        // walk, the suffix is the match pattern. Walk the root recursively and filter on each file's path
        // relative to it, so a wildcard in any segment (tests/*/*.visc, tests/**/sub/*.visc) works — we never
        // hand a separator-bearing pattern to Directory.EnumerateFiles, which throws ArgumentException on one.
        var sep = path.LastIndexOfAny(new[] { '/', '\\' }, wild);
        var root = sep < 0 ? "." : path.Substring(0, sep);
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var pattern = sep < 0 ? path : path.Substring(sep + 1);

        var regex = GlobToRegex(pattern);
        var rootPrefix = Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/') + "/";
        return Directory.EnumerateFiles(root, "*.visc", SearchOption.AllDirectories)
            .Where(f =>
            {
                var full = Path.GetFullPath(f).Replace('\\', '/');
                return full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                    && regex.IsMatch(full.Substring(rootPrefix.Length));
            });
    }

    // Compiles a glob pattern (already split off its literal root) to an anchored regex over the
    // forward-slash relative path. `**/` spans any number of directories (including none), `**` spans
    // anything, `*` stays within one segment, `?` matches one non-separator char.
    private static Regex GlobToRegex(string glob)
    {
        var body = Regex.Escape(glob.Replace('\\', '/'))
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        return new Regex("^" + body + "$", RegexOptions.IgnoreCase);
    }
}
