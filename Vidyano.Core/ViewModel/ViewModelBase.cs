using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using Vidyano.Common;

namespace Vidyano.ViewModel
{
    public abstract class ViewModelBase : NotifyableBase
    {
        // Bounded: keys are the fixed set of CallerMemberName property names.
        private static readonly ConcurrentDictionary<string, string> camelCasePropertyNames = new ConcurrentDictionary<string, string>();

        protected ViewModelBase(Client client, JObject model)
        {
            Client = client;
            Model = model;
        }

        internal string PreviousState
        {
            get => GetProperty<string>("__CurrentState");
            set => SetProperty(value, "__CurrentState");
        }

        internal string PagePath { get; set; }

        internal JObject Model { get; }

        public Client Client { get; private set; }

        /// <summary>
        ///     Checks if a property already matches a desired value.  Sets the property and
        ///     notifies listeners only when necessary.
        /// </summary>
        /// <typeparam name="T">Type of the property.</typeparam>
        /// <param name="value">Desired value for the property.</param>
        /// <param name="propertyName">
        ///     Name of the property used to notify listeners.  This
        ///     value is optional and can be provided automatically when invoked from compilers that
        ///     support CallerMemberName.
        /// </param>
        /// <returns>
        ///     True if the value was changed, false if the existing value matched the
        ///     desired value.
        /// </returns>
        protected bool SetProperty<T>(T value, [CallerMemberName] String propertyName = null)
        {
            var camelCasePropertyName = GetCamelCasePropertyName(propertyName);
            var token = value != null ? CreateToken(value) : null;
            if (JToken.DeepEquals(Model[camelCasePropertyName], token)) return false;

            Model[camelCasePropertyName] = token;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected T GetProperty<T>([CallerMemberName] String propertyName = null)
        {
            if (Model.TryGetValue(GetCamelCasePropertyName(propertyName), out var token) && token != null && token.Type != JTokenType.Null)
                return token.ToObject<T>();

            return default(T);
        }

        private static string GetCamelCasePropertyName(string propertyName)
        {
            return camelCasePropertyNames.GetOrAdd(propertyName, static name => char.ToLowerInvariant(name[0]) + name.Substring(1));
        }

        // Only types whose JValue representation matches JToken.FromObject exactly; enums and
        // complex types keep the serializer path (enum→integer conversion, converters).
        // Note: unlike FromObject, the fast path bypasses JsonConvert.DefaultSettings, so a host
        // app's global converters/DateTimeZoneHandling do not affect these primitive tokens.
        private static JToken CreateToken(object value)
        {
            return value switch
            {
                string s => new JValue(s),
                bool b => new JValue(b),
                int i => new JValue(i),
                long l => new JValue(l),
                double d => new JValue(d),
                decimal m => new JValue(m),
                float f => new JValue(f),
                DateTime dt => new JValue(dt),
                DateTimeOffset dto => new JValue(dto),
                TimeSpan ts => new JValue(ts),
                Guid g => new JValue(g),
                _ => JToken.FromObject(value)
            };
        }

        protected virtual string[] GetServiceProperties()
        {
            return Array.Empty<string>();
        }

        internal virtual JObject ToServiceObject()
        {
            var obj = new JObject();

            foreach (var p in GetServiceProperties())
            {
                if (Model.TryGetValue(p, out var token) && token != null)
                    obj[p] = token;
            }

            return obj;
        }
    }
}