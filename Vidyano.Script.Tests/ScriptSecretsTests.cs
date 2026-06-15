using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>Covers <see cref="ScriptSecrets"/> — the redaction that keeps plaintext credentials out of a
/// <c>:save</c>d .visc and secret values out of a <c>:vars</c> dump. Span handling is the load-bearing part:
/// a quoted password's source span differs from its lexeme, so redaction works off token boundaries.</summary>
public sealed class ScriptSecretsTests
{
    [Theory]
    [InlineData("password", true)]
    [InlineData("Password", true)]
    [InlineData("pwd", true)]
    [InlineData("userToken", true)]
    [InlineData("apiKey", true)]
    [InlineData("api_key", true)]
    [InlineData("clientSecret", true)]
    [InlineData("authHeader", true)]
    [InlineData("user", false)]
    [InlineData("region", false)]
    [InlineData("app", false)]
    public void IsSecretName_MatchesSecretLookingNames(string name, bool expected) =>
        Assert.Equal(expected, ScriptSecrets.IsSecretName(name));

    [Theory]
    // Bare identifier password.
    [InlineData("SIGN-IN admin / vidyano", "SIGN-IN admin / {{env:VIDYANO_PASSWORD}}")]
    // Quoted password (with a space): the source span includes the quotes — boundary-based redaction handles it.
    [InlineData("SIGN-IN admin / \"p w\"", "SIGN-IN admin / {{env:VIDYANO_PASSWORD}}")]
    // Trailing LANGUAGE clause must be preserved.
    [InlineData("SIGN-IN admin / vidyano LANGUAGE nl-NL", "SIGN-IN admin / {{env:VIDYANO_PASSWORD}} LANGUAGE nl-NL")]
    // Named session: only the password is touched.
    [InlineData("SIGN-IN @ops = admin / vidyano", "SIGN-IN @ops = admin / {{env:VIDYANO_PASSWORD}}")]
    // Secret-named assignment → env ref keyed by the (upper-cased) name.
    [InlineData("@token = \"abc123\"", "@token = {{env:TOKEN}}")]
    [InlineData("@apiKey = \"xyz\"", "@apiKey = {{env:APIKEY}}")]
    public void RedactLine_ReplacesInlineSecrets(string input, string expected)
    {
        var (line, changed) = ScriptSecrets.RedactLine(input);
        Assert.True(changed);
        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData("SIGN-IN FROM ENV")]                                   // no inline credential
    [InlineData("SIGN-IN admin / {{env:VIDYANO_PASSWORD}}")]           // already an env reference
    [InlineData("@region = \"eu-west\"")]                              // not a secret name
    [InlineData("OPEN MenuItem Home/Customers")]                       // a slash, but not SIGN-IN
    [InlineData("EXPECT TotalItems = 5")]                              // ordinary assertion
    public void RedactLine_LeavesNonSecretsUntouched(string input)
    {
        var (line, changed) = ScriptSecrets.RedactLine(input);
        Assert.False(changed);
        Assert.Equal(input, line);
    }

    [Fact]
    public void RedactedSignIn_StaysRunnableViaEnv()
    {
        // The redacted line must still be a valid SIGN-IN that resolves its password from the environment —
        // the whole point is "secure AND runnable", not "broken".
        var (line, _) = ScriptSecrets.RedactLine("SIGN-IN admin / vidyano");
        var diags = VidyanoScript.Lint(line);
        Assert.Empty(diags);
    }
}
