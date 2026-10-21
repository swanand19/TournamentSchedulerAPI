# Accounts, sign-in, role permissions and tournament access

Phases 1–5 of `docs/accounts-and-access-plan.md` and 6B of `docs/phase-6-plan.md`: people sign in
with a code sent to their email or, by text, their mobile number (no passwords in the apps), every request says who they are, each kind of account (role) may call only the
services ticked for it, inside a tournament only its owners and scorers may change it (its players
may look), squad places link to people's accounts, people can delete their account, and admins run
all of it from the admin portal (code + password).

## The flow

```
AUTH_OPTIONS                              → { phone: true|false, defaultCountryCode: "+91" }
AUTH_OTP_SEND   { contact }               → { sentTo, channel: "Email"|"Sms", expiresInSeconds: 300, resendAfterSeconds: 60 }
                                            (same answer whether or not it has an account)
AUTH_OTP_VERIFY { contact, otp }          → { outcome: "SignedIn", sessionToken, sessionId, expiresAt, user }
                                            or, for a new email or number: { outcome: "NeedsName" }
AUTH_OTP_VERIFY { contact, otp, name }    → { outcome: "SignedIn", isNewUser: true, … }  (account created)
```

**Email or mobile number (phase 6B).** `contact` is one field: anything with an @ is an email
(lower-cased), anything else a mobile number, stored in E.164 (`+919876543210`; typed without a
country code it's taken as +91, and an Indian mobile must be 10 digits starting 6–9). An account has
an email, a number or both, at least one, each proved by a code. Numbers work only when the server
can text codes (`Sms:Provider` not `None`); otherwise `AUTH_OPTIONS.phone` is false, the apps hide
every number field, and a number is refused: "Mobile numbers can't be used yet. Use an email
address." The admin portal still sends `{ email }` (email only) — admins sign in to the portal by email.

Sign-up and sign-in are the same two steps. The app keeps `sessionToken` (phone keychain / browser
storage) and seals it into every request's payload as `session` (see `Gateway/README.md`); the
clear header carries `sessionId`, which is not secret.

Signed in: `AUTH_LOGOUT` (this device), `AUTH_LOGOUT_ALL` (every device), `ACCOUNT_GET`,
`ACCOUNT_UPDATE { name }`, and the account's email and number:

```
ACCOUNT_CONTACT_SEND   { contact }        → a code (purpose AddContact) to the new email or number;
                                            409 "This email is used by another account." if taken
ACCOUNT_CONTACT_VERIFY { contact, otp }   → account: added, or replacing the one of that kind;
                                            squad places waiting on it link now
ACCOUNT_CONTACT_REMOVE { kind }           → account; refused for the last way to sign in, and for an
                                            admin's email (the portal sends codes there)
```

`ACCOUNT_GET` says which can go (`canRemoveEmail`, `canRemovePhone`).

Only `HEALTH_CHECK`, `AUTH_OPTIONS`, `AUTH_OTP_SEND` and `AUTH_OTP_VERIFY` (and the portal's
`ADMIN_AUTH_SIGN_IN`) work without a session
(`[AllowAnonymousService]`; the architecture tests hold the list). Everything else answers **401**,
which sends both apps back to their sign-in screen with the message:

| Why | Message |
|---|---|
| no token | Sign in to continue. |
| signed out / signed out everywhere / unknown token | You've been signed out. Sign in again. |
| unused for 30 days (web: 7) | You've been signed out because this device wasn't used for a while. Sign in again. |
| blocked by an admin (`Users.IsDisabled`) | This account has been blocked. |

## Codes

- 6 digits from the OS's secure random source; stored as SHA-256 of `target|code` (the email or
  number); valid 5 minutes; 5 wrong tries kill it; used once; a new code cancels the previous one.
  Purposes `SignIn`, `DeleteAccount`, `AddContact` never stand in for each other.
- Limits (all in `Auth` settings): one code a minute per email or number; 5 an hour per email, 3 an
  hour per number (texts cost money); 10 an hour per device; 30 an hour per network address. Answer
  429 with how long to wait.
- If the email or text can't be sent: 503 "We couldn't send the email (text message) just now…", and
  the code is removed so it doesn't count against the limits. The failure is in ErrorLogs; the SMTP
  call in RemoteLogs.
- Delivery: `OtpDispatcher` picks the sender per code — email by `Auth:OtpDelivery`, texts by
  `Sms:Provider`. Choosing an SMS provider means one sender class in `Otp/` plus its value in
  `SmsProvider`; nothing else changes.
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
| Add / remove owners and scorers (by sign-in email or number; invites for anyone without an account) | ✅ | ✅ (not the creator) | 403 | 404 |
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

## Invites (phase 6C)

An owner adds an owner or scorer by the email or mobile number they sign in with
(`TOURNAMENT_MEMBER_ADD { contact, role }`). An account with it is added at once ("Ravi Kumar is now
a scorer."). Anyone else gets an **invite**: a `TournamentInvites` row (target, role, inviter), sent
to them by email or text (`Messaging/`, the same delivery settings as codes; no code in it), valid
`Auth:InviteDays` (30). When that person signs up — or adds that email or number to their account
— `TournamentInviteService` turns the invite into the membership (the higher role if invited twice;
an owner is never lowered).

- The members services answer `{ members, invites, notice }`: invites for owners only, `notice` the
  sentence to show ("No account uses ravi@example.com yet, so we've sent an invite. …").
- `TOURNAMENT_INVITE_RESEND` sends it again and renews it; at most once a minute per invite (429
  with the wait). Adding the same email or number again does the same, with the new role.
- `TOURNAMENT_INVITE_CANCEL` cancels it (`IsDisabled`); an expired one stays listed as expired until
  sent again or cancelled. Neither gives the role.
- If sending fails the invite is still saved, and the notice says to send it again; the failure is
  in ErrorLogs. By number with `Sms:Provider` None: refused, like every number.

## Notifications (phase 6D)

An inbox per person (`Notifications`), shown behind a bell on Home in both apps; the bell asks
`NOTIFICATION_UNREAD` every 30 seconds while Home is on screen. `NOTIFICATION_LIST` pages it newest
first; `NOTIFICATION_READ` / `READ_ALL` mark it. Only your own — someone else's id is a 404.

What lands there (`Services/Notifications/Notifier.cs`, called by the service where it happens, saved
with that change): a scoring request to the scorer, the answer to whoever asked, scoring taken over
(by an owner, or because the scorer's app went quiet) to whoever held it; being made an owner or
scorer; an invite taken up (to the newcomer and the inviter); a squad place given your email or
number (now, or when you sign up); "all matches played" to the other owners after the last match
(once per tournament); and an end-date reminder to owners, from 8 a.m. on the end date in the
tournament's time zone, also **emailed** to owners who have an email (once per end date). Nobody
hears about their own action. Notifications older than 90 days are deleted by the hourly job.

### Message templates

Every message — the sign-in, add-contact and delete-account codes, the tournament invite, and each
notification — is worded by a row of `NotificationTemplates` (one per `NotificationTemplateCode`),
edited on the admin portal's Messages page (`ADMIN_TEMPLATE_LIST`, `ADMIN_TEMPLATE_SAVE {…}`,
`ADMIN_TEMPLATE_RESET`). A row has a switch and texts per channel — email (subject, body), text (body,
DLT template id), inbox (title, text), push (title, text; empty = the inbox's) — with `{{Placeholders}}`
filled in when it goes out. The API adds a row with the built-in wording
(`Messaging/NotificationTemplateCatalog`) for any code the table lacks; templates are cached for a
minute, and a portal save applies at once. Rules a save must keep: codes and invites go by email or
text, whichever the person gave, so both stay on; code texts must contain `{{Code}}`; notifications
never go by text; only the code's own placeholders. A row broken by hand in SQL is skipped (warning
in the log) and the built-in wording used, so sign-in can't be broken. A notification with email on
also emails the person (if they have an email) within 30 seconds (`NotificationEmailJob`).

Push isn't built: it needs a development build and Firebase (`docs/phase-6-plan.md`).

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
- **Mobile number** (phase 6B) — the same, with `phone` (`Players.PhoneNumber`, E.164). Either
  links; an email and a number of two different people → 409. A linked place stays linked while its
  email or number still belongs to its person; clearing both unlinks it. The number is never copied
  from the account (a player's number is theirs to give); owners see it as `phone`.
- **Join code** — every team has a 6-character `Teams.JoinCode` (no 0/O, 1/I; made with the team,
  older teams got one in the migration). Owners see it on the team (`joinCode`) and can
  `TEAM_JOIN_CODE_RESET`. `TEAM_JOIN_PREVIEW { code }` → the team and its unclaimed names (places
  with no account and no waiting email or number), each with a `detail` ("#7 · Batter") to tell two Rahuls
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

`ACCOUNT_DELETE_PREVIEW` says what will happen and sends a code (purpose `DeleteAccount`, its own
wording, same limits as sign-in codes) to the email, or by text to an account with only a number;
`ACCOUNT_DELETE { otp }` does it:

| | |
|---|---|
| Tournaments only they own | deleted, for everyone in them (the preview names them and counts the others) |
| Ones they created with other owners | the longest-standing other owner becomes the creator |
| Every other membership | removed; any match they were scoring is released |
| Squad places | unlinked (names stay); places waiting for their email or number lose it |
| The account row | kept (foreign keys), `Name` "Deleted user", email and phone cleared, `DeletedOn` set |
| Sessions, codes, cricket profile | removed — every device is signed out at once |

Signing up again with the same email makes a new, empty account. Admin accounts can't be deleted
from the app (`canDelete` false on `ACCOUNT_GET`).

## The admin portal (phase 5)

A separate React app, `tournament-scheduler-admin/` (port 5174), on the same encrypted gateway
(channel `PORTAL`). Pages: Tournaments (Live / Upcoming / Past, paged, search, reopen), People
(search, block, change role, sign out everywhere, reset portal password), Roles (create, rename,
delete, the role editor) and Permissions (switch services off, "Update permissions" = the sync).
Each page shows only if the signed-in role may use its list service (`ADMIN_ME.services`).

**Signing in: two factors.** The emailed code (`AUTH_OTP_SEND`, as in the apps), then
`ADMIN_AUTH_SIGN_IN { email, otp, password?, newPassword? }`:

| Answer | Means |
|---|---|
| `NeedsNewPassword` | First portal sign-in: choose a password (10–128 characters) and send it with the same code. |
| `NeedsPassword` | Send the password with the same code (a typo doesn't cost a new email). |
| `SignedIn` | A **portal session** (`UserSessions.IsPortal`, 12 hours sliding, `Auth:PortalSessionHours`). |
| 403 "You don't have access to the admin portal." | The role has no portal service ticked (EndUser never does). |
| 429 "Too many wrong passwords…" | 5 wrong in a row lock it for 15 minutes (`Auth:PasswordMaxAttempts` / `PasswordLockMinutes`). |

Passwords are hashed with ASP.NET's `PasswordHasher` (salted PBKDF2), `Users.PasswordHash`.
**Admin-only services refuse any session that isn't a portal session** — 403 "Sign in to the admin
portal to do this." — so an admin signed in to the phone app with just a code can't use them; the
portal flag is set only by `ADMIN_AUTH_SIGN_IN`, never from the request's channel.

Forgot the password: another admin clears it (People → Reset password), or on the server
`dotnet run -- users reset-password <email>` (IIS: `dotnet TournamentScheduler.Api.dll users reset-password <email>`).
The next portal sign-in asks for a new one.

**Guards** (also enforced on the server, the portal just shows them):
- Anonymous and protected services can't be switched off. The permission and role screens are
  `[ProtectedService]` + `[AdminOnly]`: Admin can never lose them.
- The role editor saves the whole set. Boxes are locked when ticks mean nothing (anonymous,
  protected app services), Admin must keep its protected ones, and portal services can never be
  ticked for EndUser (400 if tried).
- Admin and EndUser can't be renamed or deleted; a role people still have can't be deleted.
- Nobody can block themselves or change their own role; System and deleted accounts can't be
  changed. Blocking signs the person out at once; resetting a password ends their portal sessions.
- Reopening: completed → Live, cancelled → Upcoming. If the end date has passed it would close again
  within the hour, so a new end date is required (the one-year limit doesn't apply).

Every change to permissions or ticks reloads the request check at once.

## Tables (game database)

`Users`, `Roles` (1 Admin, 2 EndUser — fixed ids the code relies on), `UserSessions`,
`OtpChallenges` (script `deploy/sql/01-accounts.sql`); `Permissions`, `RolePermissions` (script
`deploy/sql/02-permissions.sql`); `TournamentMembers` plus the new tournament and match columns
(script `deploy/sql/03-tournament-access.sql`); `UserCricketProfiles`, `Players.UserId/EmailId/TournamentId`,
`Teams.JoinCode` (script `deploy/sql/05-player-accounts.sql`); the portal password columns on `Users` and
`UserSessions.IsPortal` (script `deploy/sql/06-admin-portal.sql`); `Users.PhoneVerifiedAt` and
`Players.PhoneNumber` (script `deploy/sql/07-phone-identity.sql`); `TournamentInvites`
(script `deploy/sql/08-tournament-invites.sql`); `Notifications` (script `deploy/sql/09-notifications.sql`); `NotificationTemplates` and the notification email columns (script `deploy/sql/10-notification-templates.sql`). UserId 1 is **System**: no email, can't sign in, owns
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
},
"Sms": {
  "Provider": "None"               // "Log" to test numbers; a real provider once chosen
}
```

- **`"Sms": { "Provider": "Log" }`** (the default in Development) turns mobile numbers on in both
  apps and writes texted codes to the same log and `otp-codes.txt` as Log emails — the same warning
  applies. `None` (the default elsewhere) keeps numbers off.

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
code like anyone else; in the admin portal they also choose a password at their first sign-in
(see "The admin portal" above).
