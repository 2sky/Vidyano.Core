using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vidyano.Common;

namespace Vidyano.ViewModel
{
    [DebuggerDisplay("PersistentObjectAttributeWithReference {Name}={Value} ({ObjectId,nq})")]
    public class PersistentObjectAttributeWithReference : PersistentObjectAttribute
    {
        internal PersistentObjectAttributeWithReference(Client client, JObject model, PersistentObject parent)
            : base(client, model, parent)
        {
            propertiesToBackup = new[] { "value", "isReadOnly", "isValueChanged", "options", "objectId", "validationError" };

            var lookup = (JObject)model["lookup"];
            if (lookup != null)
                Lookup = client.Hooks.OnConstruct(client, lookup, parent, false);
        }

        public string ObjectId
        {
            get => GetProperty<string>();
            internal set
            {
                if (SetProperty(value) && SelectInPlace)
                {
                    OnPropertyChanged("SelectedReferenceValue");
                    OnPropertyChanged("CanOpen");
                    OnPropertyChanged("CanRemoveReference");
                }
            }
        }

        public string DisplayAttribute => GetProperty<string>();

        public bool SelectInPlace => GetProperty<bool>();

        public bool CanRemoveReference => !IsRequired && ObjectId != null;

        public bool CanOpen => ObjectId != null && Lookup.CanRead;

        public string SelectedReferenceValue
        {
            get { return ObjectId; }
            set
            {
                if (PropertyChanging == "Options")
                    return;

                if (SelectInPlace && ObjectId != value)
                {
#pragma warning disable 4014
                    if (value != null)
                        ChangeReference(new QueryResultItem(Client, value));
                    else if (CanRemoveReference)
                        ChangeReference(null);
#pragma warning restore 4014
                }
            }
        }

        public bool CanAddNewReference => GetProperty<bool>();

        public Query Lookup { get; }

        protected override void UpdateOptions()
        {
            var options = new List<Option>();

            if (!IsRequired && Type != DataTypes.Enum)
                options.Add(new Option(null, string.Empty));

            var optionsDirect = OptionsDirect ?? Array.Empty<string>();
            optionsDirect.Run(o =>
            {
                var parts = o.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                    options.Add(new Option(parts[0], parts[1]));
            });

            Options = options.ToArray();
        }

        public async Task ChangeReference(QueryResultItem queryResultItem)
        {
            var parameters = new Dictionary<string, string> { { "PersistentObjectAttributeId", Id } };
            try
            {
                var po = await Client.ExecuteActionAsync("PersistentObject.SelectReference", Parent, Lookup, new[] { queryResultItem }, parameters).ConfigureAwait(false);

                if (po != null && Parent != null)
                    await Parent.RefreshFromResult(po).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var parent = Parent;
                if (parent != null)
                    parent.SetNotification(ex.Message);
            }
        }
    }
}