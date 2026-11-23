# Engineering conventions

## Layout
| Path | What |
|---|---|
| `shared/RogueStars.Rules/` | All game logic and content. netstandard2.1, **C# 9**, no external packages. Imported by Unity as a local UPM package and referenced by the server. |
| `shared/RogueStars.Rules.Tests/` | xUnit tests for the rules (net10.0). One folder per workstream. |
| `server/` | ASP.NET Core (.NET 10) authoritative server + tests. |
| `client/` | Unity 6.6 project (Android). Placeholder UI built in code. |
| `infra/terraform/` | GCP infrastructure. |
| `docs/design/GDD.md` | Binding game design spec. |
| `tools/RogueStars.Sim/` | Meta-progression simulator (bots play the real rules for 2 years; report in `docs/balance/`). Guard test in `tools/RogueStars.Sim.Tests/`. |

Build/test: `dotnet build RogueStars.slnx` and `dotnet test RogueStars.slnx` from the repo root.
On this machine use the native SDK: `arch -arm64 ~/.dotnet/dotnet …`.

## Rules library constraints (Unity compatibility)
- C# 9 only: no file-scoped namespaces, no global usings, no records, no `init`, no required members,
  no collection expressions, no raw string literals. `LangVersion` 9 is enforced by the build.
- netstandard2.1 APIs only. No System.Text.Json, no LINQ-heavy hot paths in combat (LINQ is allowed elsewhere).
- Determinism: never use `System.Random`, `DateTime.Now`, `Guid.NewGuid` or dictionary iteration order for
  outcomes. Use `ctx.Rng` (persistent player stream) or `Rng.Fork(purpose)`; time comes from `ctx.NowMs`.
- State classes: `[Serializable]`, public fields, parameterless constructors, collections initialised.
  Dictionary keys must be strings or enums. No polymorphic fields (use a `Kind` string/enum instead).
- Content (all numbers) lives in `ContentDb` partials; systems read content, never hard-code tuning.
- Rogue Stars names only. Never use names, text or numbers from the reference game.

## How a system is structured
For a system `Foo` owned by workstream `W`:
- `Systems/W/FooSystem.cs` — `public static class FooSystem` with `Register(GameEngine e)` that registers
  command handlers (`e.Register<FooCommand>(Handle)`), settlers, new-player initialisers, loadout
  contributors and reward granters. Pure static helper functions for queries live here too.
- `Systems/W/FooState.cs` — `public sealed partial class GameState { public FooState Foo = new FooState(); }`
  plus the state classes.
- `Systems/W/FooCommands.cs` — command classes (`public sealed class FooDoThing : GameCommand { public string X; }`).
- `Content/ContentDb.Foo.cs` — `public sealed partial class ContentDb { public Dictionary<string, FooDef> Foos …; partial void BuildFoo() { … } }`.
  Use the `BuildXxx` partial method assigned to your system (see ContentDb.cs).
- Tests in `shared/RogueStars.Rules.Tests/W/`.

Handlers validate first (throw `RuleException` via `GameEngine.Require`) and only then mutate state.
Grant rewards through `ctx.Grant(...)` so every reward kind goes through its owner's granter.

## Integration points between workstreams
- **Feature gates:** `Features.IsUnlocked(state, content, Features.X)` (owned by Meta). Reads recorded unlocks only: the
  onboarding settler applies the gate (main level AND account day) and the one-group-per-day limit (GDD §16). Tests that
  need a feature without playing the days record it (`state.Onboarding.Unlocked[id] = t`) or set `Features.DevUnlockAllFlag`.
  `Features.Next` = the single next unlock group for the hub teaser.
- **Rewards:** `RewardKinds` + granters (`Part`/`Rune` → Progression, `Blueprint`/`Alien` → City,
  `HeroShards`/`Mount` → Progression).
- **Combat loadout:** `engine.BuildLoadout(state, now)`; Progression contributes worm stats, parts, hero, mount;
  City contributes aliens squad and building boons; Run contributes laboratory draft pool.
- **Battle simulation:** `BattleSimulator` (owned by Run) is used by runs, arena and invasions.
- **Modifiers:** `state.Modifiers` + `ModifierMath.Multiplier(...)` for production, run rewards, regen.
- **Counters:** `state.Count("key")` for quests/achievements (Meta reads them). Use snake_case keys and
  document new keys in `docs/design/counters.md` (append-only).
- **Power score:** computed once, in the final loadout step registered last in `SystemsRegistry` (sees aliens, boons, lab).
- **Regen bonuses:** `EconomySystem.RegisterRegenBonusProvider` (City boons, monthly pass, talents). Economy is the only
  settler of `RegenRules` (stamina, arena tickets, burrow keys); each currency has exactly one rule.
- **Daily/weekly resets:** `Core/GameClock` (content `Resets`); never divide `NowMs` by a day yourself.
- **Ads:** every ad placement is consumed through `ShopSystem.ConsumeAdView` (daily cap, token, No Ads = instant).
- **Feature gates in commands:** `Features.Require(state, content, Features.X)` ("feature locked").
- **Onboarding items:** `ContentDb.Onboarding.StarterKit` (new player) and `FeatureDef.UnlockGrants` (on unlock).
- **Random item drops:** `Reward.Id = "*"` + `Rarity` → resolved by the owning granter (`Systems/RewardWildcard.cs`).
- **Portal progress:** Run keeps `main_level_max` (global main level number) on first clears; gates read it.
- **Timers:** long actions (construction, breeding, laboratory research) use City's `TimerSystem` so speed-ups apply.
Only edit files outside your workstream folder when this list says so; if you must touch a core file,
keep the change minimal and mention it in your summary.
