namespace Vidyano.Script.Runtime;

/// <summary>How a <c>SET</c> on a reference attribute should resolve its value.</summary>
public enum ReferenceHintKind
{
    /// <summary>Treat the value as the literal raw ID (<c>SelectedReferenceValue</c>). Bypasses the lookup.</summary>
    RawId,
    /// <summary>Treat the value as a lookup search expression (e.g. <c>"Name:Smith AND City:Springfield"</c>).</summary>
    Lookup,
}

/// <summary>Optional override for how <see cref="VidyanoSession.SetAttribute"/> resolves a reference.</summary>
public sealed record ReferenceHint(ReferenceHintKind Kind, string Value);
