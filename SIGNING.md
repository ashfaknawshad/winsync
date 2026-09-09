# Code signing (fixing the SmartScreen warning)

Right now `WinSync.exe` and `WinSync-Setup.msi` are unsigned, so Windows
SmartScreen shows "Windows protected your PC" on first run. This isn't a bug
— an unsigned file from a small/new publisher always triggers it, no matter
how clean the code is.

## Current status

Applied to **[SignPath.io](https://signpath.io/apply)'s free open-source
Foundation program** (September 2026) — the plan this repo's CI is set up
for. **Rejected on first application**: SignPath's Foundation program
requires external signals of public trust (GitHub stars/forks/contributors,
independent articles or discussions, sustained activity) before they'll issue
a certificate in their name, and this project was too new at the time to
show any of that.

Not a rejection of the code — purely a "not enough public track record yet"
call. The plan is to reapply once the project has some real usage/visibility
behind it (see the ⭐ on the [website](https://winsync.ashfaknawshad.dev) and
repo — stars and forks directly help this).

## The options

1. **Reapply to SignPath's free Foundation program later** — free, and this
   is the path being pursued. `.github/workflows/release.yml` is already
   wired for it (see below); it just needs the secrets once approved.
2. **SignPath's paid subscription** — starts at $500/year (Starter plan).
   Not worth it for a project this size; ruled out for now.
3. **Buy a code-signing certificate directly** ($70–400+/year from a CA like
   SSL.com or Certum). Since 2023 all certs — standard or EV — require the
   private key on a hardware token. EV gets instant SmartScreen trust;
   standard still needs downloads to accumulate before SmartScreen backs off.
   Cheaper than SignPath's paid tier if this becomes urgent before reapplying
   makes sense.

For now: ship unsigned, point people at the
["Run anyway" guide](https://winsync.ashfaknawshad.dev/downloads#smartscreen)
on the site, and revisit once there's a real trail to point SignPath at.

## Once SignPath approval happens

In the SignPath dashboard:

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

Then add two repo secrets — **Settings → Secrets and variables → Actions →
New repository secret**:

| Secret | Value |
|---|---|
| `SIGNPATH_API_TOKEN` | the API token from step 5 |
| `SIGNPATH_ORG_ID` | your Organization ID from step 1 |

[`.github/workflows/release.yml`](.github/workflows/release.yml) already
checks for `SIGNPATH_API_TOKEN` and only runs the signing job when it's
present — pushing a tag (`v1.0.1`, etc.) will build, sign, and attach the
signed exe + MSI to the GitHub Release automatically. Until the secrets
exist, the same workflow still builds and releases the **unsigned**
artifacts, so nothing breaks in the meantime.
