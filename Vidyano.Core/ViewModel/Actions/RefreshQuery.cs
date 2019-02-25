using System.Threading.Tasks;

namespace Vidyano.ViewModel.Actions
{
    sealed class RefreshQuery : QueryAction
    {
        public RefreshQuery(Definition definition, PersistentObject parent, Query query)
            : base(definition, parent, query)
        {
        }

        public override async Task<PersistentObject> Execute(object option)
        {
            await Query.RefreshQueryAsync().ConfigureAwait(false);

            return null;
        }
    }
}