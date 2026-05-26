using System.Collections.Generic;

namespace Vidyano.Script.Diagnostics;

/// <summary>
/// A single problem produced by parsing or executing a .visc script.
/// </summary>
/// <remarks>
/// Stored as data rather than thrown so the runner can collect many per step and the CLI/JSON layers
/// can render them uniformly. The exception path is reserved for genuinely exceptional conditions.
/// </remarks>
public sealed record Diagnostic(
    /// <summary><see cref="ErrorKind"/> string — the stable contract for agents.</summary>
    string Kind,
    /// <summary>Human-readable message. First-person plain English; no stack traces.</summary>
    string Message,
    /// <summary>Where in the source this came from. <see cref="SourceLocation.Unknown"/> if not applicable.</summary>
    SourceLocation Location,
    /// <summary>One-line suggestion to the reader. Often a "did you mean X?" or how to fix.</summary>
    string? Hint = null,
    /// <summary>Structured extras: <c>expected</c>/<c>actual</c>, candidate lists, server payloads, etc.</summary>
    IReadOnlyDictionary<string, object?>? Details = null);
