using System.Threading.Tasks;

namespace Vidyano.ViewModel.Actions
{
    sealed class ExportToExcel : QueryAction
    {
        public ExportToExcel(Definition definition, PersistentObject parent, Query query)
            : base(definition, parent, query)
        {
        }

        // An export produces a file, not a PersistentObject, so (like the web client) it runs as a single
        // GetStream request instead of going through ExecuteAction.
        public override async Task<PersistentObject> Execute(object option)
        {
            await DownloadAsync(() => client.GetStreamAsync("Query.ExportToExcel", Parent, Query, parameters: GetParameters(option))).ConfigureAwait(false);

            return null;
        }
    }
}