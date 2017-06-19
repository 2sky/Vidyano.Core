using System;
using System.Windows.Input;
using Vidyano.ViewModel;

namespace Vidyano.Commands
{
    public class ShowNotificationCommand : ICommand
    {
        private readonly Client client;

        public ShowNotificationCommand()
            : this(Client.Current)
        {
        }

        public ShowNotificationCommand(Client client)
        {
            this.client = client;
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public event EventHandler CanExecuteChanged = delegate { };

        public virtual async void Execute(object parameter)
        {
            var str = parameter as string;
            if (!string.IsNullOrEmpty(str))
                await client.Hooks.ShowNotification(str, NotificationType.Error).ConfigureAwait(false);
        }
    }
}