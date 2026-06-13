using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        var sep = path.LastIndexOfAny(new[] { '/', '\\' }, wild);
        var root = sep < 0 ? "." : path.Substring(0, sep);
        var pattern = sep < 0 ? path : path.Substring(sep + 1);
        var recursive = pattern.Contains("**");
        var filePattern = pattern.Replace("**/", "").Replace("**\\", "").Replace("**", "");
        if (filePattern.Length == 0) filePattern = "*.visc";
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateFiles(root, filePattern,
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    }
}
