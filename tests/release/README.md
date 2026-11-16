# Release verification

This suite is separate from the destructive isolated E2E suite. Production mode
accepts only the three fixed SWA origins and the fixed public API. Isolated mode
requires explicit localhost HTTPS ports. TLS verification is always enabled;
an isolated CA must be trusted by the disposable browser/Node environment rather
than bypassing certificate errors. No global setup, cleanup, operator/test routes,
screenshots, traces, videos, HAR or credential logging are permitted.

Run socket-free checks with `node --test tests/release/*.test.cjs`. The protected
entry point is `run.cjs`; `POKERATTACK_RELEASE_CONTEXT` names a mode-0600 file in
the private runner temporary directory, never a checked-in file. It contains the
run-bound credential/players, immutable server manifest and accepted client
identity/settings summary. `POKERATTACK_RELEASE_RESULT` receives private output
only after completed passing assertions and normal owned cleanup. There are no
CLI flags that turn missing gameplay into success or refresh evidence time.

The protected job must require exit zero, import the generated evidence, revoke
this verifier through its separate host capability, and run `revoked.cjs` before
requesting promotion. Failed execution never authorizes reopening or an automatic
rollback. Do not upload the context, browser state or raw provider diagnostics.

`deploy/linode/check-release-gameplay.py` is the separate disposable HTTPS
rehearsal harness. Its synthetic metadata is not production evidence. Actual
deployment integration/CI/platform rehearsal remains H; local implementation does
not authorize production sessions or constitute a live release.
