using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Runtime;

/// <summary>
/// Outcome of one session operation. Carries either a result payload or a structured
/// <see cref="Diagnostic"/>; never both. Returned instead of thrown so the interpreter can decide
/// whether to keep going (e.g. record the failure and continue with the next statement) or stop.
/// </summary>
public readonly record struct OpResult<T>(bool Ok, T? Value, Diagnostic? Error)
{
    public static OpResult<T> Success(T value) => new(true, value, null);
    public static OpResult<T> Fail(Diagnostic error) => new(false, default, error);
}

/// <summary>Non-generic outcome (no payload).</summary>
public readonly record struct OpResult(bool Ok, Diagnostic? Error)
{
    public static OpResult Success { get; } = new(true, null);
    public static OpResult Fail(Diagnostic error) => new(false, error);
}
