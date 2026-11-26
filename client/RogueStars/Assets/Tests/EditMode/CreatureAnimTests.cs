using System;
using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.Editor;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Client.World.Anim;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// The code animation of creatures (spine mesh from the sprite's alpha bounds, clips, battle event mapping, reduced-animation
    /// fallback, vertex budget, no allocation per update) and of props / buildings (shader looks by sprite and blueprint).
    /// </summary>
    public sealed class CreatureAnimTests
    {
        private AppFixture _f;
        private readonly List<UnityEngine.Object> _junk = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            _f = new AppFixture();
            AnimSettings.Enabled = true; // the machinery is tested with animation on (it ships off)
            AnimSettings.Override = false;
            SpineRig.ClearCache();
            AnimBounds.ClearCache();
            BattlePlaybackScreen.Instant = false;
        }

        [TearDown]
        public void TearDown()
        {
            AnimSettings.Enabled = false;
            AnimSettings.Override = null;
            BattlePlaybackScreen.Instant = false;
            ArtCatalog.Loader = null;
            foreach (var o in _junk) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _f.Dispose();
        }

        /// <summary>A 64 × 96 sprite: an opaque ellipse (x 10..53, y 8..87) in transparent padding, pivot at the centre.</summary>
        private Sprite Creature(string name = "test_creature", int w = 64, int h = 96, int x0 = 10, int x1 = 53, int y0 = 8, int y1 = 87)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = x >= x0 && x <= x1 && y >= y0 && y <= y1 ? new Color32(120, 200, 90, 255) : new Color32(0, 0, 0, 0);
            tex.SetPixels32(px);
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100);
            s.name = name;
            _junk.Add(tex); _junk.Add(s);
            return s;
        }

        // ---- the spine mesh -----------------------------------------------------------------------------------------

        [Test]
        public void Rig_IsBuiltFromTheAlphaBounds_WithinTheVertexBudget()
        {
            var s = Creature();
            var rig = SpineRig.For(s);
            Assert.That(rig, Is.Not.Null);
            Assert.That(SpineRig.VertexCount, Is.LessThanOrEqualTo(150), "≤ ~150 vertices per creature");
            Assert.That(rig.Rest.Length, Is.EqualTo(SpineRig.VertexCount));
            Assert.That(SpineRig.Triangles.Length, Is.EqualTo(SpineRig.Cols * SpineRig.Rows * 6));
            Assert.That(rig.Height, Is.EqualTo(80).Within(1), "the spine is as tall as the opaque part (y 8..87), not the padded rect");
            Assert.That(AnimBounds.Of(s).yMin, Is.EqualTo(8), "measured from the pixels"); Assert.That(rig.MinY, Is.EqualTo(8)); Assert.That(rig.MinX, Is.EqualTo(10));
            Assert.That(rig.RootX, Is.EqualTo(31.5f).Within(1.5f), "rooted in the middle of the body");
            foreach (var uv in rig.Uv) Assert.That(uv.x, Is.InRange(0f, 1f)); // inside the texture
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in rig.Rest) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            Assert.That(minY, Is.LessThanOrEqualTo(0f), "the grid starts at the feet (a margin below)");
            Assert.That(maxY, Is.GreaterThanOrEqualTo(rig.Height), "and covers the head");
            Assert.That(SpineRig.For(s), Is.SameAs(rig), "cached per sprite");
        }

        [Test]
        public void Deform_RestPose_KeepsTheGrid_LeanBendsTheHeadTowardForward_SquashWidensTheBase()
        {
            var rig = SpineRig.For(Creature());
            var o = new Vector2[SpineRig.VertexCount];
            var rest = SpinePose.Rest;
            rig.Deform(rest, o);
            for (int i = 0; i < o.Length; i++) Assert.That((o[i] - rig.Rest[i]).magnitude, Is.LessThan(0.01f), "rest pose = the sprite as drawn");

            int topRow = SpineRig.Rows * (SpineRig.Cols + 1);
            foreach (float dir in new[] { 1f, -1f })
            {
                var p = SpinePose.Rest; p.Lean = 0.8f; p.Dir = dir;
                rig.Deform(p, o);
                float topX = 0;
                for (int c = 0; c <= SpineRig.Cols; c++) topX += o[topRow + c].x / (SpineRig.Cols + 1);
                Assert.That(Mathf.Sign(topX), Is.EqualTo(dir), "the head leans toward Dir " + dir);
                Assert.That(Mathf.Abs(topX), Is.GreaterThan(10f));
                Assert.That(Mathf.Abs(o[SpineRig.Cols / 2].x - rig.Rest[SpineRig.Cols / 2].x), Is.LessThan(2f), "the base stays planted");
            }

            var sq = SpinePose.Rest; sq.Squash = 1.4f; sq.Stretch = 0.8f;
            rig.Deform(sq, o);
            float baseW = o[SpineRig.Rows / 4 * (SpineRig.Cols + 1) + SpineRig.Cols].x - o[SpineRig.Rows / 4 * (SpineRig.Cols + 1)].x;
            float restW = rig.Rest[SpineRig.Rows / 4 * (SpineRig.Cols + 1) + SpineRig.Cols].x - rig.Rest[SpineRig.Rows / 4 * (SpineRig.Cols + 1)].x;
            Assert.That(baseW, Is.GreaterThan(restW * 1.2f), "squash fattens the base");
            Assert.That(o[topRow].y, Is.LessThan(rig.Rest[topRow].y * 0.9f), "and shortens the body");
        }

        [Test]
        public void Rig_FallsBackToTheWholeRect_WhenTheTextureCannotBeMeasured()
        {
            var s = Creature("unmeasured");
            // an unreadable texture: simulate through a table miss by a sprite whose texture is not readable
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "unreadable" };
            tex.Apply(false, true); // makeNoLongerReadable
            var sp = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0), 100);
            sp.name = "no_table_entry";
            _junk.Add(tex); _junk.Add(sp);
            AnimBounds.ClearCache();
            var b = AnimBounds.Of(sp);
            Assert.That(b.width, Is.EqualTo(32)); Assert.That(b.height, Is.EqualTo(32));
            Assert.That(SpineRig.For(sp).Height, Is.EqualTo(32));
            Assert.That(AnimBounds.Of(s).width, Is.EqualTo(44), "a readable sprite is measured");
        }

        [Test]
        public void EveryCreatureSprite_IsInTheBoundsTable_WithItsSize()
        {
            AnimBounds.ClearCache();
            int n = 0;
            foreach (var folder in new[] { "Art/Hero", "Art/Enemies", "Art/Aliens", "Art/Sidekicks" })
                foreach (var s in Resources.LoadAll<Sprite>(folder))
                {
                    n++;
                    Assert.That(AnimBounds.Table.TryGetValue(s.name, out var e), Is.True, s.name + " is missing in anim_bounds.json: run Tools/Art/Build Anim Bounds");
                    Assert.That(e[4], Is.EqualTo((int)s.rect.width), s.name + " width"); Assert.That(e[5], Is.EqualTo((int)s.rect.height), s.name + " height");
                    var r = SpineRig.For(s);
                    Assert.That(r.Height, Is.InRange(20f, e[5]), s.name);
                    Assert.That(r.Height, Is.LessThan(e[5]), s.name + ": the spine is shorter than the padded rect");
                }
            Assert.That(n, Is.GreaterThanOrEqualTo(18), "hero + enemies + aliens");
        }

        [Test]
        public void Deform_AndMotion_AllocateNothing()
        {
            var rig = SpineRig.For(Creature());
            var m = new CreatureMotion(new CreatureSpec());
            var o = new Vector2[SpineRig.VertexCount];
            for (int i = 0; i < 50; i++) rig.Deform(m.Tick(0.016f), o); // warm up (JIT)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++)
            {
                if (i % 200 == 0) m.Play((CreatureClip)((i / 200) % 4));
                rig.Deform(m.Tick(0.016f), o);
            }
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(used, Is.EqualTo(0), "no per-frame allocation in the update path");
        }

        // ---- clips ----------------------------------------------------------------------------------------------------

        private static SpinePose Run(CreatureMotion m, float seconds, float dt = 1f / 60)
        {
            var p = m.Pose;
            for (float t = 0; t < seconds; t += dt) p = m.Tick(dt);
            return p;
        }

        [Test]
        public void Clips_OneShotsReturnToIdle_DeathHolds_Counted()
        {
            var m = new CreatureMotion(new CreatureSpec());
            Run(m, 0.5f);
            Assert.That(m.Clip, Is.EqualTo(CreatureClip.Idle));
            foreach (var c in new[] { CreatureClip.Attack, CreatureClip.Hit, CreatureClip.Celebrate })
            {
                m.Play(c);
                Assert.That(m.Clip, Is.EqualTo(c));
                Run(m, m.LengthOf(c) * 0.5f);
                Assert.That(m.Clip, Is.EqualTo(c), c + " still playing mid-way");
                Run(m, m.LengthOf(c) * 0.6f + 0.05f);
                Assert.That(m.Clip, Is.EqualTo(CreatureClip.Idle), c + " ends in idle");
            }
            m.Play(CreatureClip.Move);
            Run(m, 3f);
            Assert.That(m.Clip, Is.EqualTo(CreatureClip.Move), "move loops");
            m.Play(CreatureClip.Move, 0.5f);
            Run(m, 0.7f);
            Assert.That(m.Clip, Is.EqualTo(CreatureClip.Idle), "one hop (limited move) ends");
            m.Play(CreatureClip.Death);
            Run(m, 3f);
            Assert.That(m.Clip, Is.EqualTo(CreatureClip.Death));
            Assert.That(m.Pose.Stretch, Is.LessThan(0.5f), "collapsed");
            m.Play(CreatureClip.Attack);
            Assert.That(m.Clip, Is.EqualTo(CreatureClip.Death), "the dead stay down");
            Assert.That(m.Played[(int)CreatureClip.Attack], Is.EqualTo(1));
            m.Revive();
            Assert.That(m.Clip, Is.EqualTo(CreatureClip.Idle));
        }

        [Test]
        public void Attack_PointsAtTheTarget_PerStyle()
        {
            foreach (var style in new[] { CreatureAttack.Lunge, CreatureAttack.Slam, CreatureAttack.Swoop })
                foreach (float dir in new[] { 1f, -1f })
                {
                    var m = new CreatureMotion(new CreatureSpec { Attack = style, Hover = style == CreatureAttack.Swoop }) { Aim = dir };
                    m.Play(CreatureClip.Attack);
                    float best = 0;
                    var rig = SpineRig.For(Creature());
                    var o = new Vector2[SpineRig.VertexCount];
                    int topRow = SpineRig.Rows * (SpineRig.Cols + 1);
                    for (float t = 0; t < m.Length; t += 1f / 60)
                    {
                        var p = m.Tick(1f / 60);
                        rig.Deform(p, o);
                        best = Mathf.Max(best, o[topRow + 2].x * dir);
                    }
                    Assert.That(best, Is.GreaterThan(5f), style + " toward " + dir + ": the head reaches toward the target");
                }
        }

        [Test]
        public void FlyersHover_GroundCreaturesHop()
        {
            var fly = new CreatureMotion(CreatureSpecs.For("spark_drone", 1f));
            var hop = new CreatureMotion(CreatureSpecs.For("alien_blorp", 1f));
            Assert.That(CreatureSpecs.For("spark_drone", 1f).Hover, Is.True);
            Assert.That(CreatureSpecs.For("alien_glimmerfly", 1f).Hover, Is.True);
            fly.Play(CreatureClip.Move); hop.Play(CreatureClip.Move);
            float flyStretch = 0, hopStretch = 0;
            for (int i = 0; i < 120; i++)
            {
                var a = fly.Tick(1f / 60); var b = hop.Tick(1f / 60);
                flyStretch = Mathf.Max(flyStretch, Mathf.Abs(a.Stretch - 1)); hopStretch = Mathf.Max(hopStretch, Mathf.Abs(b.Stretch - 1));
            }
            Assert.That(flyStretch, Is.LessThan(0.01f), "flyers do not squash");
            Assert.That(hopStretch, Is.GreaterThan(0.05f), "ground creatures squash and stretch");
        }

        [Test]
        public void Specs_DefaultsFollowTheAspect_OverridesWin()
        {
            Assert.That(CreatureSpecs.For("nope", 1.8f).Bend, Is.GreaterThan(1f), "tall = bendy");
            var wide = CreatureSpecs.For("nope", 0.6f);
            Assert.That(wide.Attack, Is.EqualTo(CreatureAttack.Slam), "wide = slam");
            Assert.That(wide.Squash, Is.GreaterThan(1f));
            Assert.That(CreatureSpecs.For("nope", 1f).Amp, Is.EqualTo(1f));
            Assert.That(CreatureSpecs.For("glacier_golem", 3f).Attack, Is.EqualTo(CreatureAttack.Slam), "override beats the aspect");
        }

        // ---- battle events -------------------------------------------------------------------------------------------

        [Test]
        public void EventMapping_AttackHitDeathAbilityVictory()
        {
            foreach (var a in new[] { BattleAction.Basic, BattleAction.Combo, BattleAction.Counter, BattleAction.Skill, BattleAction.Rage })
            {
                Assert.That(CreatureEvents.Actor(a), Is.EqualTo(CreatureClip.Attack), a.ToString());
                Assert.That(CreatureEvents.Target(a, false), Is.EqualTo(CreatureClip.Hit), a.ToString());
                Assert.That(CreatureEvents.Target(a, true), Is.EqualTo(CreatureClip.Move), a + " dodged: a sidestep");
            }
            Assert.That(CreatureEvents.Target(BattleAction.Death, false), Is.EqualTo(CreatureClip.Death));
            Assert.That(CreatureEvents.Actor(BattleAction.SidekickAct), Is.EqualTo(CreatureClip.Move), "a sidekick's turn hops");
            Assert.That(CreatureEvents.Actor(BattleAction.Ability), Is.EqualTo(CreatureClip.Move));
            Assert.That(CreatureEvents.Actor(BattleAction.Heal), Is.Null);
            Assert.That(CreatureEvents.End(true, true), Is.EqualTo(CreatureClip.Celebrate));
            Assert.That(CreatureEvents.End(true, false), Is.Null);
            Assert.That(CreatureEvents.End(false, true), Is.Null);
        }

        private BattleResult Fight()
        {
            var b = new BattleResult { Winner = BattleSide.Player, Rounds = 2 };
            b.Fighters.Add(new BattleFighterInfo { Index = 0, Name = "Worm", Role = CombatRole.Worm, Side = BattleSide.Player, Targetable = true, MaxHp = 1000, StartHp = 1000, EndHp = 900, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 1, Id = "alien_blorp", Name = "Blorp", Role = CombatRole.Alien, Side = BattleSide.Player, MaxHp = 100, StartHp = 100, EndHp = 100, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 2, Id = "ember_imp", Name = "Imp", Role = CombatRole.Enemy, Side = BattleSide.Enemy, Targetable = true, MaxHp = 300, StartHp = 300, EndHp = 0 });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 0, Target = 2, Action = BattleAction.Basic, Amount = 100, TargetHp = 200 });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 2, Target = 0, Action = BattleAction.Basic, Amount = 100, TargetHp = 900 });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 1, Target = 1, Action = BattleAction.SidekickAct, SkillId = "x" });
            b.Log.Add(new BattleLogEntry { Round = 2, Actor = 0, Target = 2, Action = BattleAction.Basic, Amount = 200, TargetHp = 0 });
            b.Log.Add(new BattleLogEntry { Round = 2, Actor = 0, Target = 2, Action = BattleAction.Death, TargetHp = 0 });
            return b;
        }

        [Test]
        public void Battle_FightersAreCreatures_ClipsFollowTheLog_VictoryCelebrates()
        {
            var screen = new BattlePlaybackScreen(Fight());
            _f.App.Router.Push(screen);
            foreach (int i in new[] { 0, 1, 2 }) Assert.That(screen.AnimOf(i), Is.Not.Null, "fighter " + i + " draws as a creature");
            var imp = screen.AnimOf(2);
            Assert.That(imp.Source.enabled, Is.False, "its Image does not draw (the mesh does)");
            Assert.That(imp.VertexCount, Is.LessThanOrEqualTo(150));
            Assert.That(imp.AimScreen, Is.EqualTo(-1f), "the enemy attacks toward the worm (left)"); Assert.That(screen.AnimOf(0).AimScreen, Is.EqualTo(1f));
            for (int i = 0; i < 30 && !screen.Finished; i++) screen.Frame(1f);
            Assert.That(screen.Finished, Is.True);
            Assert.That(screen.AnimOf(0).Motion.Played[(int)CreatureClip.Attack], Is.EqualTo(2), "the worm attacked twice");
            Assert.That(imp.Motion.Played[(int)CreatureClip.Attack], Is.EqualTo(1));
            Assert.That(imp.Motion.Played[(int)CreatureClip.Hit], Is.GreaterThanOrEqualTo(2), "the imp was hit");
            Assert.That(screen.AnimOf(0).Motion.Played[(int)CreatureClip.Hit], Is.EqualTo(1));
            Assert.That(screen.AnimOf(1).Motion.Played[(int)CreatureClip.Move], Is.EqualTo(1), "the sidekick's turn hops");
            Assert.That(imp.Motion.Clip, Is.EqualTo(CreatureClip.Death), "the beaten enemy lies down");
            Assert.That(screen.AnimOf(0).Motion.Played[(int)CreatureClip.Celebrate], Is.EqualTo(1), "victory: the worm celebrates");
            Assert.That(screen.AnimOf(1).Motion.Played[(int)CreatureClip.Celebrate], Is.EqualTo(1), "and its sidekick");
            Assert.That(imp.Motion.Played[(int)CreatureClip.Celebrate], Is.EqualTo(0));
        }

        [Test]
        public void Battle_ReducedAnimation_DrawsTheStaticSprites()
        {
            AnimSettings.Override = true;
            var screen = new BattlePlaybackScreen(Fight());
            _f.App.Router.Push(screen);
            var anim = screen.AnimOf(2);
            Assert.That(anim, Is.Not.Null);
            anim.SyncSettings();
            Assert.That(anim.IsStatic, Is.True);
            Assert.That(anim.Source.enabled, Is.True, "the plain Image draws");
            AnimSettings.Override = false;
            anim.SyncSettings();
            Assert.That(anim.IsStatic, Is.False);
            Assert.That(anim.Source.enabled, Is.False);
        }

        [Test]
        public void Battle_SkipMode_StillEndsInTheRightPose()
        {
            BattlePlaybackScreen.Instant = true;
            var screen = new BattlePlaybackScreen(Fight());
            _f.App.Router.Push(screen);
            screen.Frame(0.01f);
            Assert.That(screen.Finished, Is.True);
            Assert.That(screen.AnimOf(2).Motion.Clip, Is.EqualTo(CreatureClip.Death), "the beaten enemy lies down even when the fight is skipped");
        }

        // ---- UI and world hosts ------------------------------------------------------------------------------------

        [Test]
        public void UiCreature_AttachesToAnImage_KeepsRectAndSprite_IdleOnlyIgnoresClips()
        {
            var s = Creature();
            var holder = Ui.Rect(_f.Canvas, "Holder");
            var img = Ui.Panel(holder, Color.white, "Pic");
            img.sprite = s; img.preserveAspect = true;
            Ui.Size(img.rectTransform, 200, 200, 0, 0);
            var card = UiCreature.Attach(img, true);
            Assert.That(card, Is.Not.Null);
            Assert.That(UiCreature.Attach(img, true), Is.SameAs(card), "idempotent");
            Assert.That(img.sprite, Is.SameAs(s), "the sprite stays on the Image");
            Assert.That(card.rectTransform.anchorMin, Is.EqualTo(Vector2.zero)); Assert.That(card.rectTransform.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(card.raycastTarget, Is.False, "click areas are untouched");
            card.Play(CreatureClip.Attack);
            Assert.That(card.Motion.Clip, Is.EqualTo(CreatureClip.Idle), "cards only idle");
            var before = (Vector2[])card.Positions.Clone();
            card.Tick(0.4f);
            Assert.That(card.Positions, Is.Not.EqualTo(before), "the idle sway moves the mesh");
            Assert.That(UiCreature.Attach(null), Is.Null);
            var empty = Ui.Panel(holder, Color.white, "Empty");
            Assert.That(UiCreature.Attach(empty), Is.Null, "no sprite: nothing to animate");
        }

        [Test]
        public void UiCreature_Mirrored_ByAParentScale_StillAttacksTowardTheTarget()
        {
            var holder = Ui.Rect(_f.Canvas, "Holder");
            var img = Ui.Panel(holder, Color.white, "Pic");
            img.sprite = Creature();
            var c = UiCreature.Attach(img);
            c.AimScreen = 1;
            c.Tick(0.01f);
            Assert.That(c.Motion.Aim, Is.EqualTo(1f));
            img.rectTransform.localScale = new Vector3(-1, 1, 1); // the worm's flip toward its target
            c.AimScreen = 1;
            c.Tick(0.01f);
            Assert.That(c.Motion.Aim, Is.EqualTo(-1f), "in the mirrored local space forward is the other way, so the screen direction holds");
        }

        [Test]
        public void WorldCreature_MirrorsTheSpriteRenderer_PooledMesh_ReducedAnimationShowsTheSprite()
        {
            var s = Creature();
            var go = new GameObject("W");
            _junk.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s; sr.sortingOrder = 7;
            var wc = WorldCreature.Attach(sr);
            Assert.That(wc, Is.Not.Null);
            Assert.That(WorldCreature.Attach(sr), Is.SameAs(wc));
            Assert.That(sr.enabled, Is.False, "the mesh draws instead");
            Assert.That(wc.Renderer.enabled, Is.True);
            Assert.That(wc.VertexCount, Is.EqualTo(SpineRig.VertexCount));
            wc.Tick(0.1f);
            Assert.That(wc.Renderer.sortingOrder, Is.EqualTo(7));
            Assert.That(wc.Renderer.sharedMaterial, Is.Not.Null, "AnimSprite material in Resources");
            // the mesh sits where the sprite would: its centre column on the sprite's pivot x
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var v in wc.Vertices) { minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); }
            Assert.That((minX + maxX) / 2, Is.EqualTo(0f).Within(0.1f));
            AnimSettings.Override = true;
            wc.SyncSettings();
            Assert.That(sr.enabled, Is.True); Assert.That(wc.Renderer.enabled, Is.False);
            AnimSettings.Override = false;
            wc.SyncSettings();
            int pooledBefore = WorldCreature.PooledMeshes;
            wc.Release(); // OnDestroy does the same in play mode
            UnityEngine.Object.DestroyImmediate(go);
            Assert.That(WorldCreature.PooledMeshes, Is.EqualTo(pooledBefore + 1), "the mesh returns to the pool");
            var go2 = new GameObject("W2"); _junk.Add(go2);
            var sr2 = go2.AddComponent<SpriteRenderer>(); sr2.sprite = s;
            WorldCreature.Attach(sr2);
            Assert.That(WorldCreature.PooledMeshes, Is.EqualTo(pooledBefore), "and is reused");
        }

        [Test]
        public void Board_TokenIsAnimated_SlithersOnAHop_AndWalkersMoveInTheCity()
        {
            var level = _f.Service.Content.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            var tc = screen.World.TokenCreature;
            Assert.That(tc, Is.Not.Null, "the worm token is a creature");
            Assert.That(tc.Motion.ExternalHop, Is.True, "the board hop is its own tween");
            Assert.That(tc.Motion.Clip, Is.EqualTo(CreatureClip.Idle));
            screen.World.LiftToken(0.3f);
            Assert.That(tc.Motion.Clip, Is.EqualTo(CreatureClip.Move), "slithers while hopping");
            screen.World.LiftToken(0);
            Assert.That(tc.Motion.Clip, Is.EqualTo(CreatureClip.Idle));
        }

        // ---- props and buildings -------------------------------------------------------------------------------------

        [Test]
        public void PropLooks_EveryLookBuildsAMaterial_EveryBuildingStandInHasOne()
        {
            var all = new List<PropLook>();
            all.AddRange(PropFx.Props.Values); all.AddRange(PropFx.Buildings.Values);
            foreach (var look in all)
            {
                if (look.Kind == FxKind.None) { Assert.That(look.Smoke, Is.True, "a look without a shader is a smoke vent"); continue; }
                var m = PropFx.MakeMaterial(look, 1.5f);
                Assert.That(m, Is.Not.Null, look.Kind + ": base material in Resources/Art/Anim/Materials (run Tools/Art/Anim Materials)");
                Assert.That(m.shader.name, Does.StartWith("RogueStars/Anim/"));
                UnityEngine.Object.DestroyImmediate(m);
            }
            foreach (var id in CityArt.BuildingArtKey.Keys)
                Assert.That(PropFx.LookOfBuilding(id), Is.Not.Null, id + " has a look in the building table");
            Assert.That(PropFx.Buildings["bp_biomass_farm"].Kind, Is.EqualTo(FxKind.Sway), "farms sway");
            foreach (var id in new[] { "bp_laboratory", "bp_shard_reactor", "bp_signal_beacon", "bp_command_spire", "bp_diamond_mine" })
                Assert.That(PropFx.Buildings[id].Kind, Is.EqualTo(FxKind.Pulse), id + " pulses");
            Assert.That(PropFx.Buildings["bp_fuel_refinery"].Smoke, Is.True, "the refinery smokes");
            Assert.That(PropFx.Buildings["bp_plate_foundry"].Smoke, Is.True); Assert.That(PropFx.Buildings["bp_plate_foundry"].AutoMask, Is.True, "the foundry's fire is found by colour");
            Assert.That(PropFx.Props["portal"].Kind, Is.EqualTo(FxKind.Swirl), "the portal is a swirl shader, not video");
            Assert.That(PropFx.Props["campfire_big"].Embers, Is.True);
        }

        [Test]
        public void PropFx_AppliesAnInstance_AddsExtras_RevertsWhenReduced()
        {
            var s = Creature("campfire_big");
            var go = new GameObject("Fire"); _junk.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            var plain = sr.sharedMaterial;
            var fx = PropFx.Apply(sr, PropFx.LookOfSprite(s));
            Assert.That(fx, Is.Not.Null);
            Assert.That(sr.sharedMaterial, Is.Not.SameAs(plain));
            Assert.That(sr.sharedMaterial.shader.name, Is.EqualTo("RogueStars/Anim/FlameFlicker"));
            Assert.That(fx.HasEmbers, Is.True); Assert.That(fx.HasGlow, Is.True);
            AnimSettings.Override = true;
            fx.Refresh();
            Assert.That(sr.sharedMaterial, Is.SameAs(plain), "reduced animation: the plain sprite");
            Assert.That(go.transform.Find("FxEmbers").gameObject.activeSelf, Is.False, "and no particles");
            AnimSettings.Override = false;
            fx.Refresh();
            Assert.That(sr.sharedMaterial.shader.name, Is.EqualTo("RogueStars/Anim/FlameFlicker"));
            // two copies never share a phase
            var go2 = new GameObject("Fire2"); _junk.Add(go2);
            go2.transform.position = new Vector3(3, 1, 0);
            var sr2 = go2.AddComponent<SpriteRenderer>(); sr2.sprite = s;
            var fx2 = PropFx.Apply(sr2, PropFx.LookOfSprite(s));
            Assert.That(fx2.Instance.GetFloat("_Phase"), Is.Not.EqualTo(fx.Instance.GetFloat("_Phase")));
            Assert.That(PropFx.Apply(sr, null), Is.Null);
            var autoGo = new GameObject("Foundry"); _junk.Add(autoGo);
            var asr = autoGo.AddComponent<SpriteRenderer>(); asr.sprite = Creature("chest_common");
            var afx = PropFx.Apply(asr, PropFx.LookOfBuilding("bp_plate_foundry"));
            Assert.That(afx.Instance.GetFloat("_AutoMask"), Is.EqualTo(1f));
        }

        [Test]
        public void AnimationDisabled_EverythingIsStatic_AndNothingAnimates()
        {
            AnimSettings.Enabled = false;
            AnimSettings.Override = false; // even a forced "Full" cannot switch it on
            Assert.That(AnimSettings.Reduced, Is.True);
            var host = new GameObject("host", typeof(RectTransform)).GetComponent<RectTransform>(); _junk.Add(host.gameObject);
            var img = new GameObject("img", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
            img.transform.SetParent(host, false); _junk.Add(img.gameObject);
            var tex = new Texture2D(8, 8); _junk.Add(tex);
            img.sprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0)); _junk.Add(img.sprite);
            var c = UiCreature.Attach(img);
            Assert.That(img.enabled, Is.True, "the plain sprite draws");
            c.Play(CreatureClip.Attack);
            c.Tick(0.2f);
            Assert.That(img.enabled, Is.True);
            Assert.That(AnimSettings.Reduced, Is.True);
        }

        [Test]
        public void AnimLevel_AutoResolvesByDevice_FullAndReducedForce()
        {
            var weakRam = new DeviceSpecs(2048, 8, 4000);
            var weakCpu = new DeviceSpecs(8000, 2, 4000);
            var weakGpu = new DeviceSpecs(8000, 8, 512);
            var strong = new DeviceSpecs(8000, 8, 4000);
            var unknown = new DeviceSpecs(0, 0, 0);
            Assert.That(AnimSettings.Resolve(AnimLevel.Auto, weakRam), Is.True);
            Assert.That(AnimSettings.Resolve(AnimLevel.Auto, weakCpu), Is.True);
            Assert.That(AnimSettings.Resolve(AnimLevel.Auto, weakGpu), Is.True);
            Assert.That(AnimSettings.Resolve(AnimLevel.Auto, strong), Is.False);
            Assert.That(AnimSettings.Resolve(AnimLevel.Auto, unknown), Is.False, "unknown specs: Full");
            Assert.That(AnimSettings.Resolve(AnimLevel.Auto, new DeviceSpecs(3700, 6, 3700)), Is.False, "a 4 GB phone is Full");
            Assert.That(AnimSettings.Resolve(AnimLevel.Full, weakRam), Is.False);
            Assert.That(AnimSettings.Resolve(AnimLevel.Reduced, strong), Is.True);
            Assert.That(AnimSettings.Label(AnimLevel.Auto, false), Is.EqualTo("Auto (Full)"));
            Assert.That(AnimSettings.Label(AnimLevel.Auto, true), Is.EqualTo("Auto (Reduced)"));
            Assert.That(AnimSettings.Label(AnimLevel.Reduced, false), Is.EqualTo("Reduced"));
        }

        [Test]
        public void Settings_AnimationLevel_CyclesIsSavedAndTakesEffectAtOnce()
        {
            AnimSettings.Override = null;
            PlayerPrefs.DeleteKey(AnimSettings.Key); PlayerPrefs.DeleteKey("rs.reducedAnim");
            AnimSettings.ResetCache();
            AnimSettings.Device = new DeviceSpecs(2048, 8, 4000); // a weak phone
            try
            {
                Assert.That(AnimSettings.Level, Is.EqualTo(AnimLevel.Auto), "default (an old reduced toggle value is simply ignored)");
                Assert.That(AnimSettings.Reduced, Is.True, "Auto on a weak phone");
                _f.App.Router.Push(new SettingsScreen());
                Button Toggle() { foreach (var b in _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true)) if (b.name == "AnimLevel") return b; return null; }
                string Text() => Toggle().GetComponentInChildren<Text>().text;
                Assert.That(Toggle(), Is.Not.Null, "Settings has the animation level control");
                Assert.That(Text(), Does.Contain("Auto (Reduced)"));
                Toggle().onClick.Invoke();
                Assert.That(AnimSettings.Level, Is.EqualTo(AnimLevel.Full));
                Assert.That(AnimSettings.Reduced, Is.False, "Full forces it on the weak phone, at once");
                Assert.That(PlayerPrefs.GetInt(AnimSettings.Key), Is.EqualTo(1), "saved per device");
                Assert.That(Text(), Does.Contain("Full"));
                Toggle().onClick.Invoke();
                Assert.That(AnimSettings.Level, Is.EqualTo(AnimLevel.Reduced));
                Assert.That(AnimSettings.Reduced, Is.True);
                Toggle().onClick.Invoke();
                Assert.That(AnimSettings.Level, Is.EqualTo(AnimLevel.Auto));
                AnimSettings.ResetCache(); // a fresh start reads the saved value
                Assert.That(AnimSettings.Level, Is.EqualTo(AnimLevel.Auto));
                AnimSettings.Level = AnimLevel.Reduced; AnimSettings.ResetCache();
                Assert.That(AnimSettings.Level, Is.EqualTo(AnimLevel.Reduced), "persisted");
            }
            finally { PlayerPrefs.DeleteKey(AnimSettings.Key); AnimSettings.ResetCache(); AnimSettings.Device = null; AnimSettings.Override = false; }
        }
    }
}
