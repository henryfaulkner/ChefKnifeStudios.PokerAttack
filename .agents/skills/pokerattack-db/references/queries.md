# Read-only query recipes

Run `inventory.sql` first when verifying a target. It reports identity, snapshot settings, schema/history location, live columns and physical versus non-deleted counts, without player data. The authenticated client helper defaults to production `expensetrackerdb`; explicitly pass the expected database for a development/pilot target.

## Latest persisted games

```sql
SELECT "Id", "CreatedOnUtc"
FROM "PokerAttack"."Games"
WHERE NOT "IsDeleted"
ORDER BY "CreatedOnUtc" DESC, "Id" DESC
LIMIT 20;
```

These are starts, not completed or active games. Do not expose the host/player strings merely to show timestamps.

## Persistence over a finite UTC window

Use the client helper's `-Parameters @{ from_utc = '<ISO UTC start>'; to_utc = '<ISO UTC end>' }`. The helper binds named parameters as quoted literals, including strings containing quotes. The VM helper intentionally accepts fully reviewed SQL without parameter substitution.

```sql
SELECT count(DISTINCT g."Id") AS games_with_saved_rounds,
       count(DISTINCT r."Id") AS saved_rounds,
       count(s."Id") AS saved_player_scores
FROM "PokerAttack"."Rounds" r
JOIN "PokerAttack"."Games" g ON g."Id" = r."GameId" AND NOT g."IsDeleted"
LEFT JOIN "PokerAttack"."RoundScores" s ON s."RoundId" = r."Id" AND NOT s."IsDeleted"
WHERE NOT r."IsDeleted"
  AND r."CreatedOnUtc" >= :from_utc::timestamptz
  AND r."CreatedOnUtc" < :to_utc::timestamptz;
```

The window belongs to the saved round's timestamp. A joined score creates another row, so use DISTINCT for games/rounds. Score count is a count of persisted player-round score records, not unique people or complete games. Zero rows means no retained matching records, not proof of no gameplay. State the finite UTC window, deletion filters and display limits with the result.

## History discovery

```sql
SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_name = '__EFMigrationsHistory'
ORDER BY table_schema;
```

Only after discovering `public` on the selected target:

```sql
SELECT "MigrationId", "ProductVersion"
FROM public."__EFMigrationsHistory"
ORDER BY "MigrationId";
```

Retain ExpenseTracker entries and identify PokerAttack's IDs using current source. A query request never authorizes applying a migration or destructive test cleanup.
