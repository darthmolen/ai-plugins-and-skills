# Authentication

How `reminders-set-outlook` signs in to Microsoft 365, why it does it that way rather than
with On-Behalf-Of, and the one-time setup it needs.

## The short answer

**A public client, using delegated permissions, signed in interactively once and refreshed
silently from an OS-encrypted token cache after that.**

- The app asks for one delegated Microsoft Graph scope, `Tasks.ReadWrite`. It can touch your
  To Do tasks and nothing else — not mail, not calendar, not anybody else's tasks.
- On the first run (or on `reminders-set-outlook login`) your browser opens and you sign in.
  That is the prompt you expected at startup.
- MSAL keeps the refresh token in an encrypted cache (DPAPI on Windows, Keychain on macOS,
  libsecret on Linux). Every later run gets a new access token from it silently.
- You are only asked again when the refresh token stops working: you changed your password,
  an admin revoked sessions, a Conditional Access sign-in-frequency policy expired it, or the
  app did not run for about 90 days. When that happens a sync opens the browser again, or, if
  prompting is off, logs that you need to run `login`.

No secret, certificate or password is stored anywhere by this app.

## Why not On-Behalf-Of

OBO was a reasonable first thought — it *is* the flow for "act as the user" — but it solves a
different shape of problem.

OBO is for a **middle tier**. A client application signs the user in and calls *your web API*
with that token; your API, a *confidential client* holding a secret or certificate, then
exchanges the incoming token for a new one to call a downstream API (Graph) as that user. It
needs three parties: a front end, an API you host, and the downstream API.

This app has no middle tier. It is a process on your own machine that is itself the client
calling Graph. To use OBO you would have to build and host an API with a client secret purely so
the local app could hand it a token and have it hand one back — more moving parts, a secret to
protect, and the same delegated permission at the end. The flow for a local app acting as its
signed-in user is the one the delegated public-client flows exist for.

## The options that were considered

| Option | Verdict | Why |
|---|---|---|
| **Interactive browser + persistent cache** (auth code with PKCE) | **Chosen, default** | Works for work/school and personal Microsoft accounts, supports MFA and Conditional Access, no secret, prompts only when a human is actually needed. |
| Device code | Supported, `Auth:Mode=DeviceCode` | For a machine with no browser. Many tenants block device code flow with Conditional Access (Microsoft recommends blocking it), so it is the fallback, not the default. |
| On-Behalf-Of | Rejected | Needs a hosted middle-tier API holding a secret; see above. |
| Client credentials (app-only, `Tasks.ReadWrite.All`) | Rejected | Needs admin consent and a secret or certificate on disk, grants access to *every* user's tasks in the tenant, and does not work for personal Microsoft accounts. A far larger blast radius for writing one person's to-do list. |
| Azure CLI / `DefaultAzureCredential` | Rejected | The Azure CLI's own app registration is not pre-authorised for the To Do scopes, so you cannot get a `Tasks.ReadWrite` token from `az login`. |
| Windows WAM broker (`Azure.Identity.Broker`) | Future option | Signs in with the account you are already logged in to Windows with — no browser at all. Worth adding if the browser prompt ever becomes a nuisance; it needs a parent window handle, which a console app has to fetch. |

## One-time setup: register the app

Microsoft does not let an app ask for your tasks without an app registration that names it, so
you register one of your own. It takes about five minutes and costs nothing.

> **If you only have a personal Microsoft account** (outlook.com, hotmail.com, or a Gmail
> address registered as a Microsoft account): registering an app now needs a Microsoft Entra
> directory. Sign up for a free Azure account, which creates one; the app you register there
> can still sign in your personal account.

1. Go to the [Microsoft Entra admin center](https://entra.microsoft.com) → **Identity** →
   **Applications** → **App registrations** → **New registration**.
2. **Name:** `reminders-set-outlook`.
3. **Supported account types** — pick the one that matches the account whose To Do you want:

   | Your To Do lives in | Choose | Then set `Auth:TenantId` to |
   |---|---|---|
   | A work or school tenant | *Accounts in this organizational directory only* | that tenant's **Directory (tenant) ID** |
   | A personal Microsoft account | *Accounts in any organizational directory and personal Microsoft accounts* | `consumers` |
   | Either, depending on the day | *Accounts in any organizational directory and personal Microsoft accounts* | `common` |

4. **Redirect URI:** platform **Public client/native (mobile & desktop)**, value
   `http://localhost`. Click **Register**.
5. **Authentication** → **Advanced settings** → **Allow public client flows** → **Yes** →
   **Save**. (Needed for device code; harmless otherwise.)
6. **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated
   permissions** → tick **Tasks.ReadWrite** → **Add permissions**. `User.Read` can stay or go;
   the app does not use it.
   In a work tenant where users cannot consent for themselves, an administrator clicks
   **Grant admin consent**. For a personal account you consent at first sign-in.
7. From **Overview**, copy the **Application (client) ID**.

Put the two values in your user config file (see [running.md](running.md#configure) for where
that lives):

```json
{
  "Auth": {
    "ClientId": "<application (client) id>",
    "TenantId": "consumers"
  }
}
```

Neither value is a secret. A client ID identifies the app; it grants nothing on its own.

## What is stored, and where

| What | Where | Sensitive? |
|---|---|---|
| Refresh and access tokens | MSAL cache named `reminders-set-outlook`: DPAPI-encrypted under `%LOCALAPPDATA%\.IdentityService` on Windows; the login Keychain on macOS; libsecret (GNOME Keyring / KWallet) on Linux | **Yes** — encrypted to your OS user |
| Which account signed in (`AuthenticationRecord`: username, tenant, home account id) | `auth-record.json` in the state directory | No — it holds no token |
| Client and tenant id | your `appsettings.json` | No |

On a Linux machine with no secret service running, the cache cannot be encrypted and sign-in
fails rather than write tokens in the clear. `Auth:AllowUnencryptedTokenCache=true` allows a
plain file instead; only use it on a machine you alone use.

`reminders-set-outlook logout` forgets the account record so the next run signs in fresh. To
revoke the app entirely, remove it at [myapps.microsoft.com](https://myapps.microsoft.com)
(work) or [account.live.com/consent/Manage](https://account.live.com/consent/Manage)
(personal).

## Prompting at startup, and unattended runs

`Auth:AllowInteractive` decides what happens when a silent refresh is not possible:

- **`true` (default)** — the run opens the browser and waits for you. Pair this with running
  the app at log-on (see [running.md](running.md)) and you get exactly one prompt, at startup,
  on the rare days one is needed. `sync` and `watch` sign in before they scan, so the prompt is
  the first thing that happens rather than something that appears halfway through.
- **`false`** — the run never prompts. It fails with exit code 3 and a log line telling you to
  run `reminders-set-outlook login`. Use this when the app runs somewhere no one would see a
  browser window, such as a task set to *run whether the user is logged on or not*.
