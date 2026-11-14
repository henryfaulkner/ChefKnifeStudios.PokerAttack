SELECT current_database() AS database_name,
       current_user AS role_name,
       current_setting('transaction_read_only') AS read_only,
       current_setting('transaction_isolation') AS isolation,
       current_setting('TimeZone') AS time_zone;

SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema = 'PokerAttack' OR table_name = '__EFMigrationsHistory'
ORDER BY table_schema, table_name;

SELECT table_name, column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'PokerAttack'
ORDER BY table_name, ordinal_position;

SELECT 'Games' AS table_name, count(*) AS physical_rows,
       count(*) FILTER (WHERE NOT "IsDeleted") AS non_deleted_rows
FROM "PokerAttack"."Games"
UNION ALL
SELECT 'Rounds', count(*), count(*) FILTER (WHERE NOT "IsDeleted")
FROM "PokerAttack"."Rounds"
UNION ALL
SELECT 'RoundScores', count(*), count(*) FILTER (WHERE NOT "IsDeleted")
FROM "PokerAttack"."RoundScores"
ORDER BY table_name;
