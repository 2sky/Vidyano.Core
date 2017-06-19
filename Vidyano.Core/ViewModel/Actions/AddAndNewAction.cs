﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Vidyano.ViewModel.Actions
{
    sealed class AddAndNewAction : QueryAction
    {
        private readonly ActionBase addAction, newAction;

        public AddAndNewAction(ActionBase newAction, ActionBase addAction)
            : base(addAction.definition, addAction.Parent, addAction.Query)
        {
            this.newAction = newAction;
            this.addAction = addAction;

            newAction.IsVisible = false;
            addAction.IsVisible = false;

            CanExecute = IsVisible = true;

            var options = new List<string>();
            if (newAction.Options != null && newAction.Options.Length > 0)
                options.AddRange(newAction.Options.Select(o => newAction.DisplayName + " " + o));
            else
                options.Add(newAction.DisplayName);

            options.Add(client.Messages["Existing"]);
            Options = options.ToArray();
        }

        public override async Task Execute(object option)
        {
            var idx = Array.IndexOf(Options, (string)option);

            if (idx == Options.Length - 1)
                await addAction.Execute(-1).ConfigureAwait(false);
            else
                await newAction.Execute(idx).ConfigureAwait(false);
        }
    }
}