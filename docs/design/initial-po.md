# Design: Expose server Initial PO (OnLogin gate) on `Client`

> ✅ Design phase complete (`/design-interface`) — protocol corrected after reading the v4 sign-in component.

## Problem

The Vidyano backend supports an **Initial PO** — a `PersistentObject` returned by `Advanced.OnLogin(LoginArgs)` (and the built-in `Vidyano.UserSettings/OnLogin` path) that should block the client until the user satisfies a gate. Use cases:

- License-terms acceptance before the app loads.
- Forced two-factor enrolment for users in a `TwoFactorRequired` group.
- Forced password reset (`User.ResetPasswordNextLogin`).

Server-side wiring exists (Vidyano.Service repo, for reference):

- `Vidyano.Service\Advanced.cs:809` — `OnLogin(LoginArgs) → PersistentObject?` hook.
- `Vidyano.Service\WebController.cs:645` — populates `GetApplicationResult.Initial`.
- `Vidyano.Service\WebController.Results.cs:107` — `[DataMember] public PersistentObject Initial { get; set; }`.

**Vidyano.Core silently discards `response["initial"]`** (`Vidyano.Core\Client.cs` reads `application`, `userName`, `userPicture`, `authToken`, and `session` from the GetApplication response — never `initial`). Every consumer — including `Vidyano.Script` — bypasses these gates.

## How the gate is actually satisfied (v4 protocol)

Verified against `P:\Vidyano\src`:

- **`core/service.ts:1475-1477`** — on GetApplication, store `result.initial` as `service.initial`. No wait, no handshake, no listener.
- **`web-components/sign-in/sign-in.ts:167-176`** — when `service.initial` is present, the sign-in component switches to an `"initial"` step and edits the PO.
- **`web-components/sign-in/sign-in.ts:312-323`** — `_finishInitial`: `await this.initial.save();`, surface `notification` if present, then `this.service.clearInitial()` and navigate to the return URL. **No `reloadPage` wait, no GetApplication re-fetch.**

The gate is therefore satisfied **the moment the SAVE on the Initial PO completes** (whether the response carries `reloadPage` or not). The auth-touching cases (password change, 2FA enable) rotate `authToken` *inline* in the SAVE response — Vidyano.Core's `PostAsync` already updates `AuthToken` from every response, so the client carries the new token forward automatically. `reloadPage` is only queued for Language / CultureInfo changes (`UserSettingsActions.cs:379,392`) and even then it flows through the existing `Hooks.OnClientOperation` channel — orthogonal to the gate.

## Constraints any design must satisfy

1. Expose the Initial PO post-SignIn — observable by callers.
2. Not auto-roundtripped on subsequent requests (unlike `Session`); the server only returns it from `GetApplication`.
3. Caller can explicitly clear it after satisfying the gate (mirror v4's `service.clearInitial()`).
4. Symmetry with `Client.Application` / `Client.Session`.
5. Backwards-compat: callers that don't check `Initial` keep working (they bypass gates, with documented warning).

## Chosen interface

```csharp
// Vidyano.Core\Client.cs — additions alongside Application / Session
public PersistentObject Initial { get; private set; }   // observable; set during sign-in
public void ClearInitial();                             // mirrors v4's service.clearInitial()
```

That's it. Two members on `Client`. No new hook on `Hooks`.

### Behavior

- **`Initial`** — set in `SignInAsync` immediately after `Application = po;` from `response["initial"]`. Rejected when the PO carries an `Error`-type notification (mirroring `UpdateSession`'s guard against a smuggled `Vidyano.Error` PO). `null` when the server omits the field. `private set` + `SetProperty` so XAML / Avalonia bindings + `INotifyPropertyChanged` work. Also cleared by `SignOut`.
- **`ClearInitial()`** — sets `Initial = null`. The 1:1 parallel of v4's `service.clearInitial()`. Idempotent; safe to call when `Initial` is already null. Use it after driving the gate PO to a successful `Save`.

The caller's loop is:

```csharp
await client.SignInUsingCredentialsAsync(user, pwd);
if (client.Initial is { } gate)
{
    await scriptRunner.HandleAsync(gate);   // drives the PO to Save
    client.ClearInitial();
}
// From here on Application / Session are gate-free.
```

An exception between `await` and `ClearInitial()` correctly skips the clear — the gate stays pending and the caller can retry or surface to the user. Wrapping this in a library helper hides nothing and would be a shallow module.

## Vidyano.Script follow-up (separate PR)

- New `@initial` reserved variable, parallel to `@session`, surfaces `Client.Initial`.
- Script semantics: when `@initial` is present, the script SAVEs it like any other PO, then calls `client.ClearInitial()` via the runtime once the SAVE completes clean.
- Until `@initial == null`, non-initial verbs error with `state-initial-pending`; escape via `@mode = direct`.

## Designs considered (and rejected)

### Rejected — `EnsureInitialSatisfiedAsync` with a `reloadPage` wait + `Hooks.OnInitialRequired` + `ReestablishSessionAsync` escape hatch

Initial implementation went down this path on the assumption that the server queues `ExecuteMethodOperation.ReloadPage()` to signal gate satisfaction, with `EnsureInitialSatisfiedAsync` blocking on that signal and then re-running GetApplication.

**Why rejected:** the assumption was wrong. The v4 sign-in component never waits for `reloadPage`; it treats the SAVE-success as the satisfaction signal and calls `service.clearInitial()` itself. The wait-for-`reloadPage` flow would have hung indefinitely on the common cases (accept-terms, 2FA enable, password reset — none of which queue `reloadPage`). Once that misconception is removed, the satisfaction protocol collapses to "caller calls `ClearInitial` after SAVE succeeds" and the entire `Ensure…` / `OnInitialRequired` / `Reestablish…` / polyfill / semaphore / internal-event stack is unnecessary plumbing for a problem the protocol doesn't have.

### Rejected — `HandleInitialAsync(Func<PersistentObject, Task> handler)` helper

Tempting wrapper: invoke the handler if `Initial != null`, then `ClearInitial()`; return `bool`.

**Why rejected:** four lines that hide nothing. The caller's `if (client.Initial is { } po) { … ClearInitial(); }` form is just as short, makes the exception-skips-clear behaviour visible in code rather than hidden in XML doc, and avoids growing a shallow module. Caller-side control is the right default; if a clear cross-cutting pattern emerges later, the helper can be added without breaking anything.

### Rejected — Strategy pattern / maximize flexibility

`IInitialStrategy` + 3 strategy classes + `InitialContext` (with `Disposition`/`Replacement`) + `InitialReloadMode` enum + 3 new hooks + internal `InitialCoordinator`. ~12 surface types.

**Why rejected:** premature framework-building for two known consumer shapes that want essentially the same thing.

## Tasks

- [ ] `Client.cs` — add `Initial` property with `SetProperty`/private setter, parallel to `Session`.
- [ ] `Client.cs` `SignInAsync` — set `Initial` from `response["initial"]` after `Application = po;`, rejecting `Vidyano.Error` / error-typed POs the way `UpdateSession` does for `Session`.
- [ ] `Client.cs` `SignOut` — null `Initial` alongside the other client state.
- [ ] `Client.cs` — add `ClearInitial()` public method.
- [ ] XML doc on `Initial`: one-shot, not auto-roundtripped, casual callers bypass the gate if they ignore it; pointer to `ClearInitial`.
- [ ] Tests (no test project in Vidyano.Core today; documented for manual verification):
  - Sign-in with no Initial → `Initial == null`.
  - Sign-in with Initial → `Initial` non-null; after handler + `ClearInitial()` → null.
  - Sign-in with an Initial PO that has an Error-typed notification → throws.
  - `SignOut` clears `Initial`.

## Related

- Server-side surface is already in place; this is purely a Vidyano.Core consumer-side change.
- Natural sibling to PR #8 (`@session` reserved variable). The Vidyano.Script `@initial` follow-up will reference this once landed.
