using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// The multi-identity contract of <see cref="SessionBook"/> and its three interpreter verbs
/// (<c>SIGN-IN @name</c> / <c>USE @name</c> / <c>SIGN-OUT [@name]</c>). Everything here is
/// server-free: <see cref="SessionBook"/> is exercised directly with a counting/tracking fake
/// <c>mintFresh</c> for the map/collision/disposal invariants, and the loud-failure name-miss paths
/// are also driven through <see cref="Interpreter.RunAsync"/> on real <c>.visc</c> source to prove
/// the verb wiring. <see cref="Vidyano.Client.SignOut"/> only touches the network when an
/// Application is present (after a real sign-in), so calling it on a never-signed-in session against
/// an unreachable host is a pure local-state clear — safe in CI.
/// </summary>
public sealed class SessionBookTests
{
    private const string UnreachableHost = "https://127.0.0.1:1";
    private static readonly SourceLocation Loc = SourceLocation.Unknown;

    /// <summary>An own-jar session against the unreachable host — identical to what the production
    /// <c>mintFresh</c> builds (the cookie-jar trap ctor branch: <c>httpClient: null</c>).</summary>
    private static VidyanoSession NewOwnJarSession() =>
        new(UnreachableHost, acceptAnyServerCertificate: true);

    /// <summary>A <c>mintFresh</c> that records every invocation and the instances it handed back, so
    /// tests can assert call counts (collision rule) and disposal ownership.</summary>
    private sealed class MintTracker
    {
        public int Calls { get; private set; }
        public List<VidyanoSession> Minted { get; } = new();

        public Func<ValueTask<VidyanoSession>> Factory => () =>
        {
            Calls++;
            var s = NewOwnJarSession();
            Minted.Add(s);
            return new ValueTask<VidyanoSession>(s);
        };
    }

    private static ScriptAst Parse(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        return ast;
    }

    private static List<StatementResult> Statements(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).ToList();

    // --- the "" default floor: zero-cost single session, mintFresh never touched ----------------

    [Fact]
    public async Task DefaultSlot_SignIn_NeverMints()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        // A null/empty name selects the "" floor and mints nothing — the single-session path.
        var slot = await book.SignInSlotAsync(null);

        Assert.True(slot.Ok);
        Assert.Same(initial, slot.Value);
        Assert.Same(initial, book.Current);
        Assert.Equal(0, tracker.Calls);
    }

    [Fact]
    public void DefaultSlot_IsCurrentBeforeAnySignIn()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        // The "" floor keeps Current non-null for the whole script life, even before any verb runs.
        Assert.Same(initial, book.Current);
    }

    // --- USE @unknown / SIGN-OUT @unknown: loud, non-throwing failure ---------------------------

    [Fact]
    public void Use_UnknownName_FailsWithResolveSession_DoesNotThrow()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        var res = book.Use("ghost", Loc);

        Assert.False(res.Ok);
        Assert.NotNull(res.Error);
        Assert.Equal(ErrorKind.ResolveSession, res.Error!.Kind);
        // A name miss must not move the pointer off the floor.
        Assert.Same(initial, book.Current);
    }

    [Fact]
    public async Task SignOut_UnknownName_FailsWithResolveSession_DoesNotThrow()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        var res = await book.SignOut("ghost", Loc);

        Assert.False(res.Ok);
        Assert.NotNull(res.Error);
        Assert.Equal(ErrorKind.ResolveSession, res.Error!.Kind);
    }

    [Fact]
    public async Task Use_UnknownName_OffersHintOverKnownSlots()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);
        await book.SignInSlotAsync("admin");

        // "admni" is one transposition from the known "admin" — the Suggester should propose it.
        var res = book.Use("admni", Loc);

        Assert.False(res.Ok);
        Assert.NotNull(res.Error!.Hint);
        Assert.Contains("admin", res.Error.Hint!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseUnknown_ThroughInterpreter_FailsLoudly_DoesNotThrow()
    {
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, () => new ValueTask<VidyanoSession>(NewOwnJarSession()));
        var interp = new Interpreter(book);

        var result = await interp.RunAsync(Parse("USE @ghost"));

        var stmt = Statements(result).Single();
        Assert.False(stmt.Ok);
        Assert.Equal(ErrorKind.ResolveSession, stmt.Diagnostics.Single().Kind);
    }

    [Fact]
    public async Task SignOutUnknown_ThroughInterpreter_FailsLoudly_DoesNotThrow()
    {
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, () => new ValueTask<VidyanoSession>(NewOwnJarSession()));
        var interp = new Interpreter(book);

        var result = await interp.RunAsync(Parse("SIGN-OUT @ghost"));

        var stmt = Statements(result).Single();
        Assert.False(stmt.Ok);
        Assert.Equal(ErrorKind.ResolveSession, stmt.Diagnostics.Single().Kind);
    }

    // --- distinct named slots: pointer-flip isolation -------------------------------------------

    [Fact]
    public async Task TwoNamedSignIns_YieldDistinctSessions()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        var a = await book.SignInSlotAsync("a");
        var b = await book.SignInSlotAsync("b");

        Assert.True(a.Ok && b.Ok);
        Assert.NotSame(initial, a.Value);
        Assert.NotSame(initial, b.Value);
        Assert.NotSame(a.Value, b.Value);
        Assert.Equal(2, tracker.Calls);
    }

    [Fact]
    public async Task Use_SwitchesCurrentBetweenNamedSlots()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        var a = (await book.SignInSlotAsync("a")).Value;
        var b = (await book.SignInSlotAsync("b")).Value; // b is current after its sign-in
        Assert.Same(b, book.Current);

        var useA = book.Use("a", Loc);
        Assert.True(useA.Ok);
        Assert.Same(a, book.Current);

        var useB = book.Use("b", Loc);
        Assert.True(useB.Ok);
        Assert.Same(b, book.Current);
        // Switching never mints — both slots already exist.
        Assert.Equal(2, tracker.Calls);
    }

    // --- collision rule: repeat SIGN-IN @name reuses the slot, never re-mints -------------------

    [Fact]
    public async Task RepeatNamedSignIn_ReusesSameSession_DoesNotReMint()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        var first = await book.SignInSlotAsync("a");
        var second = await book.SignInSlotAsync("a");

        Assert.Same(first.Value, second.Value);
        // mintFresh must run exactly once for the name — the collision re-auths in place.
        Assert.Equal(1, tracker.Calls);
    }

    [Fact]
    public async Task NameMatching_IsCaseInsensitive()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        var lower = await book.SignInSlotAsync("admin");
        var upper = await book.SignInSlotAsync("ADMIN");

        // OrdinalIgnoreCase keying: the second sign-in hits the same slot, no second mint.
        Assert.Same(lower.Value, upper.Value);
        Assert.Equal(1, tracker.Calls);

        // USE resolves the same slot regardless of case.
        Assert.True(book.Use("Admin", Loc).Ok);
        Assert.Same(lower.Value, book.Current);
    }

    // --- SIGN-OUT of a named slot: dispose + remove + repoint to the "" floor -------------------

    [Fact]
    public async Task SignOutCurrentNamedSlot_RepointsToDefaultFloor_AndRemovesSlot()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        await book.SignInSlotAsync("a");
        Assert.NotSame(initial, book.Current);

        var bare = await book.SignOut(null, Loc); // bare = the current slot, which is @a
        Assert.True(bare.Ok);

        // The minted slot is gone; Current falls back to the "" floor.
        Assert.Same(initial, book.Current);
        // It is fully removed from the map: USE @a now misses.
        Assert.False(book.Use("a", Loc).Ok);
    }

    [Fact]
    public async Task SignOutNamedSlot_WhileADifferentSlotIsCurrent_LeavesCurrentUntouched()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        await book.SignInSlotAsync("a");
        var b = (await book.SignInSlotAsync("b")).Value; // b current

        var res = await book.SignOut("a", Loc); // sign out the non-current slot by name
        Assert.True(res.Ok);

        // Signing out a non-current slot must not move the pointer.
        Assert.Same(b, book.Current);
        Assert.False(book.Use("a", Loc).Ok);
        Assert.True(book.Use("b", Loc).Ok);
    }

    [Fact]
    public async Task SignOutDefaultFloor_LeavesItPresentButDisconnected()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        // Bare SIGN-OUT while the "" floor is current targets it; the floor is never removed/disposed.
        var res = await book.SignOut(null, Loc);

        Assert.True(res.Ok);
        Assert.Same(initial, book.Current); // still the same instance, still present
        Assert.False(initial.IsSignedIn);   // but disconnected after the local-state clear
    }

    // --- disposal ownership split: book disposes MINTED slots, never the initial ----------------

    [Fact]
    public async Task SignOutNamedSlot_DisposesItsOwnedTransport()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        var minted = (await book.SignInSlotAsync("a")).Value!;
        await book.SignOut("a", Loc);

        // A minted slot owns its HttpClient; the book must dispose it on sign-out. A disposed
        // owned-jar session can no longer issue requests — TakeSnapshot stays safe, but a transport
        // attempt throws ObjectDisposedException. Probe via the disposed client.
        Assert.True(IsOwnedTransportDisposed(minted),
            "SIGN-OUT of a minted slot must dispose its owned HttpClient.");
    }

    [Fact]
    public async Task Dispose_DisposesMintedSlots_ButNotInitial()
    {
        var tracker = new MintTracker();
        var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);

        var m1 = (await book.SignInSlotAsync("a")).Value!;
        var m2 = (await book.SignInSlotAsync("b")).Value!;

        book.Dispose();

        Assert.True(IsOwnedTransportDisposed(m1), "Dispose() must dispose minted slot @a.");
        Assert.True(IsOwnedTransportDisposed(m2), "Dispose() must dispose minted slot @b.");
        // The caller-supplied initial slot is owned by the runner, NOT the book — it must survive.
        Assert.False(IsOwnedTransportDisposed(initial),
            "Dispose() must NOT dispose the caller-owned initial slot.");

        initial.Dispose(); // clean up the one the book intentionally left alone
    }

    /// <summary>True if the session's own-jar <c>HttpClient</c> (the <c>_ownedHttpClient</c> the
    /// cookie-jar ctor branch builds) has been disposed. <see cref="VidyanoSession.Dispose"/> disposes
    /// only that field, and a disposed <c>HttpClient</c> throws <see cref="ObjectDisposedException"/>
    /// synchronously from <c>CancelPendingRequests()</c> with no network — so this distinguishes
    /// "book disposed it" from "book left it alone" deterministically. Read through reflection because
    /// the owned transport is intentionally private (the abstraction hides it); the test asserts the
    /// disposal-ownership invariant, which has no public observable on the sealed session.</summary>
    private static bool IsOwnedTransportDisposed(VidyanoSession session)
    {
        var field = typeof(VidyanoSession).GetField("_ownedHttpClient",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var http = (HttpClient?)field!.GetValue(session);
        Assert.NotNull(http); // an own-jar session always has one; a supplied-client session would not
        try
        {
            http!.CancelPendingRequests();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    // --- regression: through the interpreter, the single-session path is unchanged --------------

    [Fact]
    public async Task SingleSessionScript_RunsWithoutMinting()
    {
        var tracker = new MintTracker();
        using var initial = NewOwnJarSession();
        var book = new SessionBook(initial, tracker.Factory);
        var interp = new Interpreter(book);

        // A server-free script that never names a session: the "" floor carries it; mintFresh stays cold.
        var result = await interp.RunAsync(Parse("@x = 1\nEXPECT {{x}} = 1"));

        Assert.True(result.Ok, result.Describe());
        Assert.Equal(0, tracker.Calls);
    }
}
