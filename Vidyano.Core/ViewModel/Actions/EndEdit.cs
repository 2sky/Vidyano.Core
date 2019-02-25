using System.Threading.Tasks;

namespace Vidyano.ViewModel.Actions
{
    sealed class EndEdit : ActionBase
    {
        public EndEdit(Definition definition, PersistentObject parent, Query query)
            : base(definition, parent, query)
        {
            CanExecute = false;
            IsVisible = false;
        }

        public override async Task<PersistentObject> Execute(object parameter)
        {
            await Parent.Save().ConfigureAwait(false);

            if ((string.IsNullOrWhiteSpace(Parent.Notification) || Parent.NotificationType != NotificationType.Error) && Parent.StateBehavior.HasFlag(StateBehavior.StayInEdit))
                Parent.Edit();

            return Parent;
        }
    }
}