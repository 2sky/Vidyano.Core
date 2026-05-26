using System.Linq;
using Newtonsoft.Json.Linq;

namespace Vidyano.Script.Runtime;

/// <summary>
/// A single entry from a Vidyano response's <c>operations</c> array — the side-effects the server
/// asks the client to perform (refresh a query, navigate elsewhere, show a dialog, …).
/// </summary>
/// <remarks>
/// Vidyano.Core doesn't currently surface these (the <c>// TODO: response["operations"]</c> at
/// <c>Client.cs:602</c> is unresolved), so the script runner harvests them through the
/// <see cref="Vidyano.Client.LogPosts"/> hook.
///
/// The server emits <c>ExecuteMethod</c> as a single op type carrying a method name (navigate,
/// showMessageBox, openUrl, …). For scripting we elevate that method name to the top-level
/// <see cref="Type"/> — so a script can write <c>EXPECT ClientOperation Navigate "/orders"</c>
/// without knowing about the <c>ExecuteMethod</c> wrapper. The original wrapper type is kept in
/// <see cref="WireType"/> for callers that need it.
/// </remarks>
public sealed record ClientOperation(string Type, JObject Raw)
{
    /// <summary>The raw <c>type</c> from the wire ("Refresh", "Open", "ExecuteMethod") before
    /// the ExecuteMethod elevation. Useful when an assertion needs to match the wire shape.</summary>
    public string WireType => (string?)Raw["type"] ?? Type;

    /// <summary>
    /// Constructs a <see cref="ClientOperation"/> with the friendly <see cref="Type"/> already
    /// resolved. For <c>ExecuteMethod</c> ops we capitalize the <c>name</c> field
    /// (<c>navigate</c> → <c>Navigate</c>) so script types stay stable across releases.
    /// </summary>
    public static ClientOperation? FromJson(JObject raw)
    {
        var wire = (string?)raw["type"];
        if (string.IsNullOrEmpty(wire)) return null;

        // ExecuteMethod is the catch-all wrapper for navigate/showMessageBox/openUrl/etc.
        // Elevate the inner method name to the script-facing Type.
        if (string.Equals(wire, "ExecuteMethod", System.StringComparison.Ordinal))
        {
            var name = (string?)raw["name"];
            if (!string.IsNullOrEmpty(name))
                return new ClientOperation(Capitalize(name!), raw);
        }
        return new ClientOperation(wire!, raw);
    }

    /// <summary>For <c>Refresh</c>: the PersistentObject full type name (when refreshing a PO).</summary>
    public string? FullTypeName => (string?)Raw["fullTypeName"];

    /// <summary>For <c>Refresh</c>: the object id (when refreshing a specific PO instance).</summary>
    public string? ObjectId => (string?)Raw["objectId"];

    /// <summary>For <c>Refresh</c>: the query id (Guid string, when refreshing a Query).</summary>
    public string? QueryId => (string?)Raw["queryId"];

    /// <summary>For elevated <c>ExecuteMethod</c> ops (Navigate, ShowMessageBox, OpenUrl, …):
    /// the positional arguments the server sent.</summary>
    public JArray Arguments => Raw["arguments"] as JArray ?? new JArray();

    /// <summary>
    /// The canonical value <c>EXPECT ClientOperation &lt;Type&gt; = "..."</c> compares against.
    /// Picks the most natural field for each known type so scripts don't have to know the wire shape.
    /// </summary>
    public string? PrimaryValue => Type switch
    {
        "Refresh"        => FullTypeName ?? QueryId,
        "Open"           => (string?)Raw["persistentObject"]?["fullTypeName"]
                            ?? (string?)Raw["persistentObject"]?["type"],
        "Navigate"       => ArgAt(0),
        "OpenUrl"        => ArgAt(0),
        "ShowMessageBox" => ArgAt(1) ?? ArgAt(0), // arguments = [title, message, rich, delay]
        "CopyToClipboard"=> ArgAt(0),
        _                => ArgAt(0) ?? (string?)Raw["value"] ?? (string?)Raw["name"],
    };

    private string? ArgAt(int index)
    {
        var args = Arguments;
        if (index < 0 || index >= args.Count) return null;
        return args[index] switch
        {
            JValue v when v.Value is string s => s,
            JValue v => v.Value?.ToString(),
            _ => args[index].ToString(),
        };
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
