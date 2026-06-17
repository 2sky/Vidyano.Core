#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Vidyano
{
    /// <summary>
    /// Client-side representation of a <see cref="DataTypes.TranslatedString"/> attribute value: a map of
    /// language code (e.g. <c>"en"</c>, <c>"nl"</c>) to the translated string. The Vidyano wire format is a
    /// flat JSON object <c>{"en":"Widget","nl":"Hulpmiddel"}</c>; this type is the round-trip between that
    /// JSON and the per-language entries. Keep <see cref="ToString"/> and <see cref="FromJson"/> inverses.
    /// <para>The <em>set of supported languages</em> is not global: it is carried per attribute by the server
    /// (in the attribute's options), so this type holds only the languages a given value actually has.
    /// To read/write a translated attribute, prefer the helpers on
    /// <see cref="ViewModel.PersistentObjectAttribute"/> (<c>(TranslatedString)attr</c>,
    /// <c>SetTranslationAsync</c>, …) which know the attribute's languages and the session's current one.</para>
    /// </summary>
    public sealed class TranslatedString : IEquatable<TranslatedString>
    {
        // Language keys are matched case-insensitively (so ts["NL"] finds "nl") but stored under the casing
        // first seen, so a value parsed from the server round-trips with the server's culture casing intact.
        private readonly Dictionary<string, string> translations;

        /// <summary>Creates an empty instance (no languages).</summary>
        public TranslatedString()
        {
            translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Creates an instance seeded from a language→value map. Null values become empty strings.</summary>
        public TranslatedString(IDictionary<string, string?> translations)
            : this()
        {
            if (translations != null)
                foreach (var kvp in translations)
                    this.translations[kvp.Key] = kvp.Value ?? string.Empty;
        }

        /// <summary>The language codes this value carries, in the order they were added.</summary>
        public IReadOnlyCollection<string> Languages => translations.Keys;

        /// <summary>True when every translation is null/empty (or there are none) — the server treats such a
        /// value as "no translation set".</summary>
        public bool IsEmpty => translations.Values.All(string.IsNullOrEmpty);

        /// <summary>Gets or sets the translation for <paramref name="language"/>. The getter returns the empty
        /// string for an absent language (never throws, never null); the setter stores null as empty.</summary>
#if !NETSTANDARD2_0
        [System.Diagnostics.CodeAnalysis.AllowNull] // (internal on netstandard2.0, so the annotation is net-only)
#endif
        public string this[string language]
        {
            get => translations.TryGetValue(language, out var value) ? value : string.Empty;
            set => translations[language] = value ?? string.Empty;
        }

        /// <summary>Resolves a single translation with the same fallback the server uses: the requested
        /// language when present; otherwise, when the value carries exactly one language, that one
        /// (the single-language optimization); otherwise the empty string. <paramref name="language"/>
        /// defaults to the current UI culture's two-letter name — but for the <em>session</em>'s current
        /// language prefer <see cref="ViewModel.PersistentObjectAttribute.Value"/>, which the server fills
        /// from the attribute's own current-language slot.</summary>
        public string GetTranslation(string? language = null)
        {
            if (translations.Count == 0)
                return string.Empty;

            language ??= CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (translations.TryGetValue(language, out var value))
                return value;

            return translations.Count == 1 ? translations.Values.First() : string.Empty;
        }

        /// <summary>Parses the Vidyano wire form — a JSON object of language→string — into a
        /// <see cref="TranslatedString"/>. Returns <c>null</c> for a null/empty input or anything that isn't a
        /// JSON object (the error-free counterpart of <see cref="ToString"/>).</summary>
        public static TranslatedString? FromJson(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            JObject obj;
            try
            {
                obj = JObject.Parse(json!);
            }
            catch
            {
                return null;
            }

            var result = new TranslatedString();
            foreach (var property in obj.Properties())
                result.translations[property.Name] = (string?)property.Value ?? string.Empty;
            return result;
        }

        /// <summary>The Vidyano wire form: a JSON object of language→string, e.g.
        /// <c>{"en":"Widget","nl":"Hulpmiddel"}</c>.</summary>
        public override string ToString()
        {
            var obj = new JObject();
            foreach (var kvp in translations)
                obj[kvp.Key] = kvp.Value;
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => ReferenceEquals(this, obj) || (obj is TranslatedString other && Equals(other));

        /// <inheritdoc />
        // Order-independent: two values are equal when they carry the same languages with the same text.
        public bool Equals(TranslatedString? other)
        {
            if (other is null || other.translations.Count != translations.Count)
                return false;

            foreach (var kvp in translations)
                if (!other.translations.TryGetValue(kvp.Key, out var value) || !string.Equals(value, kvp.Value, StringComparison.Ordinal))
                    return false;

            return true;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // Order-independent hash: XOR the per-language contributions so it matches Equals.
            var hash = 0;
            foreach (var kvp in translations)
                hash ^= StringComparer.OrdinalIgnoreCase.GetHashCode(kvp.Key) ^ (kvp.Value?.GetHashCode() ?? 0);
            return hash;
        }

        /// <summary>Implicitly converts to the JSON wire string, so a <see cref="TranslatedString"/> can be
        /// assigned straight to a string attribute value. A null instance becomes a null string.</summary>
        public static implicit operator string?(TranslatedString? value) => value?.ToString();
    }
}
