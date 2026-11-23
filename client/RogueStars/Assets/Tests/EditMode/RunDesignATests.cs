using System;
using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Run screen design A ("Top status bar") and the battle in the same layout language: the top strip's values and
    /// elements, the boss clock's miniboss markers, the skill tray (tier colours, "Lv x/max", "+N" overflow), the battle's
    /// skill strip reacting to skill fires from the log (skip mode and the queue), and the ArtCatalog's fallbacks.
    /// </summary>
    public sealed class RunDesignATests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }

        [TearDown]
        public void TearDown()
        {
            RunScreen.InstantOutcomes = false; RunFx.Instant = false; BattlePlaybackScreen.Instant = false;
            ArtCatalog.Loader = null;
            _f.Dispose();
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static List<Transform> AllNamed(Transform root, string name)
        {
            var list = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) list.Add(t);
            return list;
        }

        private static string Texts(Transform root)
        {
            var parts = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Text>(true)) if (t.gameObject.activeInHierarchy) parts.Add(t.text);
            return string.Join(" | ", parts);
        }

        private ActiveRun StartRun(int mainLevel = 1)
        {
            var level = C.MainLevel(mainLevel);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            return S.Run.Active;
        }

        private RunScreen Open()
        {
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        /// <summary>Draftable run skills of a tier (no family / prerequisite gates).</summary>
        private List<SkillDef> RunSkills(Rarity tier, int n)
        {
            var list = new List<SkillDef>();
            foreach (var s in C.Skills.Values)
            {
                if (s.Source != SkillSource.Run || !s.Draftable || s.RequiresFamilyCount > 0 || s.Requires.Count > 0 || s.UpgradesFrom != null) continue;
                if (RunTuning.RunTier(s.Rarity) != tier) continue;
                list.Add(s);
                if (list.Count == n) break;
            }
            Assert.That(list.Count, Is.EqualTo(n), "enough " + tier + " skills");
            return list;
        }

        // ---- top strip ----

        [Test]
        public void TopStrip_ShowsPauseHpStatsLevelRingAndCoins_NoAppHeader()
        {
            var run = StartRun();
            run.Xp = 3;
            run.Coins = 418;
            var screen = Open();
            Assert.That(screen.HeaderHeight, Is.EqualTo(0), "the strip replaces the app header");
            Assert.That(screen.CoversResourceBar, Is.True);
            Assert.That(FindNamed(screen.Root, "Header"), Is.Null);
            var strip = FindNamed(screen.Root, "TopStrip");
            Assert.That(strip, Is.Not.Null);
            foreach (var name in new[] { "PauseButton", "HpBar", "AtkChip", "DefChip", "SkillCount", "RunLevel", "XpRing", "CoinsChip", "BossClock" })
                Assert.That(FindNamed(strip, name), Is.Not.Null, name + " in the top strip");

            var hud = RunPresentation.Hud(C, run);
            var w = RunSystem.WormStats(C, run);
            var lv = RunSystem.LevelInfo(C, run);
            Assert.That(hud.Atk, Is.EqualTo(w.Atk));
            Assert.That(screen.HudText, Does.Contain("HP " + Fmt.Num(Math.Round(w.Hp)) + " / " + Fmt.Num(Math.Round(w.MaxHp))));
            Assert.That(RunHudStrip.Plain(FindNamed(strip, "AtkChip").GetComponent<Text>()), Is.EqualTo("ATK " + Fmt.Num(Math.Round(w.Atk))));
            Assert.That(RunHudStrip.Plain(FindNamed(strip, "DefChip").GetComponent<Text>()), Is.EqualTo("DEF " + Fmt.Num(Math.Round(w.Def))));
            Assert.That(RunHudStrip.Plain(FindNamed(strip, "SkillCount").GetComponent<Text>()), Is.EqualTo("SKILLS " + RunSystem.BuildSize(run)));
            Assert.That(FindNamed(strip, "LvNumber").GetComponent<Text>().text, Is.EqualTo(lv.Level.ToString()));
            var ring = FindNamed(strip, "XpRing").GetComponent<Image>();
            Assert.That(ring.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(ring.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(ring.fillAmount, Is.EqualTo((float)lv.Xp / lv.XpNeeded).Within(1e-4f), "XP progress as a ring");
            Assert.That(screen.HudText, Does.Contain("XP 3 / " + lv.XpNeeded));
            screen.Tick();
            Assert.That(Texts(FindNamed(strip, "CoinsChip")), Does.Contain(Fmt.Num(418)));

            // the pause button opens the pause modal naming the level
            FindNamed(strip, "PauseButton").GetComponent<Button>().onClick.Invoke();
            Assert.That(Texts(_f.App.ModalLayer), Does.Contain(screen.Title));
        }

        [Test]
        public void TopStrip_SitsAboveTheBoard_AndTheFooterHoldsTrayAndRollRow()
        {
            StartRun();
            var screen = Open();
            var strip = (RectTransform)FindNamed(screen.Root, "TopStrip");
            var board = (RectTransform)FindNamed(screen.Root, "BoardHolder");
            var corners = new Vector3[4];
            strip.GetWorldCorners(corners);
            float stripBottom = corners[0].y;
            board.GetWorldCorners(corners);
            Assert.That(corners[1].y, Is.LessThanOrEqualTo(stripBottom + 0.5f), "board under the strip");
            var footer = FindNamed(screen.Root, "Footer");
            foreach (var name in new[] { "Slots", "RollRow", "LootButton", "RollButton", "AutoButton", "AutoPickToggle", "FooterDice", "Hint" })
                Assert.That(FindNamed(footer, name), Is.Not.Null, name + " in the footer");
            Assert.That(FindNamed(FindNamed(footer, "RollButton"), "FooterDice"), Is.Not.Null, "the dice glyph sits on ROLL");
        }

        // ---- boss clock ----

        [Test]
        public void BossClock_MarkersAt25_50_75_PassedOnesBright()
        {
            var run = StartRun();
            var clock = RunPresentation.BossClock(C, run);
            Assert.That(clock.Markers.Count, Is.EqualTo(C.RunTuning.MinibossAtMeter.Count));
            Assert.That(clock.Markers.ConvertAll(m => m.At), Is.EqualTo(new List<float> { 0.25f, 0.5f, 0.75f }));
            Assert.That(clock.Markers.TrueForAll(m => !m.Passed), Is.True, "a fresh run passed none");
            Assert.That(clock.RollText, Is.EqualTo(RunScreen.RollText(C, run)));

            run.Awakening = 55;
            clock = RunPresentation.BossClock(C, run);
            Assert.That(clock.Fill, Is.EqualTo(0.55f).Within(1e-4f));
            Assert.That(clock.Markers.ConvertAll(m => m.Passed), Is.EqualTo(new List<bool> { true, true, false }));

            var screen = Open();
            var c = FindNamed(screen.Root, "BossClock");
            Assert.That(Texts(c), Does.Contain(RunScreen.RollText(C, run)).And.Contain("BOSS"));
            var m0 = FindNamed(c, "MinibossMarker:0").GetComponent<Image>().color;
            var m2 = FindNamed(c, "MinibossMarker:2").GetComponent<Image>().color;
            Assert.That(m0.a, Is.EqualTo(1f), "passed marker is bright");
            Assert.That(m2.a, Is.LessThan(0.6f), "an upcoming marker is dim");
            Assert.That(((RectTransform)FindNamed(c, "MinibossMarker:1")).anchorMin.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(FindNamed(c, "ClockFill"), Is.Not.Null);

            run.Mode = RunMode.Burrow;
            Assert.That(RunPresentation.BossClock(C, run).Markers, Is.Empty, "Burrow floors have no clock minibosses");
        }

        // ---- skill tray ----

        [Test]
        public void SkillTray_TierColours_Levels_AndOverflowOpensTheList()
        {
            var run = StartRun();
            var commons = RunSkills(Rarity.Common, 7);
            var leg = RunSkills(Rarity.Legendary, 1)[0];
            var myth = RunSkills(Rarity.Mythic, 1)[0];
            run.Skills.Add(leg.Id);
            run.Skills.Add(myth.Id);
            run.Skills.Add(commons[0].Id);
            run.SkillLevels[commons[0].Id] = 2;

            run.Loadout.MetaSkillIds.Clear(); // only the run's skills on the tray here
            var tray = RunPresentation.Tray(C, run, RunScreen.SkillSlots);
            Assert.That(tray.Overflow, Is.EqualTo(0));
            Assert.That(tray.Tiles.Count, Is.EqualTo(3));
            Assert.That(tray.Tiles[0].Color, Is.EqualTo(Theme.SkillTierColor(Rarity.Legendary)), "orange");
            Assert.That(tray.Tiles[1].Color, Is.EqualTo(Theme.SkillTierColor(Rarity.Mythic)), "red");
            Assert.That(tray.Tiles[2].Color, Is.EqualTo(Theme.SkillTierColor(Rarity.Common)), "grey");
            Assert.That(tray.Tiles[2].LevelText, Is.EqualTo("Lv 2/" + C.RunTuning.MaxSkillLevelOf(Rarity.Common)));
            Assert.That(tray.Tiles[0].LevelText, Is.EqualTo("Lv 1/" + C.RunTuning.MaxSkillLevelOf(Rarity.Legendary)));

            var screen = Open();
            var slots = FindNamed(screen.Root, "Slots");
            Assert.That(slots.GetComponentsInChildren<Button>().Length, Is.EqualTo(RunScreen.SkillSlots), "7 tiles (empty ones outlined)");
            var tile = FindNamed(slots, "Slot:" + commons[0].Id);
            Assert.That(Texts(tile), Does.Contain("Lv 2/3"));
            Assert.That(FindNamed(tile, "RarityBorder").GetComponent<Image>().color, Is.EqualTo(Theme.SkillTierColor(Rarity.Common)));
            Assert.That(AllNamed(slots, "EmptySlot").Count, Is.EqualTo(RunScreen.SkillSlots - 3));
            Assert.That(FindNamed(slots, "MoreSkills"), Is.Null);

            // more skills than tiles: 6 tiles + "+N" that opens the skills list
            for (int i = 1; i < commons.Count; i++) run.Skills.Add(commons[i].Id);
            int total = RunSystem.ActiveSkillIds(run).Count;
            Assert.That(total, Is.GreaterThan(RunScreen.SkillSlots));
            tray = RunPresentation.Tray(C, run, RunScreen.SkillSlots);
            Assert.That(tray.Tiles.Count, Is.EqualTo(RunScreen.SkillSlots - 1));
            Assert.That(tray.Overflow, Is.EqualTo(total - (RunScreen.SkillSlots - 1)));
            screen.Rebuild();
            slots = FindNamed(screen.Root, "Slots");
            Assert.That(slots.GetComponentsInChildren<Button>().Length, Is.EqualTo(RunScreen.SkillSlots));
            var more = FindNamed(slots, "MoreSkills");
            Assert.That(Texts(more), Does.Contain("+" + tray.Overflow));
            more.GetComponent<Button>().onClick.Invoke();
            foreach (var id in RunSystem.ActiveSkillIds(run)) Assert.That(FindNamed(_f.App.ModalLayer, "Skill:" + id), Is.Not.Null, "the list shows every skill: " + id);
        }

        // ---- battle: same strip, skill strip reacting to the log ----

        private BattleResult HandBattle(string skillA, string skillB, string enemySkill)
        {
            var b = new BattleResult { Winner = BattleSide.Player, Rounds = 2 };
            b.Fighters.Add(new BattleFighterInfo { Index = 0, Name = "Worm", Role = CombatRole.Worm, Side = BattleSide.Player, Targetable = true, MaxHp = 1000, StartHp = 900, EndHp = 890, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 1, Name = "Hound", Role = CombatRole.Enemy, Side = BattleSide.Enemy, Targetable = true, MaxHp = 500, StartHp = 500, EndHp = 0, Alive = false });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 0, Target = 1, Action = BattleAction.StatusApplied, Status = StatusId.Burn, Stacks = 2, SkillId = skillA, TargetHp = 500 });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 0, Target = 1, Action = BattleAction.Skill, Amount = 120, SkillId = skillA, TargetHp = 380 });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 1, Target = 0, Action = BattleAction.Skill, Amount = 40, SkillId = enemySkill, TargetHp = 860 });
            b.Log.Add(new BattleLogEntry { Round = 2, Actor = 0, Target = 0, Action = BattleAction.Heal, Amount = 30, SkillId = skillB, TargetHp = 890 });
            b.Log.Add(new BattleLogEntry { Round = 2, Actor = 0, Target = 1, Action = BattleAction.Basic, Amount = 380, TargetHp = 0 });
            b.Log.Add(new BattleLogEntry { Round = 2, Actor = 0, Target = 1, Action = BattleAction.Death, TargetHp = 0 });
            return b;
        }

        [Test]
        public void BattleSkillFx_MapsLogEntriesToTheSourceSkill()
        {
            var two = RunSkills(Rarity.Common, 3);
            var battle = HandBattle(two[0].Id, two[1].Id, two[2].Id);
            var fires = BattleSkillFx.Fires(battle, C);
            Assert.That(fires.ConvertAll(f => f.SkillId), Is.EqualTo(new List<string> { two[0].Id, two[0].Id, two[1].Id }), "only the player's side, every skill effect");
            Assert.That(fires[0].Label, Is.EqualTo(two[0].Name + " · Burn ×2"));
            Assert.That(fires[1].Effect, Is.EqualTo("120 dmg"));
            Assert.That(fires[2].Effect, Is.EqualTo("+30 HP"));
            Assert.That(BattleSkillFx.EffectText(new BattleLogEntry { Action = BattleAction.Skill, Amount = 240, Crit = true }), Is.EqualTo("Crit 240"));
            Assert.That(BattleSkillFx.EffectText(new BattleLogEntry { Action = BattleAction.Shield, Amount = 50 }), Is.EqualTo("+50 Shield"));
            Assert.That(BattleSkillFx.FireOf(new BattleLogEntry { Actor = 0, Action = BattleAction.StatusExpired, SkillId = two[0].Id }, battle, C), Is.Null);
            // the strip shows the firing skills first, the most active first, then the silent ones
            var order = BattleSkillFx.StripOrder(new List<string> { two[2].Id, two[1].Id, two[0].Id }, battle, C);
            Assert.That(order, Is.EqualTo(new List<string> { two[0].Id, two[1].Id, two[2].Id }));
            // a skill that is not one of the run's (a part's or the mount's) still gets its tile
            order = BattleSkillFx.StripOrder(new List<string> { two[2].Id }, battle, C);
            Assert.That(order, Is.EqualTo(new List<string> { two[0].Id, two[1].Id, two[2].Id }));
        }

        [Test]
        public void Battle_InstantMode_CountsEverySkillFire_NoAnimation()
        {
            var sk = RunSkills(Rarity.Common, 3);
            var run = StartRun();
            run.Skills.Add(sk[0].Id); run.Skills.Add(sk[1].Id);
            var battle = HandBattle(sk[0].Id, sk[1].Id, sk[2].Id);
            BattlePlaybackScreen.Instant = true;
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            Assert.That(screen.HeaderHeight, Is.EqualTo(0));
            var strip = FindNamed(screen.Root, "SkillStrip");
            Assert.That(strip, Is.Not.Null, "skill strip in the footer");
            Assert.That(FindNamed(strip, "Slot:" + sk[0].Id), Is.Not.Null);
            Assert.That(FindNamed(strip, "Slot:" + sk[1].Id), Is.Not.Null);
            Assert.That(((RectTransform)FindNamed(strip, "Slot:" + sk[0].Id)).rect.width, Is.GreaterThan(RunScreen.SkillTileSize), "larger tiles than the board's tray");
            Assert.That(FindNamed(screen.Root, "TopStrip"), Is.Not.Null, "same top strip as the run");
            screen.Frame(0.001f);
            Assert.That(screen.Finished, Is.True);
            Assert.That(screen.FireCount(sk[0].Id), Is.EqualTo(2));
            Assert.That(screen.FireCount(sk[1].Id), Is.EqualTo(1));
            Assert.That(screen.FireCount(sk[2].Id), Is.EqualTo(0), "the enemy's skill is not on our strip");
            Assert.That(screen.SkillFires, Does.Contain(sk[0].Name + " · Burn ×2"));
            Assert.That(screen.FireQueue.Busy, Is.False, "skip mode queues no animation");
            Assert.That(FindNamed(screen.Root, "FireFlash"), Is.Null);
            Assert.That(screen.HudText, Does.Contain("HP " + Fmt.Num(890) + " / " + Fmt.Num(1000)), "the worm's HP from the log in the top strip");
            Assert.That(screen.HudText, Does.Contain("ATK " + Fmt.Num(Math.Round(RunSystem.WormStats(C, run).Atk))));
        }

        [Test]
        public void Battle_AnimatedFires_QueuePerTile_AndFollowTheSpeed()
        {
            var sk = RunSkills(Rarity.Common, 3);
            var run = StartRun();
            run.Skills.Add(sk[0].Id); run.Skills.Add(sk[1].Id);
            var battle = HandBattle(sk[0].Id, sk[1].Id, sk[2].Id);
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            var started = new List<string>();
            screen.FireQueue.Started += (k, l) => started.Add(k + "=" + l);
            screen.Frame(screen.StepSeconds); // entry 0: Burn ×2 starts on its tile
            Assert.That(started, Is.EqualTo(new List<string> { sk[0].Id + "=" + sk[0].Name + " · Burn ×2" }));
            Assert.That(screen.FireQueue.Duration, Is.EqualTo(screen.FireSeconds).Within(1e-4f));
            // entry 1 comes before the first fire is over: it waits on the same tile
            Assert.That(screen.StepSeconds, Is.LessThan(screen.FireSeconds));
            var q = new SkillFireQueue { Duration = 1 };
            var log = new List<string>();
            q.Started += (k, l) => log.Add(l);
            q.Push("a", "Magma Tail · Burn ×2");
            q.Push("a", "Magma Tail · 120 dmg");
            q.Push("a", "Magma Tail · 120 dmg");
            q.Push("b", "Ice Fang · Freeze");
            Assert.That(log, Is.EqualTo(new List<string> { "Magma Tail · Burn ×2", "Ice Fang · Freeze" }), "one at a time per tile, tiles in parallel");
            Assert.That(q.Waiting("a"), Is.EqualTo(1), "the same effect twice in a row waits once");
            q.Tick(0.5f);
            Assert.That(log.Count, Is.EqualTo(2));
            q.Tick(0.6f);
            Assert.That(log[2], Is.EqualTo("Magma Tail · 120 dmg ×2"));
            Assert.That(q.Playing("a"), Is.EqualTo("Magma Tail · 120 dmg ×2"));
            for (int i = 0; i < 10; i++) q.Push("c", "x" + i);
            Assert.That(q.Waiting("c"), Is.EqualTo(SkillFireQueue.MaxPending), "a tile never piles up");
            q.Tick(5); q.Tick(5); q.Tick(5); q.Tick(5);
            Assert.That(q.Busy, Is.False);

            // faster playback = shorter fires
            for (int guard = 0; guard < 100 && !screen.Finished; guard++) screen.Frame(screen.StepSeconds);
            Assert.That(screen.FireCount(sk[0].Id), Is.EqualTo(2));
            float x1 = screen.FireSeconds;
            FindNamed(screen.Root, "Speed2").GetComponent<Button>().onClick.Invoke();
            Assert.That(screen.FireSeconds, Is.LessThan(x1));
            FindNamed(screen.Root, "Speed1").GetComponent<Button>().onClick.Invoke();
            for (int i = 0; i < 20; i++) screen.Frame(1);
            Assert.That(screen.FireQueue.Busy, Is.False, "the queue drains after the fight");
            Assert.That(FindNamed(screen.Root, "FireFlash"), Is.Null, "no flash left behind");
        }

        // ---- art ----

        [Test]
        public void ArtCatalog_FallsBackToTheProceduralLook()
        {
            var loaded = new List<string>();
            var fake = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            var have = new HashSet<string> { "Art/Boards/lava/background", "Art/Boards/lava/tiles/slab", "Art/Boards/seabed/tiles/shop" };
            ArtCatalog.Loader = p => { loaded.Add(p); return have.Contains(p) ? fake : null; };

            Assert.That(ArtCatalog.FamilyOf(C.MainLevel(1)), Is.EqualTo("ember"));
            Assert.That(ArtCatalog.BiomeFor("ember").Id, Is.EqualTo("lava"));
            Assert.That(ArtCatalog.BiomeFor("glacier").Id, Is.EqualTo("seabed"));
            Assert.That(ArtCatalog.BiomeFor("void"), Is.SameAs(ArtCatalog.Default), "an unmapped family: the procedural board");
            Assert.That(ArtCatalog.BiomeFor((string)null), Is.SameAs(ArtCatalog.Default));
            Assert.That(ArtCatalog.Background(ArtCatalog.Default), Is.Null, "the default biome has no ground image");

            var lava = ArtCatalog.BiomeFor("ember");
            var sea = ArtCatalog.BiomeFor("glacier");
            Assert.That(ArtCatalog.Background(lava), Is.SameAs(fake));
            Assert.That(ArtCatalog.Background(sea), Is.Null, "missing ground file: plain board");
            Assert.That(ArtCatalog.TileSprite(lava, TileCategory.Enemy), Is.SameAs(fake), "no enemy sprite: the biome's generic slab");
            Assert.That(ArtCatalog.TileSprite(sea, TileCategory.Shop), Is.SameAs(fake), "a per-kind sprite");
            Assert.That(ArtCatalog.TileSprite(sea, TileCategory.Enemy), Is.Null, "no sprite at all: procedural slab");
            Assert.That(ArtCatalog.TileSprite(ArtCatalog.Default, TileCategory.Enemy), Is.Null);
            Assert.That(ArtCatalog.TileKey(TileCategory.BossGate), Is.EqualTo("boss_gate"));
            int before = loaded.Count;
            ArtCatalog.TileSprite(sea, TileCategory.Enemy);
            Assert.That(loaded.Count, Is.EqualTo(before), "misses are cached");

            // procedural slab colours per biome: landed tiles take the kind's colour, passed ones keep the slab face
            var look = new RunScreen.TileLook { Label = "FIGHT", Accent = Theme.Bad };
            RunScreen.TileColors(look, false, sea, false, out var fill, out var border, out var text);
            Assert.That(fill, Is.EqualTo(sea.SlabFace));
            Assert.That(border, Is.EqualTo(Theme.Bad));
            Assert.That(Theme.TextOn(fill), Is.EqualTo(Theme.OnAccent), "the seabed slab is light: dark label");
            RunScreen.TileColors(look, true, sea, false, out var landed, out _, out _);
            Assert.That(landed, Is.Not.EqualTo(fill));
            RunScreen.TileColors(look, false, lava, true, out var spriteFill, out _, out _);
            Assert.That(spriteFill, Is.EqualTo(Color.white), "a tile sprite is drawn untinted");

            // nothing at all → the hero falls back to the drawn worm and the board to the plain one
            ArtCatalog.Loader = p => null;
            var run = StartRun();
            var screen = Open();
            Assert.That(screen.Biome.Id, Is.EqualTo("lava"));
            Assert.That(FindNamed(screen.Root, "BoardGround"), Is.Null);
            var worm = FindNamed(FindNamed(screen.Root, "Token"), "Worm");
            Assert.That(worm.GetComponent<UiBob>(), Is.Not.Null);
            Assert.That(worm.GetComponent<Image>().sprite, Is.Not.SameAs(fake));
            Assert.That(FindNamed(screen.Root, "SlabSide"), Is.Not.Null, "procedural slabs with a bevel side");
            Assert.That(FindNamed(FindNamed(screen.Root, "Tile:1"), "Marker") != null || RunScreen.LookOf(run, run.Tiles[1], C).Empty, Is.True);
        }

        [Test]
        public void Art_IsImported_BoardGroundScrollsWithParallax_HeroIsTheToken()
        {
            ArtCatalog.Loader = null;
            Assert.That(ArtCatalog.Hero(), Is.Not.Null, "Assets/Art/Resources/Art/Hero/hero_worm.png is a sprite");
            Assert.That(ArtCatalog.Background(ArtCatalog.BiomeFor("ember")), Is.Not.Null, "lava ground imported");
            Assert.That(ArtCatalog.Background(ArtCatalog.BiomeFor("glacier")), Is.Not.Null, "seabed ground imported");

            // parallax: periodic over two (mirrored) copies, continuous, slower than the camera
            float T = 1700;
            Assert.That(RunFx.GroundTop(0, 0.5f, T), Is.EqualTo(2 * T));
            Assert.That(RunFx.GroundTop(100, 0.5f, T), Is.EqualTo(2 * T - 50).Within(1e-3f));
            Assert.That(RunFx.GroundTop(4 * T + 100, 0.5f, T), Is.EqualTo(RunFx.GroundTop(100, 0.5f, T)).Within(1e-2f), "one period later the same");
            for (float c = 0; c < 8 * T; c += 37)
            {
                float y = RunFx.GroundTop(c, 0.5f, T);
                Assert.That(y, Is.GreaterThan(0).And.LessThanOrEqualTo(2 * T));
            }

            StartRun();
            var screen = Open();
            var ground = FindNamed(screen.Root, "BoardGround");
            Assert.That(ground, Is.Not.Null, "level 1 (ember) draws the lava ground");
            Assert.That(ground.GetSiblingIndex(), Is.LessThan(FindNamed(screen.Root, "Board").GetSiblingIndex()), "behind the path");
            var copies = new List<RectTransform>();
            foreach (Transform t in ground) copies.Add((RectTransform)t);
            Assert.That(copies.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(copies[1].localScale.y, Is.EqualTo(-1), "mirror-repeated: no seam");
            Assert.That(copies[0].localScale.y, Is.EqualTo(1));
            float g0 = screen.GroundY, c0 = screen.CameraY;
            Assert.That(g0, Is.EqualTo(RunFx.GroundTop(c0, RunScreen.GroundParallax, copies[0].rect.height - 1)).Within(0.5f));
            var worm = FindNamed(FindNamed(screen.Root, "Token"), "Worm").GetComponent<Image>();
            Assert.That(worm.sprite, Is.SameAs(ArtCatalog.Hero()), "the hero sprite is the token");
            Assert.That(worm.GetComponent<UiBob>(), Is.Not.Null);

            // battle: the worm fighter is the hero sprite too
            BattlePlaybackScreen.Instant = true;
            var sk = RunSkills(Rarity.Common, 3);
            var b = new BattlePlaybackScreen(HandBattle(sk[0].Id, sk[1].Id, sk[2].Id));
            _f.App.Router.Push(b);
            Assert.That(FindNamed(FindNamed(b.Root, "Fighter:0"), "Sprite").GetComponent<Image>().sprite, Is.SameAs(ArtCatalog.Hero()));
        }
    }
}
