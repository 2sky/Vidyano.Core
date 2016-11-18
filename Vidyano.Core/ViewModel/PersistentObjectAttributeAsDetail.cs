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
                    obj.OwnerDetailAttribute = this;
                    return obj;
                }).ToArray();
            }
            else
                Objects = new PersistentObject[0];
        }

        public Query Details { get; private set; }

        public PersistentObject[] Objects { get; internal set; }

        public bool CanNew { get; set; }

        public bool CanDelete { get; set; }

        public bool CanEdit { get; set; }

        internal override JObject ToServiceObject()
        {
            var serviceObject = base.ToServiceObject();
            serviceObject["objects"] = new JArray(Objects.Select(obj => obj.ToServiceObject()));
            return serviceObject;
        }
    }
}