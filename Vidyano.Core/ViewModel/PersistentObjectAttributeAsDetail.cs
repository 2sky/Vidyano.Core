using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Vidyano.ViewModel
{
    [DebuggerDisplay("PersistentObjectAttributeAsDetail {Name}")]
    public class PersistentObjectAttributeAsDetail : PersistentObjectAttribute
    {
        internal PersistentObjectAttributeAsDetail(Client client, JObject model, PersistentObject parent)
            : base(client, model, parent)
        {
            propertiesToBackup = new[] { "value", "isReadOnly", "isValueChanged", "options", "validationError" };

            var hooks = client.Hooks;

            var details = (JObject)model["details"];
            if (details != null)
                Details = hooks.OnConstruct(client, details, parent, false);

            var objects = (JArray)model["objects"];
            if (objects != null)
            {
                Objects = objects.Cast<JObject>().Select(jObj =>
                {
                    var obj = hooks.OnConstruct(client, jObj);
                    obj.Parent = parent;
                    obj.OwnerDetailAttribute = this;
                    return obj;
                }).ToArray();
            }
            else
                Objects = Array.Empty<PersistentObject>();
        }

        public Query Details { get; }

        public PersistentObject[] Objects { get; set; }

        public bool CanNew => Details.Actions.Any(a => a.Name == "New");

        public bool CanDelete => Details.Actions.Any(a => a.Name == "Delete");

        public bool CanEdit => Details.Actions.Any(a => a.Name == "BulkEdit");

        /// <summary>Creates a new detail row by running the details query's <c>New</c> action, as the web client's
        /// add button does. The row is NOT added yet — pass it to <see cref="AddObjects"/> (after editing it, e.g.
        /// for a dialog-style row). Returns <c>null</c> when the server returned no object.</summary>
        public async Task<PersistentObject> NewObjectAsync()
        {
            var po = await Client.ExecuteActionAsync("Query.New", Parent, Details, Array.Empty<QueryResultItem>()).ConfigureAwait(false);
            if (po == null)
                return null;

            po.OwnerQuery = null;
            po.OwnerDetailAttribute = this;
            return po;
        }

        /// <summary>Appends rows (typically from <see cref="NewObjectAsync"/>) and marks the attribute changed.</summary>
        public void AddObjects(params PersistentObject[] objects)
        {
            foreach (var obj in objects)
            {
                obj.Parent = Parent;
                obj.OwnerDetailAttribute = this;
            }

            Objects = Objects.Concat(objects).ToArray();
            MarkChanged();
        }

        /// <summary>Removes a row the way the web client does: it is flagged <see cref="PersistentObject.IsDeleted"/>
        /// so the save carries it to the server's <c>DeletedObjects</c>; a row that was never saved (new) is simply
        /// dropped.</summary>
        public void DeleteObject(PersistentObject obj)
        {
            obj.IsDeleted = true;
            if (obj.IsNew)
                Objects = Objects.Where(o => o != obj).ToArray();

            MarkChanged();
        }

        private void MarkChanged()
        {
            IsValueChanged = true;
            Parent.IsDirty = true;
        }

        internal override JObject ToServiceObject()
        {
            var serviceObject = base.ToServiceObject();
            serviceObject["objects"] = new JArray(Objects.Select(obj =>
            {
                try
                {
                    obj.Parent = null;
                    return obj.ToServiceObject();
                }
                finally
                {
                    obj.Parent = Parent;
                }
            }));
            return serviceObject;
        }
    }
}