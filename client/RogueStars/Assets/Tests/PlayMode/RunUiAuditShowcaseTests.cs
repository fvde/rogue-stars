using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Run UI audit (playtest 0.1.359, docs/design/run-ui-glitches.md): every run UI state at 1080×2340 and 720×1600 — the
    /// board HUD and footer, fork, event card, outcome sheet, chest, campfire, shop, level-up draft, Mythic choice, signature
    /// pick, quest card, revive, battle playback (mid-fight, victory, defeat) and the run result (before / after doubling) —
    /// plus an automatic layout log per shot ("[audit]": truncated or overflowing text, text off screen, overlapping
    /// labels, and the battle fighters' sprite vs HP bar centres). Explicit; run WITHOUT -nographics
    /// (ROGUESTARS_SHOTS_DIR = output folder, ROGUESTARS_SHOT_SIZES = "1080x2340,720x1600").
    /// </summary>
    [Explicit("renders the run UI audit screenshots; run with -testFilter RunUiAuditShowcaseTests")]
    public sealed class RunUiAuditShowcaseTests
    {
        private const long T0 = 1_772_452_800_000L;
        private int W = 1080, H = 2340;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots, _tag;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;
        private readonly StringBuilder _log = new StringBuilder();

        [UnityTest, Timeout(1800000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "runaudit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_runaudit", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "run-ui-audit"));
            Directory.CreateDirectory(_shots);
            var sizes = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZES") ?? "1080x2340,720x1600").Split(',');
            SetupCapture(1080, 2340);
            yield return Wait(0.6f);
            var c = _svc.Content;
            RunScreen.BoardSpeed = 1;

            // a geared player at level 12, every feature open and celebrated
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

            foreach (var size in sizes)
            {
                var wh = size.Split('x');
                SetupCapture(int.Parse(wh[0]), int.Parse(wh[1]));
                _tag = wh[0];
                yield return Wait(0.3f);
                yield return Section("board", Board(c));
                yield return Section("fork", Fork(c));
                yield return Section("decisions", Decisions(c));
                yield return Section("shop", Shop(c));
                yield return Section("quest", Quest(c));
                yield return Section("signature", Signature(c));
                yield return Section("battle", Battles(c));
                yield return Section("result", Results(c));
            }
            File.WriteAllText(Path.Combine(_shots, "audit_log.txt"), _log.ToString());
        }

        /// <summary>Runs a section; an exception is logged and the next section goes on.</summary>
        private IEnumerator Section(string name, IEnumerator body)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception ex)
                {
                    Note("[section-error] " + _tag + " " + name + ": " + ex);
                    yield break;
                }
                yield return cur;
            }
            Ui.Clear(_app.ModalLayer);
        }

        // ---- sections ------------------------------------------------------------------------------------------------

        private ActiveRun FreshRun(ContentDb c, int levelNumber)
        {
            if (_svc.State.Run.Active != null) _svc.ExecuteNow(new RunAbandon());
            var level = c.MainLevel(levelNumber);
            _svc.State.Wallet.Add(Currencies.Stamina, 200);
            var r = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(r.Ok, Is.True, r.Error);
            var run = _svc.State.Run.Active;
            run.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            run.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            run.Coins = 291;
            return run;
        }

        private RunScreen ShowRun()
        {
            SkillTooltip.Close();
            Ui.Clear(_app.ModalLayer);
            _app.Router.Reset(new HubScreen());
            var rs = new RunScreen();
            _app.Router.Push(rs);
            rs.SkipCoach();
            return rs;
        }

        private IEnumerator Board(ContentDb c)
        {
            var run = FreshRun(c, 12);
            GiveSkills(c, run, 9);
            run.Xp = 7;
            var path = MainPath(run);
            MoveTo(run, path, 4);
            ShowRun();
            yield return Wait(1.6f);
            yield return Shot("board");
            yield return ShotCrop("board_hud", 0, 0, 1, 0.2f);
            yield return ShotCrop("board_footer", 0, 0.76f, 1, 0.24f);
            // the dice mid-roll and the hop
            _svc.ExecuteNow(new RunRoll());
            if (_app.Router.Current is RunScreen rs) rs.Rebuild();
            yield return Wait(0.5f);
            yield return Shot("board_rolling");
            for (int f = 0; f < 900; f++)
            {
                if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                if (_app.Router.Current is RunScreen s && !s.Animating) break;
                yield return null;
            }
            yield return Wait(0.8f);
            yield return Shot("board_after_roll");
            // the hint line, the auto state, a long name on the tray
            Ui.Clear(_app.ModalLayer);
        }

        private IEnumerator Fork(ContentDb c)
        {
            var run = FreshRun(c, 12);
            for (int guard = 0; guard < 6 && run.Phase != RunPhase.ChooseBranch; guard++)
            {
                var path = MainPath(run);
                int fork = path.FindIndex(id => run.Tiles[id].Next.Count > 1);
                if (fork < 0) { MoveTo(run, path, Math.Min(15, path.Count - 1)); typeof(RunSystem).GetMethod("EnsureBoardAhead", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { c, run, 60 }); continue; }
                if (fork > 1) MoveTo(run, path, fork - 1);
                run.ExtraDiceNext = 2;
                _svc.ExecuteNow(new RunRoll());
                run = _svc.State.Run.Active;
                if (run.Phase != RunPhase.ChooseBranch && run.Phase != RunPhase.AwaitRoll) { run.Phase = RunPhase.AwaitRoll; run.EventId = null; run.DraftOffer.Clear(); }
            }
            ShowRun();
            yield return Wait(1.6f);
            Note("[fork] " + _tag + " phase " + run.Phase);
            yield return Shot("fork");
        }

        private IEnumerator Decisions(ContentDb c)
        {
            var run = FreshRun(c, 12);
            GiveSkills(c, run, 5);
            ShowRun();
            yield return Wait(1.0f);

            foreach (var (id, shot) in new[] { ("abandoned_cache", "event_card"), ("smuggler", "event_vendor"), ("treasure_pod", "chest_pod"), ("campfire", "campfire") })
            {
                OpenEvent(run, id);
                yield return Rebuild(1.2f);
                yield return Shot(shot);
            }
            if (Click("Option:Train")) { yield return Wait(0.8f); yield return Shot("campfire_train"); }
            Reset(run);

            // the outcome sheet after a chance option
            OpenEvent(run, "strange_spores");
            yield return Rebuild(0.2f);
            Ui.Clear(_app.ModalLayer);
            _svc.ExecuteNow(new RunChooseEventOption { Index = 0 });
            if (_app.Router.Current is RunScreen rs1) rs1.Rebuild();
            yield return Wait(1.0f);
            yield return Shot("outcome_spinning");
            yield return Wait(2.6f);
            yield return Shot("outcome_sheet");
            run = _svc.State.Run.Active;
            Reset(run);
            // an outcome sheet after the treasure pod (chest)
            OpenEvent(run, "treasure_pod");
            yield return Rebuild(0.2f);
            Ui.Clear(_app.ModalLayer);
            _svc.ExecuteNow(new RunChooseEventOption { Index = 0 });
            if (_app.Router.Current is RunScreen rs2) rs2.Rebuild();
            yield return Wait(3.4f);
            yield return Shot("chest_outcome");
            run = _svc.State.Run.Active;
            Reset(run);

            // level-up draft and the Mythic choice (chest intro, then the cards)
            if (OpenDraft(c, run, false)) { yield return Rebuild(1.2f); yield return Shot("levelup_draft"); Reset(run); }
            if (OpenDraft(c, run, true))
            {
                yield return Rebuild(1.2f);
                yield return Shot("mythic_chest");
                if (Click("ChestTap")) { yield return Wait(1.4f); yield return Shot("mythic_choice"); }
                Reset(run);
            }

            // revive
            run.Phase = RunPhase.Defeated;
            run.WormHp = 0;
            yield return Rebuild(1.0f);
            yield return Shot("revive");
            run.WormHp = run.WormMaxHp;
            Reset(run);
        }

        private IEnumerator Shop(ContentDb c)
        {
            var run = FreshRun(c, 12);
            ShowRun();
            yield return Wait(0.8f);
            var fill = typeof(RunSystem).GetMethod("FillShop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            run.ShopsVisited = 1;
            fill.Invoke(null, new object[] { c, run, false });
            if (!run.Shop.Exists(o => o.ListPrice > 0))
            {
                var o = run.Shop.Find(x => x.Kind == ShopOfferKind.Skill);
                if (o != null) { o.ListPrice = o.Price; o.Price /= 2; }
            }
            run.Phase = RunPhase.Shop;
            yield return Rebuild(1.2f);
            yield return Shot("shop");
            // hurt: + the heal; one offer bought
            run.WormHp = run.WormMaxHp * 0.3;
            run.Shop.Clear();
            fill.Invoke(null, new object[] { c, run, false });
            run.Shop[0].Sold = true;
            run.Coins = 40;
            yield return Rebuild(1.2f);
            yield return Shot("shop_hurt_sold_poor");
            run.WormHp = run.WormMaxHp;
            Reset(run);
        }

        private IEnumerator Quest(ContentDb c)
        {
            var run = FreshRun(c, 12);
            var q = c.RunQuest(RogueStars.Rules.Content.Quests.QuestMothwick.Id);
            if (q == null) { Note("[quest] none"); yield break; }
            var s = _svc.State;
            if (!s.RunQuests.Quests.TryGetValue(q.Id, out var p)) s.RunQuests.Quests[q.Id] = p = new QuestProgress();
            foreach (var node in new[] { "m04", "m27", "m18a" })
            {
                p.Node = node; p.Completed = false; p.CoinBonus = 1;
                run.Quest = new RunQuestRun { QuestId = q.Id, Placed = true, Tile = run.Position, Met = true, CoinBonus = 1, BonusName = "Starglint" };
                run.Coins = 420;
                run.Phase = RunPhase.Quest;
                ShowRun();
                yield return Wait(1.0f);
                yield return Shot("quest_" + node);
            }
            Reset(run);
        }

        private IEnumerator Signature(ContentDb c)
        {
            var offer = c.Skills.Values.Where(x => x.Signature).Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).Take(3).ToList();
            Ui.Clear(_app.ModalLayer);
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.4f);
            SignaturePick.Show(_app.ModalLayer, c, offer, _ => { });
            yield return Wait(1.0f);
            yield return Shot("signature_pick");
            Ui.Clear(_app.ModalLayer);
        }

        private IEnumerator Battles(ContentDb c)
        {
            var run = FreshRun(c, 12);
            var skills = FiringSkills(c, 7);
            foreach (var (name, enemies, mount, weak) in new[]
            {
                ("battle_two_enemies", new[] { "cinder_hound", "cinder_hound" }, "skiff", false),
                ("battle_three_enemies", new[] { "ember_imp", "magma_slug", "ash_wraith" }, "skiff", false),
                ("battle_elite_no_mount", new[] { "elite_lava_brute" }, null, false),
                ("battle_miniboss_champion", new[] { "mb_cinder_alpha" }, "skiff", false),
                ("battle_boss_defeat", new[] { "boss_magma_tyrant" }, "skiff", true),
            })
            {
                var battle = SkillBattle(c, skills, enemies, mount, weak);
                var ctx = BattleHudContext.FromRun(_svc.State.Run.Active, c);
                ctx.Skills = skills;
                ctx.Levels = new Dictionary<string, int>();
                for (int i = 0; i < skills.Count; i++) ctx.Levels[skills[i]] = 1 + i % 3;
                Ui.Clear(_app.ModalLayer);
                _app.Router.Reset(new HubScreen());
                var playback = new BattlePlaybackScreen(battle, ctx);
                _app.Router.Push(playback);
                yield return Wait(0.5f);
                yield return Shot(name + "_start");
                LogFighters(name);
                yield return Wait(1.6f);
                yield return Shot(name + "_mid");
                yield return ShotCrop(name + "_mid_hud", 0, 0, 1, 0.2f);
                yield return ShotCrop(name + "_mid_footer", 0, 0.72f, 1, 0.28f);
                playback.PlayAll();
                yield return Wait(0.7f);
                yield return Shot(name + "_end");
                playback.Continue();
                yield return Wait(0.2f);
            }
            _app.Router.Reset(new HubScreen());
        }

        private IEnumerator Results(ContentDb c)
        {
            if (_svc.State.Run.Active != null) _svc.ExecuteNow(new RunAbandon());
            var level = c.MainLevel(12);
            foreach (var won in new[] { true, false })
            {
                _svc.State.Run.LastSummary = Summary(c, level, won);
                Ui.Clear(_app.ModalLayer);
                _app.Router.Reset(new HubScreen());
                _app.Router.Push(new RunResultScreen());
                yield return Wait(1.6f);
                Ui.Clear(_app.ModalLayer); // the pull reveal over a Legendary item
                yield return Wait(0.2f);
                yield return Shot(won ? "result_victory" : "result_defeat");
            }
            // a real won run's summary, doubled
            _svc.State.Run.LastSummary = Summary(c, level, true);
            _svc.State.Run.LastSummary.Doubled = true;
            foreach (var r in _svc.State.Run.LastSummary.Granted) if (r.Kind == RewardKinds.Currency) r.Amount *= 2;
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunResultScreen());
            yield return Wait(1.4f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("result_victory_doubled");
            _app.Router.Reset(new HubScreen());
        }

        // ---- helpers ---------------------------------------------------------------------------------------------------

        private static RunSummary Summary(ContentDb c, LevelDef level, bool won)
        {
            var s = new RunSummary { RunId = "show", LevelId = level.Id, Mode = RunMode.Normal, Won = won, Rolls = won ? 31 : 17, Level = won ? 9 : 6, Minibosses = won ? 2 : 1, ShopBuys = 3, LongestRolls = 33 };
            s.Gains.Add(new RunGain { Source = "draft" });
            if (won)
            {
                s.Granted.Add(Reward.Cur(Currencies.Biomass, 1250));
                s.Granted.Add(Reward.Cur(Currencies.Ore, 340));
                s.Granted.Add(Reward.Cur(Currencies.Diamonds, 30));
                s.Granted.Add(Reward.Cur(Currencies.AlienTokens, 1));
                s.Granted.Add(new Reward(RewardKinds.Part, "*", 1, 2));
                s.Granted.Add(Reward.Cur(Currencies.HeroXp, 38));
                s.Doublable.AddRange(s.Granted);
                s.BiomassBonus = true;
                s.NewFeats.Add(1);
            }
            return s;
        }

        private void OpenEvent(ActiveRun run, string id)
        {
            run.Phase = RunPhase.Event;
            run.EventId = id;
            run.EventTile = -1;
        }

        private void Reset(ActiveRun run)
        {
            Ui.Clear(_app.ModalLayer);
            run = _svc.State.Run.Active ?? run;
            run.Phase = RunPhase.AwaitRoll;
            run.EventId = null;
            run.MythicDraft = false;
            run.DraftOffer.Clear();
        }

        private IEnumerator Rebuild(float wait)
        {
            Ui.Clear(_app.ModalLayer);
            if (_app.Router.Current is RunScreen rs) rs.Rebuild();
            yield return Wait(wait);
        }

        private bool Click(string name)
        {
            foreach (var b in _app.ModalLayer.GetComponentsInChildren<Button>(false))
                if (b.name == name && b.interactable) { b.onClick.Invoke(); return true; }
            return false;
        }

        private static bool OpenDraft(ContentDb c, ActiveRun run, bool mythic)
        {
            var offer = new List<string>();
            foreach (var s in c.Skills.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (offer.Count >= 3) break;
                if (s.Source != SkillSource.Run || !s.Draftable || s.Signature || s.RequiresFamilyCount > 0 || s.Requires.Count > 0 || run.Skills.Contains(s.Id)) continue;
                var tier = RunTuning.RunTier(s.Rarity);
                if (mythic ? tier != Rarity.Mythic : tier == Rarity.Mythic) continue;
                offer.Add(s.Id);
            }
            if (offer.Count < 3) return false;
            run.DraftOffer.Clear();
            run.DraftOffer.AddRange(offer);
            run.Phase = RunPhase.Draft;
            run.PendingDrafts = Math.Max(1, run.PendingDrafts);
            run.MythicDraft = mythic;
            return true;
        }

        private static void GiveSkills(ContentDb c, ActiveRun run, int n)
        {
            int added = 0;
            foreach (var s in c.Skills.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (added >= n) break;
                if (s.Source != SkillSource.Run || !s.Draftable || s.Signature || s.RequiresFamilyCount > 0 || s.Requires.Count > 0 || run.Skills.Contains(s.Id)) continue;
                if (added % 3 == 1 && RunTuning.RunTier(s.Rarity) != Rarity.Legendary) continue;
                run.Skills.Add(s.Id);
                if (added % 2 == 0) run.SkillLevels[s.Id] = 2;
                added++;
            }
        }

        private static List<string> FiringSkills(ContentDb c, int n)
        {
            var list = new List<string>();
            var families = new List<SkillFamily>();
            foreach (var s in c.Skills.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (list.Count >= n) break;
                if (s.Source != SkillSource.Run || !s.Draftable || s.RequiresFamilyCount > 0 || s.Requires.Count > 0) continue;
                if (s.Triggers == null || s.Triggers.Count == 0) continue;
                if (families.Contains(s.Family) && list.Count < 4) continue;
                families.Add(s.Family);
                list.Add(s.Id);
            }
            return list;
        }

        private static BattleResult SkillBattle(ContentDb c, List<string> skills, string[] enemies, string mount, bool weak)
        {
            var setup = new BattleSetup { RoundCap = 12 };
            var lo = new CombatLoadout { MountId = mount };
            lo.WormStats.Add(new StatMod(StatId.Hp, weak ? 900 : 4000)).Add(new StatMod(StatId.Atk, weak ? 20 : 90)).Add(new StatMod(StatId.Speed, 100)).Add(new StatMod(StatId.CritChance, 25));
            int k = 0;
            foreach (var id in c.AlienTypes.Keys.OrderBy(x => x, StringComparer.Ordinal)) { if (k++ >= 2) break; lo.Aliens.Add(new CombatAlien { AlienTypeId = id, Name = id }); }
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, skills));
            foreach (var e in enemies) setup.Enemy.Add(BattleSimulator.BuildEnemy(c, e, 4));
            return BattleSimulator.Simulate(setup, c, new Rng(5));
        }

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
            for (int i = 1; i <= index && i < path.Count; i++) run.Trail.Add(path[i]);
            run.Position = path[Math.Min(index, path.Count - 1)];
        }

        // ---- the layout log --------------------------------------------------------------------------------------------

        private void Note(string line)
        {
            _log.AppendLine(line);
            Debug.Log(line);
        }

        private Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = _cam.WorldToScreenPoint(corners[0]);
            var b = _cam.WorldToScreenPoint(corners[2]);
            // top-left origin (like the PNGs)
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), H - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), H - Mathf.Min(a.y, b.y));
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 5; i++, t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        private static readonly Regex Tags = new Regex("<[^>]+>");

        /// <summary>Flags truncated / overflowing / off-screen / overlapping visible texts of the current shot.</summary>
        private void Audit(string shot)
        {
            var texts = new List<(Text t, Rect r)>();
            var all = _app.Root.GetComponentsInChildren<Text>(false).Concat(_app.ModalLayer.GetComponentsInChildren<Text>(false));
            foreach (var t in all)
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.color.a < 0.2f) continue;
                var cg = t.GetComponentInParent<CanvasGroup>();
                if (cg != null && cg.alpha < 0.2f) continue;
                var r = ScreenRect(t.rectTransform);
                if (r.width < 2 || r.height < 2) continue;
                string plain = Tags.Replace(t.text, "");
                int glyphs = plain.Count(ch => !char.IsWhiteSpace(ch));
                var gen = t.cachedTextGenerator;
                int visible = gen != null ? gen.characterCountVisible : glyphs;
                // characterCountVisible counts spaces too: compare to the whitespace-free count only when clearly short
                if (t.verticalOverflow == VerticalWrapMode.Truncate && visible < glyphs - 1)
                    Note($"[audit] {_tag} {shot} TRUNCATED '{Short(plain)}' visible {visible}/{glyphs} size {t.fontSize}{(t.resizeTextForBestFit ? " bestfit min " + t.resizeTextMinSize : "")} at {Fmt(r)} {PathOf(t.transform)}");
                if (!t.resizeTextForBestFit)
                {
                    float sc = r.width / Mathf.Max(1, t.rectTransform.rect.width);
                    if (t.horizontalOverflow == HorizontalWrapMode.Overflow && t.preferredWidth * sc > r.width + 6)
                        Note($"[audit] {_tag} {shot} H-OVERFLOW '{Short(plain)}' pref {t.preferredWidth * sc:F0} > {r.width:F0} at {Fmt(r)} {PathOf(t.transform)}");
                    if (t.verticalOverflow == VerticalWrapMode.Overflow && t.preferredHeight * sc > r.height + 6)
                        Note($"[audit] {_tag} {shot} V-OVERFLOW '{Short(plain)}' pref {t.preferredHeight * sc:F0} > {r.height:F0} at {Fmt(r)} {PathOf(t.transform)}");
                }
                if (r.xMin < -2 || r.xMax > W + 2 || r.yMin < -2 || r.yMax > H + 2)
                {
                    if (r.xMax > 0 && r.xMin < W && r.yMax > 0 && r.yMin < H)
                        Note($"[audit] {_tag} {shot} OFFSCREEN '{Short(plain)}' at {Fmt(r)} {PathOf(t.transform)}");
                    continue;
                }
                texts.Add((t, r));
            }
            for (int i = 0; i < texts.Count; i++)
                for (int j = i + 1; j < texts.Count; j++)
                {
                    var a = texts[i]; var b = texts[j];
                    if (a.t.transform.IsChildOf(b.t.transform) || b.t.transform.IsChildOf(a.t.transform)) continue;
                    // the drawn glyph boxes (not the rects): the preferred size inside the rect, by its alignment
                    var ga = GlyphBox(a.t, a.r); var gb = GlyphBox(b.t, b.r);
                    float ix = Mathf.Min(ga.xMax, gb.xMax) - Mathf.Max(ga.xMin, gb.xMin);
                    float iy = Mathf.Min(ga.yMax, gb.yMax) - Mathf.Max(ga.yMin, gb.yMin);
                    if (ix <= 4 || iy <= 4) continue;
                    float small = Mathf.Min(ga.width * ga.height, gb.width * gb.height);
                    if (ix * iy > 0.25f * small)
                        Note($"[audit] {_tag} {shot} OVERLAP '{Short(Tags.Replace(a.t.text, ""))}' x '{Short(Tags.Replace(b.t.text, ""))}' at {Fmt(ga)} / {Fmt(gb)} {PathOf(a.t.transform)} | {PathOf(b.t.transform)}");
                }
        }

        private static Rect GlyphBox(Text t, Rect r)
        {
            var gen = t.cachedTextGenerator;
            if (gen == null || gen.characterCount == 0) return r;
            float sc = r.width / Mathf.Max(1, t.rectTransform.rect.width);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var verts = gen.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            if (minX > maxX) return r;
            // verts are local to the text's rect (pivot origin), in canvas units / pixelsPerUnit
            float ppu = t.pixelsPerUnit > 0 ? t.pixelsPerUnit : 1;
            var rt = t.rectTransform;
            float px = rt.pivot.x * rt.rect.width, py = rt.pivot.y * rt.rect.height;
            float x0 = r.xMin + (minX / ppu + px) * sc, x1 = r.xMin + (maxX / ppu + px) * sc;
            float yTop = r.yMax - (maxY / ppu + py) * sc, yBot = r.yMax - (minY / ppu + py) * sc;
            return Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(yTop, yBot), Mathf.Max(x0, x1), Mathf.Max(yTop, yBot));
        }

        private static string Short(string s) { s = s.Replace("\n", "\\n"); return s.Length > 40 ? s.Substring(0, 40) + "…" : s; }
        private static string Fmt(Rect r) => $"[{r.xMin:F0},{r.yMin:F0} {r.width:F0}x{r.height:F0}]";

        /// <summary>Each battle fighter: its sprite's rect and drawn (opaque) centre vs its HP bar's centre, in shot px.</summary>
        private void LogFighters(string shot)
        {
            foreach (var root in _app.Root.GetComponentsInChildren<RectTransform>(false).Where(t => t.name.StartsWith("Fighter:")))
            {
                var sprite = root.Find("Sprite") as RectTransform;
                var hp = root.Find("HpHolder") as RectTransform;
                if (sprite == null || hp == null || !hp.gameObject.activeInHierarchy) continue;
                var img = sprite.GetComponent<Image>();
                var sr = ScreenRect(sprite);
                var hr = ScreenRect(hp);
                string drawn = "-";
                if (img != null && img.sprite != null)
                {
                    var box = OpaqueBox(img.sprite);
                    // preserveAspect: the sprite is fitted into the rect, centred
                    var spr = img.sprite.rect;
                    float fit = Mathf.Min(sr.width / spr.width, sr.height / spr.height);
                    float ox = sr.center.x - spr.width * fit / 2, oy = sr.center.y - spr.height * fit / 2;
                    bool flipped = sprite.localScale.x < 0;
                    float cx = (box.center.x - spr.x) * fit;
                    if (flipped) cx = spr.width * fit - cx;
                    drawn = $"opaque centre x {ox + cx:F0} (art box {box.width:F0}x{box.height:F0} of {spr.width:F0}x{spr.height:F0}{(flipped ? ", flipped" : "")}, sprite {img.sprite.name})";
                }
                Note($"[fighter] {_tag} {shot} {root.name} sprite rect centre x {sr.center.x:F0} [{Fmt(sr)}] | hp bar centre x {hr.center.x:F0} [{Fmt(hr)}] | {drawn}");
            }
        }

        private readonly Dictionary<Sprite, Rect> _boxes = new Dictionary<Sprite, Rect>();

        /// <summary>The bounding box of the sprite's opaque pixels (alpha > 0.1) in texture px (the texture need not be readable).</summary>
        private Rect OpaqueBox(Sprite s)
        {
            if (_boxes.TryGetValue(s, out var cached)) return cached;
            var tex = s.texture;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var rect = s.textureRect;
            int x0 = (int)rect.x, y0 = (int)rect.y, w = (int)rect.width, h = (int)rect.height;
            var px = read.GetPixels(x0, y0, w, h);
            int minX = w, maxX = -1, minY = h, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 0.1f) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            UnityEngine.Object.Destroy(read);
            var box = maxX < 0 ? new Rect(s.rect.x, s.rect.y, w, h) : new Rect(s.rect.x + minX, s.rect.y + minY, maxX - minX + 1, maxY - minY + 1);
            _boxes[s] = box;
            return box;
        }

        // ---- capture ---------------------------------------------------------------------------------------------------

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
                _cam.cullingMask = ~(1 << World.WorldBoard.Layer);
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

        private IEnumerator Shot(string name) => ShotCrop(name, 0, 0, 1, 1);

        /// <summary>A part of the screen (fractions of the shot, top-left origin) at full resolution.</summary>
        private IEnumerator ShotCrop(string name, float fx, float fy, float fw, float fh)
        {
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_app.Router.Current is RunScreen run && run.World != null && run.World.Cam.enabled) run.World.Cam.Render();
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            var bytes = _tex.EncodeToPNG();
            if (fw < 1 || fh < 1)
            {
                int x = (int)(fx * W), w = (int)(fw * W), h = (int)(fh * H), y = H - (int)(fy * H) - h;
                var part = new Texture2D(w, h, TextureFormat.RGB24, false);
                part.SetPixels(_tex.GetPixels(x, y, w, h));
                part.Apply();
                bytes = part.EncodeToPNG();
                UnityEngine.Object.Destroy(part);
            }
            else Audit(name);
            File.WriteAllBytes(Path.Combine(_shots, _tag + "_" + name + ".png"), bytes);
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
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
