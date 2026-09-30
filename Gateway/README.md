# Secure gateway

Every app request is encrypted, posted to one URL, decrypted here, run as the normal action, and
answered encrypted. Controllers, services and the `{ status, data }` envelope don't know it exists.

```
app                                                API
 │ POST /api/gateway                                │
 │ { requestHeader, requestBody.encryptedData } ──► │ GatewayMiddleware: decrypt, check, rewrite to the
 │                                                  │ action's own method + URL, run the normal pipeline
 │ { responseHeader, responseBody.encryptedData } ◄─│ capture the envelope, encrypt it
```

## What it protects, and what it doesn't

- **Only this server can read a request.** It is encrypted for the server's public key; the private
  key never leaves `C:\ProgramData\TournamentScheduler\keys`.
- **Only the app that sent a request can read its answer.** Each request carries a fresh one-time key.
- **Nothing can be altered, replayed, or moved to another service.** AES-GCM rejects any changed
  byte; request ids are single-use; timestamps must be within 5 minutes; the clear header must match
  the sealed copy.
- **It does not prove the caller is our app.** The public key ships inside the apps, so anyone can
  read it and build requests. Proving *who* is calling is sign-in's job (`sessionId` is reserved for
  it) or app attestation's. It also doesn't replace HTTPS: the phones use plain HTTP on the home
  Wi-Fi today, which is exactly where this layer matters.

## Request

`POST /api/gateway`

```json
{
  "requestHeader": {
    "serviceRequestId": "CRICKET_BALL_RECORD",
    "requestUUID": "3f0e9d8c-7b6a-4f5e-9d3c-2b1a0f9e8d7c",
    "timestamp": "2026-09-29T10:15:30.123Z",
    "journeyId": "b21e…",
    "sessionId": null,
    "channel": "MOBILE_ANDROID",
    "appVersion": "1.0.0",
    "deviceId": "random per install",
    "keyId": "20260929",
    "apiVersion": "1"
  },
  "requestBody": { "encryptedData": "<JWE compact>" }
}
```

| Field | Required | Meaning |
|---|---|---|
| `serviceRequestId` | yes | Which action — the `[ServiceRequestId]` on it. |
| `requestUUID` | yes | New for every request, retries included. Refused if seen before. Echoed as `X-Request-Id`. |
| `timestamp` | yes | ISO 8601 UTC. Refused if more than `Gateway:MaxClockSkewSeconds` (300) from the server clock. |
| `journeyId` | no | Groups one user flow's requests in the logs. Per app launch / page load today. |
| `sessionId` | no | Reserved for sign-in. `null`. |
| `channel`, `appVersion`, `deviceId` | no | For logs and support. `WEB`, `MOBILE_ANDROID`, `MOBILE_IOS`. |
| `keyId`, `apiVersion` | no | Informational; the key actually used is the JWE's `kid`. |

`encryptedData` decrypts to the action's input:

```json
{ "routeParams": { "matchId": 42 }, "query": { "sport": "Cricket" }, "body": { "runsOffBat": 4 } }
```

Route parameters are filled **by name** into the action's route template, so names are part of the
contract: the tournament is `id`, then `teamId`, `playerId`, `scheduleId`, `matchId`. `body` is
exactly what the action would receive on its own URL.

## Response

```json
{
  "responseHeader": { "serviceRequestId": "…", "requestUUID": "…", "journeyId": "…", "timestamp": "…" },
  "responseBody": { "encryptedData": "<JWE compact>" }
}
```

`encryptedData` decrypts to the usual envelope:
`{ "status": { "isSuccess", "message", "statusCode" }, "data" }`.
The HTTP status mirrors `statusCode`, as every response of this API does. A 201's `Location` header
is removed (it would name the real URL and new id in the clear).

**Refused before decryption** — malformed envelope, a key the server doesn't hold, a token that
fails its integrity check, no server key yet (503), wrong method (405), too large (413) — there is no
key to answer with, so the answer is the plain envelope with `data: null` and a message only.
Everything after decryption is encrypted, including: replay (409), clock skew (400), header mismatch
(400), unknown service (404), missing route parameter (400), and crashes (500).

## Encryption (JWE, RFC 7516/7518, compact serialisation)

**Request:** `{"alg":"ECDH-ES","enc":"A256GCM","kid":…,"epk":{P-256 JWK},"serviceRequestId":…,"requestUUID":…,"timestamp":…}`

1. The app makes a throwaway P-256 key pair and does ECDH with the server's public key.
2. The AES-256 key is Concat KDF (RFC 7518 §4.6.2): SHA-256 over
   `0x00000001 ‖ Z ‖ len‖"A256GCM" ‖ len‖apu ‖ len‖apv ‖ 256`.
3. AES-256-GCM, random 96-bit IV, the base64url protected header as associated data.
4. Compact: `header..iv.ciphertext.tag` (the encrypted-key part is empty for ECDH-ES).

**Response:** `{"alg":"dir","enc":"A256GCM","requestUUID":…}`, the same one-time key, a fresh IV.

The server checks the ephemeral key is on the curve, and accepts no `zip`, `crit`, or other algorithms.

| Where | Implementation |
|---|---|
| API | `GatewayCrypto.cs` — built-in .NET only (`ECDiffieHellman`, `AesGcm`) |
| Mobile | `src/api/gatewayCrypto.ts` — `@noble/curves`, `@noble/ciphers`, `@noble/hashes` (pure JS, runs in Expo Go) |
| Website | `src/api/gatewayCrypto.js` — same code as mobile |

All three are held together by `TournamentScheduler.Tests/Gateway/gateway-vectors.json`: fixed inputs
and the exact tokens they must produce. `GatewayVectorTests` checks the C#; `npm run verify:gateway`
checks each app. The request token was cross-checked against the independent `jose` library when
the file was made, and `joseRequestToken` was sealed by jose itself.

## Keys

```
dotnet run -- gateway-keys new       # make a key (becomes current); prints the apps' settings
dotnet run -- gateway-keys show      # print the current key's public half and the settings again
dotnet run -- gateway-keys ensure    # make one only if there is none (Setup-IisSite.ps1 uses this)
```

- Stored as `{keyId}.pem` (PKCS#8, P-256) in `Gateway:KeyDirectory`, default
  `C:\ProgramData\TournamentScheduler\keys`, readable by SYSTEM, Administrators, you, and the IIS app pool.
  `dotnet run` and IIS share it, so both answer to the same key.
- The apps hold the public half: `EXPO_PUBLIC_GATEWAY_KEY_ID` / `_PUBLIC_KEY` (mobile `.env.local`)
  and `VITE_GATEWAY_KEY_ID` / `_PUBLIC_KEY` (website `.env`). Public by design.
- Keys load at start-up; restart after adding or removing one (`Publish-IisSite.ps1` restarts).
- **Rotating:** `gateway-keys new` → restart the API → update both apps → once no phone uses the old
  key, delete its `.pem` → restart. Every key in the folder is accepted until then.
- **Lost or leaked private key:** make a new one, delete the old `.pem`, update the apps. Old
  captured traffic stays readable to whoever has the old key, so treat a leak seriously.

## Configuration (`Gateway` section)

| Setting | Default | |
|---|---|---|
| `EnforceEncryption` | `true` (`false` in Development, for Swagger) | Direct calls to non-skipped actions get 403. |
| `PlainServiceIds` | `[]` | Service ids allowed in the clear without a code change. |
| `KeyDirectory` | `%ProgramData%\TournamentScheduler\keys` | |
| `CurrentKeyId` | newest key | Pin which key the apps should use. |
| `MaxClockSkewSeconds` | `300` | Also bounds how long request ids are remembered. |
| `MaxRequestBytes` | `1048576` | |

## Adding or skipping a service

- **New action:** put `[ServiceRequestId("AREA_ACTION")]` on it, then add the id to
  `tournament-scheduler-mobile/src/api/services.ts` and/or `tournament-scheduler-ui/src/api/services.js`.
  Architecture tests fail if an action has no id, an id is duplicated or badly named, or an app
  calls an id the API doesn't have.
- **Skipping encryption:** `[SkipEncryption]` on the action (or controller), or its id under
  `Gateway:PlainServiceIds`. A skipped action is callable on its own URL *and* through the gateway.
  Only `HEALTH_CHECK` skips today — for the deploy scripts and uptime checks. The architecture test
  `OnlyReviewedServices_SkipEncryption` lists the allowed ones, so adding one is a deliberate change.

## Known limits

- The replay cache is in memory — right for one API instance; several would need a shared store.
- One ECDH per request (a few ms in pure JS on a phone). A per-session key could be negotiated later.
- The header is readable in transit (service id, device id, journey id); the payload and answer are not.
