﻿using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Vidyano.ViewModel
{
    public class QueryResultItem : ViewModelBase
    {
        private readonly JArray values;

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

        public string Id
        {
            get { return GetProperty<string>(); }
        }

        public string Breadcrumb
        {
            get { return GetProperty<string>(); }
        }

        public Query Query { get; private set; }

        public bool HasValues
        {
            get { return values.HasValues; }
        }

        public object this[string key]
        {
            get
            {
                var column = Query.Columns.FirstOrDefault(c => c.Name == key);

                var value = values.FirstOrDefault(v => (string)v["key"] == key);
                if (value != null)
                    return Client.FromServiceString((string)value["value"], column.Type);

                return null;
            }
            set
            {
                var val = values.FirstOrDefault(v => (string)v["key"] == key);
                if (val != null)
                {
                    val["value"] = Client.ToServiceString(value);
                    OnPropertyChanged("Item[]");
                }
            }
        }

        #endregion

        #region Public Methods

        public async Task<PersistentObject> Load()
        {
            if (!Query.CanRead)
                return null;

            var po = await Client.GetPersistentObjectAsync(Query.PersistentObject.Id, Id, Query.Parent).ConfigureAwait(false);
            po.OwnerQuery = Query;
            return po;
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