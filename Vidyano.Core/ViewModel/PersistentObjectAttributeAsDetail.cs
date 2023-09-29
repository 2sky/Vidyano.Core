using System;
using System.Diagnostics;
using System.Linq;
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