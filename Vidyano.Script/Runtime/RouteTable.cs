using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Vidyano.Script.Runtime;

/// <summary>Where a web-client path leads: a PersistentObject (type id + optional object id) or a Query.</summary>
public sealed record NavigateTarget(bool IsPersistentObject, string Id, string? ObjectId);

/// <summary>
/// The web client's route table — turns a <c>Navigate(path)</c> path into the PersistentObject or Query the
/// browser would open there. Built from the Application's <c>Routes</c> attribute (the server's
/// <c>{programUnits, persistentObjects, queries}</c> name → id maps) exactly as <c>application.ts</c>
/// <c>#setRoutes</c> does: route names are matched raw and kebab-cased, optionally behind a program-unit
/// prefix. Paths the friendly routes don't cover fall back to the client's raw
/// <c>[pu/]persistent-object.&lt;id&gt;[/&lt;objectId&gt;]</c> and <c>[pu/]query.&lt;id&gt;</c> routes.
/// </summary>
internal sealed class RouteTable
{
    private static readonly Regex rawPoRe = new(@"^(?:[^/]+/)?(?:persistent-object|PersistentObject)\.([^/]+)(?:/(.+))?$");
    private static readonly Regex rawQueryRe = new(@"^(?:[^/]+/)?(?:query|Query)\.([^/]+)$");

    private readonly Dictionary<string, string> _persistentObjects; // kebab route name → PO type id
    private readonly Dictionary<string, string> _queries;           // kebab route name → query id
    private readonly Regex? _poRe;
    private readonly Regex? _queryRe;

    private RouteTable(JObject routes)
    {
        var programUnits = Names(routes["programUnits"]);
        var poRoutes = Map(routes["persistentObjects"]);
        var queryRoutes = Map(routes["queries"]);

        _persistentObjects = Kebab(poRoutes);
        _queries = Kebab(queryRoutes);

        // Same shapes as application.ts: ^((<pu>)/)?(<poRoute>)(/.+)?$ and ^((<pu>)/)?(<queryRoute>)$, where
        // each alternation holds the kebab-cased names followed by the raw ones.
        var puRoutes = "^((" + Alternation(programUnits.Concat(programUnits.Select(ToKebabCase))) + ")/)?";
        if (poRoutes.Count > 0)
            _poRe = new Regex(puRoutes + "(" + Alternation(_persistentObjects.Keys.Concat(poRoutes.Keys)) + ")(/.+)?$");
        if (queryRoutes.Count > 0)
            _queryRe = new Regex(puRoutes + "(" + Alternation(_queries.Keys.Concat(queryRoutes.Keys)) + ")$");

        RouteNames = _persistentObjects.Keys.Concat(_queries.Keys).ToArray();
    }

    /// <summary>The kebab-cased PersistentObject and Query route names, for "did you mean" hints.</summary>
    public IReadOnlyList<string> RouteNames { get; }

    /// <summary>Parses the Application's <c>Routes</c> attribute value (JSON).</summary>
    public static RouteTable Parse(string json) => new(JObject.Parse(json));

    /// <summary>Resolves a navigate path, or returns <c>null</c> when no route matches. Leading slashes are
    /// ignored, as <c>changePath</c> does; everything after the PO route's first <c>/</c> is the object id, so
    /// an id that itself contains <c>/</c> survives intact.</summary>
    public NavigateTarget? Resolve(string path)
    {
        path = path.TrimStart('/');

        if (_poRe?.Match(path) is { Success: true } po
            && _persistentObjects.TryGetValue(ToKebabCase(po.Groups[3].Value), out var poId))
        {
            var objectId = po.Groups[4].Success ? po.Groups[4].Value.Substring(1) : null;
            return new NavigateTarget(true, poId, objectId);
        }

        if (_queryRe?.Match(path) is { Success: true } q
            && _queries.TryGetValue(ToKebabCase(q.Groups[3].Value), out var queryId))
            return new NavigateTarget(false, queryId, null);

        if (rawPoRe.Match(path) is { Success: true } rawPo)
            return new NavigateTarget(true, rawPo.Groups[1].Value, rawPo.Groups[2].Success ? rawPo.Groups[2].Value : null);

        if (rawQueryRe.Match(path) is { Success: true } rawQuery)
            return new NavigateTarget(false, rawQuery.Groups[1].Value, null);

        return null;
    }

    /// <summary>A port of the web client's <c>String.prototype.toKebabCase</c> (<c>common/string.ts</c>):
    /// <c>VestaChargePoint</c> → <c>vesta-charge-point</c>, <c>HTMLPage</c> → <c>html-page</c>. Must stay
    /// character-for-character identical, or a route the browser resolves would miss here.</summary>
    internal static string ToKebabCase(string s)
    {
        if (string.IsNullOrEmpty(s) || s == s.ToLowerInvariant())
            return s;

        var sb = new StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var cLower = char.ToLowerInvariant(c);
            if (c == cLower || i == 0)
            {
                sb.Append(cLower);
                continue;
            }

            var cPrev = s[i - 1];
            if (!IsAsciiLetter(cPrev))
                sb.Append(cLower);
            else if (cPrev == char.ToLowerInvariant(cPrev))
                sb.Append('-').Append(cLower);
            else if (i + 1 == s.Length || s[i + 1] == char.ToUpperInvariant(s[i + 1]))
                sb.Append(cLower);
            else
                sb.Append('-').Append(cLower);
        }
        return sb.ToString();
    }

    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static Dictionary<string, string> Map(JToken? token)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (token is JObject obj)
            foreach (var prop in obj.Properties())
                map[prop.Name] = (string?)prop.Value ?? "";
        return map;
    }

    private static List<string> Names(JToken? token) =>
        token is JObject obj ? obj.Properties().Select(p => p.Name).ToList() : new List<string>();

    // Later keys win, matching Object.assign over the kebab-cased names.
    private static Dictionary<string, string> Kebab(Dictionary<string, string> routes)
    {
        var kebab = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in routes)
            kebab[ToKebabCase(kv.Key)] = kv.Value;
        return kebab;
    }

    private static string Alternation(IEnumerable<string> names) =>
        string.Join("|", names.Distinct(StringComparer.Ordinal).Select(Regex.Escape));
}
