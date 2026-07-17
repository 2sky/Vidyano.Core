using System.Text;

namespace Vidyano.Script.Runtime;

/// <summary>The result of an action that returned a <c>Vidyano.RegisteredStream</c>: the delivered file
/// <see cref="Name"/> and the fetched <see cref="Bytes"/>. The web client auto-fetches such a stream (see
/// <c>ActionBase</c>) and hands it to <c>Hooks.OnStream</c>; the runner mirrors that fetch and buffers the
/// result here — like <see cref="ClientOperation"/>, a stream is a verb-observable side effect, not a
/// navigation frame — so <c>EXPECT Stream.Name</c> / <c>Stream.Length</c> / <c>Stream.Text</c> can assert it.
/// The buffer is cleared by the interpreter's per-verb reset, so it reflects only the immediately preceding
/// verb.</summary>
public sealed record CapturedStream(string? Name, byte[] Bytes)
{
    /// <summary>The byte length of the delivered stream — <c>EXPECT Stream.Length &gt; 0</c>.</summary>
    public int Length => Bytes.Length;

    /// <summary>The stream decoded as UTF-8 text — <c>EXPECT Stream.Text CONTAINS "%PDF"</c>. A server
    /// <c>OnGetStream</c> fault is served as the stream body, so its text is assertable here too.</summary>
    public string Text => Encoding.UTF8.GetString(Bytes);
}
