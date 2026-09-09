# Code signing (fixing the SmartScreen warning)

Right now `WinSync.exe` and `WinSync-Setup.msi` are unsigned, so Windows
SmartScreen shows "Windows protected your PC" on first run. This isn't a bug
— an unsigned file from a small/new publisher always triggers it, no matter
how clean the code is. There are two real fixes:

1. **[SignPath.io](https://signpath.io/apply)'s free open-source signing
   program** — what this repo is set up for. Used by Git for Windows,
   Wireshark, and others. Free, but requires approval and some CI setup.
2. **Buy a code-signing certificate** ($70–400+/year from a CA like SSL.com
   or Certum). Since 2023 all certs — standard or EV — require the private
   key on a hardware token. EV gets instant SmartScreen trust; standard still
   needs downloads to accumulate before SmartScreen backs off.

This doc covers path 1, since that's free and this is an open-source project.

## 1. Apply

Go to <https://signpath.io/apply> and apply for the open-source program.
You'll need:

- Link to this repo (public, MIT-licensed — both required and already true)
- A short description of what WinSync does
- Confirmation you can build it reproducibly (you can: `dotnet publish`, see
  the README)

Approval isn't instant — expect some back-and-forth.

## 2. Set up your SignPath project

Once approved, in the SignPath dashboard:

1. Create an **Organization** (or use the one they set up for you) and note
   its **Organization ID**.
2. Create a **Project** — the workflow assumes the slug `winsync`. If you use
   a different slug, update `project-slug` in
   [`.github/workflows/release.yml`](.github/workflows/release.yml).
3. Create a **Signing Policy** for release builds — the workflow assumes the
   slug `release-signing`. Adjust `signing-policy-slug` if yours differs.
4. Create an **Artifact Configuration** describing what's inside the zip
   (`WinSync.exe` — Authenticode exe, `WinSync-Setup.msi` — Authenticode
   MSI). The workflow assumes the slug `winsync-artifacts`; adjust
   `artifact-configuration-slug` if yours differs.
5. Generate an **API token** for CI use.

## 3. Add repo secrets

In this repo: **Settings → Secrets and variables → Actions → New repository
secret**:

| Secret | Value |
|---|---|
| `SIGNPATH_API_TOKEN` | the API token from step 2.5 |
| `SIGNPATH_ORG_ID` | your Organization ID from step 2.1 |

## 4. That's it

[`.github/workflows/release.yml`](.github/workflows/release.yml) already
checks for `SIGNPATH_API_TOKEN` and only runs the signing job when it's
present — pushing a tag (`v1.0.1`, etc.) will build, sign, and attach the
signed exe + MSI to the GitHub Release automatically. Until the secrets
exist, the same workflow still builds and releases the **unsigned**
artifacts, so nothing breaks in the meantime.

Once signed builds are live for a while and enough people have run them
without incident, SmartScreen's reputation warning should stop appearing
even without EV — that's the "free" path's tradeoff versus buying an EV
certificate for instant trust.
