# WP04 client recovery and isolated tests — 2026-11-14

Source: `b6a4d17d8eb42e9f9bee8214021406ff8be22659` plus uncommitted migration branch changes. No publication, production cleanup or deployment.

## Implemented behavior

`SignalRNotificationService` exposes connection state. Initial failure or any lost transport requires a fresh page/session. Reconnected transport does not establish surviving server game state or group membership: gameplay invocations remain blocked until reload. Player IDs are URL encoded. MainLayout makes old content inert and offers **Reload and return home**, including while automatic reconnect is running.

Both client API URLs remain on ACA in source and ordinary Release output. `deploy/prepare-client-artifact.mjs` creates a new pilot copy, changes both API entries in base/environment settings, regenerates gzip/Brotli settings, and selects the `Pilot` SWA environment. It refuses production API targets, existing output and overlap with source. No deploy operation exists in the tool. This app has no service worker manifest to rewrite.

`tests/helpers/targets.ts` requires explicit `CLIENT_URL` and `API_URL_CLEANUP`. Optional `API_PUBLIC_URL` allows a private SSH cleanup tunnel while browsers use public pilot HTTPS. Known production clients and all API targets except loopback/dedicated HTTPS pilot are refused. Cleanup checks the actually served base configuration, `Blazor-Environment` header and JSON environment override before POST. Blazor array overrides merge by index. The live `prod` override's HTML fallback is ignored. Redirects are never followed for these verification/cleanup requests.

TLS validation stays enabled. `ALLOW_LOCAL_INSECURE_TLS=true` is accepted only outside CI with all three origins on loopback. The global Node TLS bypass is refused. Old Azure E2E execution is disabled; the GitHub build workflow runs guards and fails on test errors. WP09 must wire the full pilot suite as a deployment prerequisite.

## Verification

| Check | Result |
| --- | --- |
| WebAPI.Tests Release | **28 passed, 0 failed, 0 skipped**: 26 hosting checks and 2 client recovery checks |
| Real SignalR recovery fixture | Private ephemeral Kestrel hub: connection established, initial hand delivered, transport aborted, automatic reconnect observed, stale hand blocked; initial failure also offers recovery |
| `npm --prefix tests run test:guards` | **21 passed**; includes local HTTP fixtures proving zero cleanup POST for base/override production settings and no POST redirect following |
| `node --test deploy/prepare-client-artifact.test.mjs` | **2 passed**; source preservation, both URLs/all overrides, compressed contents, unsafe targets/output refusal |
| Client Release publish | Passed to ignored `artifacts/client-wp04`; existing compiler/Razor and dependency advisories remain visible |

Subsequent WP05 local fixture: 3 actual browser smoke tests passed for multiplayer transition, solo hand and rendered recovery/reload; see [WP05 evidence](LINODE_WP05_EVIDENCE.md). Full multiplayer/solo game and public proxy acceptance remain WP07. The recovery service test uses a real transport; it does not claim rendered UI acceptance or complete gameplay. The local full-app disposable fixture will use the WP05 container/bundle, rather than the live shared database. Browser control failures from the earlier monitoring investigation are not counted as passing UI checks.

## Pilot artifact recipe

From repository root in PowerShell:

```powershell
dotnet publish src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp -c Release -o artifacts/client-wp04
node deploy/prepare-client-artifact.mjs --source artifacts/client-wp04/wwwroot --output artifacts/client-pilot --api https://pilot-api.playpokerattack.com
py -m http.server 5173 --bind 127.0.0.1 --directory artifacts/client-pilot
```

Use a fresh output name if it already exists. The HTTP server is local only; keep it running during pilot tests. WP06 must include `http://localhost:5173` in the pilot's exact CORS allowlist. Do not publish the pilot artifact to production SWA. Local static serving has no SWA environment header, so `Production` is used; every copied environment setting is rewritten consistently.

After WP06/07 starts the isolated pilot, establish a separate SSH tunnel:

```powershell
ssh -N -L 18082:127.0.0.1:8082 -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes -i "$env:USERPROFILE/.ssh/id_ed25519_linode_us_southeast" root@170.187.142.115
```

In the test terminal:

```powershell
$env:CLIENT_URL = 'http://localhost:5173'
$env:API_URL_CLEANUP = 'http://127.0.0.1:18082'
$env:API_PUBLIC_URL = 'https://pilot-api.playpokerattack.com'
npm --prefix tests test
```

The proxy must deny public `/test/*`; cleanup intentionally travels through the tunnel. The isolated Staging server must explicitly enable test endpoints and use `pokerattack_pilot`. CI must keep the test command's nonzero exit code; no continue-on-error.

## Maintenance and restoration

`deploy/maintenance/` is a prepared standalone SWA artifact (index plus navigation fallback, no-store). At WP08, publish it through the controlled client release path, verify apex/www/default hostname show it, then restart the backend. This step ends active games. After verified cutover publish the reviewed client artifact with **both** URLs switched. On failure restore the saved production ZIP documented in WP01; it restores both URLs to ACA. Verify the served `prod` header and base/override URLs after either publication. Do not change DNS to compensate for a client artifact mismatch. No maintenance artifact has been published.
