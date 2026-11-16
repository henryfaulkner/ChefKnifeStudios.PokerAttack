# Validation ownership

`migration-ci.yml` remains enabled until L01 proves the published workflows.
These are source definitions, not evidence that CI has run them on main.

| Retained migration validation | New owner |
| --- | --- |
| Business logic tests, API build, WebAPI disposable PostgreSQL tests | Server / isolated |
| Isolated target guards | Client / checks and Server / isolated |
| Pilot artifact preparation | Client / checks and Server / isolated |
| Isolated client publish for browser fixture | Server / isolated; never SWA publication |
| API and migration image builds | Server / isolated; exact built image inputs to both container harnesses |
| Release script recovery tests | Server / isolated; all host tests in socket-free root Linux |
| Disposable API/container/browser smoke | Server / isolated; original smoke plus HTTPS release gameplay and client verification |
| Client artifact upload | Client / package stores exact production archive; Server / isolated stores tested images and sanitized fixture results |

Both workflows run change detection without trigger path filters and report stable
`Client validation` and `Server validation` checks, including documentation-only
success. PR/fork jobs receive no production secrets, environments or Azure login.
Production delivery requires the original repository, main, push/dispatch,
successful validation, affected source and an explicitly true rollout variable.
The default-off variable remains off until H/I/K prove bootstrap prerequisites.

Linux HTTPS fixture trust uses a one-day local CA, only on a disposable hosted
runner, imported under a unique nickname in Chromium's NSS store and removed on
exit. Existing system roots are untouched. Windows uses an already trusted dev
certificate. Neither path disables TLS validation. NSS selection follows
[Chromium's certificate management](https://chromium.googlesource.com/chromium/src/+/main/docs/linux/cert_management.md).
