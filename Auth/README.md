# Accounts, sign-in, role permissions and tournament access

Phases 1–4 of `docs/accounts-and-access-plan.md`: people sign in with a code emailed to them (no
passwords), every request says who they are, each kind of account (role) may call only the
services ticked for it, inside a tournament only its owners and scorers may change it (its players
may look), squad places link to people's accounts, and people can delete their account.

## The flow

```
AUTH_OTP_SEND   { email }                 → { email, expiresInSeconds: 300, resendAfterSeconds: 60 }
                                            (same answer whether or not the email has an account)
AUTH_OTP_VERIFY { email, otp }            → { outcome: "SignedIn", sessionToken, sessionId, expiresAt, user }
                                            or, for a new email: { outcome: "NeedsName" }
AUTH_OTP_VERIFY { email, otp, name }      → { outcome: "SignedIn", isNewUser: true, … }  (account created)
```

Sign-up and sign-in are the same two steps. The app keeps `sessionToken` (phone keychain / browser
storage) and seals it into every request's payload as `session` (see `Gateway/README.md`); the
clear header carries `sessionId`, which is not secret.

Signed in: `AUTH_LOGOUT` (this device), `AUTH_LOGOUT_ALL` (every device), `ACCOUNT_GET`,
`ACCOUNT_UPDATE { name }`.

Only `HEALTH_CHECK`, `AUTH_OTP_SEND` and `AUTH_OTP_VERIFY` work without a session
(`[AllowAnonymousService]`; the architecture tests hold the list). Everything else answers **401**,
which sends both apps back to their sign-in screen with the message:

| Why | Message |
|---|---|
| no token | Sign in to continue. |
| signed out / signed out everywhere / unknown token | You've been signed out. Sign in again. |
| unused for 30 days (web: 7) | You've been signed out because this device wasn't used for a while. Sign in again. |
| blocked by an admin (`Users.IsDisabled`) | This account has been blocked. |

## Codes

- 6 digits from the OS's secure random source; stored as SHA-256 of `email|code`; valid 5 minutes;
  5 wrong tries kill it; used once; a new code cancels the previous one.
- Limits (all in `Auth` settings): one code a minute and 5 an hour per email; 10 an hour per device;
  30 an hour per network address. Answer 429 with how long to wait.
- If the email can't be sent: 503 "We couldn't send the email just now…", and the code is removed
  so it doesn't count against the limits. The failure is in ErrorLogs; the SMTP call in RemoteLogs.
- Wrong code: "That code isn't right. 4 tries left." Expired or used: "This code has expired. Ask
  for a new one."

## Sessions

`UserSessions`: one row per signed-in device. The token is 32 random bytes (43 characters); only
its SHA-256 is stored, so the table can't be used to sign in. Every request looks it up (one
indexed read), which is why signing out and blocking work immediately. Use moves the expiry forward
(checked at most every 5 minutes): phones 30 days, web 7 days — browser storage is readable by any
script on the page, the phone keychain isn't.

Where the token is read: the sealed gateway payload for app requests. A **direct** call (Swagger,
scripts — only possible while `Gateway:EnforceEncryption` is false) may send
`Authorization: Bearer <token>` instead. A gateway request never reads that header.

In code: `SessionAuthenticationMiddleware` (after routing and gateway enforcement) fills
`ICurrentUser` and the request's log context. Services take `ICurrentUser` (who) and `ClientInfo`
(which app/device, handed over by the controller) — never `HttpContext` (architecture tests).

## Role permissions

Two tables (script `deploy/sql/02-permissions.sql`):

- **`Permissions`** — one row per service, keyed by `ServiceRequestId` (so renaming a method keeps
  its grants), plus ControllerName, ActionName, HttpMethod, URL (the route template) and the flags
  below. **`IsDisabled`** is the admin's switch (nobody may call it); **`IsObsolete`** is the sync's
  (gone from the code). Neither ever touches the other.
- **`RolePermissions`** — the ticks: (RoleId, PermissionId). Unticking sets `IsDisabled = 1` and
  keeps the row, so a later deploy can tell "an admin said no" from "never decided".

**The sync** runs when the API starts (`PermissionSyncHostedService`; if the database isn't
reachable it keeps trying every minute while the API runs):

| Service in the code is… | The sync |
|---|---|
| new | adds the row; ticks **Admin** and the code's `[DefaultRoles(...)]` — the only time code sets ticks |
| still there | refreshes controller/action/URL/flags; leaves ticks and `IsDisabled` alone |
| gone | `IsObsolete = 1` (ticks kept) |
| back | `IsObsolete = 0`; its old ticks apply again |

So an admin's "no" survives every deploy. The start-up log says what changed, e.g.
"Permissions synced with the code: 3 added, 0 refreshed, 0 retired, 0 back."

**The check** (`PermissionAuthorizationMiddleware`, after the session check, on every request):

1. Anonymous services (`[AllowAnonymousService]`: health, the two sign-in steps) — allowed.
2. **Protected** services (`[ProtectedService]`: `AUTH_LOGOUT`, `AUTH_LOGOUT_ALL`, `ACCOUNT_GET`) —
   allowed for every signed-in user whatever the table says (for an admin-only one, always for
   Admin), so nobody can be locked out of signing out.
3. Switched off or obsolete — 403 "This feature is currently unavailable."
4. The user's role ticked — allowed; otherwise 403 "You don't have access to this." An
   **`[AdminOnly]`** (portal) service is never allowed for EndUser, even if ticked.
5. Not in the table yet (sync hasn't run) — 403 "This feature isn't available yet."

Permissions and ticks are cached in memory and reloaded every `Auth:PermissionRefreshSeconds` (60);
if they can't be loaded at all, non-protected services answer 503 rather than guess.

**In code**: every action needs `[DefaultRoles(...)]` (on the action or its controller — all the
app's controllers carry `[DefaultRoles(RoleIds.EndUser)]`) or `[AllowAnonymousService]`; the
architecture tests check, and hold the reviewed lists of anonymous and protected services.

**Changing ticks before the portal exists**: `deploy/sql/grant-permission.sql` — set the service id,
`TICK`/`UNTICK` (for a role) or `DISABLE`/`ENABLE` (for everyone), run it; it applies the same guards
and the API picks the change up within a minute. Its last query lists every service and who has it.

## Tournament access (phase 3)

**Who is what** — `TournamentMembers` (one row per person per tournament): **Owner** or **Scorer**.
Whoever creates a tournament is its creator (`Tournaments.CreatedByUserId`) and first owner.

| | Creator | Owner | Scorer | Anyone else |
|---|---|---|---|---|
| See it (home list, everything in it) | ✅ | ✅ | ✅ | 404 "Tournament not found." |
| Edit it, its teams, groups, schedule; start; change dates; complete | ✅ | ✅ | 403 | 404 |
| Add / remove owners and scorers (by sign-in email) | ✅ | ✅ (not the creator) | 403 | 404 |
| Set up and score matches (subject to the scorer lock) | ✅ | ✅ | ✅ | 404 |
| Take over scoring without asking | ✅ | ✅ | asks instead | — |
| Delete it | ✅ | 403 | 403 | 404 |
| Leave it | ❌ | ✅ | ✅ | — |

Strangers always hear "not found", never "forbidden": a private tournament isn't confirmed to exist.

**How it's enforced** — every action carries `[TournamentAccess(TournamentPermission.X, From = …)]`
or `[NotTournamentScoped]` (architecture test). `TournamentAccessFilter` (a global MVC action
filter) finds the tournament from the route's `id` / `matchId` / `scheduleId` / `sport` + `matchId`,
or the body (`ITournamentScopedRequest`), and `TournamentAccessService` checks role, status and the
scorer lock, filling `ITournamentContext` for the services. The scoring engines know nothing of it.

**Dates and status** — a tournament gets its start and end date when its schedule is approved
(`TOURNAMENT_SCHEDULE_APPROVE` takes `startDate`/`endDate`, required unless already set; they can
also be sent on create, both or neither). End ≥ start, ≤ a year, not already past. Starting a
tournament without dates is refused. `Status`: **Upcoming** → **Live** (started) → **Completed** (an owner marks it —
refused while a match is in play — or `TournamentLifecycleJob`, hourly, at midnight India time after
the day after the end date) or **Cancelled** (never started, same deadline). Completed/cancelled:
nothing can be changed (409), no match can start; one already in play can still be finished.
Tournaments from before dates existed have none and never close by themselves.
`Tournaments.TimeZoneId` (default `Asia/Kolkata`) is ready for other countries.

**The scorer lock** (both sports, columns on `Matches` / `CricketMatches`) — whoever first scores a
match (sets it up) holds it; anyone else's scoring call answers 409 "Amit Rao is scoring this
match." `SCORING_REQUEST` asks (granted at once if nobody holds it or the holder's app hasn't been
seen for 5 minutes; otherwise the holder has 2 minutes to `SCORING_RESPOND` allow/decline),
`SCORING_TAKE_OVER` is owners only. Every match answer carries `scoring` (`activeScorer`,
`isActiveScorer`, `canScore`, `canRequestScoring`, `canTakeOverScoring`, `pendingRequest`,
`canRespondToRequest`); the apps hide or disable their scoring controls from it. Leaving or being
removed releases any match held. The holder's 5-second refreshes keep it fresh (stored every 30 s).

Tournament answers carry `access` (`myRoles`, `canEdit`, `canScore`, `canManageMembers`,
`canDelete`, `canLeave`, `canComplete`, `allMatchesPlayed`); the list carries `myRoles` and `status`.

**Existing tournaments** — the phase-3 migration makes started ones Live, the rest Upcoming, and
System their creator and owner, so they show on nobody's list until
`deploy/sql/04-take-over-existing-tournaments.sql` adds someone (by email) as an owner.

## Players are people (phase 4)

A `Players` row is a **squad place** (this person, this team, this number) and stays one, because
organisers add people who don't have the app and every goal and ball points at it. A place can be
**linked** to an account (`Players.UserId`); a person has at most one place per tournament (unique
index on `Players.TournamentId` + `UserId`; `TournamentId` is copied from the team for that).

**Two ways to link, both instant:**
- **Email** — an owner types the player's sign-in email on the place (`PLAYER_CREATE` / `PLAYER_UPDATE`
  `email`). An account with it → linked now; none yet → `Players.EmailId` waits and the place links
  when that person signs in (`AUTH_OTP_VERIFY`). On an update, no `email` = unchanged, `""` = clear
  and unlink, another address = relink. "Amit Rao is already in Lions in this tournament." if taken.
- **Join code** — every team has a 6-character `Teams.JoinCode` (no 0/O, 1/I; made with the team,
  older teams got one in the migration). Owners see it on the team (`joinCode`) and can
  `TEAM_JOIN_CODE_RESET`. `TEAM_JOIN_PREVIEW { code }` → the team and its unclaimed names (places
  with no account and no waiting email), each with a `detail` ("#7 · Batter") to tell two Rahuls
  apart; `TEAM_JOIN { code, playerId }` claims one, `playerId: null` joins as a new player. Refused
  when the tournament is over or you're already in one of its teams. 10 wrong codes in 15 minutes →
  429 (in memory, like the replay cache).

**While linked** the place shows the account's name, and in cricket the person's own profile
(`UserCricketProfiles`, edited on the Account screen via `ACCOUNT_UPDATE { name, cricket }`) — both
copied onto the place, so the engines and stats read squads as before. The organiser's name and
cricket fields are ignored for a linked place. Linking someone with no profile yet starts theirs
from the organiser's description. Renames reach every linked place; past cricket scorecards keep
the name they were scored under.

**Unlinking:** `PLAYER_UNLINK` — an owner for any place, the player for their own ("Leave team");
`TOURNAMENT_LEAVE` also drops the caller's place. The name stays on the team.

**What a player may do:** see the tournament (it's on their home list with `myRoles: ["player"]`,
`access.myPlayerId` / `myTeamId`), its teams, matches and stats — nothing else (scoring answers
403 "Only the tournament's owners and scorers can do that."). Players' emails and join codes are
shown to owners only (`email` / `joinCode` are null for everyone else); `isMe` marks your own place.

## Deleting an account (phase 4)

`ACCOUNT_DELETE_PREVIEW` says what will happen and emails a code (purpose `DeleteAccount`, its own
wording, same limits as sign-in codes); `ACCOUNT_DELETE { otp }` does it:

| | |
|---|---|
| Tournaments only they own | deleted, for everyone in them (the preview names them and counts the others) |
| Ones they created with other owners | the longest-standing other owner becomes the creator |
| Every other membership | removed; any match they were scoring is released |
| Squad places | unlinked (names stay); places waiting for their email lose it |
| The account row | kept (foreign keys), `Name` "Deleted user", email and phone cleared, `DeletedOn` set |
| Sessions, codes, cricket profile | removed — every device is signed out at once |

Signing up again with the same email makes a new, empty account. Admin accounts can't be deleted
from the app (`canDelete` false on `ACCOUNT_GET`).

## Tables (game database)

`Users`, `Roles` (1 Admin, 2 EndUser — fixed ids the code relies on), `UserSessions`,
`OtpChallenges` (script `deploy/sql/01-accounts.sql`); `Permissions`, `RolePermissions` (script
`deploy/sql/02-permissions.sql`); `TournamentMembers` plus the new tournament and match columns
(script `deploy/sql/03-tournament-access.sql`); `UserCricketProfiles`, `Players.UserId/EmailId/TournamentId`,
`Teams.JoinCode` (script `deploy/sql/05-player-accounts.sql`). UserId 1 is **System**: no email, can't sign in, owns
pre-accounts data and is `CrdBy` on seeded and synced rows.

## Settings

```json
"Auth": {
  "SessionDays": 30, "WebSessionDays": 7,
  "OtpDelivery": "Smtp"            // or "Log" — see below
},
"Smtp": {
  "Host": "smtp.gmail.com", "Port": 587, "EnableSsl": true,
  "UserName": "you@gmail.com", "Password": "<app password>",
  "FromAddress": "you@gmail.com", "FromName": "Tournament Scheduler"
}
```

- **Gmail**: turn on 2-step verification, create an *App password* (Google Account → Security),
  use it as `Password`. About 500 emails a day. **Brevo** (free, 300/day): host
  `smtp-relay.brevo.com`, port 587, the SMTP login and key from its dashboard.
- Put the password only in the server's own `appsettings.json` (the IIS folder) or user secrets —
  never in the repo's.
- **`"OtpDelivery": "Log"`** (the default in Development) sends no email: the code is written to the
  API log and to `C:\ProgramData\TournamentScheduler\logs\otp-codes.txt`. Handy on a LAN test server
  before email is set up — but anyone who can read that file can sign in as anyone, so never leave
  it on for real users. The IIS app pool needs modify on that folder (it already needs it for the
  log fallback).

## The first admin

```
dotnet run -- users create-admin you@example.com "Your Name"
# on the IIS server, from the site folder:
dotnet TournamentScheduler.Api.dll users create-admin you@example.com "Your Name"
```

Promotes the account if the email has one, otherwise creates it. The admin then signs in with a
code like anyone else. Admin is ticked for every service, but there are no admin-only services
until the portal (phase 5).
