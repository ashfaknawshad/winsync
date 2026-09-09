# winsync.ashfaknawshad.dev

Static site for WinSync — no build step, no framework. `index.html`,
`downloads.html`, one shared `styles.css`. The downloads page fetches
[GitHub Releases](https://github.com/ashfaknawshad/winsync/releases) live via
the public API, so new releases show up automatically with no site rebuild.

## Deploy on Vercel

1. In the Vercel dashboard: **New Project** → import `ashfaknawshad/winsync`.
2. Set **Root Directory** to `website`.
3. Framework preset: **Other** (it's static — no build command, no output
   directory override needed).
4. Add the domain `winsync.ashfaknawshad.dev` under Project → Settings →
   Domains, and point a CNAME for that subdomain at the target Vercel gives
   you.

`vercel.json` in this folder just turns on clean URLs (`/downloads` instead
of `/downloads.html`).

## Local preview

```
cd website
python -m http.server 8000
```

Then open `http://localhost:8000`.
