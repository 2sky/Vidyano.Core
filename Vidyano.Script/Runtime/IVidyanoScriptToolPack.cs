using System.Collections.Generic;

namespace Vidyano.Script.Runtime;

/// <summary>
/// Plugin entry point for external tool packs loaded by the <c>vidyano</c> CLI via
/// <c>--tools &lt;path.dll&gt;</c>. The loader scans the assembly for public, non-abstract types
/// with a public parameterless constructor that implement this interface, instantiates each one,
/// and calls <see cref="Register"/> with the same dictionary backing
/// <see cref="VidyanoScriptOptions.Tools"/>. After that, any <c>TOOL &lt;name&gt;</c> verb in a
/// script resolves against the registered handlers exactly as it would for an in-process host.
/// </summary>
/// <remarks>
/// Plugins compile against <c>Vidyano.Script</c>; at runtime the CLI's already-loaded copy wins,
/// so plugins built against an older Script version still load as long as they use a subset of
/// the current surface. Cross-version drift is the plugin author's responsibility — the loader
/// does no isolation.
/// </remarks>
public interface IVidyanoScriptToolPack
{
    /// <summary>Registers handlers on <paramref name="tools"/>. Names are case-insensitive; a
    /// later registration with the same name overwrites the earlier one, matching the in-process
    /// API on <see cref="VidyanoScriptOptions.Tools"/>.</summary>
    void Register(IDictionary<string, ScriptToolHandler> tools);
}
