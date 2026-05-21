# Design: Expose server Initial PO (OnLogin gate) on `Client`

> ✅ Design phase complete (`/design-interface`)

## Problem

The Vidyano backend supports an **Initial PO** — a `PersistentObject` returned by `Advanced.OnLogin(LoginArgs)` (and the built-in `Vidyano.UserSettings/OnLogin` path) that should block the client until the user satisfies a gate. Use cases:

- License-terms acceptance before the app loads.
- Forced two-factor enrolment for users in a `TwoFactorRequired` group.
- Forced password reset (`User.ResetPasswordNextLogin`).

Server-side wiring exists (Vidyano.Service repo, for reference):

- `Vidyano.Service\Advanced.cs:809` — `OnLogin(LoginArgs) → PersistentObject?` hook.
- `Vidyano.Service\WebController.cs:645` — populates `GetApplicationResult.Initial`.
- `Vidyano.Service\WebController.Results.cs:107` — `[DataMember] public PersistentObject Initial { get; set; }`.
- `Vidyano.Service\Repository\UserSettingsActions.cs:448-449` — server signals satisfaction by queuing `ExecuteMethodOperation.ReloadPage()` once the gate is cleared.

**Vidyano.Core silently discards `response["initial"]`** (`Vidyano.Core\Client.cs:394` reads `application`, `userName`, `userPicture`, `authToken`, and `session` from the GetApplication response — never `initial`). Every consumer — including `Vidyano.Script` — bypasses these gates.

## Constraints any design must satisfy

1. Expose the Initial PO post-SignIn — observable by callers.
2. **Not** auto-roundtripped on subsequent requests (unlike `Session`); it's a one-shot screen.
3. Discovery without polling.
4. Satisfaction protocol: server queues `ExecuteMethod` ClientOperation with `Name == "reloadPage"` when the gate is cleared → Vidyano.Core must offer a way to re-establish the session in response.
5. Symmetry with `Client.Application` / `Client.Session`.
6. Backwards-compat: callers that don't check `Initial` keep working (they bypass gates, with documented warning).

## Chosen interface

```csharp
// Vidyano.Core\Client.cs — additions alongside Application / Session
public PersistentObject Initial { get; private set; }   // observable; set during GetApplication, cleared after ReloadPage
public Task EnsureInitialSatisfiedAsync(CancellationToken ct = default);
public Task ReestablishSessionAsync(CancellationToken ct = default);

// Vidyano.Core\Hooks.cs — one new hook (THE extension point)
protected internal virtual Task OnInitialRequired(PersistentObject initial) => Task.CompletedTask;
```

### Behavior

- **`Initial`** — set in `GetApplicationAsync` immediately after `Application = po;` from `response["initial"]` (null when omitted). `private set` + `SetProperty` so XAML/Avalonia bindings + `INotifyPropertyChanged` work. Cleared by `SignOut`.
- **`EnsureInitialSatisfiedAsync(ct)`** — the common-case one-liner.
  - If `Initial == null`: returns immediately.
  - Otherwise: calls `Hooks.OnInitialRequired(Initial)`, awaits the returned `Task`, then waits internally for the server's `ReloadPage` ClientOperation, re-runs `GetApplication` under the existing `AuthToken`, and returns. `Initial` is `null` on return.
- **`ReestablishSessionAsync(ct)`** — escape hatch for advanced callers that drove the Initial PO to SAVE themselves (multi-step wizard, custom flow). Performs only the re-run-GetApplication step. Idempotent.
- **`Hooks.OnInitialRequired(po)`** — default no-op. Custom hosts override to render the PO; `Vidyano.Script` overrides to park the PO and complete when the script SAVEs the gate.

### Satisfaction handshake (concrete protocol)

1. `GetApplicationAsync` reads `response["initial"]` → `Initial = Hooks.OnConstruct(this, json)`.
2. Caller drives the PO (typically `Initial.Actions["Save"].Execute()`), either directly or via `EnsureInitialSatisfiedAsync` → `Hooks.OnInitialRequired`.
3. Server action handler clears the gate and queues `ExecuteMethodOperation.ReloadPage()`.
4. The operation flows through the existing `Hooks.OnClientOperation` pipeline. `EnsureInitialSatisfiedAsync` has an internal `TaskCompletionSource` listening for that op.
5. On match: re-run `GetApplication` (preserves `AuthToken`), refresh `Application` / `Session` / `Messages` / `Actions`, null `Initial`, return.

The handshake reuses two existing rails (PO action execution + `OnClientOperation`). No new wire protocol.

### Usage

```csharp
// 95% caller — custom .NET host / Vidyano.Script
class ScriptHooks : Hooks
{
    protected override Task OnInitialRequired(PersistentObject po)
        => scriptRunner.HandleAsync(po);   // saves the PO, or throws on strict
}

await client.SignInUsingCredentialsAsync(user, pwd);
await client.EnsureInitialSatisfiedAsync();   // <-- the one line
// From here on Application/Session are guaranteed gate-free.

// Advanced caller — multi-step wizard drives Save itself
await client.SignInUsingCredentialsAsync(user, pwd);
if (client.Initial != null)
{
    await myMultiStepWizard.Run(client.Initial);
    await client.ReestablishSessionAsync();   // explicit re-handshake
}
```

## Vidyano.Script follow-up (separate PR)

Falls out cleanly from this design — sketched here for traceability, not in scope for the Vidyano.Core PR:

- New `@initial` reserved variable, parallel to `@session`.
- `VidyanoScriptHooks.OnInitialRequired(po)` parks `Client.Initial` as `@initial` and returns a `Task` that completes when the script SAVEs it (or errors out in strict mode).
- Until `@initial == null`, non-initial verbs error with `state-initial-pending`; escape via `@mode = direct`.

## Designs considered (and rejected)

### Rejected — Minimal surface (one property, nothing else)

Expose `Client.Initial` and stop. Each caller subscribes to `OnClientOperation`, filters for `ExecuteMethodOperation` with `Name == "reloadPage"`, re-calls `GetApplication`, handles re-entrancy.

**Why rejected:** punts the entire satisfaction protocol onto every consumer. The `ReloadPage` mechanism is one concrete protocol — Vidyano.Core should own it, not document it as a recipe. Inverts "pull complexity downwards."

### Rejected — Strategy pattern / maximize flexibility

`IInitialStrategy` interface + `DefaultInitialStrategy` / `AutoSaveInitialStrategy` / `RequireExplicitInitialStrategy` + `InitialContext` (with `Disposition`/`Replacement`) + `InitialReloadMode` enum (4 modes) + 3 new hooks (`OnInitialReceiving`, `OnInitialReceived`, `OnInitialSatisfied`) + internal `InitialCoordinator`.

**Why rejected:** ~12 surface types for two known consumer shapes (Vidyano.Script + custom host) that want essentially the same thing — "block until handled, then proceed." Premature framework-building; the 4×3 reload/disposition matrix solves problems we haven't seen and creates problems we will see.

### Rejected — Common-case design, as originally drafted

Same shape as the chosen design plus `AcknowledgeInitialBypass(reason)` (with a "warn on next mutation" side-channel through `OnException`) and `Hooks.OnInitialSatisfied()`.

**Why partially rejected:** the bypass-warning channel is speculative and confuses errors with warnings; a caller who ignores `Client.Initial` chose to bypass — XML-doc the consequence. `OnInitialSatisfied` is redundant with `INotifyPropertyChanged` firing when `Initial` goes null. Both dropped from the chosen shape.

## Tasks

- [ ] `Client.cs` — add `Initial` property with `SetProperty`/private setter, parallel to `Session` (line ~162).
- [ ] `Client.cs` `GetApplicationAsync` — set `Initial` from `response["initial"]` after `Application = po;` (line ~409).
- [ ] `Client.cs` `SignOut` — null out `Initial` (line ~750).
- [ ] `Client.cs` — implement `EnsureInitialSatisfiedAsync(CancellationToken)`: invokes `Hooks.OnInitialRequired(Initial)`, awaits, then waits for a `ReloadPage` `ExecuteMethodClientOperation` via internal `TaskCompletionSource`, re-runs `GetApplication` under existing `AuthToken`, returns.
- [ ] `Client.cs` — implement `ReestablishSessionAsync(CancellationToken)` as the re-run-GetApplication portion of the above.
- [ ] `Hooks.cs` — add `OnInitialRequired(PersistentObject) → Task` virtual hook with XML doc explaining the contract (return a Task that completes once the gate has been driven to Save / otherwise resolved).
- [ ] XML doc on `Initial`: one-shot, not auto-roundtripped, casual callers bypass the gate if they ignore it.
- [ ] Tests:
  - Sign-in with no Initial → `Initial == null`, `EnsureInitialSatisfiedAsync` is a no-op.
  - Sign-in with Initial → `Initial` non-null; `OnInitialRequired` fires; SAVE → `ReloadPage` → `Initial` cleared.
  - `ReestablishSessionAsync` after caller-driven SAVE clears `Initial`.
  - Cancellation token honoured during both the hook await and the `ReloadPage` wait.
  - `SignOut` clears `Initial`.

## Related

- Server-side surface is already in place; this is purely a Vidyano.Core consumer-side change.
- Natural sibling to PR #8 (`@session` reserved variable). The Vidyano.Script `@initial` follow-up will reference this once landed.
