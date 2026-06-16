using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Vidyano.Script.Runtime;

/// <summary>
/// Resolves a script-supplied relative path against a trusted root directory while guaranteeing the
/// result cannot escape it. The <c>SET attr = FILE "&lt;path&gt;"</c> verb runs this before touching the
/// file system, turning the path-traversal class of mistake into a clean "not contained → reject".
/// <para>Ported from <c>Vidyano.Core.Security.SafePath</c> in Vidyano.Service (the dependency-free
/// overload only). Keep the containment semantics — trailing-separator prefix match, OS-matched case
/// sensitivity, broad catch on canonicalization — in sync if the upstream logic changes.</para>
/// </summary>
internal static class SafePath
{
    /// <summary>
    /// Resolves <paramref name="relativePath"/> against <paramref name="baseDirectory"/> and guarantees the
    /// result stays inside it. Returns <c>false</c> (and a null <paramref name="fullPath"/>) for any path
    /// that escapes the base — <c>..</c> traversal, a rooted or drive-qualified path, or a leading separator.
    /// </summary>
    public static bool TryResolveContained(string baseDirectory, string relativePath, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;

        if (string.IsNullOrEmpty(baseDirectory) || string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        string baseRoot, candidate;
        try
        {
            baseRoot = Path.GetFullPath(baseDirectory);
            candidate = Path.GetFullPath(Path.Combine(baseRoot, relativePath));
        }
        catch (Exception)
        {
            // relativePath is script-controlled; the contract is "any failure to resolve → not contained".
            // Catch broadly so a stray canonicalization exception becomes a clean reject.
            return false;
        }

        // The trailing separator is load-bearing: it stops a sibling-prefix match
        // (base "...\site" must not admit a candidate under "...\siteEVIL").
        var prefix = baseRoot.EndsWith(Path.DirectorySeparatorChar) ? baseRoot : baseRoot + Path.DirectorySeparatorChar;

        // Match the filesystem's case semantics: case-insensitive only on Windows. On a case-sensitive
        // filesystem an OrdinalIgnoreCase check would admit a case-variant sibling (base "/var/www" +
        // "../WWW/x" → "/var/WWW/x"), a different, uncontained directory there.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(prefix, comparison))
            return false;

        fullPath = candidate;
        return true;
    }
}
