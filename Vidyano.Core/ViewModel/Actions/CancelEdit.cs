using System.Threading.Tasks;

namespace Vidyano.ViewModel.Actions
{
    sealed class CancelEdit : ActionBase
    {
        public CancelEdit(Definition definition, PersistentObject parent, Query query)
            : base(definition, parent, query)
        {
            CanExecute = parent.IsInEdit;
        }

        public override Task<PersistentObject> Execute(object parameter)
        {
            Parent.CancelEdit();

            return Task.FromResult(Parent);
        }
    }
}