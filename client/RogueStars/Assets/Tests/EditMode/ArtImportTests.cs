using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.Editor;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// The owner-approved art import of 2026-11-25: the importer's new pick forms (redo packages "slice-hud2", home paths,
    /// files of another package), every approved key in the real picks file and under Resources, the legless worm (body vs.
    /// icon), max HP showing the heart, the home sky states, and where the hub and the top bar use the new art.
    /// </summary>
    public sealed class ArtImportTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() { _f = new AppFixture(); ArtCatalog.Loader = null; }

        [TearDown]
        public void TearDown()
        {
            HubScreen.SkyOverride = null;
            ArtCatalog.Loader = null;
            _f.Dispose();
        }

        private static string PicksPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../tools/art/slice-picks.json"));

        // ---- importer ----

        [Test]
        public void Importer_RedoPackagesHomePathsAndOtherPackages_GoToTheirArtCatalogKeys()
        {
            Assert.That(ArtImportRules.BaseCategory("slice-hud2"), Is.EqualTo("slice-hud"));
            Assert.That(ArtImportRules.BaseCategory("slice-markers4"), Is.EqualTo("slice-markers"));
            Assert.That(ArtImportRules.BaseCategory("hub-icons"), Is.EqualTo("hub-icons"));
            var plan = ArtImportRules.PlanSlice("/assets", ArtImportRules.ReadPicks(
                "{ \"slice-hud2/exp\": \"L_s5412.png\", \"slice-markers4/elite\": \"I_s5132.png\", \"hero/hero_worm\": \"../hub-icons/tab_worm/S_s5001.png\", " +
                "\"hub-icons/patrol\": \"quick_harvest/C_s5002.png\", \"hub-scene/sky_day\": \"~/AI/out/pkg-hub-scene2/space_day__Z__flux__s5211.png\", " +
                "\"run-result2/crown\": \"N_s6511.png\", \"board-ground/ground_long_2\": \"ground_long/I_s5902.png\", \"slice-markers4/boss\": \"I_s1.png\" }"));
            var by = plan.ToDictionary(i => i.Dest, i => i);
            Assert.That(by.Keys, Is.EquivalentTo(new[]
            {
                "Art/Slice/Hud/exp.png", "Art/Slice/Markers/elite.png", "Art/Hero/hero_worm.png", "Art/Hub/Icons/patrol.png",
                "Art/Hub/Scene/sky_day.png", "Art/Result/crown.png", "Art/Slice/Ground/ground_long_2.png",
            }), "the retired boss marker stays retired from a redo package too");
            Assert.That(by["Art/Slice/Hud/exp.png"].Source.Replace('\\', '/'), Is.EqualTo("/assets/slice-hud2/exp/L_s5412.png"));
            Assert.That(Path.GetFullPath(by["Art/Hero/hero_worm.png"].Source).Replace('\\', '/'), Does.EndWith("/assets/hub-icons/tab_worm/S_s5001.png"));
            Assert.That(by["Art/Hub/Icons/patrol.png"].Source.Replace('\\', '/'), Is.EqualTo("/assets/hub-icons/quick_harvest/C_s5002.png"));
            Assert.That(by["Art/Hub/Scene/sky_day.png"].Source.Replace('\\', '/'), Does.EndWith("/AI/out/pkg-hub-scene2/space_day__Z__flux__s5211.png"));
            Assert.That(by["Art/Hub/Scene/sky_day.png"].Source, Does.Not.StartWith("~"), "a home path is expanded");
            Assert.That(by["Art/Hub/Scene/sky_day.png"].Trim, Is.False, "backdrops keep their frame");
            Assert.That(by["Art/Hero/hero_worm.png"].Trim, Is.False, "the hero keeps its square frame");
            Assert.That(by["Art/Hub/Icons/patrol.png"].Trim && by["Art/Result/crown.png"].Trim && by["Art/Slice/Ground/ground_long_2.png"].Trim, Is.True);
            Assert.That(ArtImportRules.PickSource("/assets", "x", "y", "/abs/file.png"), Is.EqualTo("/abs/file.png"));
        }

        [Test]
        public void PicksFile_HoldsEveryApprovedKey_OnceEach_NoCurseNoMaxHp()
        {
            var plan = ArtImportRules.PlanSlice("/assets", ArtImportRules.ReadPicks(File.ReadAllText(PicksPath)));
            var dests = plan.Select(i => i.Dest).ToList();
            Assert.That(dests, Is.Unique, "two picks never write the same file");
            var want = new List<string> { "Art/Hero/hero_worm.png", "Art/Slice/Markers/elite.png" };
            want.AddRange(new[] { "tab_worm", "patrol", "chapter_rewards", "profile_level", "play_button", "shop", "pass", "event" }.Select(i => "Art/Hub/Icons/" + i + ".png"));
            want.AddRange(ArtCatalog.SkyStates.Select(s => "Art/Hub/Scene/sky_" + s + ".png"));
            want.AddRange(new[] { "skull", "heart", "coin", "book", "exp", "dice", "pedestal", "boss_rail_top", "cog" }.Select(i => "Art/Slice/Hud/" + i + ".png"));
            want.AddRange(new[] { "blessing", "coins_big", "coins_small", "heal", "reroll", "sale_tag", "skill_book", "upgrade" }.Select(i => "Art/Run/Icons/" + i + ".png"));
            want.AddRange(ScreenArt.ResultIds.Select(i => "Art/Result/" + i + ".png"));
            foreach (var g in ArtCatalog.SliceGroundIds) { want.Add("Art/Slice/Ground/" + g + ".png"); want.Add("Art/Slice/Ground/" + g + "_2.png"); }
            Assert.That(dests, Is.SupersetOf(want));
            Assert.That(dests, Has.None.EqualTo("Art/Run/Icons/curse.png"), "the curse icon's arrow is being removed: not imported yet");
            Assert.That(dests, Has.None.EqualTo("Art/Run/Icons/max_hp.png"), "max HP shows the heart (owner)");
            // the result emblems: the lowest approved seed
            foreach (var id in ScreenArt.ResultIds) Assert.That(plan.Find(i => i.Dest == "Art/Result/" + id + ".png").Source, Does.EndWith(id == "banner" ? "N_s6513.png" : "N_s6511.png"), id); // victory pennant: the plain seed (s6511 has "VICTORY" painted in)
        }

        // ---- imported art ----

        [Test]
        public void ImportedArt_EveryNewKeyLoads()
        {
            foreach (var id in new[] { "skull", "heart", "coin", "book", "exp", "dice", "pedestal", "boss_rail_top", "cog" })
            {
                Assert.That(ArtCatalog.SliceHud(id), Is.Not.Null, "Art/Slice/Hud/" + id);
                Assert.That(HudArt.Icon(id), Is.SameAs(ArtCatalog.SliceHud(id)), id + ": the art replaces the drawn icon");
            }
            foreach (var id in new[] { "blessing", "coins_big", "coins_small", "heal", "reroll", "sale_tag", "skill_book", "upgrade" })
                Assert.That(ScreenArt.Icon(id), Is.SameAs(ArtCatalog.Load(ArtCatalog.RunIcons + "/" + id)).And.Not.Null, id);
            foreach (var id in ScreenArt.ResultIds) Assert.That(ArtCatalog.Result(id), Is.Not.Null, id);
            foreach (var id in new[] { "tab_worm", "patrol", "chapter_rewards", "profile_level", "play_button", "shop", "pass", "event" })
                Assert.That(ArtCatalog.HubIcon(id), Is.Not.Null, "Art/Hub/Icons/" + id);
            foreach (var s in ArtCatalog.SkyStates) Assert.That(ArtCatalog.HubSceneArt("sky_" + s), Is.Not.Null, "sky_" + s);
            foreach (var g in ArtCatalog.SliceGroundIds) Assert.That(ArtCatalog.SliceGround(g), Is.Not.Null, g);
            Assert.That(ArtCatalog.SliceMarker("elite"), Is.Not.Null);
            Assert.That(ArtCatalog.SliceBattle(ArtCatalog.BattleDefault), Is.Not.Null, "the shared side-view battle background ships");
            Assert.That(ArtCatalog.BattleBackground("any_biome_without_art"), Is.SameAs(ArtCatalog.SliceBattle(ArtCatalog.BattleDefault)), "no per-biome art: the default");
        }

        // ---- batch 2 (2026-11-26): aliens3 / enemies3 / skills-v2 / home planet ----

        private static readonly string[] Batch2Aliens =
        {
            "alien_mossling", "alien_shiverpup", "alien_zib", "alien_arcwhisk", "alien_whirrbit", "alien_sporeling",
            "alien_emberkit", "alien_thornqueen", "alien_nebulisk", "alien_solarch", "alien_swarmheart", "alien_chronoslug",
        };
        private static readonly string[] Batch2Enemies = { "volt_beetle", "void_spawn", "tunnel_stalker", "frost_mite", "elite_void_reaver", "elite_frost_knight" };
        private static readonly string[] Batch2Skills =
        {
            "sig_bleed", "sig_fire", "sig_alien", "sig_counter", "ice_armor", "gen_pathfinder", "gen_giant_slayer", "gen_camper", "sig_radiation", "fire_kindle",
        };
        [Test]
        public void Batch2_PicksRouteToTheArtCatalogKeys()
        {
            var plan = ArtImportRules.PlanSlice("/assets", ArtImportRules.ReadPicks(File.ReadAllText(PicksPath)));
            var dests = plan.Select(i => i.Dest).ToList();
            Assert.That(dests, Is.Unique);
            var want = new List<string>();
            want.AddRange(Batch2Aliens.Select(i => "Art/Aliens/" + i + ".png"));
            want.AddRange(Batch2Enemies.Select(i => "Art/Enemies/" + i + ".png"));
            want.AddRange(Batch2Skills.Select(i => "Art/Skills/" + i + ".png"));
            want.AddRange(new[] { 1, 2, 3, 4 }.Select(i => "Art/Hub/Planet/planet_stage" + i + ".png"));
            Assert.That(dests, Is.SupersetOf(want));
            Assert.That(plan.Find(i => i.Dest == "Art/Aliens/alien_chronoslug.png").Source, Does.EndWith("alien_chronoslug_v8/S_s1041.png"));
        }

        [Test]
        public void Batch2_EverySpriteLoads_AliensAndEnemiesByDefId_SkillsAndPlanet()
        {
            foreach (var id in Batch2Aliens) Assert.That(ArtCatalog.Alien(id), Is.Not.Null, id);
            foreach (var id in Batch2Enemies) Assert.That(ArtCatalog.Enemy(id), Is.Not.Null, id);
            foreach (var id in Batch2Skills) Assert.That(ArtCatalog.SkillIcon(id), Is.Not.Null, id);
            for (int i = 1; i <= 4; i++) Assert.That(ArtCatalog.Planet(i), Is.Not.Null, "planet_stage" + i);
            var content = _f.Service.Content;
            foreach (var id in Batch2Aliens) Assert.That(content.AlienType(id), Is.Not.Null, id + " is a real alien species");
            foreach (var id in Batch2Enemies) Assert.That(content.Enemies.ContainsKey(id), Is.True, id + " is a real enemy def");
        }

        // ---- batch 3 (2026-11-26): bosses, status icons, summon / menu icons, the edited enemies, the drone, the portal marker ----

        /// <summary>The enemies held back in batch 2 (now without fire / dirt) and the new null shade.</summary>
        private static readonly string[] Batch3Enemies = { "magma_slug", "cinder_hound", "ash_wraith", "elite_burrow_horror", "null_shade" };
        /// <summary>Boss sprites by their enemy def id (the battle and the boss gate, ArtCatalog.EnemyOrFamily).</summary>
        private static readonly string[] Batch3Bosses = { "boss_frost_leviathan", "boss_gloop_matriarch" };
        private static readonly string[] Batch3Status = { "bleed", "charm", "chill", "delay", "exposed", "freeze", "heat", "radiation", "stun", "guard", "shock" };
        private static readonly string[] Batch3Summon = { "awaken_core", "exchange", "hero_sigil", "packs", "pass", "star", "tasks" };
        /// <summary>
        /// Approved but not shipped: a pending edit that has not landed (luck_gem), or a visible defect (white fill / cut edge,
        /// text on the art), or no enemy def (elder_magma, chrono_wyrm). Batch 4 shipped the cleaned magma_tyrant / void_maw /
        /// prism_tyrant / rust_queen / storm_hive.
        /// </summary>
        private static readonly string[] Batch3HeldBack =
        {
            "Art/Summon/Icons/luck_gem.png", "Art/Summon/Icons/fuel.png", "Art/Summon/Icons/reactor.png",
            "Art/Enemies/boss_hollow_cantor.png", "Art/Enemies/boss_elder_magma.png", "Art/Enemies/boss_chrono_wyrm.png",
        };

        [Test]
        public void Batch3_PicksRouteToTheArtCatalogKeys_AndHoldBackTheRest()
        {
            var plan = ArtImportRules.PlanSlice("/assets", ArtImportRules.ReadPicks(File.ReadAllText(PicksPath)));
            var dests = plan.Select(i => i.Dest).ToList();
            Assert.That(dests, Is.Unique);
            var want = new List<string>();
            want.AddRange(Batch3Enemies.Concat(Batch3Bosses).Select(i => "Art/Enemies/" + i + ".png"));
            want.AddRange(Batch3Status.Select(i => ArtCatalog.StatusFolder + "/" + i + ".png"));
            want.AddRange(Batch3Summon.Select(i => SummonArt.IconFolder + "/" + i + ".png"));
            want.AddRange(new[] { "Art/Sidekicks/sk_drone.png", "Art/Slice/Markers/portal.png", "Art/Skills/gen_guard.png", "Art/Skills/gen_regen.png" });
            Assert.That(dests, Is.SupersetOf(want));
            foreach (var h in Batch3HeldBack) Assert.That(dests, Has.None.EqualTo(h), h + " is held back");
            string Src(string dest) => plan.Find(i => i.Dest == dest).Source.Replace('\\', '/');
            Assert.That(Src("Art/Slice/Markers/portal.png"), Does.EndWith("slice-markers6/portal/N_s5161.png"), "the simpler portal redo");
            Assert.That(Src("Art/Sidekicks/sk_drone.png"), Does.EndWith("aliens-alt/sk_drone__alt/C_s6101.png"));
            Assert.That(Src("Art/Aliens/alien_shiverpup.png"), Does.EndWith("aliens-alt/alien_shiverpup__alt/S_s6101.png"), "the owner-picked second version stays");
            Assert.That(Src("Art/Aliens/alien_swarmheart.png"), Does.EndWith("aliens-alt/alien_swarmheart__alt/C_s6101.png"), "the owner-picked second version stays");
            Assert.That(Src(ArtCatalog.StatusFolder + "/shock.png"), Does.EndWith("status-icons2/shock/N_s7002.png"), "the bolt-only redo");
            Assert.That(plan.Find(i => i.Dest == ArtCatalog.StatusFolder + "/bleed.png").Trim, Is.True, "icons are trimmed");
            Assert.That(plan.Find(i => i.Dest == "Art/Enemies/boss_frost_leviathan.png").Trim, Is.False, "creatures stay square like their peers");
        }

        [Test]
        public void Batch3_EverySpriteLoads_AndReachesItsConsumer()
        {
            var content = _f.Service.Content;
            foreach (var id in Batch3Enemies) { Assert.That(ArtCatalog.Enemy(id), Is.Not.Null, id); Assert.That(content.Enemies.ContainsKey(id), Is.True, id + " is a real enemy def"); }
            foreach (var id in Batch3Bosses)
            {
                Assert.That(ArtCatalog.Enemy(id), Is.Not.Null, id);
                Assert.That(content.Enemies[id].Boss, Is.True, id + " is a boss def");
                var boss = new BattleFighterInfo { Id = id, Role = CombatRole.Boss, Side = BattleSide.Enemy };
                Assert.That(BattlePlaybackScreen.FighterSprite(boss, content, out bool family, out _), Is.SameAs(ArtCatalog.Enemy(id)), id + " fights with its own sprite");
                Assert.That(family, Is.False, id);
            }
            foreach (var kv in content.RunBosses)
                if (Batch3Bosses.Contains(kv.Value.EnemyId))
                    Assert.That(IsoArt.BossMarker(content, kv.Key), Is.SameAs(ArtCatalog.Enemy(kv.Value.EnemyId)), kv.Key + ": the boss gate shows the boss");
            // status icons: every status with art draws it in the battle chips (Poison = radiation, Burn = heat); Stagger keeps its glyph
            foreach (var id in Batch3Status) Assert.That(ArtCatalog.StatusIcon(id), Is.Not.Null, id);
            foreach (StatusId st in System.Enum.GetValues(typeof(StatusId)))
            {
                if (st == StatusId.None) continue;
                var art = ArtCatalog.StatusIcon(ArtCatalog.StatusKey(st));
                if (st == StatusId.Stagger) Assert.That(art, Is.Null, "no stagger art yet: the drawn spiral");
                else Assert.That(StatusArt.Icon(st), Is.SameAs(art), st.ToString());
            }
            Assert.That(ArtCatalog.StatusKey(StatusId.Poison), Is.EqualTo("radiation"));
            Assert.That(ArtCatalog.StatusKey(StatusId.Burn), Is.EqualTo("heat"));
            // summon / menu icons: the summon screens, the shop and the resource bar (awaken cores)
            foreach (var id in Batch3Summon) Assert.That(ArtCatalog.Load(SummonArt.IconFolder + "/" + id), Is.Not.Null, id);
            foreach (var id in Batch3Summon) Assert.That(SummonArt.Icon(id), Is.SameAs(ArtCatalog.Load(SummonArt.IconFolder + "/" + id)), id);
            Assert.That(SummonArt.IconOfCurrency(ProgressionCurrencies.PartAwakenCores), Is.EqualTo("awaken_core"));
            Assert.That(SummonArt.IconOfCurrency(Currencies.GearCores), Is.EqualTo("core"), "gear cores keep their own icon");
            Assert.That(SummonArt.IconOfCurrency(Currencies.HeroSigils), Is.EqualTo("hero_sigil"));
            // the drone sidekick, the portal marker, the skill icons without their text label
            var drone = new BattleFighterInfo { Id = "sk_drone", Role = CombatRole.Summon, Side = BattleSide.Player };
            Assert.That(ArtCatalog.Sidekick("sk_drone"), Is.Not.Null);
            Assert.That(BattlePlaybackScreen.FighterSprite(drone, content, out _, out _), Is.SameAs(ArtCatalog.Sidekick("sk_drone")));
            Assert.That(ArtCatalog.SliceMarker("portal"), Is.Not.Null);
            Assert.That(ArtCatalog.SkillIcon("gen_guard"), Is.Not.Null);
            Assert.That(ArtCatalog.SkillIcon("gen_regen"), Is.Not.Null);
            foreach (var id in Batch3HeldBack) Assert.That(ArtCatalog.Load(id.Replace(".png", "")), Is.Null, id);
        }

        // ---- batch 4 (2026-11-27): the cleaned bosses, the City buildings ----

        /// <summary>The bosses cleaned of their ground shadow / white fill (owner, 2026-11-27).</summary>
        private static readonly string[] Batch4Bosses = { "boss_magma_tyrant", "boss_void_maw", "boss_prism_tyrant", "boss_rust_queen", "boss_storm_hive" };

        [Test]
        public void Batch4_PicksRouteToTheArtCatalogKeys_EveryBlueprintHasItsBuilding()
        {
            var plan = ArtImportRules.PlanSlice("/assets", ArtImportRules.ReadPicks(File.ReadAllText(PicksPath)));
            Assert.That(plan.Select(i => i.Dest), Is.Unique);
            var content = _f.Service.Content;
            foreach (var id in Batch4Bosses)
            {
                var item = plan.Find(i => i.Dest == ArtCatalog.EnemyFolder + "/" + id + ".png");
                Assert.That(item, Is.Not.Null, id);
                Assert.That(item.Source.Replace('\\', '/'), Does.Contain("/bosses/" + id + "/"), id);
                Assert.That(item.Trim, Is.False, "creatures stay square like their peers");
            }
            // held back: hollow_cantor is cut off at the edge; elder_magma and chrono_wyrm have no enemy to show them
            Assert.That(content.Enemies.ContainsKey("boss_hollow_cantor"), Is.True);
            Assert.That(content.Enemies.ContainsKey("boss_elder_magma"), Is.False);
            Assert.That(content.Enemies.ContainsKey("boss_chrono_wyrm"), Is.False);
            Assert.That(content.Blueprints.Count, Is.EqualTo(25));
            foreach (var id in content.Blueprints.Keys)
            {
                var item = plan.Find(i => i.Dest == ArtCatalog.BuildingFolder + "/" + id + ".png");
                Assert.That(item, Is.Not.Null, id + " has its building");
                Assert.That(item.Source.Replace('\\', '/'), Does.Contain("/buildings/" + id + "/"), id);
                Assert.That(item.Trim, Is.True, "trimmed: the sprite is as wide as its foundation plate");
            }
            Assert.That(plan.Count(i => i.Dest.StartsWith(ArtCatalog.BuildingFolder + "/")), Is.EqualTo(content.Blueprints.Count), "one building per blueprint, no strays");
        }

        [Test]
        public void Batch4_BossesFightWithTheirOwnSprite_AndGuardTheirGate()
        {
            var content = _f.Service.Content;
            foreach (var id in Batch4Bosses)
            {
                Assert.That(ArtCatalog.Enemy(id), Is.Not.Null, id);
                Assert.That(content.Enemies[id].Boss, Is.True, id + " is a boss def");
                var boss = new BattleFighterInfo { Id = id, Role = CombatRole.Boss, Side = BattleSide.Enemy };
                Assert.That(BattlePlaybackScreen.FighterSprite(boss, content, out bool family, out _), Is.SameAs(ArtCatalog.Enemy(id)), id + " fights with its own sprite");
                Assert.That(family, Is.False, id);
            }
            int gates = 0;
            foreach (var kv in content.RunBosses)
                if (Batch4Bosses.Contains(kv.Value.EnemyId)) { gates++; Assert.That(IsoArt.BossMarker(content, kv.Key), Is.SameAs(ArtCatalog.Enemy(kv.Value.EnemyId)), kv.Key + ": the boss gate shows the boss"); }
            Assert.That(gates, Is.GreaterThan(0));
        }

        [Test]
        public void Batch4_FindPlate_TheBottomFaceCentreOfAnIsoPlate_NotTheBuildingOnIt()
        {
            // a 200 px wide diamond plate (half depth 60 px, 10 px thick) from row 5, a 60 px wide tower on it up to row 280,
            // and a crane arm sticking out left above the plate
            int w = 220, h = 300;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - 110), half = 100;
                    bool lower = y >= 5 && y < 65 && dx <= half * (y - 5 + 1) / 60f; // bottom face: grows from the bottom vertex
                    bool sides = y >= 65 && y < 75 && dx <= half; // the plate's thickness at its side vertices
                    bool top = y >= 75 && y < 135 && dx <= half * (135 - y) / 60f; // top face: back in
                    bool tower = y >= 60 && y < 280 && dx <= 30;
                    bool arm = y >= 200 && y < 210 && x >= 0 && x < 110;
                    if (lower || sides || top || tower || arm) px[y * w + x] = new Color32(90, 100, 140, 255);
                }
            var c = SpriteContact.FindPlate(px, w, h);
            Assert.That(c.Valid, Is.True);
            Assert.That(c.Bottom, Is.EqualTo(5));
            Assert.That(c.Width, Is.EqualTo(200).Within(2), "the plate, not the arm");
            Assert.That(c.X, Is.EqualTo(110).Within(1));
            Assert.That(c.Y, Is.EqualTo(65).Within(2), "the side vertices' height over the bottom vertex: the bottom face's centre");
            Assert.That(SpriteContact.FindPlate(new Color32[16], 4, 4).Valid, Is.False);
        }

        [Test]
        public void Batch4_EveryBuilding_StandsOnItsPlate_FitsItsFootprint_AndReachesItsIcons()
        {
            var content = _f.Service.Content;
            var go = new GameObject("FitProbe");
            try
            {
                var holder = new GameObject("Body");
                holder.transform.SetParent(go.transform, false);
                var sr = holder.AddComponent<SpriteRenderer>();
                foreach (var def in content.Blueprints.Values)
                {
                    var sprite = ArtCatalog.Building(def.Id);
                    Assert.That(sprite, Is.Not.Null, def.Id);
                    Assert.That(CityArt.HasOwnArt(def), Is.True, def.Id);
                    Assert.That(CityArt.Building(def), Is.SameAs(sprite), def.Id + ": the City draws its own building, not the stand-in");
                    // the import pivot is the plate's bottom-face centre
                    string path = "Assets/Art/Resources/" + ArtCatalog.BuildingFolder + "/" + def.Id + ".png";
                    Assert.That(ArtImportSettings.IsBuilding(path), Is.True);
                    var plate = ArtImportSettings.PlateOfFile(path, out int pw, out int ph);
                    Assert.That(plate.Valid, Is.True, def.Id);
                    var want = SpriteContact.Pivot01(plate, pw, ph);
                    var have = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
                    Assert.That(Vector2.Distance(have, want), Is.LessThan(0.01f), def.Id + ": pivot " + have + " should be the plate centre " + want);
                    Assert.That(plate.Width / pw, Is.GreaterThan(0.9f), def.Id + ": the trimmed sprite is as wide as its plate (FitBuilding scales by it)");
                    Assert.That(have.y, Is.InRange(0.1f, 0.4f), def.Id + ": the plate centre sits in the lower part");
                    // on its footprint (either rotation): the plate spans the footprint width unless the tower hits the height cap
                    foreach (bool rotated in new[] { false, true })
                    {
                        CityLayout.Footprint(def, rotated, out int fw, out int fh);
                        float top = CityWorld.FitBuilding(sr, sprite, true, fw, fh);
                        float fpW = (fw + fh) * Iso.StepX;
                        var size = Vector2.Scale(sprite.bounds.size, sr.transform.localScale);
                        Assert.That(size.x, Is.LessThanOrEqualTo(fpW * CityArt.PlateFill + 1e-3f), def.Id);
                        Assert.That(size.y, Is.LessThanOrEqualTo(CityArt.BuildingMaxHeight(fw, fh) + 1e-3f), def.Id);
                        Assert.That(Mathf.Abs(size.x - fpW * CityArt.PlateFill) < 1e-3f || Mathf.Abs(size.y - CityArt.BuildingMaxHeight(fw, fh)) < 1e-3f, Is.True,
                            def.Id + ": the plate fills the footprint or the height is capped");
                        Assert.That(size.x, Is.GreaterThan(fpW * 0.7f), def.Id + ": reads at its footprint");
                        Assert.That(((Vector2)sr.transform.localPosition).magnitude, Is.LessThan(1e-3f), def.Id + ": the pivot is the contact: the plate centre on the footprint centre");
                        Assert.That(top, Is.GreaterThan(0.5f * size.y).And.LessThan(size.y), def.Id);
                    }
                    // icons: reward slots, the pull reveal
                    var reward = new Reward(RewardKinds.Blueprint, def.Id, 1);
                    Assert.That(SummonArt.RewardIcon(reward), Is.SameAs(sprite), def.Id);
                    Assert.That(PullReveal.ArtFor(reward), Is.Not.Null, def.Id);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Batch4_TheCity_DrawsEachBuildingOnItsFootprint_AndTheCardShowsIt()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            var bps = new[] { "bp_gene_lab", "bp_biomass_farm", "bp_fuel_refinery", "bp_ore_mine", "bp_supply_depot" };
            var grant = new List<Reward> { Reward.Cur(Currencies.Biomass, 9_000_000), Reward.Cur(Currencies.Ore, 9_000_000) };
            foreach (var b in bps) grant.Add(new Reward(RewardKinds.Blueprint, b, 1));
            _f.Service.DevGrant(grant);
            var s = _f.Service.State;
            var c = _f.Service.Content;
            var uids = new Dictionary<string, string>();
            foreach (var bp in bps)
            {
                var spot = CityPlanner.Find(s, c, bp, null, 2, null);
                if (spot == null) continue; // the start land fills up: as many as fit
                if (spot.Streets.Count > 0) _f.Service.ExecuteNow(new CityStreets { Adds = spot.Streets });
                var r = _f.Service.ExecuteNow(new CityPlace { BlueprintId = bp, X = spot.X, Y = spot.Y, Rotated = spot.Rotated });
                if (r.Ok) uids[bp] = (string)r.Payload;
            }
            Assert.That(uids.Keys, Does.Contain("bp_gene_lab"));
            Assert.That(uids.Count, Is.GreaterThanOrEqualTo(2), "the start land holds a few (the showcase digs for all 25)");
            _f.Clock.Advance(13 * 3_600_000L);
            _f.Service.Refresh(_ => { });
            _f.App.Open("city");
            var city = (CityScreen)_f.App.Router.Current;
            foreach (var kv in uids)
            {
                var body = city.World.BodyOf(kv.Value);
                Assert.That(body, Is.Not.Null, kv.Key);
                Assert.That(body.sprite, Is.SameAs(ArtCatalog.Building(kv.Key)), kv.Key);
                Assert.That(city.World.HeightOf(kv.Value), Is.GreaterThan(CityArt.BuildingHeight(1, 1) * 0.8f), kv.Key + ": the pins sit over the roof");
            }
            city.Select(uids["bp_gene_lab"]);
            var card = Named(city.Root, "BuildingCard");
            Assert.That(Image(card, "BuildingArt").sprite, Is.SameAs(ArtCatalog.Building("bp_gene_lab")), "the card shows the building");
        }

        // ---- batch 5 (2026-11-27): four biome elites with their own sprite, the revive's tombstone ----

        /// <summary>The biome elites that drew a regular enemy's sprite (EnemyDef.ArtId) until now, with their source package file.</summary>
        private static readonly (string id, string file)[] Batch5Elites =
        {
            ("elite_stalker_prime", "C_s1621.png"), ("elite_glacier_warden", "C_s10611.png"), ("elite_cinder_alpha", "S_s1622.png"), ("elite_ash_matriarch", "S_s1621.png"),
        };

        [Test]
        public void Batch5_PicksRouteToTheArtCatalogKeys()
        {
            var plan = ArtImportRules.PlanSlice("/assets", ArtImportRules.ReadPicks(File.ReadAllText(PicksPath)));
            Assert.That(plan.Select(i => i.Dest), Is.Unique);
            foreach (var (id, file) in Batch5Elites)
            {
                var item = plan.Find(i => i.Dest == ArtCatalog.EnemyFolder + "/" + id + ".png");
                Assert.That(item, Is.Not.Null, id);
                Assert.That(Path.GetFullPath(item.Source).Replace('\\', '/'), Does.EndWith("/enemies-elites2/" + id + "/" + file), id);
                Assert.That(item.Trim, Is.False, "creatures stay square like their peers");
            }
            var tomb = plan.Find(i => i.Dest == ArtCatalog.RunIcons + "/tombstone.png");
            Assert.That(tomb, Is.Not.Null);
            Assert.That(Path.GetFullPath(tomb.Source).Replace('\\', '/'), Does.EndWith("/run-icons-tombstone/tombstone/N_s6721.png"));
            Assert.That(tomb.Trim, Is.True, "trimmed like the other run icons");
        }

        [Test]
        public void Batch5_TheFourElites_FightWithTheirOwnSprite_TheRestKeepTheirStandIn()
        {
            var content = _f.Service.Content;
            foreach (var (id, _) in Batch5Elites)
            {
                var def = content.Enemies[id];
                Assert.That(def.Elite, Is.True, id);
                Assert.That(def.ArtId, Is.Null, id + ": no stand-in any more");
                var own = ArtCatalog.Enemy(id);
                Assert.That(own, Is.Not.Null, id);
                Assert.That(own.rect.width, Is.EqualTo(512).And.EqualTo(own.rect.height), id + ": square like the other elites");
                Assert.That(own.pivot / own.rect.size, Is.EqualTo(ArtCatalog.Enemy("elite_frost_knight").pivot / ArtCatalog.Enemy("elite_frost_knight").rect.size), id + ": the elites' pivot");
                Assert.That(ArtCatalog.EnemyOrFamily(id, content, out bool family), Is.SameAs(own), id);
                Assert.That(family, Is.False, id + ": drawn as itself, not at the stand-in scale");
                var fighter = new BattleFighterInfo { Id = id, Role = CombatRole.Enemy, Side = BattleSide.Enemy };
                Assert.That(BattlePlaybackScreen.FighterSprite(fighter, content, out bool f2, out _), Is.SameAs(own), id + " fights with its own sprite");
                Assert.That(f2, Is.False, id);
                Assert.That(AnimBoundsTable(id), Is.True, id + " is in anim_bounds.json");
            }
            // the other biome elites still borrow a regular enemy's sprite until their art lands
            var standIns = content.Enemies.Values.Where(e => e.Elite && e.ArtId != null).Select(e => e.Id).OrderBy(i => i).ToList();
            Assert.That(standIns, Is.EquivalentTo(new[] { "elite_lurker_alpha", "elite_volt_monarch", "elite_eel_tyrant", "elite_null_wraith", "elite_grub_mother" }));
        }

        [Test]
        public void Batch5_TheTombstoneLoads_AndTheReviveShowsIt()
        {
            var tomb = ArtCatalog.RunIcon("tombstone");
            Assert.That(tomb, Is.Not.Null);
            Assert.That(RunScreen.TombstoneSprite(), Is.SameAs(tomb), "the art, not a drawn fallback");
            Assert.That(tomb.rect.width, Is.InRange(400, 512));
            Assert.That(tomb.rect.height, Is.InRange(400, 512));
        }

        private static bool AnimBoundsTable(string id)
        {
            var json = File.ReadAllText(Path.Combine(Application.dataPath, "Art/Resources/Art/anim_bounds.json"));
            return json.Contains("\"" + id + "\":");
        }

        [Test]
        public void MaxHp_ShowsTheHeart()
        {
            Assert.That(ArtCatalog.Load(ArtCatalog.RunIcons + "/max_hp"), Is.Null, "no max HP icon of its own");
            Assert.That(ScreenArt.Icon("max_hp"), Is.SameAs(ArtCatalog.SliceHud("heart")).And.Not.Null);
            var fake = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            ArtCatalog.Loader = p => p == ArtCatalog.RunIcons + "/max_hp" ? fake : null;
            Assert.That(ArtCatalog.RunIcon("max_hp"), Is.SameAs(fake), "its own icon wins once one is imported");
        }

        [Test]
        public void Worm_BodyIsTheStandingHero_IconIsTheTabWorm()
        {
            var body = ArtCatalog.Hero();
            Assert.That(body, Is.Not.Null);
            Assert.That(ArtCatalog.WormIcon(), Is.SameAs(ArtCatalog.HubIcon("tab_worm")).And.Not.SameAs(body));
            // the body stands on its footprint (ArtImportSettings: the hero is standing art)
            Assert.That(ArtImportSettings.IsStanding("Assets/Art/Resources/Art/Hero/hero_worm.png"), Is.True);
            var c = SpriteContact.Of(body);
            Assert.That(c.Y / body.rect.height, Is.InRange(0.05f, 0.3f), "the pivot is the ground contact, not the centre");
            Assert.That(c.X / body.rect.width, Is.InRange(0.35f, 0.65f));
        }

        // ---- home ----

        [Test]
        public void SkyState_InvasionThenEventThenNightOrDay()
        {
            var inv = new ActiveLiveEvent { Kind = LiveOpsKind.Invasion };
            var ev = new ActiveLiveEvent { Kind = LiveOpsKind.PlanetaryEvent };
            Assert.That(HubScreen.SkyState(new List<ActiveLiveEvent> { ev, inv }, 12), Is.EqualTo(ArtCatalog.SkyInvasion));
            Assert.That(HubScreen.SkyState(new List<ActiveLiveEvent> { ev }, 23), Is.EqualTo(ArtCatalog.SkyEvent));
            Assert.That(HubScreen.SkyState(new List<ActiveLiveEvent>(), 12), Is.EqualTo(ArtCatalog.SkyDay));
            Assert.That(HubScreen.SkyState(null, HubScreen.NightFrom), Is.EqualTo(ArtCatalog.SkyNight));
            Assert.That(HubScreen.SkyState(null, HubScreen.NightUntil - 1), Is.EqualTo(ArtCatalog.SkyNight));
            Assert.That(HubScreen.SkyState(null, HubScreen.NightUntil), Is.EqualTo(ArtCatalog.SkyDay));
        }

        [Test]
        public void Hub_DrawsTheStatesSky_TheWormTab_TheQuickIcons_AndTheStartRocket()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            foreach (var sky in ArtCatalog.SkyStates)
            {
                HubScreen.SkyOverride = sky;
                _f.App.Router.Reset(new HubScreen());
                var hub = (HubScreen)_f.App.Router.Current;
                Assert.That(hub.Sky, Is.EqualTo(sky));
                Assert.That(Image(hub.Root, "Sky").sprite, Is.SameAs(ArtCatalog.HubSceneArt("sky_" + sky)), sky);
            }
            var root = _f.App.Router.Current.Root;
            Assert.That(Image(Named(root, "Tab:worm"), "Icon").sprite, Is.SameAs(ArtCatalog.HubIcon("tab_worm")));
            Assert.That(Image(Named(root, "Feature:patrol"), "Icon").sprite, Is.SameAs(ArtCatalog.HubIcon("patrol")), "quick_harvest");
            Assert.That(Image(Named(root, "Feature:chapter_rewards"), "Icon").sprite, Is.SameAs(ArtCatalog.HubIcon("chapter_rewards")), "quick_chest");
            Assert.That(Image(Named(root, "Tab:shop"), "Icon").sprite, Is.SameAs(ArtCatalog.HubIcon("shop")), "the Shop tab shows the shop stall");
            Assert.That(Image(Named(root, "Side:pass"), "Icon").sprite, Is.SameAs(ArtCatalog.HubIcon("pass")));
            Assert.That(Image(Named(root, "PlayButton"), "PlayRocket").sprite, Is.SameAs(ArtCatalog.HubIcon("play_button")));
            // the hub's worm on the planet is the body, not the icon
            Assert.That(Image(root, "Worm").sprite, Is.SameAs(ArtCatalog.Hero()));
        }

        [Test]
        public void TopBar_AvatarIsTheWormIcon_LevelOnTheStarBadge()
        {
            _f.App.Bar.Rebuild();
            var chip = Named(_f.Canvas, "ProfileChip");
            Assert.That(Image(chip, "Portrait").sprite, Is.SameAs(ArtCatalog.WormIcon()));
            var badge = Image(chip, "LevelBadge");
            Assert.That(badge.sprite, Is.SameAs(ArtCatalog.HubIcon("profile_level")));
            Assert.That(badge.GetComponentInChildren<Text>().text, Is.EqualTo(_f.Service.State.Worm.Level.ToString()));
        }

        private static Transform Named(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) ?? throw new AssertionException(name + " not found");

        // ---- white backgrounds in imported icons ----
        private static readonly string[] IconFolders = { "Hub/Icons", "Slice/Hud", "Result", "Run/Icons", "Status", "Summon/Icons" };
        /// <summary>
        /// Icons whose art is designed white (dice faces, the sale tag's paper, the banner's pennant; batch 3, by folder: the
        /// hourglass glass of Delay and the summon pass, the Exposed target's face, the Freeze ice cube's highlights).
        /// </summary>
        private static readonly HashSet<string> DesignedWhite = new HashSet<string>
        {
            "dice", "sale_tag", "banner", "Status/delay", "Status/exposed", "Status/freeze", "Summon/Icons/pass",
        };

        private static bool[] Morph(bool[] src, int w, int h, int r, bool erode)
        {
            var tmp = new bool[src.Length]; var dst = new bool[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool acc = erode;
                    for (int k = -r; k <= r && (erode ? acc : !acc); k++)
                    {
                        int xx = x + k; bool v = xx >= 0 && xx < w && src[y * w + xx];
                        if (erode) acc &= v; else acc |= v;
                    }
                    tmp[y * w + x] = acc;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool acc = erode;
                    for (int k = -r; k <= r && (erode ? acc : !acc); k++)
                    {
                        int yy = y + k; bool v = yy >= 0 && yy < h && tmp[yy * w + x];
                        if (erode) acc &= v; else acc |= v;
                    }
                    dst[y * w + x] = acc;
                }
            return dst;
        }

        /// <summary>Thick (9 px opened) opaque near-white regions of an icon file: (area, area touching transparency within 4 px, opaque pixels).</summary>
        private static (int thick, int nearTransparent, int opaque) WhiteStats(string path)
        {
            var tex = new Texture2D(2, 2);
            try
            {
                Assert.That(ImageConversion.LoadImage(tex, File.ReadAllBytes(path)), Is.True, path);
                int w = tex.width, h = tex.height; var px = tex.GetPixels32();
                var white = new bool[px.Length]; var transp = new bool[px.Length]; int opaque = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    if (c.a >= 200)
                    {
                        opaque++;
                        int mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)), mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                        white[i] = mn >= 225 && mx - mn <= 22;
                    }
                    transp[i] = c.a < 128;
                }
                var thick = Morph(Morph(white, w, h, 4, true), w, h, 4, false);
                var near = Morph(transp, w, h, 4, false);
                int t = 0, n = 0;
                for (int i = 0; i < px.Length; i++) { if (thick[i]) { t++; if (near[i]) n++; } }
                return (t, n, opaque);
            }
            finally { Object.DestroyImmediate(tex); }
        }

        [Test]
        public void ImportedIcons_HaveNoOpaqueWhiteBackground()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "Art/Resources/Art"));
            int checkedCount = 0; var bad = new List<string>();
            foreach (var folder in IconFolders)
                foreach (var f in Directory.GetFiles(Path.Combine(root, folder), "*.png"))
                {
                    var id = Path.GetFileNameWithoutExtension(f); checkedCount++;
                    var (thick, near, opaque) = WhiteStats(f);
                    // a baked pale ground shadow / background halo: thick near-white pixels right against transparency
                    if (near > 150) bad.Add(folder + "/" + id + ": " + near + " opaque near-white px against transparency");
                    // a white background trapped inside the art (basket, stall window, gear hole)
                    if (!DesignedWhite.Contains(id) && !DesignedWhite.Contains(folder + "/" + id) && thick > 0.10f * opaque) bad.Add(folder + "/" + id + ": " + (100 * thick / opaque) + "% thick near-white");
                }
            Assert.That(checkedCount, Is.GreaterThan(25));
            Assert.That(bad, Is.Empty, string.Join("\n", bad));
        }

        private static Image Image(Transform root, string name) => Named(root, name).GetComponent<Image>();
    }
}
