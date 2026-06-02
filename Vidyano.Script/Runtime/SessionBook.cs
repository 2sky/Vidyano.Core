using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vidyano.Script.Diagnostics;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Vidyano.Script.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("vidyano")]

namespace Vidyano.Script.Runtime;

/// <summary>
/// Owns the multi-identity machinery behind the <c>SIGN-IN @name</c> / <c>USE @name</c> /
/// <c>SIGN-OUT</c> verbs: a name→<see cref="VidyanoSession"/> map, the current pointer, the
/// transport-mint factory, and disposal. The interpreter never sees the map — it reads
/// <see cref="Current"/> for the ~43 verb/EXPECT sites and makes exactly three calls
/// (<see cref="SignInSlotAsync"/>, <see cref="Use"/>, <see cref="SignOut"/>), each returning an
/// <see cref="OpResult"/> so a name miss is a loud diagnostic, never a throw.
/// </summary>
/// <remarks>
/// Three invariants the names alone don't carry:
/// <list type="bullet">
/// <item>The cookie-jar trap: every minted identity MUST get its OWN <c>HttpClient</c> +
/// <c>CookieContainer</c>. That is why <c>mintFresh</c> is built from the
/// <c>VidyanoSession(baseUri, acceptAnyServerCertificate)</c> own-jar ctor branch and must never
/// close over a shared client — sharing a jar silently conflates two identities at runtime with no
/// compile error.</item>
/// <item>The <c>""</c> floor: the default (unnamed) slot has map key <c>""</c>, is unaddressable by
/// <c>USE</c> (the parser requires <c>@name</c>), and is never removed or disposed. It keeps
/// <see cref="Current"/> non-null for a script's whole life, so <c>SIGN-OUT</c> of the active slot
/// can always repoint to it.</item>
/// <item>The disposal-ownership split: the caller-supplied <c>initial</c> transport is owned by the
/// runner and never disposed here; only sessions the book MINTS are owned and disposed by the book
/// (on <c>SIGN-OUT</c> of a named slot and in <see cref="Dispose"/>).</item>
/// </list>
/// </remarks>
internal sealed class SessionBook : IDisposable
{
    private const string DefaultSlot = "";

    private readonly Dictionary<string, VidyanoSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _minted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<ValueTask<VidyanoSession>> _mintFresh;
    private string _currentName = DefaultSlot;

    /// <param name="initial">The default-slot (<c>""</c>) session the caller already built. Owned by
    /// the caller — the book never disposes it.</param>
    /// <param name="mintFresh">Mints a NEW identity with its OWN <c>HttpClient</c> +
    /// <c>CookieContainer</c> rooted at the same base URI. Sessions the book mints are owned and
    /// disposed by the book. Must NOT close over a shared/supplied client (the cookie-jar trap).</param>
    public SessionBook(VidyanoSession initial, Func<ValueTask<VidyanoSession>> mintFresh)
    {
        _sessions[DefaultSlot] = initial;
        _mintFresh = mintFresh;
    }

    /// <summary>The active session. Never null after construction — the <c>""</c> floor is present
    /// until a script explicitly signs it out, and even then the slot stays in the map (disconnected).</summary>
    public VidyanoSession Current => _sessions[_currentName];

    /// <summary>Selects (and, for a new named slot, mints) the session a following
    /// <c>SignInAsync</c> authenticates. A <c>null</c>/empty <paramref name="name"/> selects the
    /// default <c>""</c> slot and mints nothing — the zero-cost single-session path. A named slot is
    /// minted on first use; a repeat name switches to the EXISTING session and re-authenticates it in
    /// place (no second jar, no nav-state reset — the collision rule). Returns the selected session.</summary>
    public async ValueTask<OpResult<VidyanoSession>> SignInSlotAsync(string? name)
    {
        var key = name ?? DefaultSlot;
        if (!_sessions.TryGetValue(key, out var session))
        {
            session = await _mintFresh().ConfigureAwait(false);
            _sessions[key] = session;
            _minted.Add(key);
        }
        _currentName = key;
        return OpResult<VidyanoSession>.Success(session);
    }

    /// <summary>Switches the active session to the named slot. Only NAMED slots are addressable — the
    /// default <c>""</c> floor is unreachable by name (the parser requires <c>@name</c>). An unknown
    /// name is a non-throwing failure carrying <see cref="ErrorKind.ResolveSession"/> and a
    /// "did you mean" hint over the known names.</summary>
    public OpResult<VidyanoSession> Use(string name, SourceLocation loc)
    {
        if (!_sessions.TryGetValue(name, out var session))
            return OpResult<VidyanoSession>.Fail(UnknownSession(name, loc));
        _currentName = name;
        return OpResult<VidyanoSession>.Success(session);
    }

    /// <summary>Signs out a slot (the current one when <paramref name="name"/> is null, else the named
    /// one). Always performs the faithful <c>viSignOut</c> server action + auth clear via
    /// <see cref="Vidyano.Client.SignOut"/> (already error-swallowing). A MINTED slot is then disposed
    /// and removed from the map; the default <c>""</c> floor is left present-but-disconnected (never
    /// removed) so the next verb fails loudly with the existing not-signed-in guard. When the
    /// signed-out slot was the active one, <see cref="Current"/> repoints to the <c>""</c> floor.</summary>
    public async ValueTask<OpResult<bool>> SignOut(string? name, SourceLocation loc)
    {
        var key = name ?? _currentName;
        if (!_sessions.TryGetValue(key, out var session))
            return OpResult<bool>.Fail(UnknownSession(key, loc));

        await session.Client.SignOut().ConfigureAwait(false);

        // Only a minted slot owns its transport, so only a minted slot is disposed + removed. The ""
        // floor stays in the map (disconnected) to keep Current non-null.
        if (_minted.Contains(key))
        {
            session.Dispose();
            _sessions.Remove(key);
            _minted.Remove(key);
            if (string.Equals(_currentName, key, StringComparison.OrdinalIgnoreCase))
                _currentName = DefaultSlot;
        }

        return OpResult<bool>.Success(true);
    }

    private Diagnostic UnknownSession(string name, SourceLocation loc) =>
        new(ErrorKind.ResolveSession,
            $"No session named '@{name}'.",
            loc,
            Hint: Suggester.Hint(name, _sessions.Keys.Where(k => k.Length > 0), "session")
                  ?? "Open one first with `SIGN-IN @name = user / pass`.");

    public void Dispose()
    {
        // Dispose only the sessions the book minted (own-jar transport). The caller-supplied default
        // slot is owned by the runner and disposed there.
        foreach (var key in _minted)
            if (_sessions.TryGetValue(key, out var session))
                session.Dispose();
    }
}
