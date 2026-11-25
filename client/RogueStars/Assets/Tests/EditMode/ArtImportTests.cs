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
            Assert.That(ArtCatalog.SliceBattle("battle_meadow"), Is.Null, "the side-view battle backgrounds are only tested, not shipped");
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
            Assert.That(Image(Named(root, "Side:shop"), "Icon").sprite, Is.SameAs(ArtCatalog.HubIcon("shop")));
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

        private static Image Image(Transform root, string name) => Named(root, name).GetComponent<Image>();
    }
}
