using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>What a landing popup shows and how it moves (the run screen picks the particles by kind).</summary>
    public enum PopupKind { Text, Coins, Heal, Hurt, Stat, Xp, LevelUp, Fight, Event, Boss, Loot, Shard }

    /// <summary>One floating feedback line after a landing ("+6% ATK", "+25 coins", "FIGHT!").</summary>
    public struct LandingPopup
    {
        public string Text;
        public Color Color;
        public PopupKind Kind;
        public LandingPopup(string text, Color color, PopupKind kind) { Text = text; Color = color; Kind = kind; }
        public override string ToString() => Text;
    }

    /// <summary>The worm's numbers for the run HUD, with every run modification applied.</summary>
    public struct RunHudStats
    {
        public double Hp, MaxHp, Atk, Def;
        public int Level, Xp, XpNeed;
    }

    /// <summary>One miniboss marker on the boss clock: where (0..1 of the bar) and whether the clock passed it.</summary>
    public struct BossClockMarker
    {
        public float At;
        public bool Passed;
    }

    /// <summary>The boss clock bar: fill (awakening 0..1), "Roll n/N" and the miniboss markers.</summary>
    public sealed class BossClockInfo
    {
        public float Fill;
        public string RollText;
        public readonly List<BossClockMarker> Markers = new List<BossClockMarker>();
    }

    /// <summary>One tile of the skill tray.</summary>
    public struct SkillTile
    {
        public string SkillId, Name, LevelText;
        public Rarity Rarity;
        public Color Color;
        public int Level, MaxLevel;
    }

    /// <summary>The skill tray: shown tiles and how many skills hide behind the "+N" tile (0 = none).</summary>
    public sealed class SkillTray
    {
        public readonly List<SkillTile> Tiles = new List<SkillTile>();
        public int Overflow;
    }

    /// <summary>
    /// Pure presentation helpers of the run screen (tested): HUD numbers, skill colours by rarity, landing feedback from
    /// the rules' outcome data (or the tile definition where a tile kind has none yet).
    /// </summary>
    public static class RunPresentation
    {
        // ---- HUD ----------------------------------------------------------------------------------

        /// <summary>Run XP needed for the next run level (the rules' RunSystem.XpNeeded).</summary>
        public static int XpNeeded(ContentDb c, ActiveRun run) => RunSystem.XpNeeded(c, run.Level);

        /// <summary>
        /// HP, ATK and DEF of the worm in this run with every run modification (skills at their levels, perks, buffs, curses,
        /// level modifiers) and the run level / XP: the rules' RunSystem.WormStats and RunSystem.LevelInfo.
        /// </summary>
        public static RunHudStats Hud(ContentDb c, ActiveRun run)
        {
            var lv = RunSystem.LevelInfo(c, run);
            var s = new RunHudStats { Hp = Math.Max(0, run.WormHp), MaxHp = run.WormMaxHp, Level = lv.Level, Xp = lv.Xp, XpNeed = lv.XpNeeded };
            try
            {
                var w = RunSystem.WormStats(c, run);
                s.Atk = w.Atk;
                s.Def = w.Def;
                s.MaxHp = w.MaxHp;
                s.Hp = w.Hp;
            }
            catch (Exception) { /* an incomplete test loadout: show what the run state has */ }
            if (run.WormHp < 0) s.Hp = s.MaxHp;
            return s;
        }

        // ---- boss clock -------------------------------------------------------------------------

        /// <summary>
        /// The boss clock under the top strip: "Roll n/N" (N = RunSystem.BossRollEstimate), the boss's awakening as the bar's
        /// fill and one skull pip per miniboss of the run (playtest 0.1.308: RunTuning.MinibossesPerRun lairs on the path,
        /// spread evenly along the rail), bright once beaten; none on levels without lairs or in the Burrow (pure).
        /// </summary>
        public static BossClockInfo BossClock(ContentDb c, ActiveRun run)
        {
            var info = new BossClockInfo { Fill = (float)Math.Max(0, Math.Min(1, run.Awakening / 100.0)), RollText = RunScreen.RollText(c, run) };
            if (run.Mode == RunMode.Burrow) return info; // floors have no minibosses
            var level = c.RunLevel(run.LevelId);
            bool lairs = level != null && level.FixedTiles.Exists(ft => ft.TileId == "miniboss");
            int n = lairs ? c.RunTuning.MinibossesPerRun : 0;
            for (int i = 0; i < n; i++)
                info.Markers.Add(new BossClockMarker { At = (i + 1f) / (n + 1f), Passed = run.MinibossesDefeated > i });
            return info;
        }

        // ---- skill tray ---------------------------------------------------------------------------

        /// <summary>"Lv 2/3": a run skill's level and its tier's max level (Common 3, Legendary 2, Mythic 1).</summary>
        public static string LevelText(ContentDb c, SkillDef s, int level) =>
            "Lv " + Math.Max(1, level) + "/" + c.RunTuning.MaxSkillLevelOf(s?.Rarity ?? Rarity.Common);

        /// <summary>
        /// The skill tray of <paramref name="slots"/> tiles (pure): every run skill in the order gained with its tier colour and
        /// "Lv x/max"; with more skills than slots the last tile is "+N" (the skills not shown) and opens the full list.
        /// </summary>
        public static SkillTray Tray(ContentDb c, ActiveRun run, int slots) => Tray(c, RunSystem.ActiveSkillIds(run), id => RunDraft.Level(run, id), slots);

        public static SkillTray Tray(ContentDb c, IList<string> skillIds, Func<string, int> level, int slots)
        {
            var tray = new SkillTray();
            int count = skillIds?.Count ?? 0;
            int show = count <= slots ? count : Math.Max(0, slots - 1);
            for (int i = 0; i < show; i++)
            {
                string id = skillIds[i];
                var s = c.Skill(id);
                int lv = Math.Max(1, level != null ? level(id) : 1);
                tray.Tiles.Add(new SkillTile
                {
                    SkillId = id, Name = s?.Name ?? id, Rarity = s?.Rarity ?? Rarity.Common, Color = SkillColor(s),
                    Level = lv, MaxLevel = c.RunTuning.MaxSkillLevelOf(s?.Rarity ?? Rarity.Common), LevelText = LevelText(c, s, lv),
                });
            }
            tray.Overflow = count - show;
            return tray;
        }

        // ---- skill colours ------------------------------------------------------------------------

        /// <summary>A run skill's colour: its tier (Common grey, Legendary orange, Mythic red).</summary>
        public static Color SkillColor(SkillDef s) => Theme.SkillTierColor(s?.Rarity ?? Rarity.Common);

        public static Color SkillColor(ContentDb c, string skillId) => SkillColor(skillId != null ? c.Skill(skillId) : null);

        // ---- landing feedback ---------------------------------------------------------------------

        /// <summary>Outcome sources that are a player decision with their own result sheet; any other source is a tile's outcome shown as popups.</summary>
        public static bool IsSheetSource(string source) =>
            source == "event" || source == "treasure" || source == "campfire" || source == "minigame" || source == "quest";

        /// <summary>
        /// Feedback after the worm lands on <paramref name="tile"/>: from the rules' outcome lines when this landing published
        /// one (<paramref name="outcome"/>, not a sheet source), otherwise from what the tile is (fight, event, shop…) or its
        /// definition's numbers (buff/curse, supply, hazard, shard, boss tile). Never empty for a real tile (pure).
        /// </summary>
        public static List<LandingPopup> Landing(ContentDb c, ActiveRun run, RunTile tile, bool battle, RunOutcome outcome)
        {
            var list = new List<LandingPopup>();
            var def = tile != null && c.BoardTiles.TryGetValue(tile.TileId ?? "", out var d) ? d : null;
            var cat = def?.Category ?? TileCategory.Empty;
            if (battle || cat == TileCategory.Enemy || cat == TileCategory.Elite || cat == TileCategory.Miniboss)
                list.Add(new LandingPopup(cat == TileCategory.Elite ? "ELITE FIGHT!" : cat == TileCategory.Miniboss ? "MINIBOSS!" : "FIGHT!", Theme.Bad, PopupKind.Fight));
            // a resolved tile: the rules' tile outcome of this landing (ActiveRun.TileOutcomes)
            if ((outcome == null || IsSheetSource(outcome.Source)) && run != null && tile != null) outcome = TileOutcome(run, tile) ?? outcome;
            if (outcome != null && !IsSheetSource(outcome.Source))
            {
                foreach (var l in outcome.Lines) { var p = FromLine(l, c); if (p.HasValue) list.Add(p.Value); }
                if (list.Count > 0) return list;
            }
            switch (cat)
            {
                case TileCategory.Event: list.Add(new LandingPopup("EVENT!", Theme.Purple, PopupKind.Event)); break;
                case TileCategory.Treasure: list.Add(new LandingPopup("TREASURE!", Theme.Gold, PopupKind.Event)); break;
                case TileCategory.Campfire: list.Add(new LandingPopup("CAMPFIRE", Theme.Good, PopupKind.Event)); break;
                case TileCategory.Shop: list.Add(new LandingPopup("SHOP!", Theme.Blue, PopupKind.Event)); break;
                case TileCategory.Minigame: list.Add(new LandingPopup("MINI-GAME!", Theme.Premium, PopupKind.Event)); break;
                case TileCategory.Quest: list.Add(new LandingPopup("A VISITOR!", Theme.Gold, PopupKind.Event)); break;
                case TileCategory.BossGate: list.Add(new LandingPopup("BOSS ARENA", Theme.Bad, PopupKind.Boss)); break;
                default:
                    if (!battle) list.AddRange(FromTileDef(def));
                    break;
            }
            if (list.Count == 0) list.Add(new LandingPopup(def != null ? def.Name : "Landed", Theme.TextDim, PopupKind.Text));
            return list;
        }

        /// <summary>The rules' outcome of landing on <paramref name="tile"/> in the latest move (not a pass), or null.</summary>
        public static RunOutcome TileOutcome(ActiveRun run, RunTile tile)
        {
            for (int i = run.TileOutcomes.Count - 1; i >= 0; i--)
            {
                var o = run.TileOutcomes[i];
                if (o.Tile == tile.Id && o.Source != "pass") return o;
            }
            return null;
        }

        /// <summary>
        /// Tile kinds that resolve on landing: the rules publish a structured outcome for each (ActiveRun.TileOutcomes), so
        /// their popups come from outcome data; <see cref="FromTileDef"/> only serves boards without it (hand-built tests).
        /// </summary>
        public static readonly TileCategory[] FallbackKinds =
        {
            TileCategory.Empty, TileCategory.StatBuff, TileCategory.StatDebuff, TileCategory.Loot, TileCategory.Hazard,
            TileCategory.Shard, TileCategory.Pickup, TileCategory.BossTile, TileCategory.Start,
        };

        /// <summary>Popups from a tile definition (the fallback for tile kinds without outcome data; pure).</summary>
        public static List<LandingPopup> FromTileDef(TileDef def)
        {
            var list = new List<LandingPopup>();
            if (def == null) return list;
            switch (def.Category)
            {
                case TileCategory.StatBuff:
                case TileCategory.StatDebuff:
                {
                    bool up = def.Value >= 0;
                    list.Add(new LandingPopup((up ? "+" : "−") + Fmt.Pct(Math.Abs(def.Value)) + " " + StatName(def.Stat.ToString()), up ? Theme.Good : Theme.Bad, PopupKind.Stat));
                    break;
                }
                case TileCategory.Loot:
                {
                    int n = Math.Max(1, (int)def.Value);
                    list.Add(new LandingPopup("+" + n + " loot", Theme.Gold, PopupKind.Loot));
                    break;
                }
                case TileCategory.Pickup:
                    if (def.Coins > 0) list.Add(new LandingPopup("+" + def.Coins + " coins", Theme.Gold, PopupKind.Coins));
                    if (def.Value > 0) list.Add(new LandingPopup("+" + Fmt.Whole(def.Value) + " XP", Theme.Premium, PopupKind.Xp));
                    break;
                case TileCategory.Shard:
                    list.Add(new LandingPopup("+1 shard", Theme.Purple, PopupKind.Shard));
                    break;
                case TileCategory.Hazard:
                    if (def.Value > 0) list.Add(new LandingPopup("−" + Fmt.Pct(def.Value) + " HP", Theme.Bad, PopupKind.Hurt));
                    if (def.Coins > 0) list.Add(new LandingPopup("−" + def.Coins + " coins", Theme.Bad, PopupKind.Hurt));
                    if (def.PushBack > 0) list.Add(new LandingPopup("Next roll −" + def.PushBack, Theme.Warn, PopupKind.Text));
                    break;
                case TileCategory.BossTile:
                    if (Math.Abs(def.MeterDelta) > 0.01)
                        list.Add(new LandingPopup("Boss " + (def.MeterDelta > 0 ? "+" : "−") + Fmt.Whole(Math.Abs(def.MeterDelta)) + "% awake", def.MeterDelta > 0 ? Theme.Bad : Theme.Good, PopupKind.Boss));
                    if (def.BossStatPct > 0) list.Add(new LandingPopup("Boss +" + Fmt.Pct(def.BossStatPct) + " stronger", Theme.Bad, PopupKind.Boss));
                    if (def.Coins > 0) list.Add(new LandingPopup("+" + def.Coins + " coins", Theme.Gold, PopupKind.Coins));
                    break;
                case TileCategory.Empty:
                    list.Add(new LandingPopup("Quiet… nothing here", Theme.TextDim, PopupKind.Text));
                    break;
                case TileCategory.Start:
                    list.Add(new LandingPopup(def.Name, Theme.TextDim, PopupKind.Text));
                    break;
            }
            return list;
        }

        private static string StatName(string id) => id == "Hp" ? "max HP" : id == "Atk" ? "ATK" : id == "Def" ? "DEF" : id == "CritChance" ? "CRIT" : id == "Speed" ? "SPD" : id;

        /// <summary>A short popup for one rules outcome line (null for "nothing happened"; pure).</summary>
        public static LandingPopup? FromLine(OutcomeLine l, ContentDb c)
        {
            string sign = l.Amount >= 0 ? "+" : "−";
            double a = Math.Abs(l.Amount);
            var good = l.Good ? Theme.Good : Theme.Bad;
            switch (l.Kind)
            {
                case OutcomeKind.Coins: return new LandingPopup(sign + Fmt.Whole(a) + " coins", l.Good ? Theme.Gold : Theme.Bad, l.Good ? PopupKind.Coins : PopupKind.Hurt);
                case OutcomeKind.Hp: return new LandingPopup(sign + Fmt.Num(a) + " HP", good, l.Good ? PopupKind.Heal : PopupKind.Hurt);
                case OutcomeKind.Stat: return new LandingPopup(sign + Fmt.Pct(a) + " " + StatName(l.Id), good, PopupKind.Stat);
                case OutcomeKind.SkillGained: return new LandingPopup("New skill: " + (c.Skill(l.Id)?.Name ?? l.Id), SkillColor(c, l.Id), PopupKind.Text);
                case OutcomeKind.SkillLost: return new LandingPopup("Lost " + (c.Skill(l.Id)?.Name ?? l.Id), Theme.Bad, PopupKind.Hurt);
                case OutcomeKind.SkillLevel: return new LandingPopup(c.Skill(l.Id) != null ? SkillText.Title(c, c.Skill(l.Id), (int)a) : l.Id + " Lv " + Fmt.Whole(a), SkillColor(c, l.Id), PopupKind.Text);
                case OutcomeKind.Xp: return new LandingPopup("+" + Fmt.Whole(a) + " XP", Theme.Premium, PopupKind.Xp);
                case OutcomeKind.LevelUp: return new LandingPopup("LEVEL UP!", Theme.Gold, PopupKind.LevelUp);
                case OutcomeKind.Shards: return new LandingPopup(sign + Fmt.Whole(a) + " shard" + (a == 1 ? "" : "s"), Theme.Purple, PopupKind.Shard);
                case OutcomeKind.AccountLoot: return new LandingPopup("+" + (l.Reward != null ? Fmt.Reward(l.Reward, c) : l.Id), Theme.Gold, PopupKind.Loot);
                case OutcomeKind.Awakening: return new LandingPopup("Boss " + sign + Fmt.Whole(a) + "% awake", good, PopupKind.Boss);
                case OutcomeKind.AwakenRate: return new LandingPopup("Boss wakes " + Fmt.Pct(a) + (l.Amount >= 0 ? " faster" : " slower"), good, PopupKind.Boss);
                case OutcomeKind.BossStat: return new LandingPopup("Boss " + sign + Fmt.Pct(a) + " stronger", good, PopupKind.Boss);
                case OutcomeKind.Danger: return new LandingPopup("Danger " + sign + Fmt.Whole(a), good, PopupKind.Text);
                case OutcomeKind.Battle: return null; // the battle playback is the feedback
                case OutcomeKind.SkillRoll: return new LandingPopup("Skill roll!", Theme.Purple, PopupKind.Text);
                case OutcomeKind.Nothing: return null;
                case OutcomeKind.Empty: return new LandingPopup("Quiet… nothing here", Theme.TextDim, PopupKind.Text);
                case OutcomeKind.Avoided: return new LandingPopup("Avoided!", Theme.Good, PopupKind.Text);
                case OutcomeKind.StepPenalty: return new LandingPopup("Next roll −" + Fmt.Whole(a), Theme.Warn, PopupKind.Text);
                case OutcomeKind.CoinBonus: return new LandingPopup("+" + Fmt.Whole(a) + " " + (l.Id ?? "bonus"), Theme.Gold, PopupKind.Coins); // the quest coin bonus
                case OutcomeKind.QuestStep: return new LandingPopup(l.Good ? "Story moves on!" : "The story waits", l.Good ? Theme.Gold : Theme.TextDim, PopupKind.Text);
                default: return new LandingPopup(RunScreen.OutcomeLineText(l, c), good, PopupKind.Text);
            }
        }

        /// <summary>
        /// The pickup a passed tile pays (shards, coin clusters trigger on pass): a small popup, the tile itself keeps its
        /// look (only landed tiles are marked). Null when passing does nothing (pure).
        /// </summary>
        public static LandingPopup? Pass(ContentDb c, RunTile tile) => Pass(c, null, tile);

        /// <summary>The pass outcome the rules recorded for this tile in the latest move (pickups, skill coins), else the tile's pickup.</summary>
        public static LandingPopup? Pass(ContentDb c, ActiveRun run, RunTile tile)
        {
            if (run != null && tile != null)
                for (int i = run.TileOutcomes.Count - 1; i >= 0; i--)
                {
                    var o = run.TileOutcomes[i];
                    if (o.Tile != tile.Id || o.Source != "pass") continue;
                    foreach (var l in o.Lines) { var p = FromLine(l, c); if (p.HasValue) return p; }
                }
            var def = tile != null && c.BoardTiles.TryGetValue(tile.TileId ?? "", out var d) ? d : null;
            if (def == null || !def.TriggersOnPass) return null;
            if (def.Category == TileCategory.Shard) return new LandingPopup("+1 shard", Theme.Purple, PopupKind.Shard);
            if (def.Category == TileCategory.Pickup && def.Coins > 0) return new LandingPopup("+" + def.Coins + " coins", Theme.Gold, PopupKind.Coins);
            return null;
        }
    }
}
