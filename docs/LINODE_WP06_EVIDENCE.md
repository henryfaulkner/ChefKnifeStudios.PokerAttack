# WP06 operational preparation — 2026-11-14

Status: IN PROGRESS. Fresh backup/restore and host compatibility verified; no app installation, role creation, registry publication, proxy reload or production cutover yet.

## C01 shared recovery snapshot

- Protected VM snapshot `/var/backups/pokerattack-migration-20261114T154128Z`, approximately 133 MiB, individually consistent dumps of all four application databases plus globals, PostgreSQL/SSH/shared-app configuration, current container definitions and Caddy certificate/config volume contents. Capture began 15:41 UTC. DB dumps use separate transactions; no cross-database consistency claim.
- Off-host `C:\Users\hfaul\PokerAttack-Backups\pokerattack-migration-20261114T154128Z`, access restricted to current user and SYSTEM. Eight manifest files transferred with matching SHA256 and encrypted using CurrentUser DPAPI; decrypted roundtrip hashes matched before temporary plaintext removal. Recovery instructions and aggregate restore report retained beside ciphertext. Recovery depends on this Windows profile/DPAPI material; it is not portable password encryption. Protected source originals remain on VM.
- Actual isolated restore recreated every source role (only skipped CREATE for already initialized postgres), original ownership and ACLs, then all four databases with pg_restore exit-on-error. Compared every restored table against row counts parsed from dump COPY streams without printing row values. 41 tables / 3,953,618 rows verified. Duration **39.6 seconds**, below the 4-hour default. Fresh snapshot meets the 24-hour age target at capture; ongoing off-host automation is still pending.

| Restored database | Tables | Verified rows |
| --- | ---: | ---: |
| expensetrackerdb | 13 | 63,511 |
| expensetrackerdb_dev | 13 | 2,772 |
| personal_health_hub | 8 | 1,283 |
| transitjazz | 7 | 3,886,052 |

PokerAttack totals: Games 178, Rounds 17,701, RoundScores 44,655; shared EF history 10. ExpenseTracker and other databases were restored and counted too. PokerAttack owner remains postgres in the restored source, matching inventory. No live restore occurred.

Restore engine: PostgreSQL **16.16**, source previously verified **16.15**. Available pgvector image restored vector **0.8.7**, source **0.6.0**. This proves logical restore on a newer compatible major-16/extension setup, not binary recovery on the exact original extension build. No live PostgreSQL/extension upgrade is proposed. `restore-shared-snapshot.py` is the reviewable reproduction; decrypt backups to a protected directory first. Config archives are preserved for explicit operator recovery; live config replacement/reboot recovery remains WP07.

## C02 existing host

Read-only verification after snapshot: Docker server 29.9.3, Compose 5.7.1. Docker, PostgreSQL and timesync services active/enabled; UTC/NTP synchronized. Ports 8081/8082 remain free. PostgreSQL accepts connections. Existing TransitJazz server/proxy running, zero restarts/OOM; current immutable images match prior inventory.

Owner explicitly confirms LISH. Host INPUT policy ACCEPT and UFW previously inactive. No Linode API credential/CLI was discovered, so provider firewall attachment remains unverified; owner believes none. Planned work adds loopback services and Caddy hosts on already used 80/443, with **no port/firewall changes**. This uncertainty does not require modifying shared networking; public pilot HTTPS and loopback inaccessibility must still pass WP07. No host package upgrade, resize, reboot or shared-service restart is needed for compatibility.

## Remaining WP06

C03: commit the tested source, build/publish traceable digest off-host, establish a dedicated pull-only registry token and install protected app tooling. C04: isolated pilot DB/migrator/runtime roles, scoped production runtime role, migration rehearsal/production comparison, consumer-aware shared-credential replacement and recurring backups. Production client URLs remain ACA. No production schema migration is currently indicated.
