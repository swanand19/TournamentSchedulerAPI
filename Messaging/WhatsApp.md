# WhatsApp — codes and invites to mobile numbers

With `Sms:Provider` set to `WhatsApp`, everything the API sends to a **mobile number** goes through
Meta's WhatsApp Cloud API instead of a text message:

| What | Meta template (in use) | Category | Sent by |
|---|---|---|---|
| Every one-time code (sign-in / sign-up, adding a number, deleting the account) | `otp_template_kickcricket` (approved 2026-11-25) | Authentication | `Auth/Otp/WhatsAppOtpSender` |
| Invite to own or score a tournament, to a number with no account | `tournament_invite` (to submit) | Utility | `MessageDispatcher` |

Nothing else goes on WhatsApp: notifications stay in the inbox and email. Emails are unchanged.

Meta only lets a business start a conversation with a template it has **approved in advance**, so
the wording of these messages lives at Meta. **Which** template each one goes out as — its name,
language and which values fill `{{1}}`, `{{2}}`, … — is on the admin portal's Messages page, under
"Mobile number → WhatsApp" (the message's `NotificationTemplates` row, since 2026-11-26; it was in
appsettings before). That card's "Text wording" is used only with `Sms:Provider` `Log`. A message whose
WhatsApp template is empty isn't sent to Meta at all (an invite is still saved and works at sign-up).
Code: `Messaging/WhatsAppClient.cs` (one POST per message, RemoteLogs row with provider `WHATSAPP` — the
code is logged as `<code>`).

## Settings (the IIS site's own `appsettings.json` — edit by hand, never publish over it)

```json
"Sms": { "Provider": "WhatsApp" },
"WhatsApp": {
  "PhoneNumberId": "<the number's id from WhatsApp Manager>",
  "AccessToken": "<system user's permanent token>"
}
```

Optional, with their defaults: `ApiVersion` `v23.0`, `BaseUrl` `https://graph.facebook.com`, `TimeoutSeconds` `15`.
Restart the site (recycle the app pool) after a change. On Azure the same names with `__`
(`Sms__Provider`, `WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken` as a Key Vault reference to
`whatsapp-access-token`); on the laptop, `dotnet user-secrets`. The old `CodeTemplate`, `InviteTemplate`,
`InviteParameters` and `Language` settings are no longer read — the templates are on the portal.

With the provider on but `PhoneNumberId` / `AccessToken` empty, sending fails with "We couldn't send
the WhatsApp message just now" and the reason is in ErrorLogs.

## Setting up Meta (once)

1. **Meta Business account** at business.facebook.com (your business's legal name). Verification
   (Business settings → Security centre) needs a registration document (Udyam, GST, …). Until
   verified you can message about 250 people a day; that's fine for testing.
2. **developers.facebook.com → My apps → Create app → Business**, add the **WhatsApp** product. Meta
   gives you a **test number** and a temporary token (24 hours). The test number can send only to up
   to five numbers you add under *API Setup → To* (each confirmed with a code) — enough to try both
   messages before your own number is ready.
3. **The real number:** WhatsApp Manager → Phone numbers → Add. It must not be on WhatsApp already
   (a new SIM, or a landline that can take the verification call). Its **Phone number ID** goes in
   `PhoneNumberId`. Set the display name (Meta checks it matches the business).
4. **A permanent token:** Business settings → Users → System users → Add (Admin) → Assign assets
   (the app, and the WhatsApp account with full control) → Generate token with
   `whatsapp_business_messaging` and `whatsapp_business_management`, expiry *Never*. That token is
   `AccessToken`. Keep it only in the server's appsettings.
5. **Payment method** in WhatsApp Manager → Billing (codes are charged per message delivered).
6. **Submit the two templates below** (WhatsApp Manager → Message templates → Create). Approval
   usually takes minutes to a day. Then put each approved name and language on the portal's Messages
   page (Mobile number → WhatsApp) for the messages it serves.

## Template 1 — the codes (Authentication; in use: `otp_template_kickcricket`)

- Category **Authentication**, language **English** (`en`).
- Code delivery: **Copy code** (one-tap autofill needs a store build with the app's signing key).
- Tick **Add security recommendation** and **Add expiry time for the code: 5 minutes** (matches
  `Auth:OtpLifetimeSeconds` 300).
- Button text: `Copy code`.

Meta writes the body itself: "*123456* is your verification code. For your security, do not share
this code." and the footer "This code expires in 5 minutes." Nothing else to fill in. The same
template serves sign-in, adding a number and deleting the account (the portal's three code messages name
it; they can't be left without one). Set its **message validity period** to 5 minutes too, so a message
delivered late is dropped instead of arriving with a dead code.

## Template 2 — `tournament_invite` (Utility)

- Category **Utility**, language **English** (`en`). No header, no buttons.
- Body (its four variables; the portal maps them, see below):

  ```
  {{1}} has invited you to be {{2}} of {{3}} on KickCricket.

  Sign up in the app with this mobile number before {{4}} and you'll be added straight away.
  ```

- Samples Meta asks for: `{{1}}` Asha Patil, `{{2}}` a scorer, `{{3}}` Monsoon Cup 2026, `{{4}}` 6 Dec 2026.
- Once approved: the portal's Messages page → Tournament invite → Mobile number → WhatsApp: name
  `tournament_invite`, language `en`, values `InviterName`, `Role`, `TournamentName`, `ExpiresOn` (in the
  order of `{{1}}…{{4}}`). If Meta asks to change the wording, change it there; if the variables' order
  changes, change the values on the portal to match. Meta may move a utility template to *Marketing* if it reads as
  promotion (marketing costs more) — keep it to the invite itself.

## Things to know

- **An accepted send isn't a delivered one.** Meta answers at once; a number that isn't on WhatsApp
  (error 131026) or a blocked message is reported later to a webhook, which isn't built yet. Such a
  person just doesn't get the code; they can use an email instead. The apps say "it needs WhatsApp
  on this number".
- **Refusals Meta answers at once** (wrong template name 132001, bad token 190, unpaid 131042, …) are
  in RemoteLogs and ErrorLogs with Meta's own message; the person sees "We couldn't send the
  WhatsApp message just now", an owner's invite says "couldn't send it just now — try Send again".
- **Cost:** see Meta's current rate card for India (authentication and utility are charged per
  delivered message; check before switching invites on for many people).
- **Limits** still apply as for texts: 3 codes an hour per number, one a minute (`Auth` settings).
- To go back to codes in the log for testing, set `Sms:Provider` to `Log`.
