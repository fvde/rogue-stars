using System.IO;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.Editor;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using UnityEditor;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// UI kit v2 batch 1 (2026-11-28): every approved kit piece is picked into <c>Resources/Art/Ui/Kit/&lt;id&gt;</c>, loads as a
    /// sprite by id, carries the doc's 9-slice border (fixed pieces none), and the kit helpers pick the coloured sprites up.
    /// </summary>
    public sealed class KitArtImportTests
    {
        [SetUp] public void SetUp() => KitArt.ClearCache();
        [TearDown] public void TearDown() => KitArt.ClearCache();

        private static string PicksPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../tools/art/slice-picks.json"));

        [Test]
        public void PicksFile_RoutesEveryKitId_ToTheKitFolder()
        {
            var plan = ArtImportRules.PlanSlice("/assets", ArtImportRules.ReadPicks(File.ReadAllText(PicksPath)));
            Assert.That(plan.Select(i => i.Dest), Is.Unique);
            foreach (var id in KitImportRules.All)
            {
                var item = plan.Find(i => i.Dest == "Art/Ui/Kit/" + id + ".png");
                Assert.That(item, Is.Not.Null, id);
                Assert.That(item.Trim, Is.EqualTo(KitImportRules.IsIcon(id)), id + ": pieces stay full-frame (borders), icons are trimmed");
            }
            Assert.That(plan.Any(i => i.Dest == "Art/Ui/Kit/copy_diamond.png"), Is.False, "tiles show the copies as xN");
            Assert.That(KitImportRules.All.Count(), Is.EqualTo(41 + 4));
        }

        [Test]
        public void EveryKitId_LoadsAsSprite_NineSlicesHaveBorders()
        {
            foreach (var id in KitImportRules.All)
            {
                var s = KitArt.Get(id);
                Assert.That(s, Is.Not.Null, id + " loads from Resources/" + KitArt.Folder);
                var b = s.border;
                if (KitImportRules.NineSlice.TryGetValue(id, out var spec))
                {
                    Assert.That(b.x > 0 && b.y > 0 && b.z > 0 && b.w > 0, Is.True, id + " has a 9-slice border, got " + b);
                    Assert.That(b.x, Is.EqualTo(Mathf.Round(spec.border * s.rect.width / spec.w)), id + " border scaled to the file's width");
                    // a pill's border may take its full height (timer_chip: 40 px, border 20)
                    Assert.That(b.x + b.z, Is.LessThanOrEqualTo(s.rect.width), id);
                    Assert.That(b.y + b.w, Is.LessThanOrEqualTo(s.rect.height), id);
                }
                else Assert.That(b, Is.EqualTo(Vector4.zero), id + " is a fixed piece");
            }
        }

        [Test]
        public void KitTextures_ImportAsUiSprites()
        {
            foreach (var id in KitImportRules.All)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath("Assets/Art/Resources/Art/Ui/Kit/" + id + ".png");
                Assert.That(ti, Is.Not.Null, id);
                Assert.That(ti.textureType, Is.EqualTo(TextureImporterType.Sprite), id);
                Assert.That(ti.mipmapEnabled, Is.False, id);
                Assert.That(ti.filterMode, Is.EqualTo(FilterMode.Bilinear), id);
                Assert.That(ti.textureCompression, Is.EqualTo(TextureImporterCompression.CompressedHQ), id);
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect), id);
            }
        }

        [Test]
        public void KitHelpers_UseTheV2Sprites()
        {
            Assert.That(KitV2.PipGemId(PartTileView.GemColor(1)), Is.EqualTo("pip_gem_blue"));
            Assert.That(KitV2.PipGemId(PartTileView.GemColor(2)), Is.EqualTo("pip_gem_purple"));
            Assert.That(KitV2.PipGemId(PartTileView.GemColor(3)), Is.EqualTo("pip_gem_gold"));
            Assert.That(KitV2.PipGemId(Color.green), Is.Null, "other colours stay code-drawn and tinted");
            Assert.That(KitV2.CurrencyIcon(Currencies.Stamina), Is.SameAs(KitArt.Get("icon_stamina")));
            Assert.That(KitV2.CurrencyIcon(Currencies.Diamonds), Is.SameAs(KitArt.Get("icon_diamond")));
            Assert.That(KitV2.CurrencyIcon(Currencies.Ore), Is.SameAs(KitArt.Get("icon_ore")));

            var go = new GameObject("KitHost", typeof(RectTransform));
            try
            {
                var dot = Kit.NotifyDot(go.transform);
                var img = dot.GetComponent<UnityEngine.UI.Image>();
                Assert.That(img.sprite, Is.SameAs(KitArt.Get("badge_red")), "the old red dot draws kit v2's badge_red");
                Assert.That(img.color, Is.EqualTo(Color.white), "untinted");
                var chip = Kit.CurrencyChip(go.transform, Currencies.Diamonds, "12");
                Assert.That(chip.GetComponent<UnityEngine.UI.Image>().sprite, Is.SameAs(KitArt.Get("currency_pill")));
                Assert.That(chip.GetComponent<UnityEngine.UI.Image>().type, Is.EqualTo(UnityEngine.UI.Image.Type.Sliced));
                var strip = KitV2.PipStrip(go.transform, 0, 0, new System.Collections.Generic.List<Color?> { PartTileView.GemColor(3) }, 2);
                var gem = strip.Find("Gem").GetComponent<UnityEngine.UI.Image>();
                Assert.That(gem.sprite, Is.SameAs(KitArt.Get("pip_gem_gold")));
                Assert.That(gem.color, Is.EqualTo(Color.white));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
