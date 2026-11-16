# WP06 operational preparation — 2026-11-14

Status: core pilot prerequisites verified and isolated pilot installed. Shared-credential coordination remains a production gate; WP07 acceptance is still open. No production cutover.

## C01 shared recovery snapshot

- Protected VM snapshot `/var/backups/pokerattack-migration-20261114T154128Z`, approximately 133 MiB, individually consistent dumps of all four application databases plus globals, PostgreSQL/SSH/shared-app configuration, current container definitions and Caddy certificate/config volume contents. Capture began 15:41 UTC. DB dumps use separate transactions; no cross-database consistency claim.
- Off-host `C:\Users\hfaul\PokerAttack-Backups\pokerattack-migration-20261114T154128Z`, access restricted to current user and SYSTEM. Eight manifest files transferred with matching SHA256 and encrypted using CurrentUser DPAPI; decrypted roundtrip hashes matched before temporary plaintext removal. Recovery instructions and aggregate restore report retained beside ciphertext. Recovery depends on this Windows profile/DPAPI material; it is not portable password encryption. Protected source originals remain on VM.
- Actual isolated restore recreated every source role (only skipped CREATE for already initialized postgres), original ownership and ACLs, then all four databases with pg_restore exit-on-error. Compared every restored table against row counts parsed from dump COPY streams without printing row values. 41 tables / 3,953,618 rows verified. Duration **39.6 seconds**, below the 4-hour default. Fresh snapshot meets the 24-hour age target at capture; recurring backup evidence appears below.

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

Core pilot prerequisites C01-C04 are verified below. Consumer-aware replacement of the legacy shared credential remains a production gate. Production client URLs remain ACA; no production schema migration is indicated.

## C03 publication and installed tooling

API source `250b03496c69e33a206e294cbad861a94fa6844a`, published off-host with matching OCI revision label and UID 1654:

- API: `chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi@sha256:693d3d6a193810000f7ba2c74e51fc14dee641806ebd2c5266bcc471f7154588`.
- Bundle: `chefknife.azurecr.io/chefknifestudios.pokerattack.server.data@sha256:3a59a2a868a77c77ab12e4fe144f2e6766eb4b859f31748dc41bc97389d0810e`.
- Tag on both: full source SHA. Local build wrappers reported stderr/progress handling errors in PowerShell, but Docker logs completed exports; independent image inspection verified labels/user and registry digest lookups plus actual VM pulls verified publication.
- Dedicated ACR token/scope map `pokerattack-linode-pull`, exactly content/read and metadata/read on these two repositories, no write permissions. VM pull succeeds; unrelated TransitJazz manifest access explicitly denied. Password generated with 365-day expiry; rotate before 2027-11-14. Root-only Docker config `/etc/pokerattack/registry/config.json` mode 0600. Workstation's temporary publisher auth was removed after pushes.
- Root-owned tooling installed under `/opt/pokerattack` (compose 0644, validator 0755), commands `/usr/local/sbin/pokerattack-deploy` and `pokerattack-ssh-deploy`; protected `/etc/pokerattack` mode 0700. No dedicated CI SSH key/sudo grant enabled yet (WP09).
- GitHub first run 43612182389 failed because ubuntu-latest selected 26.04 unsupported by locked Playwright. Pinning ubuntu-24.04 in e640e85 fixed it: run 43612643584 passed. Client component-name fix 7bde504 and stronger hand smoke passed run 43613065004. No automated production deployment exists.

## C04 scoped roles and compatibility

Created fresh, non-superuser/non-inheriting/non-createdb/non-createrole/non-replication/non-bypassrls roles `pokerattack_runtime`, `pokerattack_pilot_runtime`, `pokerattack_pilot_migrator`, each with a new generated credential. No existing password was changed. Separate database `pokerattack_pilot` is owned by its migrator; PUBLIC database privileges revoked there. Production runtime has only PokerAttack schema usage/current-table DML/current-sequence usage and shared history SELECT. It cannot create objects in public/PokerAttack or read/write ExpenseTracker tables. Pilot roles cannot read/write production PokerAttack tables.

Protected config identities:

- Pilot `/etc/pokerattack/pilot/configs/986c914b8081aff576edb6cc588762449b869306029885bc1551f5bdaa56bb81.env`.
- Staged production `/etc/pokerattack/production/configs/7c830479833d8b9a8ed119a75a73069fd70c1ce510f67c060cfc365f9936355f.env`.
- Pilot-only migrator `/etc/pokerattack/pilot/migrator.env`; production gets no migration credential.

Bundle ran only against pilot, at 0.25 CPU/512 MiB with read-only root/private tmp. It applied all three PokerAttack migrations. Both runtime roles authenticated over loopback TCP with their actual passwords; schema column reads and all three migration IDs verified. Pilot successfully inserted a game, round and score in a rolled-back transaction. Production validation was read-only. Existing 10-row shared EF history is intact; **no production migration required** for this release. No history relocation/clearing or unrelated-schema change.

Production timer values inventoried from ACA: round 90000, shop 20000, elimination 10000, power-selection 20000 milliseconds; staged config preserves them. Disposable pilot uses 10000/5000/3000/10000 for practical gameplay tests. New secrets are only in protected host files, never source/evidence.

The legacy ACA rollback connection still works unchanged. Updating ACA environment would restart a live game process, so replacing that legacy connection and rotating the shared postgres password must be coordinated with WP08 maintenance and the identified ExpenseTracker/TransitJazz consumers. This remaining incident is explicitly open; supplying new scoped PokerAttack credentials does not claim shared-password rotation is finished. Production cutover stays gated on that coordination. Recurring off-host backup/alert delivery also remains WP07 work.

## Recurring shared backups

Installed root-owned `pokerattack-backup` and systemd service/timer. Twice-daily 03:00/15:00 UTC capture (up to five minutes jitter), seven-day retention, all four shared application databases plus globals, host configuration and protected PokerAttack release state. Pilot database is disposable and excluded. Backup and deploy commands share the host operation lock. Completed capture `20261114T162424Z` exited zero; incomplete captures never become latest.

`Sync-PokerAttackBackup.ps1` transferred that completed capture, verified seven SHA256 manifest entries, encrypted each using CurrentUser DPAPI and verified decryption hashes before removing local plaintext. Protected directory: `C:\Users\hfaul\PokerAttack-Backups\recurring\20261114T162424Z`. Owner Windows Scheduled Task `PokerAttack-OffHostBackup` runs the protected script hourly and at logon; actual first task run returned **LastTaskResult 0**. Source script copy is under the private backup scripts directory, ACL restricted to owner and SYSTEM.

The workstation task uses the current interactive user and runs while logged on. A powered-off/logged-out workstation cannot provide continuous off-host transfers; host snapshots continue independently. This is not proof of uninterrupted 24-hour off-host RPO or external freshness alert delivery. DPAPI recovery needs the same Windows profile/key material. The earlier full isolated restore remains the measured restore proof; this later encrypted capture includes the newly provisioned roles/configuration and has transfer/decryption hash proof.
