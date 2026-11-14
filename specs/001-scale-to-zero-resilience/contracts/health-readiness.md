# Contract: Production Readiness Probe

**Step 7** (Story 3, P3). Satisfies FR-019, supports FR-018 and FR-020.

---

## The defect

`ServiceDefaults/Extensions.cs:109–118`:

```csharp
public static WebApplication MapDefaultEndpoints(this WebApplication app)
{
    // Adding health checks endpoints to applications in non-development environments has security implications.
    if (app.Environment.IsDevelopment())
    {
        // /health and /alive mapped here — development only
    }
```

**Production has no readiness endpoint.** Container Apps cannot tell when the app is ready to serve and falls back to slower heuristics, which inflates perceived cold start — the thing Story 3 exists to minimize. The default template comment is correct that exposing full health detail publicly has security implications; the fix is to expose a minimal endpoint, not to keep exposing nothing.

---

## Endpoint

```
GET /alive
```

| Property | Value |
|---|---|
| Response | `200 OK`, empty or `"Healthy"` body |
| Auth | Anonymous (the platform probe cannot authenticate) |
| Detail | **None.** No dependency names, versions, or check results — that is the security concern the template comment refers to. |
| Cost | No database round-trip. Liveness only — "the process is up and serving." |
| Environment | All environments, including Production |

`/health` (the full check set, including database) stays development-only. The distinction matters: a readiness probe that touches Postgres would report unready during a transient database blip and cause the platform to cycle a container that is otherwise fine.

---

## Platform wiring

Configure the Container App to probe `/alive`, so scale-from-zero can route traffic the moment the app is genuinely ready rather than after a heuristic delay.

---

## Warm-on-intent (Step 9)

The same endpoint is the warm target. The client fires `GET /alive` on evident intent to play — opening the lobby screen, or hovering Play — before the user commits:

- Fire-and-forget; ignore the response.
- Never block UI on it.
- Debounce so hover does not produce a burst.

For multiplayer this is especially effective: the host warms the app while waiting for others to join, so the game start never eats the cold start.

**Explicitly not a keep-warm pinger.** Warming happens on real user intent only. A scheduled pinger would recreate exactly the idle cost this feature removes (research R7) and would violate FR-015.

---

## Startup path trimming

Also in Step 7, `Program.cs:104–118` maps OpenAPI and Scalar unconditionally. Both are documentation UI and do not belong in the production hot start path — gate them behind a non-production check.

---

## Tests

**Component (Tier 1)** — cheap host-configuration tests that directly catch the `IsDevelopment()` regression:
- `/alive` returns 200 when the environment is Production.
- `/alive` requires no authentication.
- `/alive` response body exposes no dependency detail.
- `/health` is **not** mapped in Production.
- OpenAPI/Scalar routes are not mapped in Production.

**Performance (Tier 3)** — not asserted in CI:
- Cold start to first successful `/alive`, measured before and after ReadyToRun. Gates Step 8 (see the plan's Risks).
