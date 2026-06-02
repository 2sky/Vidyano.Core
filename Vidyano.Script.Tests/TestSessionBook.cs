using System;
using System.Threading.Tasks;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tests;

/// <summary>
/// Test helpers for wrapping a <see cref="VidyanoSession"/> in a <see cref="SessionBook"/> — the
/// interpreter now takes a book, not a bare session. The server-free tests only ever hit the
/// default ("") slot, so a <c>mintFresh</c> that hands back fresh own-jar sessions against the same
/// unreachable host is all they need.
/// </summary>
internal static class TestSessionBook
{
    private const string UnreachableHost = "https://127.0.0.1:1";

    /// <summary>Wraps an existing session as the default slot of a book whose <c>mintFresh</c> spins up
    /// further own-jar sessions against the same unreachable host (never exercised in server-free tests).</summary>
    public static SessionBook Wrap(VidyanoSession initial) =>
        new(initial, () => new ValueTask<VidyanoSession>(
            new VidyanoSession(UnreachableHost, acceptAnyServerCertificate: true)));
}
