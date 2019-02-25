using System.Threading.Tasks;

namespace Vidyano.ViewModel.Actions
{
    sealed class Edit : ActionBase
    {
        public Edit(Definition definition, PersistentObject parent, Query query)
            : base(definition, parent, query)
        {
        }

        internal override Definition[] DependentActions => new[] { client.Actions["EndEdit"], client.Actions["CancelEdit"] };

        public override Task<PersistentObject> Execute(object parameter)
        {
            Parent.Edit();

            return Task.FromResult(Parent);
        }
    }
}