# WP07 public pilot evidence - 2026-11-14

Status: IN PROGRESS. Public isolated pilot is installed; production remains ACA. No pilot acceptance, production cutover or 30-minute observation completion is claimed here.

## Installed identity and proxy

Backend source `250b03496c69e33a206e294cbad861a94fa6844a`, API digest `sha256:693d3d6a193810000f7ba2c74e51fc14dee641806ebd2c5266bcc471f7154588`, configuration `986c914b8081aff576edb6cc588762449b869306029885bc1551f5bdaa56bb81`. Real controlled deploy started pilot at **15:59:46Z**, Staging, isolated `pokerattack_pilot`, loopback 8082, 0.25 CPU / 512 MiB. Candidate remains pending external verification until the promotion command succeeds.

Owner manually created M01 and M02 A records to 170.187.142.115, TTL 300; authoritative/public resolution verified. Additive pilot site loaded into existing Caddy at **16:00:25Z**, preserving all TransitJazz sites and certificate volumes. Saved pre-change Caddy file is protected on host. No shared proxy stop, firewall change or production API process was introduced.

Public pilot HTTPS validates normally (TLS 1.3, Let's Encrypt, expiry 2027-02-12); live/ready 200. Public `/_ops/status`, `/test/cleanup`, mixed-case and encoded test paths return 404. CORS allows exactly localhost:5173; unapproved origin gets no allow-origin header. Private readiness/session checks use SSH tunnel only. TransitJazz readiness 200 and actual MessagePack hub JoinCityV2(atlanta) returned replay after proxy reload.

## Gameplay and corrective work

Four independent browsers completed **three consecutive multiplayer games**, with real winner/loser results, exactly one winner each, and result dialogs closed before the next game. `artifacts/public-pilot-three-games.log` records the passing run (3.5 minutes). Read-only pilot database verification found nine rounds and 27 round scores for these three games, at 16:21:01-16:23:12Z. Production tables were not used.

Initial failures exposed stale Razor multiplayer component tags and browser startup before hub connection. Component tags were fixed in `7bde504`; startup interaction gating and lobby snapshot handling are subsequent client changes. An intermittent missing lobby was traced to fetching before the hub connected, plus potential stale GET overwrite of broadcasts. Client now fetches after connection and replays lobby notifications received during the snapshot request, with upserts to avoid duplicates. A dedicated real-browser regression deliberately delays both connection and a fetched snapshot while another browser creates a lobby.

Private message checks observe actual WebSocket notification frames in two independent players. Each hand must deliver HandPlayed and CardsDealt only to its actor. A subsequent group notification with a unique ID fences prior messages on both connections, avoiding a guessed wait. The earlier public private-delivery run passed; final corrected run evidence is appended after completion. Solo verification plays a card, ends the round, continues from the normal scoreboard and checks the final game-over result. Network failure requires reload and creates a fresh playable session.

Earlier failed runs remain failed evidence (`public-pilot-final-smoke.log`); the solo test incorrectly skipped the scoreboard Continue step and has been corrected. No timeout increase or ignored assertion substitutes for a result.

## Monitoring and outstanding acceptance

Read-only Grafana existing node dashboard queries over **15:48-16:18Z**, one-minute step, returned 31 samples: node up=1 throughout, CPU 4.0-14.6%, memory 42.586-48.745%. Private query expressions/results in `artifacts/linode-migration/pilot-monitoring-1548-1618`. These are whole-host measurements, not application latency/soak proof. At 16:05 pilot memory was 79.75 MiB with zero restarts/OOM.

Owner approved replacing the planned 24 hours with **30 minutes after gameplay and rollback checks pass**, retaining shared-database backups. This window has not started. Live controlled rollback/failure/interruption checks, remaining gameplay edge cases, measured runtime observation, external alert delivery and coordinated reboot recovery remain open. Mock release failure checks are not presented as live recovery evidence. Shared postgres credential replacement still requires consumer-aware maintenance before production cutover; no other application's credential or process has been changed.

## Corrected public browser verification

`artifacts/public-pilot-corrected-smoke.log`: **5/5 passed, 56.4 seconds**, normal public HTTPS browser API/SignalR with private SSH cleanup only. Includes the deterministic delayed-connection/stale-snapshot regression, multiplayer private frame fencing, solo scoreboard-to-game-over result, initial action gating, and failed-connection reload recovery. Release client publish passed with existing NU1903 SignalR advisory retained. Backend digest is unchanged; client fixes are recorded in the commit containing this evidence. This establishes a gameplay baseline for controlled recovery rehearsal, not completion of all D02 edge cases.
