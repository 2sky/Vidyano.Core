using Vidyano.Common;

namespace Vidyano.ViewModel
{
    public sealed class PersistentObjectAttributeGroup : NotifyableBase
    {
        private readonly Client client;
        private string _Name;

        internal PersistentObjectAttributeGroup(Client client, string name, PersistentObjectAttribute[] attributes)
        {
            this.client = client;
            _Name = name;
            Attributes = attributes;
            Attributes.Run(attr => attr.Group = this);
            IsNameVisible = true;
        }

        public string Name
        {
            get
            {
                if (string.IsNullOrEmpty(_Name))
                    _Name = client.Messages["DefaultAttributesGroup"];

                return _Name;
            }
        }

        public bool IsNameVisible { get; internal set; }

        public PersistentObjectAttribute[] Attributes { get; private set; }
    }
}