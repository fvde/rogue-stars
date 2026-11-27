# Run Quests: the recipe

A **Run Quest** is a rare character the worm meets on the board. Over about 30 meetings the character tells a story; every
meeting ends with a choice that shapes it. This file is the recipe for writing more of them. The first quest is
[Mothwick, the Lost Courier](mothwick.md) (`shared/RogueStars.Rules/Content/Quests/QuestMothwick.cs`).

Code: rules in `shared/RogueStars.Rules/Systems/Run/RunSystem.Quests.cs` (+ `RunQuestState.cs`), data classes in
`Content/RunQuestDefs.cs`, registry and shared tuning in `Content/ContentDb.RunQuests.cs`, client card in
`client/.../Screens/RunScreen.Quests.cs`, journal in `JournalScreen.cs`, texts in `QuestView.cs`.

## 1. The pieces

| Node type | Share | What the player sees | What happens |
|---|---|---|---|
| **Story** | ~50% | 2 (or 3) choices, no cost | Always moves on. Each option leads to a different next node (a new direction) or to the same node with a different journal line (a different tone). |
| **Battle** | ~20% | "Fight" / "Not now" | Won: moves on. Declined: the story **waits on this node** until the next meeting. **Lost: the run ends** like any lost battle (the normal defeat flow with its revive offer; a revive retries the battle and a win moves the story on); the story waits on this node. |
| **Sacrifice** | ~20% | "Pay" / "Not now", the price as a chip | Paid: moves on. Refused, or the run cannot pay: the story **waits on this node**. |
| **Boon** | ~10% | one "Thank you" | Moves on and grants a **permanent, account-wide** benefit. |

Ratios are per playthrough (every path from the first to the last chapter) and the content tests allow ±5 points.
A 30-chapter quest is exactly **15 story, 6 battles, 6 sacrifices, 3 boons**.

### Sacrifice kinds (`QuestCostKind`)
| Kind | Amount | Notes |
|---|---|---|
| `Coins` | 100–300 run coins, steps of 50 | A run earns ~550–800; 300 means saving up. The journal shows the waiting price so players can plan. |
| `LegendarySkill` | 1 | A **random** owned Legendary-tier run skill (never a signature): the player does not pick. Drawn from the run's random stream (deterministic). The chip says "A random Legendary skill"; the result line names the skill taken ("Lost skill: …"). No Legendary owned: the price cannot be paid and the story waits. |
| `MythicSkill` | 1 | Same for Mythic tier (Mythics come from minibosses: until the run has one, the story waits). |
| `CurrentHpPct` | 50–80%, steps of 10 | Of the worm's **current** HP; never below 1. |
| `AllRunSkills` | – | The drastic one: every run skill but the signatures. Needs at least one skill to give. Use once, late. |

### Battles (`QuestBattleDef`)
"Difficult" = above the level's elites, scaled to wherever the player meets the character:

> leader = the level's miniboss at the run's board strength (threat, danger, `BoardEnemyPct`, +`EnemyPctPerRoll` per roll)
> **+ `BattleBasePct` (40%) + the node's `BonusPct`** HP/ATK, plus `Escorts` elites of the level at elite strength (+`EliteBonusPct`).

An elite fight is one elite at +20% and one ordinary enemy; a quest battle is roughly 2× that at the start and 3.5× at
the end (Mothwick: bonus 0 / 5 / 10 / 15 / 20 / 30%, escorts 1 / 1 / 1 / 1 / 2 / 2). Wins pay the fight's normal XP and
coins and the usual 15% heal.

### Boons (`QuestBoonDef`)
Every boon is the same kind of thing for one quest: **+1 run coin on every run-coin gain** (fights, caches, pickups, event
coins, mini-game coins, skill coin triggers; costs never). Three boons per quest → +3 after the chain, shown as its own line
("+3 Starglint") wherever coin gains are shown and in the journal. Give the bonus a name in the quest's world
(`RunQuestDef.BonusName`). Later quests may use other boon kinds; add a field to `QuestBoonDef` and one place in
`RunSystem.QuestChoose` that applies it.

## 2. Graph shape

* **Chapters are layers.** Every node has a `Chapter`; every option goes to a node of the next chapter. The last
  chapter's options have `Next = null` (the end). So every path is exactly `Chapters` meetings long and the chip
  "Chapter 7" is always true.
* **Never show the total.** The player sees "Chapter N" only (encounter card, result lines, journal: "Reached chapter N"
  and the story so far). No "/ 30", no progress bar toward a total, no "chapters left": the story's length is not spoiled.
  The total lives in the data and this review material only.
* **At most two variants per chapter** (`m05a`, `m05b`). Variants exist only after a story node (it chooses the
  direction); a variant may carry on for a few chapters (`m16a → m17a → m18a`) and then **re-merges** into one node.
  This keeps content at ~40 nodes for a 30-chapter story (Mothwick: 42 nodes, 128 different playthroughs).
* **Variants of a chapter share the node type** (both battles, both prices), so every path has the same ratios. Prices
  may differ between variants (Mothwick ch. 10: 50% HP or 150 coins; ch. 18: 300 coins or 80% HP).
* **Pacing rules** (tested): start with a story node; end with a boon; never two battles or two prices in a row.
  Mothwick's rhythm: `S S B S X S B G | S X S B S X S S B X G | S S X B S S S B S X G`
  (S story, B battle, X sacrifice, G boon). Boons at about chapters 8, 19 and 30: early enough to motivate.
* **Ramp:** battles get harder (`BonusPct`, escorts), prices get bigger, the drastic price (`AllRunSkills`) comes last.

## 3. Meeting rate and placement

* **No unlock:** part of the game from the start (no level gate, no feature entry, no intro). Only the tutorial run (main
  level 1, its own small tile pool) and the Burrow (short floors) never have it; normal, Overcharged and Dark Side runs
  count. Prices the run cannot pay yet (no Mythic before the first miniboss) simply make the story wait; before minibosses
  exist on a level the battle's leader is the level's first elite.
* **About one run in five, plain chance** (`QuestTuning.ChancePct` = 20%): an independent roll per eligible run. **No
  pity**: no guaranteed meeting after X runs, no "not two in a row" rule; back-to-back meetings and long droughts both
  happen (mean 5 runs between meetings).
* **One meeting per run at most:** decided at run start (from a fork of the run's random stream, so nothing else about
  the run changes), then one tile of the second board segment's **main path** becomes the visitor tile (marker: a star
  lantern; it forces a stop like a shop, so no fork bypasses it).
* **Several quests:** the meeting goes to the first unfinished chain (`RunQuestDef.Order`); when all are finished, to the
  chain whose epilogue was seen least.

## 4. The epilogue (standard part of every quest)

After the last chapter the character keeps visiting at the same rate with a **purely positive** scene: no battle, no
price, one "Thank you!" button and a small reward, in rotation (`RunQuestDef.Epilogues`, 3–6 scenes). Rewards are a nice
surprise, never a power spike:

| Kind | Size | Mothwick |
|---|---|---|
| `Coins` | 20–100 run coins, steps of 10 (the coin bonus applies) | Tea Break, 80 |
| `HealPct` | 10–40% HP, steps of 5 | A Warm Glow, 30% |
| `SkillPick` | one free level-up draft (1 of 3) | Special Delivery |
| `MaxHpPct` | 5–10% max HP for the run (a shop perk) | Star Biscuit, 10% |

Epilogues never add account power.

## 5. Writing voice

* **World:** cute cartoon space-worm world, light humour, a little mysterious. Short sentences, warm jokes, no cynicism,
  no real-world references, no violence beyond cartoon scuffles.
* **The character** has one clear look (that the art prompt can use literally), one verbal tic or vocabulary (Mothwick:
  postal jargon, "Dear me!") and a personal stake in the story.
* **Arc in three acts:** meet the character and the goal (chapters 1–8), the obstacle and a rival with a reason
  (9–19), the reveal and the finale (20–30). Hide one small mystery about the worm or the planet and pay it off late.
* **Node text:** 60–300 characters (tested), present tense, the character speaks in “curly quotes”. It is read on a phone
  in a few seconds: 2–3 sentences.
* **Option text:** 3–26 characters (tested), an action in the player's voice ("Pay the toll", "Fight the guard").
  "Not now" for every wait.
* **Journal line:** past tense, first person as the worm ("I paid 100 coins at the star bridge."). One line per chapter;
  the journal reads as the story so far.
* **Battles** describe the foe vaguely ("something big and rude"): the actual enemy is the level's miniboss.
* **Round numbers only** in every price and reward (tested).

## 6. How to author a new quest

1. Copy `Content/Quests/QuestMothwick.cs` to `Content/Quests/Quest<Name>.cs`; new `Id`, character, epithet, portrait key
   `quest_<id>`, bonus name, `Order` (after the existing ones), `Chapters`, start node; fill `Look`, `Voice`, `Premise`.
2. Plan the rhythm (section 2) on paper: the type of each chapter, where variants split and re-merge.
3. Write the nodes with the `QuestBuilder` helpers: `Story(id, chapter, title, text, Go(option, next, journal)...)`,
   `Battle(id, chapter, title, text, bonusPct, escorts, fightText, next, journal)`,
   `Sacrifice(id, chapter, title, text, QuestBuilder.Coins(100) / HpPct(50) / Legendary() / Mythic() / AllSkills(), payText, next, journal)`,
   `Boon(id, chapter, title, text, 1, acceptText, next, journal)`, and 3–6 `Epilogue(...)` scenes.
4. Register it in `ContentDb.RunQuests.cs` (`BuildRunQuests`: `AddRunQuest(QuestName.Build())`).
5. Art: add `tools/art/packages/quest-<id>.json` (copy `quest-mothwick.json`: one subject `quest_<id>`, single creature,
   full body, facing left, plain white background, no ground / shadow / pedestal, 2 seeds; the prompt says only what the
   `Look` says). The accepted sprite goes to `Art/Run/Characters/quest_<id>`; until then the client draws a placeholder
   (add a case to `ScreenArt.DrawCharacter` for a nicer one).
6. Run the Rules tests with `ROGUESTARS_WRITE_DOCS=1` to write the review doc `docs/design/run-quests/<id>.md`, then without it.
7. Run the simulator: `dotnet run --project tools/RogueStars.Sim -c Release -- --run-quests --days 365 --seeds 5`
   (`docs/balance/run-quests.md`).

## 7. Checklist (most of it is a test in `RunQuestContentTests`)

- [ ] Every node reachable from the start; no dead ends; every option goes to the next chapter; only the last chapter ends.
- [ ] Every path is `Chapters` long (25–35) with 50 / 20 / 20 / 10% ±5 story / battle / sacrifice / boon.
- [ ] Every path ends with the same total boon (the bonus the review doc promises).
- [ ] Starts with a story node, ends with a boon, no two battles or two prices in a row.
- [ ] Story nodes: ≥2 options, all advance. Battle: one Fight + one Wait. Sacrifice: one Pay + one Wait. Boon: advance only.
- [ ] All five sacrifice kinds appear at least once; coin prices 100–300 in 50s; HP 50–80% in 10s; battle bonus in 5s.
- [ ] Every advancing option has a journal line; texts within the length limits.
- [ ] 3–6 epilogue scenes, positive and small (section 4).
- [ ] Requirements resolvable from the start: every main level from 2 and every Dark Side level has a leader for the
      battles (miniboss, or an elite before minibosses); unpayable prices only make the story wait.
- [ ] No player-facing text shows the chapter total ("Chapter N" only).
- [ ] Portrait art package added; review doc generated; simulator run noted.
- [ ] A server deploy ships it (the rules are shared): no save migration needed, all state is new fields.

## 8. What to expect (simulator, Mothwick)

About 45 meetings (30 chapters + retries) from the first day (no level gate, plain 20%): **~340 runs (median; 160–430)**
for an active free player (~12 weeks, 23–134 days), ~210–230 runs (~10 days) for a hardcore player, and more than a
year for most casual players. Lost quest battles now end the run: an active free player loses more than half of its first
tries (38 of 68 over five seeds), a hardcore one a third; coin prices are the most common reason to wait (bots spend their
coins in shops). Details: `docs/balance/run-quests.md`.
