using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Vidyano.ViewModel
{
    public class QueryResultItem : ViewModelBase
    {
        private readonly JArray values;
        private Dictionary<string, JToken> valuesByKey;

        public QueryResultItem(Client client, string id)
            : base(client, new JObject(new JProperty("id", id)))
        {
        }

        internal QueryResultItem(JObject model, Query query)
            : base(query.Client, model)
        {
            Query = query;
            values = (JArray)model["values"];
        }

        #region Properties

        public string Id => GetProperty<string>();

        public string Breadcrumb => GetProperty<string>();

        public Query Query { get; }

        public bool HasValues => values.HasValues;

        public object this[string key]
        {
            get
            {
                var value = GetValue(key);
                if (value != null)
                {
                    var column = Query?.GetColumn(key);
                    return Client.FromServiceString((string)value["value"], column?.Type);
                }

                return null;
            }
            set
            {
                var val = GetValue(key);
                if (val != null)
                {
                    val["value"] = Client.ToServiceString(value);
                    OnPropertyChanged("Item[]");
                }
            }
        }

        // The setter only mutates elements in place (never adds/removes), so the map stays valid once built.
        private JToken GetValue(string key)
        {
            if (key == null)
                return null;

            if (valuesByKey == null)
            {
                // First-wins on duplicate keys, matching the FirstOrDefault semantics this map replaces.
                var map = new Dictionary<string, JToken>();
                if (values != null)
                {
                    foreach (var value in values)
                    {
                        var valueKey = (string)value["key"];
                        if (valueKey != null && !map.ContainsKey(valueKey))
                            map[valueKey] = value;
                    }
                }

                valuesByKey = map;
            }

            valuesByKey.TryGetValue(key, out var result);
            return result;
        }

        #endregion

        #region Public Methods

        public async Task<PersistentObject> Load(bool throwExceptions = false)
        {
            if (!Query.CanRead)
                return null;

            try
            {
                var po = await Client.GetPersistentObjectAsync(Query.PersistentObject.Id, Id, Query.Parent).ConfigureAwait(false);
                po.OwnerQuery = Query;
                return po;
            }
            catch (Exception e)
            {
                // Mirror the web client (query-result-item.ts): a failed row-open sets the error on the
                // owning query so callers can observe it, then returns null unless the caller opts into the throw.
                Query.SetNotification(e.Message);
                if (throwExceptions)
                    throw;

                return null;
            }
        }

        public async Task Open()
        {
            await Query.InvokeOpenItem(this).ConfigureAwait(false);
        }

        #endregion

        #region Service Serialization

        protected override string[] GetServiceProperties()
        {
            return new[] { "id", "values" };
        }

        #endregion
    }
}