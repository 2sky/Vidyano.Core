using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Vidyano.Script;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>
/// Loads <see cref="IVidyanoScriptToolPack"/> implementations from external DLLs supplied with
/// <c>--tools &lt;path.dll&gt;</c>. The CLI does no per-plugin isolation: the assembly is loaded
/// into the default ALC, so a plugin's <c>Vidyano.Script</c> reference resolves against the
/// CLI's already-loaded copy. That gives type identity for <see cref="IVidyanoScriptToolPack"/>
/// (essential for the <c>is</c>/<c>as</c> checks below) at the cost of zero dep-version
/// isolation — plugins must build against a Vidyano.Script surface compatible with the
/// installed CLI.
/// </summary>
public static class ToolPackLoader
{
    /// <summary>One discovered + registered pack. <see cref="ToolNames"/> is the diff of tool
    /// names the pack added to the options dictionary (so the CLI can report what's loaded
    /// without trusting the pack's documentation).</summary>
    public sealed record Loaded(string Path, string PackTypeName, IReadOnlyList<string> ToolNames);

    /// <summary>
    /// Loads every pack listed in <paramref name="paths"/> into <paramref name="options"/>.
    /// Throws on missing file, load failure, or a DLL that contains no pack types. Callers
    /// catch and surface as a usage error.
    /// </summary>
    public static List<Loaded> LoadInto(IEnumerable<string> paths, VidyanoScriptOptions options)
    {
        var loaded = new List<Loaded>();
        foreach (var p in paths)
        {
            var full = Path.GetFullPath(p);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Tool pack DLL not found: {full}", full);

            Assembly asm;
            try
            {
                asm = Assembly.LoadFrom(full);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load tool pack '{full}': {ex.Message}", ex);
            }

            // GetTypes() can partially succeed even when some types fail to load (missing
            // transitive deps, etc.). Surface the loadable subset rather than failing the whole
            // pack — a single broken type shouldn't sink the others.
            Type[] candidates;
            try { candidates = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { candidates = rtle.Types.Where(t => t is not null).Cast<Type>().ToArray(); }

            var packTypes = candidates
                .Where(t => typeof(IVidyanoScriptToolPack).IsAssignableFrom(t)
                            && !t.IsAbstract
                            && !t.IsInterface
                            && t.IsPublic
                            && t.GetConstructor(Type.EmptyTypes) is not null)
                .ToList();

            if (packTypes.Count == 0)
                throw new InvalidOperationException(
                    $"No IVidyanoScriptToolPack implementations found in '{full}'. " +
                    "A pack must be public, non-abstract, and have a public parameterless constructor.");

            foreach (var t in packTypes)
            {
                var before = new HashSet<string>(options.Tools.Keys, StringComparer.OrdinalIgnoreCase);
                IVidyanoScriptToolPack pack;
                try { pack = (IVidyanoScriptToolPack)Activator.CreateInstance(t)!; }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to construct tool pack '{t.FullName}' from '{full}': {ex.Message}", ex);
                }

                try { pack.Register(options.Tools); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Tool pack '{t.FullName}' threw during Register: {ex.Message}", ex);
                }

                var added = options.Tools.Keys
                    .Where(k => !before.Contains(k))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                loaded.Add(new Loaded(full, t.FullName ?? t.Name, added));
            }
        }
        return loaded;
    }
}
