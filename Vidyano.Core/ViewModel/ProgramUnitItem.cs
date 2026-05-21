using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Vidyano.ViewModel
{
    /// <summary>
    /// One node in a <see cref="ProgramUnit"/>'s menu tree — either a leaf (Query / PersistentObject /
    /// Url / Separator) or a container (<see cref="ProgramUnitItemGroup"/>). Mirrors the v4 frontend's
    /// <c>ProgramUnitItem</c> hierarchy, with shapes drawn from the server-emitted
    /// <c>application.attributes["ProgramUnits"]</c> JSON.
    /// </summary>
    public abstract class ProgramUnitItem
    {
        private protected ProgramUnitItem(JObject model)
        {
            Id    = (string)model["id"];
            Name  = (string)model["name"];
            Title = (string)model["title"];
        }

        /// <summary>Server-assigned identifier for the item.</summary>
        public string Id { get; }

        /// <summary>Internal name (used for navigation routing). May contain backslashes when the
        /// item belongs to a path-implied group.</summary>
        public string Name { get; }

        /// <summary>Display title shown in the menu.</summary>
        public string Title { get; }

        internal static ProgramUnitItem Create(JObject model)
        {
            if (model["isSeparator"] is JValue { Value: bool isSep } && isSep)
                return new ProgramUnitItemSeparator(model);
            if (model["query"] != null)
                return new ProgramUnitItemQuery(model);
            if (model["persistentObject"] != null || model["persistentObjectType"] != null)
                return new ProgramUnitItemPersistentObject(model);
            if (model["url"] != null)
                return new ProgramUnitItemUrl(model);
            if (model["items"] != null || model["groups"] != null)
                return new ProgramUnitItemGroup(model);
            return new ProgramUnitItemSeparator(model);
        }
    }

    /// <summary>A leaf item that opens a Query.</summary>
    public sealed class ProgramUnitItemQuery : ProgramUnitItem
    {
        internal ProgramUnitItemQuery(JObject model) : base(model)
        {
            QueryId              = (string)model["query"];
            QueryName            = (string)model["queryName"];
            PersistentObjectType = (string)model["persistentObjectType"];
        }

        /// <summary>The Query GUID. Accepted by <see cref="Client.GetQueryAsync(string)"/>.</summary>
        public string QueryId { get; }

        /// <summary>Friendly query name. Preferred for human-readable navigation;
        /// <see cref="Client.GetQueryAsync(string)"/> also accepts this form.</summary>
        public string QueryName { get; }

        /// <summary>Target PersistentObject type that rows of this query open into.</summary>
        public string PersistentObjectType { get; }
    }

    /// <summary>A leaf item that opens a PersistentObject directly.</summary>
    public sealed class ProgramUnitItemPersistentObject : ProgramUnitItem
    {
        internal ProgramUnitItemPersistentObject(JObject model) : base(model)
        {
            PersistentObjectId   = (string)model["persistentObject"];
            PersistentObjectType = (string)model["persistentObjectType"];
            ObjectId             = (string)model["objectId"];
        }

        /// <summary>The PersistentObject GUID.</summary>
        public string PersistentObjectId { get; }

        /// <summary>The PersistentObject type name (passed as the <c>type</c> arg to
        /// <see cref="Client.GetPersistentObjectAsync(string, string)"/>).</summary>
        public string PersistentObjectType { get; }

        /// <summary>Optional object id when the item targets a specific instance.</summary>
        public string ObjectId { get; }
    }

    /// <summary>A leaf item that opens an external URL — not driveable through the Vidyano client.</summary>
    public sealed class ProgramUnitItemUrl : ProgramUnitItem
    {
        internal ProgramUnitItemUrl(JObject model) : base(model)
        {
            Url = (string)model["url"];
        }

        /// <summary>The destination URL.</summary>
        public string Url { get; }
    }

    /// <summary>A separator entry — visual only, has no target.</summary>
    public sealed class ProgramUnitItemSeparator : ProgramUnitItem
    {
        internal ProgramUnitItemSeparator(JObject model) : base(model) { }
    }

    /// <summary>A nested group of items inside a <see cref="ProgramUnit"/>. Groups can recurse.</summary>
    public sealed class ProgramUnitItemGroup : ProgramUnitItem
    {
        internal ProgramUnitItemGroup(JObject model) : base(model)
        {
            Items = ParseChildren(model);
        }

        /// <summary>The group's child items (already includes nested groups flattened into the list).</summary>
        public IReadOnlyList<ProgramUnitItem> Items { get; }

        internal static IReadOnlyList<ProgramUnitItem> ParseChildren(JObject node)
        {
            var children = new List<ProgramUnitItem>();
            if (node["items"] is JArray items)
                children.AddRange(items.OfType<JObject>().Select(Create));
            if (node["groups"] is JArray groups)
                children.AddRange(groups.OfType<JObject>().Select(g => new ProgramUnitItemGroup(g)));
            return children;
        }
    }
}
