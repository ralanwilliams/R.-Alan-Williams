# ADR 0004: Remote editor access: a Cloudflare Tunnel behind Cloudflare Access

- **Status:** Accepted
- **Date:** 2026-10-08
- **Implementation:** `src/Cv.Editor/Security` (`EditorSecurityMiddleware`, `AccessTokenValidator`), `scripts/Start-Editor.ps1`, `scripts/Register-EditorTask.ps1`, `scripts/Show-EditorTray.ps1`, `tests/Cv.Editor.Tests/RemoteAccessTests.cs`

## Context

The editor runs on the author's computer and answers only to that computer ([ADR 0002](0002-cv-editor.md) §1–2). It has no login: being unreachable is its security. [ADR 0003](0003-public-cv-downloads.md) left hosting it as a follow-up, so that it can publish from anywhere.

The constraints:

- **Occasional use.** The CV changes a couple of times a month.
- **No running costs, and no bill from an attack** (as in ADR 0003). Ideally no payment method at all.
- **From any browser,** including a phone or a borrowed laptop, with no software to install there.
- **The editor needs Chromium** to print PDFs on publish (ADR 0003 §2), so it can't run as a Worker or Pages Function.
- **No new password to manage,** as ADR 0001 asked: an identity provider or proxy, not a password table.

## Decision

### 1. The editor stays on the author's computer; a Cloudflare Tunnel reaches it

`cloudflared` runs on the author's computer and holds an outbound connection to Cloudflare. Requests to **`editor.ralanwilliams.com`** travel down that connection to `http://localhost:5180`. Nothing listens on the internet: no port forward, no public IP, and Kestrel still binds to loopback only. The tunnel is free.

The cost is availability. The editor is reachable only while that computer is on, awake and online (§4).

### 2. Cloudflare Access decides who gets through

An Access application covers `editor.ralanwilliams.com` with one policy: allow the author's email. Cloudflare's edge signs the author in (a one-time code by email, or an identity provider such as Google or GitHub) before any request reaches the tunnel. Zero Trust's free plan covers up to 50 users. No password is stored anywhere in this project.

### 3. The editor verifies the Access token itself

Tunnel traffic arrives from `cloudflared` on the same computer, so ADR 0002's "loopback only" no longer proves a request came from the author. The editor therefore treats any request that came through Cloudflare as remote: one for the public host, or one carrying Cloudflare's headers (`Cf-Ray`, `Cf-Connecting-IP`, `Cf-Visitor`, `Cf-Access-Jwt-Assertion`). Such a request must carry a valid **`Cf-Access-Jwt-Assertion`**:

- an RS256 token signed by one of the team's keys (`https://{team}.cloudflareaccess.com/cdn-cgi/access/certs`, cached and fetched again when a token names an unknown key, since Access rotates keys every 6 weeks);
- issued by `https://{team}.cloudflareaccess.com`, for this application's AUD tag, and not expired;
- for an `email` equal to the author's `cv.users` row.

Access is the gate. This check means that a misconfigured or deleted Access application, or a second tunnel route to the same port, still yields nothing. When remote access isn't configured, any request through Cloudflare is refused outright.

The other ADR 0002 defences adapt rather than go away:

- **Host filtering** also admits the public host, and only when remote access is configured.
- **CSRF:** the required `Origin` is `https://editor.ralanwilliams.com` for remote requests, so a local origin doesn't count through the tunnel.
- **HSTS:** remote responses add `Strict-Transport-Security`.
- **Local use** is unchanged.

Remote access is off by default. It turns on only when all three of `CV_EDITOR_PUBLIC_HOST`, `CV_ACCESS_TEAM_DOMAIN` and `CV_ACCESS_AUD` are set, and the editor refuses to start with only some of them.

### 4. It comes back by itself after a restart

- `cloudflared` runs as a Windows service.
- The editor runs from a scheduled task that starts at boot, before anyone logs in (`scripts/Register-EditorTask.ps1`). It runs `scripts/Start-Editor.ps1`, which loads `.env`, runs the editor in Production mode (no developer error pages through the tunnel) and starts it again whenever it exits.
- The computer must not sleep while plugged in.

The author can still turn the editor off when it isn't needed, from a tray icon (`scripts/Show-EditorTray.ps1`). The task is registered by an administrator, so the author's own account can only read it, and the editor runs in the task's background session. The icon therefore doesn't control either. It creates or deletes a flag file (`%LOCALAPPDATA%\cv-editor\off`), and `Start-Editor.ps1` stops the editor while the file exists and starts it when it's gone. The task keeps running either way, and off stays off after a restart. Granting the account the right to start and stop the task would have worked too, but stopping the task leaves the editor process running in the background session, out of the icon's reach.

A crash or update reboot therefore needs no action. A hung or powered-off computer can't be fixed remotely, and that is accepted (§5).

### 5. No remote reboot or remote desktop

Remote Desktop through the same tunnel could restart a misbehaving computer, but a leaked or phished login would then expose the whole computer: files, browser sessions, and `.env` with the database owner's password. That is much more than the CV editor exposes, whose `cv_api` role can only append. For a tool used a couple of times a month, a hung computer waiting until the author is home is the better trade. If remote desktop is ever added, it gets its own Access application with a stricter policy (MFA, short sessions), never the editor's.

## Consequences

**Positive**

- The author can edit and publish from any browser, at no cost.
- Nothing listens on the internet, and only Cloudflare-authenticated traffic for one email reaches the editor. The editor checks that itself.
- No new secret leaves the computer. The Access AUD tag and team domain are not secrets.
- Local use, and every existing defence, is unchanged.

**Negative / costs**

- **Availability depends on one home computer** being on, awake and online. Away from home, a crash that doesn't reboot cleanly means no editing until return.
- The computer must not sleep while plugged in, which uses more power.
- The editor's security now also rests on Cloudflare Access and the author's email account. Someone who controls that inbox (one-time codes) can edit the CV. An identity provider with MFA narrows this.
- Two more moving parts on the computer: the `cloudflared` service and the scheduled task.
- **Not tested end to end** until it is configured in Cloudflare. The token checks are tested with keys the tests create, not Cloudflare's.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Cloudflare Containers (editor and Chromium in a container) | Always available, but needs the Workers Paid plan ($5 a month), a payment method, and billing past its included usage. More build and deploy work for a tool used a couple of times a month. Remains the upgrade path if availability starts to matter. |
| Router port forward plus a login in the editor | Puts a listener on the internet and a password table in the editor: exactly what ADR 0001 and ADR 0002 avoid. |
| Tailscale or another private network | Free and secure, but every device needs the app and to join the network, so not "any browser". |
| Remote Desktop through the tunnel | See §5: full-computer exposure for a CV editor. |
| Trust Access alone, without checking the token | One misconfiguration (a deleted application, an extra route) would expose an editor with no login. The check is cheap. |

## Follow-ups

- If availability matters more later: move the editor into a container (Cloudflare Containers or another host) behind the same Access application. The token check carries over unchanged.
