using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vidyano.Common;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Vidyano.ViewModel
{
    [DebuggerDisplay("PersistentObjectAttribute {Name}={Value}")]
    public class PersistentObjectAttribute : ViewModelBase
    {
        private readonly Lazy<Type> clrType;

        private Option[] _Options;
        private volatile KeyValueList<string, string> _TypeHints;
        private AttributeVisibility? visibility;
        protected string[] propertiesToBackup = { "value", "isReadOnly", "isValueChanged", "options", "validationError" };

        private static readonly Task<bool> getFalse;

        static PersistentObjectAttribute()
        {
            var source = new TaskCompletionSource<bool>();
            source.SetResult(false);
            getFalse = source.Task;
        }

        internal PersistentObjectAttribute(Client client, JObject model, PersistentObject parent)
            : base(client, model)
        {
            Parent = parent;
            clrType = new Lazy<Type>(() => Client.GetClrType(Type));

            UpdateOptions();
            HasValue = ValueDirect != null;
        }

        internal string Id => GetProperty<string>();

        public string Name => GetProperty<string>();

        public string Type => GetProperty<string>();

        public Type ClrType => clrType.Value;

        public string Label => GetProperty<string>();

        public int Offset => GetProperty<int>();

        public string Rules => GetProperty<string>() ?? string.Empty;

        internal string[] OptionsDirect
        {
            get => GetProperty<string[]>("Options");
            set
            {
                if (OptionsDirect != null && value != null && OptionsDirect.SequenceEqual(value))
                    return;

                SetProperty(value, "Options");
                UpdateOptions();
            }
        }

        public Option[] Options
        {
            get => _Options;
            protected set => SetProperty(ref _Options, value);
        }

        public Option SelectedOption
        {
            get { return Options.FirstOrDefault(o => o.Key == ValueDirect); }

            [EditorBrowsable(EditorBrowsableState.Never)]
            [Obsolete("Use SetValueAsync instead to ensure the UI is properly refreshed when the value changes. This method will not trigger a refresh and may cause the UI to be out of sync with the underlying data.", true)]
            set => _ = SetValueAsync(value?.Key);
        }

        public string GroupName => GetProperty<string>("Group");

        public PersistentObjectAttributeGroup Group { get; internal set; }

        public string Tab => GetProperty<string>();

        public string ToolTip => GetProperty<string>();

        public bool IsReadOnly
        {
            get => GetProperty<bool>();
            internal set => SetProperty(value);
        }

        public bool TriggersRefresh
        {
            get => GetProperty<bool>();
            internal set => SetProperty(value);
        }

        public bool IsValueChanged
        {
            get => GetProperty<bool>();
            internal set => SetProperty(value);
        }

        public KeyValueList<string, string> TypeHints
        {
            get
            {
                if (_TypeHints == null)
                {
                    lock (Model)
                    {
                        if (_TypeHints == null)
                        {
                            Dictionary<string, string> dict = null;
                            var typeHints = (JObject)Model["typeHints"];
                            if (typeHints != null)
                                dict = typeHints.Properties().ToDictionary(p => p.Name, p => (string)p);

                            _TypeHints = new KeyValueList<string, string>(new ReadOnlyDictionary<string, string>(dict ?? new Dictionary<string, string>()));
                        }
                    }
                }

                return _TypeHints;
            }
        }

        public bool IsRequired
        {
            get => GetProperty<bool>();
            internal set => SetProperty(value);
        }

        /// <summary>
        /// Free-form per-attribute tag carried through the round-trip. The server can post any JSON
        /// value (string, number, bool, object, array); callers receive the deserialized payload —
        /// primitives as their CLR type, objects/arrays as <c>JObject</c>/<c>JArray</c>. <c>null</c>
        /// when the server emits none.
        /// </summary>
        public object Tag => GetProperty<object>();

        [EditorBrowsable(EditorBrowsableState.Never)]
        public string ValueDirect
        {
            get => GetProperty<string>("Value");
            internal set
            {
                SetProperty(value, "Value");
                HasValue = value != null;
                OnPropertyChanged("DisplayValue");
                OnPropertyChanged("SelectedOption");
            }
        }

        public object Value
        {
            get { return Client.FromServiceString(GetProperty<string>(), Type); }

            [EditorBrowsable(EditorBrowsableState.Never)]
            [Obsolete("Use SetValueAsync instead to ensure the UI is properly refreshed when the value changes. This method will not trigger a refresh and may cause the UI to be out of sync with the underlying data.", true)]
            set
            {
                _ = SetValueAsync(value);
            }
        }

        public Task SetValueAsync(object value)
        {
            if (UpdateValue(value) && Parent != null && TriggersRefresh)
                return Parent.RefreshAttributesAsync(this);

            return getFalse;
        }

        public string DisplayValue => GetDisplayValue(Client, Value, Type, TypeHints, Options);

        public string ValidationError
        {
            get => GetProperty<string>();
            internal set
            {
                SetProperty(value);
                OnPropertyChanged("HasValidationError");
            }
        }

        public bool HasValue
        {
            get => GetProperty<bool>();
            private set => SetProperty(value);
        }

        public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

        public bool IsVisible => Parent.IsNew ? Visibility.HasFlag(AttributeVisibility.New) : Visibility.HasFlag(AttributeVisibility.Read);

        internal AttributeVisibility Visibility
        {
            get => visibility ??= (AttributeVisibility)Enum.Parse(typeof(AttributeVisibility), GetProperty<string>());
            set
            {
                // Cache before SetProperty: PropertyChanged fires synchronously and a handler
                // reading this property during the notification must see the new value.
                visibility = value;
                if (SetProperty(value.ToString()))
                    OnPropertyChanged("IsVisible");
            }
        }

        public PersistentObject Parent { get; }

        internal bool UpdateValue(object value)
        {
            if (IsReadOnly)
                return false;

            if (SetProperty(Client.ToServiceString(value), "Value"))
            {
                IsValueChanged = true;
                Parent.IsDirty = true;
                HasValue = value != null;
                OnPropertyChanged("DisplayValue");
                OnPropertyChanged("SelectedOption");

                return true;
            }

            return false;
        }

        #region TranslatedString

        /// <summary>Reads a <see cref="DataTypes.TranslatedString"/> attribute's full per-language value.
        /// The server carries every translation in the attribute options; <see cref="Value"/> holds only the
        /// current-language string. Returns <c>null</c> for a non-translated attribute or one with no
        /// options, mirroring the server-side <c>(TranslatedString)attribute</c> conversion.</summary>
        public static explicit operator TranslatedString(PersistentObjectAttribute attribute)
        {
            if (attribute == null || attribute.Type != DataTypes.TranslatedString)
                return null;

            var options = attribute.OptionsDirect;
            return options != null && options.Length > 0 ? TranslatedString.FromJson(options[0]) : null;
        }

        /// <summary>Sets the translation for one language of a <see cref="DataTypes.TranslatedString"/>
        /// attribute, merging it over the existing translations (other languages are untouched). For the
        /// session's current language use <see cref="SetCurrentTranslationAsync"/>, which names it for you.</summary>
        public Task SetTranslationAsync(string language, string value)
        {
            var translations = (TranslatedString)this ?? new TranslatedString();
            translations[language] = value;
            return SetTranslationsCoreAsync(translations);
        }

        /// <summary>Sets the translation for the session's current language (the one the server marked
        /// current for this attribute) without the caller having to name the code.</summary>
        public Task SetCurrentTranslationAsync(string value) => SetTranslationAsync(GetCurrentLanguage(), value);

        /// <summary>Merges a set of translations over the attribute's existing ones — languages present in
        /// <paramref name="translations"/> are overwritten, the rest are left as the server sent them.</summary>
        public Task SetTranslationsAsync(TranslatedString translations)
        {
            var merged = (TranslatedString)this ?? new TranslatedString();
            if (translations != null)
                foreach (var language in translations.Languages)
                    merged[language] = translations[language];
            return SetTranslationsCoreAsync(merged);
        }

        /// <summary>Merges a language→value map over the attribute's existing translations.</summary>
        public Task SetTranslationsAsync(IDictionary<string, string> translations)
        {
            var ts = new TranslatedString();
            if (translations != null)
                foreach (var kvp in translations)
                    ts[kvp.Key] = kvp.Value;

            return SetTranslationsAsync(ts);
        }

        private Task SetTranslationsCoreAsync(TranslatedString translations)
        {
            if (UpdateTranslations(translations) && Parent != null && TriggersRefresh)
                return Parent.RefreshAttributesAsync(this);

            return getFalse;
        }

        // Writes the full translation map to OptionsDirect[0] — the channel the server reads translations
        // back from on save (Value carries only the current-language string, which the save path ignores for
        // a TranslatedString) — and mirrors the current-language translation into Value for display + dirty
        // tracking. Returns whether anything actually changed.
        internal bool UpdateTranslations(TranslatedString translations)
        {
            if (Type != DataTypes.TranslatedString)
                throw new InvalidOperationException(
                    "SetTranslation(s) requires a " + DataTypes.TranslatedString + " attribute; '" + Name + "' is a " + Type + ".");

            if (IsReadOnly)
                return false;

            var json = translations.ToString();
            var options = (OptionsDirect ?? Array.Empty<string>()).ToArray();
            if (options.Length == 0)
                options = new[] { json };
            else
                options[0] = json;

            var optionsChanged = OptionsDirect == null || !OptionsDirect.SequenceEqual(options);
            OptionsDirect = options;

            var current = translations.GetTranslation(GetCurrentLanguage());
            var valueChanged = SetProperty(Client.ToServiceString(current), "Value");

            if (optionsChanged || valueChanged)
            {
                IsValueChanged = true;
                Parent.IsDirty = true;
                HasValue = !translations.IsEmpty;
                OnPropertyChanged("DisplayValue");

                return true;
            }

            return false;
        }

        // The language the server marked current for this attribute (OptionsDirect[1] of a TranslatedString),
        // falling back to the current UI culture when the attribute carries no such slot.
        private string GetCurrentLanguage()
        {
            var options = OptionsDirect;
            return options != null && options.Length > 1 && !string.IsNullOrEmpty(options[1])
                ? options[1]
                : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        }

        #endregion

        internal void BackupBeforeEdit()
        {
            Model["Backup"] = Model.CopyProperties(propertiesToBackup);
        }

        internal void RestoreEditBackup()
        {
            var backup = (JObject)Model["Backup"];
            if (backup != null)
            {
                foreach (var property in backup.Properties())
                {
                    var propertyName = property.Name;
                    Model[propertyName] = property.Value;

                    // Raise property
                    OnPropertyChanged(char.ToUpperInvariant(propertyName[0]) + propertyName.Substring(1));
                }

                Model.Remove("Backup");

                HasValue = Value != null;
                OnPropertyChanged("DisplayValue");
                OnPropertyChanged("SelectedOption");
            }
            else
            {
                if (Client.StrictMode)
                    throw new InvalidOperationException("There's no backup to restore/edit. Please create a backup before restoring/editing it.");
            }
        }

        protected virtual void UpdateOptions()
        {
            var options = new List<Option>();

            if (!IsRequired && Type != DataTypes.Enum)
                options.Add(new Option(null, string.Empty));

            if (Type == DataTypes.NullableBoolean)
            {
                options.Add(new Option("True", Client.Messages["True"]));
                options.Add(new Option("False", Client.Messages["False"]));
            }
            else if (Type == DataTypes.DropDown || Type == DataTypes.Enum || Type == DataTypes.ComboBox)
            {
                var optionsDirect = OptionsDirect ?? Array.Empty<string>();
                optionsDirect.Run(o => options.Add(new Option(o, o)));
            }
            else if (Type == DataTypes.KeyValueList && OptionsDirect != null)
                options.AddRange(OptionsDirect.Select(o => o.Split(new[] { '=' }, 2)).Select(p => new Option(p[0], p.Length > 1 ? p[1] : null)));

            Options = options.ToArray();
        }

        #region Class Methods

        public static string GetDisplayValue(Client client, object value, string type, KeyValueList<string, string> typeHints, Option[] options)
        {
            string text;
            try
            {
                var format = typeHints["DisplayFormat"];
                switch (type)
                {
                    case DataTypes.Time:
                    case DataTypes.NullableTime:
                        {
                            if (value != null)
                            {
                                var time = (TimeSpan)value;
                                text = string.Format(CultureInfo.CurrentCulture, format ?? "{0:" + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern + "}", new DateTime(1, 1, 1, time.Hours, time.Minutes, time.Seconds, time.Milliseconds));
                            }
                            else
                                text = string.Empty;

                            break;
                        }
                    case DataTypes.Date:
                    case DataTypes.NullableDate:
                        text = value != null ? string.Format(CultureInfo.CurrentCulture, format ?? "{0:" + CultureInfo.CurrentCulture.DateTimeFormat.LongDatePattern.Replace("dddd", null).Trim(',', ' ') + "}", value) : string.Empty;
                        break;
                    case DataTypes.DateTime:
                    case DataTypes.NullableDateTime:
                        text = value != null ? string.Format(CultureInfo.CurrentCulture, format ?? "{0:" + CultureInfo.CurrentCulture.DateTimeFormat.LongDatePattern.Replace("dddd", null).Trim(',', ' ') + " " + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern + "}", value) : string.Empty;
                        break;
                    case DataTypes.DateTimeOffset:
                    case DataTypes.NullableDateTimeOffset:
                        text = value != null ? string.Format(CultureInfo.CurrentCulture, format ?? "{0:" + CultureInfo.CurrentCulture.DateTimeFormat.LongDatePattern.Replace("dddd", null).Trim(',', ' ') + " " + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern + " (UTCzzz)}", value) : string.Empty;
                        break;
                    case DataTypes.Boolean:
                    case DataTypes.NullableBoolean:
                        text = value != null ? client.Messages[((bool)value) ? (typeHints["TrueKey"] ?? "True") : (typeHints["FalseKey"] ?? "False")] : string.Empty;
                        break;
                    case DataTypes.YesNo:
                        text = value != null ? client.Messages[((bool)value) ? (typeHints["TrueKey"] ?? "Yes") : (typeHints["FalseKey"] ?? "No")] : string.Empty;
                        break;
                    case DataTypes.KeyValueList:
                        text = Convert.ToString(value);
                        if (options != null && options.Length > 0)
                        {
                            foreach (var option in options.Where(option => (option.Key) == text))
                            {
                                text = option.DisplayValue;
                                break;
                            }
                        }
                        break;
                    case DataTypes.BinaryFile:
                        {
                            var str = (value as String) ?? string.Empty;
                            text = str.Split('|').FirstOrDefault();
                        }
                        break;
                    default:
                        text = value != null ? string.Format(CultureInfo.CurrentCulture, format ?? "{0}", value) : string.Empty;
                        break;
                }
            }
            catch
            {
                text = value != null ? value.ToString() : string.Empty;
            }
            return text;
        }

        #endregion

        #region Service Serialization

        protected override string[] GetServiceProperties()
        {
            return new[] { "id", "name", "value", "label", "options", "type", "isReadOnly", "triggersRefresh", "isRequired", "differsInBulkEditMode", "isValueChanged", "displayAttribute", "objectId", "visibility", "metadata", "tag" };
        }

        #endregion

        #region Nested Classes

        public class Option : NotifyableBase
        {
            internal Option(string key, string displayValue)
            {
                Key = key;
                DisplayValue = displayValue;
            }

            public string Key { get; }

            public string DisplayValue { get; }
        }

        #endregion
    }
}