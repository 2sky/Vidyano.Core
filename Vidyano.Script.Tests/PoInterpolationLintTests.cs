using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// <c>{{PO.ObjectId}}</c> / <c>{{PO.Attr.&lt;name&gt;}}</c> read the current PersistentObject, not the variable table,
/// so the variable-use lint must not flag them. Evaluation needs a live PO and runs in the integration tests.
/// </summary>
public sealed class PoInterpolationLintTests
{
    [Theory]
    [InlineData("@id = {{PO.ObjectId}}")]
    [InlineData("@name = {{PO.Attr.Name}}")]
    [InlineData("SET Note = \"copy of {{PO.Attr.Name}} ({{PO.ObjectId}})\"")]
    [InlineData("EXPECT {{PO.Metadata.brand}} IS NULL")]
    public void PoReads_AreNotVariables(string body) =>
        Assert.Empty(VidyanoScript.Lint(body));

    [Fact]
    public void LookalikeName_IsStillAVariable() =>
        Assert.Contains(VidyanoScript.Lint("@x = {{POx}}"), d => d.Kind == ErrorKind.ResolveVariable);
}
