using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Vidyano.ViewModel
{
    [DebuggerDisplay("QueryColumn {Name}")]
    public class QueryColumn : ViewModelBase
    {
        internal QueryColumn(JObject model, Query query)
            : base(query.Client, model)
        {
            Query = query;
        }

        public Query Query { get; private set; }

        internal bool DisableSort => GetProperty<bool>();

        internal string[] Includes
        {
            get => GetProperty<string[]>();
            set => SetProperty(value);
        }

        internal string[] Excludes
        {
            get => GetProperty<string[]>();
            set => SetProperty(value);
        }

        public string Label => GetProperty<string>();

        public string Name => GetProperty<string>();

        public int Offset => GetProperty<int>();

        public string Type => GetProperty<string>();

        #region Service Serialization

        protected override string[] GetServiceProperties()
        {
            return new[] { "id", "name", "label", "includes", "excludes", "type", "displayAttribute" };
        }

        #endregion
    }
}