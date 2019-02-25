using System.Threading.Tasks;

namespace Vidyano.ViewModel.Actions
{
    public class QueryAction : ActionBase
    {
        protected internal QueryAction(Definition definition, PersistentObject parent, Query query)
            : base(definition, parent, query)
        {
            CanExecute = definition.SelectionRule(0);
        }

        internal void Invalidate(int selectedItemsCount)
        {
            CanExecute = definition.SelectionRule(selectedItemsCount);
        }

        public override async Task<PersistentObject> Execute(object option)
        {
            var result = await base.Execute(option).ConfigureAwait(false);
            Query.PendingSemanticZoomTabsRefresh = true;

            if (Query.SemanticZoomOwner != null)
                Query.SemanticZoomOwner.PendingSemanticZoomTabsRefresh = true;

            return result;
        }
    }
}