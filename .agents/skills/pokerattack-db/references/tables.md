# PokerAttack storage model

Source paths are relative to the repository root. Recheck live columns and migration state before interpreting a different release.

| Table | Meaning and columns |
| --- | --- |
| `"PokerAttack"."Games"` | Persisted multiplayer starts. `Id` integer PK; `ClientId` game/session string; `HostPlayerClientId` host identity. Created by `LobbyService.StartGameAsync`. No completion flag or durable active-session indicator. |
| `"PokerAttack"."Rounds"` | A persisted multiplayer round boundary. `Id` integer PK; `GameId` integer FK to Games. No explicit persisted round number: do not label `Id` as the game's round ordinal. |
| `"PokerAttack"."RoundScores"` | Player score captured at a saved round boundary. `Id` integer PK; `RoundId` FK; `ClientUserId`, `ClientUserDisplayName`, `Score`. Identifiers/names may be personal data; aggregate by default. |

All three inherit `BaseEntity`: `Id`, `IsDeleted`, `CreatedOnUtc`, nullable `ModifiedOnUtc`. `AppDbContext` supplies audit values on save. EF configurations apply `!IsDeleted` global query filters. SQL must apply equivalent filters explicitly for a report of non-deleted data, including parent tables.

`GameService.EndRoundAsync` persists one Round with a RoundScore for each active player. Missing in-memory player state currently yields a saved score of zero. Thus a zero score alone does not prove a player completed a zero-scoring round, and total scores are not a complete event history. Determine scoring/reset semantics from current GameService/GamePlayer code before deriving per-hand or cross-round measures.

`Lobby`, `ActiveGame`, `GamePlayer`, game states/modes and `SoloGame` use singleton `IKeyValueRepository<T>` implementations. `SoloGameplayService` does not write these three relational tables. Reboots/redeployments lose in-memory sessions; historical relational rows cannot restore them. Counts of Games/Rounds do not give active player counts, wins or completed sessions without additional evidence.

## Migration boundary

PokerAttack's checked-in migrations include `20251008022524_Init_Data`, `20251008234907_NoUsers`, `20251015221621_Update_Game_HostPlayerClientId`. Discover `__EFMigrationsHistory` before querying it: the shared production database uses `public.__EFMigrationsHistory`, containing ExpenseTracker rows too. Match PokerAttack migration IDs; unrelated rows are not pending PokerAttack changes. A fresh pilot database may have different history contents.

Do not change shared history ownership/location or run migrations while answering a database query. The existing table/schema owner may be `postgres`; ownership and runtime least privilege are different facts.

## Source of truth

- `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/AppDbContext.cs`
- `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Models/{BaseEntity,Game,Round,RoundScore}.cs`
- `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Configuration/`
- `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Migrations/`
- `src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Specifications/`
- `src/Server/ChefKnifeStudios.PokerAttack.Server.BL/Services/{LobbyService,GameService,SoloGameplayService}.cs`
- `src/Server/ChefKnifeStudios.PokerAttack.Server.Infrastructure/InMemoryKeyValueRepository.cs`
