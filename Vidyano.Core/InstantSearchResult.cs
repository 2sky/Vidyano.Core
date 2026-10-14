#nullable enable

namespace Vidyano
{
    /// <summary>
    /// One row of the server's instant search (the dropdown under the web client's menu search box), as returned by
    /// <see cref="Client.GetInstantSearchAsync"/>. A row points at a PersistentObject: open it with
    /// <c>Client.GetPersistentObjectAsync(Id, ObjectId)</c>, the target the web client links to
    /// (<c>PersistentObject.{Id}/{ObjectId}</c>).
    /// </summary>
    public sealed class InstantSearchResult
    {
        /// <summary>Creates a result row.</summary>
        public InstantSearchResult(string? id, string? label, string? objectId, string? breadcrumb)
        {
            Id = id;
            Label = label;
            ObjectId = objectId;
            Breadcrumb = breadcrumb;
        }

        /// <summary>The id of the PersistentObject type the row opens.</summary>
        public string? Id { get; }

        /// <summary>The label of that PersistentObject type (the row's group caption in the dropdown).</summary>
        public string? Label { get; }

        /// <summary>The object id of the row's PersistentObject.</summary>
        public string? ObjectId { get; }

        /// <summary>The row's display text.</summary>
        public string? Breadcrumb { get; }
    }
}
