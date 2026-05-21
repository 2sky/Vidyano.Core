using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Vidyano.ViewModel
{
    /// <summary>
    /// A top-level menu container in <see cref="Client.ProgramUnits"/>. Each ProgramUnit shows as
    /// one tab/region in the v4 frontend and holds a tree of <see cref="ProgramUnitItem"/>s.
    /// </summary>
    public sealed class ProgramUnit : ProgramUnitItem
    {
        internal ProgramUnit(JObject model) : base(model)
        {
            OpenFirst = (bool?)model["openFirst"] ?? false;
            Icon      = (string)model["icon"];
            Offset    = (int?)model["offset"] ?? 0;
            Items     = ProgramUnitItemGroup.ParseChildren(model);
        }

        /// <summary>When true, the v4 frontend treats this unit's first openable item as its
        /// landing page (used by <c>OPEN MenuItem &lt;Unit&gt;</c> with no further segments).</summary>
        public bool OpenFirst { get; }

        /// <summary>Icon name (rendered by the web UI; opaque to non-UI consumers).</summary>
        public string Icon { get; }

        /// <summary>Display ordering hint among units.</summary>
        public int Offset { get; }

        /// <summary>Top-level items in this unit (groups are nested inside).</summary>
        public IReadOnlyList<ProgramUnitItem> Items { get; }
    }
}
