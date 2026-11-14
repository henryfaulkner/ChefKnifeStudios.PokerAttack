# Connection and VM access

## Target selection

Production uses existing shared Linode IPv4 `170.187.142.115`, PostgreSQL port 5432, database `expensetrackerdb`, schema `PokerAttack`. `expensetrackerdb_dev` is development. An isolated pilot database/role must be explicitly selected and verified; its existence is not implied by this skill. Preserve every other schema and database.

`Program.cs` reads the named `PokerAttackDB` connection using normal ASP.NET configuration. `Server.Data/ServiceCollectionExtensions.cs` registers `AppDbContext` with Npgsql. The checked-in `AppDbContextFactory.cs` reads only its working directory's appsettings file according to build configuration; it does not currently add environment-variable configuration. Do not assume runtime and design-time resolution are identical.

Development appsettings and historical cheat sheets may contain exposed credentials. Do not copy them into outputs, commands, new files or image layers. Use an already configured current connection or existing authorized deployment access. The migration worksheet records current access findings and shared-credential consumers; recheck before operations.

## Local PostgreSQL client

Requires PowerShell 5.1+ and an installed `psql` supporting `\getenv` (PostgreSQL 15+). The original helper comes from TransitJazz, adapted to `PokerAttackDB`, with an added database-identity check.

```powershell
# Uses existing ConnectionStrings__PokerAttackDB without echoing it.
& ./.agents/skills/pokerattack-db/scripts/query.ps1 -Sql 'SELECT current_database(), current_user;'

# An explicitly selected development connection requires matching expected DB.
& ./.agents/skills/pokerattack-db/scripts/query.ps1 `
  -AppSettingsPath '<selected-appsettings-file>' `
  -ExpectedDatabase expensetrackerdb_dev `
  -Sql 'SELECT current_database(), current_user;'
```

An explicit appsettings path takes precedence over the connection environment variable. Otherwise the helper uses `ConnectionStrings__PokerAttackDB`, or a caller-selected `-ConnectionEnvironmentVariable`; absent these, it accepts existing libpq `PGSERVICE` or `PGHOST`/`PGDATABASE`/`PGUSER` settings. `-ExpectedDatabase` defaults to `expensetrackerdb` and must match the actual connection before user SQL runs.

Connection strings are parsed with explicit .NET `set_ConnectionString`, `get_Keys` and `get_Item` methods. PowerShell dictionary adaptation can treat property assignment as a dictionary key; do not use a metadata dump or `$builder.ConnectionString = ...` as a redaction method.

Passwords travel only through temporary process `PGPASSWORD`, not command arguments/files. The helper restores overridden environment variables and output encoding in `finally`. It preserves explicitly configured SSL modes/certificate paths; an absent SSL policy is not evidence of verified TLS. Complex multi-host/client-certificate setups should use the caller's approved libpq service rather than guessed translations. Parameter values use temporary environment variables and psql literal quoting. Reviewed SQL only: the helper rejects psql command lines, but does not authorize arbitrary SQL.

When current ACA settings must be inspected under an authorized inventory, select `poker-attack-rg` / `poker-attack-api`, capture the response in process memory, and read only `ConnectionStrings__PokerAttackDB`. Inspect whether it is a direct setting or a secret reference before resolving it. Never print unfiltered resource JSON, secret lists or environment settings. The existing runtime uses direct settings; revalidate that fact.

## VM route without a database password

Defaults were independently verified in the migration inventory:

| Item | Value |
| --- | --- |
| VM IPv4 | `170.187.142.115` |
| VM provisioning metadata | Akamai/Linode instance `79395147`, region `us-southeast`; recheck narrow cloud-init fields or provider metadata when needed |
| Reference provider label / OS hostname | `expense-tracker-vm` / `localhost` |
| SSH user / identity | `root` / `~/.ssh/id_ed25519_linode_us_southeast` |
| Trusted ED25519 fingerprint | `SHA256:bV3vsdj3SnjD64ATPEpUNxjoXPrr0eQTut0R1rgDRu4` |
| Native PostgreSQL | `/var/lib/postgresql/16/main`, version must be rechecked |

```powershell
& ./.agents/skills/pokerattack-db/scripts/query-vm.ps1 `
  -SqlFile ./.agents/skills/pokerattack-db/references/inventory.sql
```

The SSH helper requires BatchMode, IdentitiesOnly and StrictHostKeyChecking, then invokes `sudo -n -u postgres psql` over the host's Unix socket. SQL travels on stdin. It does not change SSH trust, firewall rules, HBA, passwords or services. Local socket access does not prove application password authentication or transport TLS; verify those separately using the selected app identity when relevant.

The local `postgres` role is privileged. Its transaction is read-only, but this is not an authorization sandbox; run only reviewed SELECT/catalog queries, never side-effecting functions or transaction-control statements. Do not infer that a runtime/pilot role has the same privileges. Current shared-password rotation must account for ExpenseTracker and TransitJazz consumers, not only PokerAttack.

Both routes use UTC, a 10-second connection timeout, a configurable statement timeout (default 30 seconds), database identity checks and a single rolled-back Repeatable Read read-only snapshot. A failure or partial result is not a complete report. On connection/authentication errors, preserve existing access controls and report the failing layer rather than changing credentials or permissions.
