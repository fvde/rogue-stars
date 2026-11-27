using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Playtest 0.1.325 pictures: the signature pick at the first run's start, the shop (3 skills + stat perk, and + heal while
    /// hurt), the miniboss marker on its tile, a fork both of whose ways land on one tile (before / after the roll: no choice),
    /// battles with enemies drawn from a family sprite (no red glow under them) and the run HUD at 1080×2340 and 720×1600.
    /// Explicit; run WITHOUT -nographics (ROGUESTARS_SHOTS_DIR = output folder).
    /// </summary>
    [Explicit("renders the playtest 0.1.325 screenshots; run with -testFilter Playtest325ShowcaseTests")]
    public sealed class Playtest325ShowcaseTests
    {
        private const long T0 = 1_772_452_800_000L;
        private int W = 1080, H = 2340;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        [UnityTest, Timeout(900000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "p325_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_p325", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "playtest-325"));
            Directory.CreateDirectory(_shots);
            SetupCapture(1080, 2340);
            yield return Wait(0.6f);
            var c = _svc.Content;
            RunScreen.BoardSpeed = 1;

            // 1. once the signature pick is unlocked (the second unlock: level 3's runs on), every run starts with it (3 cards)
            int from = c.Campaign.SignatureChoiceFromLevel;
            _svc.State.Counters[Requirements.MainLevelCounter] = from - 1;
            for (int n = 1; n < from; n++) _svc.State.Run.Levels[c.MainLevelId(n)] = new LevelProgress { Cleared = true, Clears = 1 };
            var l1 = c.MainLevel(from);
            _app.Open("portals");
            yield return Wait(0.6f);
            bool started = false;
            SignaturePick.StartRun(_app, new RunStart { PortalId = l1.PortalId, LevelId = l1.Id, Mode = RunMode.Normal }, r => started = r.Ok);
            yield return Wait(0.8f);
            Assert.That(_app.ModalLayer.GetComponentsInChildren<Button>(true).Count(b => b.name.StartsWith("Button:Signature:")), Is.EqualTo(3));
            yield return Shot("s1_signature_pick_first_run");
            _app.ModalLayer.GetComponentsInChildren<Button>(true).First(b => b.name.StartsWith("Button:Signature:")).onClick.Invoke();
            yield return Wait(0.3f);
            Assert.That(started, Is.True, "the pick starts the run");
            _svc.ExecuteNow(new RunAbandon());

            // a geared player at level 12 for the rest
            _svc.DevGrantStarterKit();
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                foreach (var uid in PartsSystem.SortedPartUids(_svc.State))
                {
                    if (PartsSystem.IsEquipped(_svc.State, uid)) continue;
                    if (_svc.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok) break;
                }
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            _svc.Refresh(_ => { });
            foreach (var f in c.FeatureOrder) { _svc.State.Onboarding.Unlocked[f.Id] = T0; _svc.State.Onboarding.Celebrated.Add(f.Id); }

            // 2. the board two tiles before the miniboss lair (its marker on its tile), the HUD at 1080 and at 720
            var level = c.MainLevel(8);
            Assert.That(_svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = _svc.State.Run.Active;
            run.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            run.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            var path = MainPath(run);
            int lair = path.FindIndex(id => run.Tiles[id].TileId == "miniboss");
            Assert.That(lair, Is.GreaterThan(2), "a lair on the path");
            MoveTo(run, path, lair - 2);
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunScreen());
            yield return Wait(1.6f);
            yield return Shot("s3_board_miniboss_marker");
            yield return ShotCrop("s3_board_miniboss_marker_crop", new Rect(140, 700, 800, 900));
            foreach (var mk in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var holder = mk.transform.parent;
                if (holder == null || !holder.name.StartsWith("Marker:") || holder.parent == null) continue;
                var tile = holder.parent;
                var sp = mk.sprite;
                var top = tile.Find("Top")?.GetComponent<SpriteRenderer>();
                var blk = tile.Find("Block")?.GetComponent<SpriteRenderer>();
                if (sp != null && sp.name != "rs_softshadow" && (holder.name == "Marker:miniboss" || holder.name == "Marker:elite"))
                    Debug.Log("[p325top] " + holder.name + " topBounds=" + (top != null ? top.bounds.center.ToString("F3") + " size=" + top.bounds.size.ToString("F3") : "-")
                        + " blockBounds=" + (blk != null ? blk.bounds.center.ToString("F3") + " size=" + blk.bounds.size.ToString("F3") : "-")
                        + " pedestalC=" + ((Vector2)mk.transform.position + new Vector2((174.5f - sp.pivot.x) / 100f, (97f - sp.pivot.y) / 100f) * mk.transform.lossyScale.x).ToString("F3")
                        + " screenPivot=" + Camera.main?.WorldToScreenPoint(mk.transform.position).ToString("F1"));
            }
            yield return ShotCrop("s6_hud_1080x2340", new Rect(0, 0, 1080, 420));
            SetupCapture(720, 1600);
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunScreen());
            yield return Wait(1.2f);
            yield return Shot("s6_hud_720x1600_full");
            yield return ShotCrop("s6_hud_720x1600", new Rect(0, 0, 720, 290));
            SetupCapture(1080, 2340);
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunScreen());
            yield return Wait(1.0f);

            // 3. the shop: full HP = 3 skills + a stat perk; hurt = + the heal
            var screen = (RunScreen)_app.Router.Current;
            run = _svc.State.Run.Active;
            path = MainPath(run);
            run.Tiles[path[1]].TileId = "shop";
            run.Coins = 500;
            _app.Session.Run(new RunRoll());
            for (int f = 0; f < 1200 && OfferSlots() == 0; f++) yield return null;
            yield return Wait(0.8f);
            Assert.That(OfferSlots(), Is.EqualTo(4), "3 skills + a stat perk");
            yield return Shot("s2a_shop_full_hp");
            var reroll = _app.ModalLayer.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.StartsWith("Button:Reroll"));
            if (reroll != null) { reroll.onClick.Invoke(); yield return Wait(0.9f); yield return Shot("s2b_shop_after_reroll"); }
            Ui.Clear(_app.ModalLayer);
            _svc.ExecuteNow(new RunShopLeave());
            run = _svc.State.Run.Active;
            run.WormHp = run.WormMaxHp * 0.3;
            path = MainPath(run);
            run.Tiles[path[1]].TileId = "shop";
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunScreen());
            yield return Wait(0.8f);
            _app.Session.Run(new RunRoll());
            for (int f = 0; f < 1200 && OfferSlots() == 0; f++) yield return null;
            yield return Wait(0.8f);
            Assert.That(OfferSlots(), Is.EqualTo(5), "3 skills + the heal + a stat perk");
            yield return Shot("s2c_shop_hurt_heal_and_perk");
            Ui.Clear(_app.ModalLayer);
            _svc.ExecuteNow(new RunShopLeave());

            // 4. a fork whose arms are equally long: a long roll walks past the join on either way, so the worm just moves
            run = _svc.State.Run.Active;
            run.WormHp = run.WormMaxHp;
            int fork = -1;
            for (int guard = 0; guard < 3 && fork < 0; guard++)
            {
                fork = EqualFork(run);
                if (fork < 0) MoveTo(run, MainPath(run), Math.Min(20, MainPath(run).Count - 1));
            }
            Assert.That(fork, Is.GreaterThanOrEqualTo(0), "an equal-armed fork ahead");
            typeof(RunSystem).GetMethod("EnsureBoardAhead", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { c, run, 120 }); // built far enough that the roll appends nothing new
            path = MainPath(run);
            int at = path.IndexOf(fork);
            // the long roll must not meet a forced stop or another fork (those are real choices): plain path after this fork
            foreach (var arm in new[] { run.Tiles[fork].Next[0], run.Tiles[fork].Next[1] })
                foreach (var id in Walk(run, arm)) if (c.BoardTiles.TryGetValue(run.Tiles[id].TileId ?? "", out var td) && td.ForceStop) run.Tiles[id].TileId = "empty";
            foreach (var id in path.Skip(at + 1))
            {
                var t = run.Tiles[id];
                if (t.Next.Count > 1) t.Next.RemoveRange(1, t.Next.Count - 1);
                if (c.BoardTiles.TryGetValue(t.TileId ?? "", out var td) && td.ForceStop) t.TileId = "empty";
            }
            MoveTo(run, path, Math.Max(0, at - 1));
            run.ExtraDicePermanent = 6; // 8 dice (8–48 steps, ~28): the move walks past the join
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunScreen());
            yield return Wait(1.4f);
            screen = (RunScreen)_app.Router.Current;
            yield return Shot("s4a_fork_before_roll");
            int landed = screen.LastLandedTile;
            _app.Session.Run(new RunRoll());
            Assert.That(_svc.State.Run.Active.Phase, Is.Not.EqualTo(RunPhase.ChooseBranch), "same landing: no fork prompt");
            for (int f = 0; f < 2400; f++)
            {
                if (_app.Router.Current is BattlePlaybackScreen bp) { bp.PlayAll(); bp.Continue(); } // landed on a fight: back to the board
                if (_app.Router.Current == screen && screen.LastLandedTile != landed && !screen.Animating) break;
                yield return null;
            }
            yield return Wait(1.5f);
            if (_app.Router.Current != screen) { _app.Router.Reset(new HubScreen()); _app.Router.Push(new RunScreen()); yield return Wait(1.2f); }
            Ui.Clear(_app.ModalLayer);
            yield return Wait(0.2f);
            Assert.That(_svc.State.Run.Active.Trail, Does.Contain(run.Tiles[fork].Next[0]), "it took the default arm");
            yield return Shot("s4b_fork_after_roll_auto_moved");

            // 5. battles: enemies drawn from a family sprite (they had the red glow under them), with a boss and an elite
            var famRegular = c.Enemies.Keys.OrderBy(k => k, StringComparer.Ordinal).Where(id => { ArtCatalog.EnemyOrFamily(id, c, out bool fam); return fam; }).ToList();
            Debug.Log("[p325] enemies on a family sprite: " + string.Join(", ", famRegular));
            int shot = 0;
            foreach (var group in new[] { famRegular.Take(3).ToList(), famRegular.Skip(3).Take(3).ToList() })
            {
                if (group.Count == 0) continue;
                var battle = SampleBattle(c, group);
                var playback = new BattlePlaybackScreen(battle, new BattleHudContext { InRun = true });
                _app.Router.Push(playback);
                yield return Wait(0.8f);
                yield return Shot("s5_battle_family_sprites_" + (++shot));
                playback.Continue();
                if (_app.Router.Current == playback) _app.Router.Pop();
                yield return null;
            }
            var mixed = SampleBattle(c, new List<string> { "cinder_hound", "frost_mite", "tunnel_stalker" });
            var mp = new BattlePlaybackScreen(mixed, new BattleHudContext { InRun = true });
            _app.Router.Push(mp);
            yield return Wait(0.8f);
            yield return Shot("s5_battle_own_sprites");
        }

        /// <summary>A fork on the main path ahead whose two arms rejoin after the same number of steps (or -1).</summary>
        private static int EqualFork(ActiveRun run)
        {
            foreach (var id in MainPath(run).Skip(1))
            {
                var t = run.Tiles[id];
                if (t.Next.Count != 2) continue;
                var a = Walk(run, t.Next[0]); var b = Walk(run, t.Next[1]);
                int ia = a.FindIndex(x => b.Contains(x));
                if (ia >= 0 && ia == b.IndexOf(a[ia])) return id;
            }
            return -1;
        }

        private static List<int> Walk(ActiveRun run, int from)
        {
            var list = new List<int>();
            for (int id = from, g = 0; id >= 0 && g < 30; g++) { list.Add(id); var t = run.Tiles[id]; id = t.Next.Count > 0 ? t.Next[0] : -1; }
            return list;
        }

        private int OfferSlots() => _app.ModalLayer.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("OfferSlot:"));

        private static List<int> MainPath(ActiveRun run)
        {
            var list = new List<int>();
            int id = run.Position;
            for (int guard = 0; guard < 400 && id >= 0; guard++)
            {
                list.Add(id);
                var t = run.Tiles[id];
                if (t.Next.Count == 0) break;
                id = t.Next[0];
            }
            return list;
        }

        private static void MoveTo(ActiveRun run, List<int> path, int index)
        {
            for (int i = 1; i <= index; i++) run.Trail.Add(path[i]);
            run.Position = path[index];
        }

        private static BattleResult SampleBattle(ContentDb c, List<string> enemies)
        {
            var setup = new BattleSetup { RoundCap = 12 };
            var lo = new CombatLoadout();
            lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 400)).Add(new StatMod(StatId.Speed, 100)).Add(new StatMod(StatId.CritChance, 25));
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c));
            foreach (var id in enemies) setup.Enemy.Add(BattleSimulator.BuildEnemy(c, id, 4));
            return BattleSimulator.Simulate(setup, c, new Rng(5));
        }

        private void SetupCapture(int w, int h)
        {
            W = w; H = h;
            if (_rt != null) { if (_cam != null) _cam.targetTexture = null; _rt.Release(); }
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            if (_cam == null)
            {
                var go = new GameObject("ShowcaseCamera");
                _cam = go.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Color.black;
                _cam.orthographic = true;
                _cam.nearClipPlane = 0.1f;
                _cam.farClipPlane = 10;
            }
            _cam.targetTexture = _rt;
            var canvas = _app.Root.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _cam;
            canvas.planeDistance = 1;
            var fitter = _app.Root.GetComponent<SafeAreaFitter>();
            if (fitter != null) fitter.enabled = false;
            Ui.Stretch(_app.Root);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        }

        private IEnumerator Shot(string name) => ShotCrop(name, new Rect(0, 0, W, H));

        /// <summary>A part of the screen (top-left origin, px) at full resolution.</summary>
        private IEnumerator ShotCrop(string name, Rect topLeft)
        {
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            var bytes = _tex.EncodeToPNG();
            if (topLeft.width < W || topLeft.height < H)
            {
                int x = (int)topLeft.x, h = (int)topLeft.height, y = H - (int)topLeft.y - h, w = (int)topLeft.width;
                var part = new Texture2D(w, h, TextureFormat.RGB24, false);
                part.SetPixels(_tex.GetPixels(x, y, w, h));
                part.Apply();
                bytes = part.EncodeToPNG();
                UnityEngine.Object.Destroy(part);
            }
            File.WriteAllBytes(Path.Combine(_shots, name + ".png"), bytes);
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            BattlePlaybackScreen.Instant = false;
            ArtCatalog.Loader = null;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
