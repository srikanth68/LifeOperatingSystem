# Vitara accounts: who may open which profile

Status: **design note, nothing built.** It exists so the decision can be made with the
trade-offs in front of you, not discovered halfway through a build.

## Where things stand

Profiles exist (one SQLite file per person, chosen by the `X-Profile-Id` header). Identity does
not. Maaya's login is a single operator whose name, password hash and PIN come from environment
variables; the token it mints carries only `sub` and `unique_name`, and every module trusts it
because they share one signing secret.

So the rule today is: **anyone who is signed in can open any profile whose id they can name.**
Ids are opaque (`p-3f9a01bc`) so they cannot be guessed, but "cannot be guessed" is not access
control, and the profile menu lists every id to whoever is signed in.

That is fine for one household that trusts itself. It is not fine the moment a profile belongs to
someone who should not see the others, and it is the first thing a stranger's data would need.

## What has to be true for a second household

1. **A login belongs to a person**, not to the installation.
2. **A login may open only its own profile(s)**, enforced on the server. A hidden menu entry is
   decoration; the check must live where the database is chosen.
3. **Devices belong to a profile.** The phone's HealthKit push uses one shared `X-Device-Key` and
   lands in the default profile. Two people means two keys, each bound to a profile.
4. **Everything that reads health data on someone's behalf carries the same rule**, including the
   assistant's tools, the MCP gateway and the background workers. Today those all see the default
   profile only, which is safe by being incomplete.

## The one seam to build first

Whatever identity system comes later, the enforcement point is the same and is small:

```
app.Use: resolve profile id  ->  IProfileAccess.CanOpen(user, profileId)  ->  403 or continue
```

`IProfileAccess` is one method. In Maaya today its implementation returns true for the operator,
which preserves current behaviour exactly. A later implementation reads a claim or a table.
Building the seam first means the enforcement, its tests and its failure responses are done once,
and swapping the identity underneath never touches the request path again. This is also the part
that carries over unchanged if Vitara leaves Maaya.

## Three ways to get identity

| | **A. Stay a household** | **B. Logins inside Maaya** | **C. Vitara's own identity** |
|---|---|---|---|
| What it is | One login, many profiles (today) | Vault's auth server gains users; a token lists the profiles it may open | Vitara runs its own accounts, independent of Maaya |
| Good for | You and people you share a login with | Family or friends you personally host for | Strangers; the standalone product |
| Cost | none | medium | large |
| Main risk | no privacy between profiles | Maaya's single-operator assumptions leak into the product | building login, recovery, email and abuse handling |
| Reversible | n/a | yes, it is a subset of C | n/a |

**Recommendation: build the seam now, choose B only if a real second household appears, and treat
C as part of the spin-out rather than something to grow out of Maaya's login.** Growing B into C
inside Maaya is how a product ends up with a hard-coded operator in its auth code.

Note on C: do not hand-roll it. Passkeys or an established identity provider remove password
storage, reset flows and most credential-stuffing exposure, which are the parts that go wrong.

## Gaps this design must close (found while reviewing the profile work)

- **Profile creation is unlimited.** Each person is a file; an authenticated caller can create
  them until the disk is full. Needs a cap per account and an audit line.
- **Oura's `state` is not a nonce.** It carries the profile id so the callback knows whom the token
  belongs to, but it is not a one-time value bound to the session, so a crafted callback could
  attach someone's ring to a profile they were not linking. Fix: issue a random single-use state,
  remember `state -> (profile, user)` for a few minutes, and refuse anything else.
- **One shared device key.** Replace with a key per device, stored hashed, mapped to a profile.
- **One shared signing secret.** Any module that is compromised can mint a token for any user.
  Acceptable inside one household; not acceptable as a product. Separate the issuer from the
  verifiers (asymmetric signing).
- **Refresh tokens live in memory.** A restart signs everyone out and there is no per-user
  revocation. Move to storage with a revoke action.
- **Backups hold everyone.** Fine for a household; a product needs per-person export and per-person
  delete that provably removes backups too. One file per person makes this tractable.
- **Background jobs and the assistant act as nobody.** Define "system" as a principal with an
  explicit list of what it may do, instead of bypassing the check.

## Build order, each step shippable on its own

1. `IProfileAccess` + enforcement in Vitara.API and Vitara.Insight; operator allowed everywhere.
   Tests: wrong user gets 403 on every route, the check cannot be skipped by `?profile=`. *(small)*
2. Single-use Oura `state`. *(small)*
3. Per-device keys bound to a profile; phone ingest picks the profile from the key. *(medium)*
4. Creation cap and an audit log of who created or deleted which profile. *(small)*
5. Users and a user-to-profiles table; token carries the profile list; profile menu shows only the
   caller's own. *(medium; this is choice B)*
6. Separate issuer and verifiers, durable revocable refresh tokens. *(medium; required for C)*

Steps 1 to 4 are worth doing even if a second household never arrives: they close real gaps in the
single-household build.

## Questions that decide the rest

1. Is the second person someone you will host and trust (B), or a stranger (C)?
2. How should they sign in: passkey, password, or a link sent to their email?
3. Who may see whom: strictly private, or a family view where one adult can see a child's profile?
4. Does a second person bring their own wearable and phone? That decides how much of the device and
   Oura work is needed before they can use it.
