using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// UI responsiveness probe (explicit; <c>-testFilter UiResponsivenessProbeTests</c>, run WITHOUT -nographics): times the
    /// synchronous work of typical interactions (state-change rebuilds, opening screens, tab switches, back, run roll / draft /
    /// skills book) with GameObject counts and managed allocations, plus the online per-response JSON cost. Optional
    /// <c>$ROGUESTARS_PROBE_STATE</c>: a GET /v1/state body (wire JSON) used as the player (e.g. a 60-day dev jump), else a
    /// populated offline player. Writes the table to <c>$ROGUESTARS_PROBE_OUT</c> (default Temp/ui_probe.txt) and the log.
    /// </summary>
    [Explicit("measurement harness; run with -testFilter UiResponsivenessProbeTests")]
    public sealed class UiResponsivenessProbeTests
    {
        private readonly StringBuilder _out = new StringBuilder();
        private ClientApp _app;
        private LocalGameService _svc;
        private RectTransform _canvas;

        private void Line(string s) { _out.AppendLine(s); Debug.Log("[probe] " + s); }

        /// <summary>Managed bytes allocated so far; Unity's Mono returns 0 (then the alloc columns read 0: profile on device).</summary>
        private static long Alloc() => GC.GetAllocatedBytesForCurrentThread();

        private int GoCount() => _canvas.GetComponentsInChildren<Transform>(true).Length;

        /// <summary>Runs <paramref name="act"/> <paramref name="reps"/> times: sync ms (first / median), next frame ms, KB allocated (0 under Mono), GOs after.</summary>
        private IEnumerator Measure(string name, Action act, int reps = 5, Action between = null)
        {
            var ms = new List<double>();
            var frame = new List<double>();
            var kb = new List<double>();
            var sw = new Stopwatch();
            for (int i = 0; i < reps; i++)
            {
                if (between != null && i > 0) { between(); yield return null; yield return null; }
                long a0 = Alloc();
                sw.Restart();
                try { act(); }
                catch (Exception ex) { Line(name + ": threw " + ex.GetType().Name + ": " + ex.Message); yield break; }
                sw.Stop();
                ms.Add(sw.Elapsed.TotalMilliseconds);
                kb.Add((Alloc() - a0) / 1024.0);
                sw.Restart();
                yield return null; // the frame that lays out / renders the result
                sw.Stop();
                frame.Add(sw.Elapsed.TotalMilliseconds);
                yield return null;
            }
            Line(string.Format("{0,-44} first {1,7:F1} ms | median {2,7:F1} ms | next frame {3,6:F1} ms | alloc {4,8:F0} KB | GOs {5}",
                name, ms[0], Median(ms), Median(frame), Median(kb), GoCount()));
        }

        private static double Median(List<double> v)
        {
            var s = v.OrderBy(x => x).ToList();
            return s[s.Count / 2];
        }

        private IEnumerator Idle(string name)
        {
            var sw = new Stopwatch();
            var f = new List<double>();
            for (int i = 0; i < 90; i++) { sw.Restart(); yield return null; sw.Stop(); if (i >= 10) f.Add(sw.Elapsed.TotalMilliseconds); }
            Line(string.Format("{0,-44} idle frame median {1:F2} ms", name, Median(f)));
        }

        private void Census(string name)
        {
            var root = _canvas;
            Line(string.Format("{0,-44} GOs {1} | Text {2} | Image {3} | LayoutGroup {4} | ContentSizeFitter {5} | Button {6} | Shadow {7} | KitGradient {8} | raycastTargets {9}",
                name, GoCount(), root.GetComponentsInChildren<Text>(true).Length, root.GetComponentsInChildren<Image>(true).Length,
                root.GetComponentsInChildren<LayoutGroup>(true).Length, root.GetComponentsInChildren<ContentSizeFitter>(true).Length,
                root.GetComponentsInChildren<Button>(true).Length, root.GetComponentsInChildren<Shadow>(true).Length,
                root.GetComponentsInChildren<KitGradient>(true).Length,
                root.GetComponentsInChildren<Graphic>(true).Count(g => g.raycastTarget)));
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator Probe()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            _app = AppBootstrap.Current;
            _canvas = (RectTransform)_app.Root.root;
            string dir = Path.Combine(Application.temporaryCachePath, "probe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Line("device: " + SystemInfo.deviceModel + " / " + SystemInfo.graphicsDeviceType + " / " + Screen.width + "x" + Screen.height);

            // ---- online response cost: a full state per command response ------------------------------------------
            string statePath = Environment.GetEnvironmentVariable("ROGUESTARS_PROBE_STATE");
            GameState loaded = null;
            if (!string.IsNullOrEmpty(statePath) && File.Exists(statePath))
            {
                string body = File.ReadAllText(statePath);
                var sw = new Stopwatch();
                var parse = new List<double>();
                for (int i = 0; i < 6; i++)
                {
                    long a0 = Alloc();
                    sw.Restart();
                    var json = JObject.Parse(body); // RemoteGameService.Call
                    loaded = json["state"].ToObject<GameState>(ClientJson.WireSerializer); // ApplyState
                    sw.Stop();
                    parse.Add(sw.Elapsed.TotalMilliseconds);
                    if (i == 5) Line(string.Format("online response apply ({0} KB body): first {1:F1} ms, median {2:F1} ms, alloc {3:F0} KB",
                        body.Length / 1024, parse[0], Median(parse), (Alloc() - a0) / 1024.0));
                }
                var clone = new List<double>();
                for (int i = 0; i < 5; i++) { sw.Restart(); ClientJson.Clone(loaded); sw.Stop(); clone.Add(sw.Elapsed.TotalMilliseconds); }
                Line(string.Format("ClientJson.Clone(state) (engine Cloner per command): median {0:F1} ms", Median(clone)));
                File.WriteAllText(Path.Combine(dir, LocalGameService.SaveFileName), JsonConvert.SerializeObject(loaded, ClientJson.Storage));
            }

            _svc = new LocalGameService(dir, loaded?.PlayerId ?? "local_probe", "Probe", SystemClock.Instance, 7) { AdsDisabled = true };
            _app.Start(_svc);
            for (int i = 0; i < 10; i++) yield return null;
            if (loaded == null)
            {
                _svc.DevGrantStarterKit();
                _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
                _svc.Refresh(_ => { });
            }
            foreach (var id in _svc.State.Onboarding.Unlocked.Keys.ToList()) _svc.State.Onboarding.Celebrated.Add(id);
            if (_svc.State.Run.Active != null) _svc.ExecuteNow(new RunAbandon());
            // close whatever popped up (unlock intros, login)
            while (_app.ModalLayer.childCount > 0) Ui.DestroyGo(_app.ModalLayer.GetChild(0).gameObject);
            _app.Router.Reset(new HubScreen());
            for (int i = 0; i < 10; i++) yield return null;
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 5; i++) _svc.Save();
                Line(string.Format("LocalGameService.Save (offline, every command): {0:F1} ms", sw.Elapsed.TotalMilliseconds / 5));
                sw.Restart();
                for (int i = 0; i < 20; i++) _svc.Engine.BuildLoadout(_svc.State, _svc.NowMs);
                Line(string.Format("Engine.BuildLoadout: {0:F2} ms", sw.Elapsed.TotalMilliseconds / 20));
                sw.Restart();
                for (int i = 0; i < 200; i++) Resources.Load<Sprite>("Art/Ui/Kit/button_lip");
                Line(string.Format("Resources.Load miss (KitArt.Get in play mode, no cache of misses): {0:F3} ms each", sw.Elapsed.TotalMilliseconds / 200));
            }

            Census("hub census");
            yield return Idle("hub");
            yield return Measure("state change on hub (NotifyChanged)", () => _app.Session.NotifyChanged());
            yield return Measure("  of which: ResourceBar.Rebuild", () => _app.Bar.Rebuild());
            yield return Measure("  of which: hub Rebuild", () => _app.Router.RebuildCurrent());

            foreach (var entry in ScreenCatalog.All)
            {
                if (entry.Id == "settings") continue;
                string id = entry.Id;
                yield return Measure("open " + id, () => _app.Open(id), 3, () => { _app.Router.Reset(new HubScreen()); });
                if (_app.Router.Current is HubScreen) continue;
                yield return Measure("state change on " + id, () => _app.Session.NotifyChanged(), 3);
                Census(id + " census");
                if (id == "shop") yield return Idle("shop");
                yield return Measure("back from " + id + " (pop + hub rebuild)", () => _app.Router.Pop(), 3, () => _app.Open(id));
                while (_app.ModalLayer.childCount > 0) Ui.DestroyGo(_app.ModalLayer.GetChild(0).gameObject);
                _app.Router.Reset(new HubScreen());
                yield return null;
            }

            // main tab bar: reset to a new hub + open the tab's screen (the old path), then the tab switch itself
            yield return Measure("main tab bar → shop (Reset hub + open)", () => { _app.Router.Reset(new HubScreen()); _app.Open("shop"); }, 3);
            _app.Router.Reset(new HubScreen());
            _app.Open("worm");
            yield return null;
            yield return Measure("main tab bar worm → shop (OpenTab)", () => _app.OpenTab("shop"), 3, () => _app.OpenTab("worm"));
            yield return Measure("main tab bar shop → planet (OpenTab hub)", () => _app.OpenTab(null), 3, () => _app.OpenTab("shop"));
            // shop section chips
            _app.Router.Reset(new HubScreen());
            _app.Open("shop");
            yield return null;
            foreach (var group in new[] { "PullSections", "OfferSections" })
            {
                var row = _app.Router.Current.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == group);
                if (row == null) { Line("no " + group); continue; }
                int n = row.GetComponentsInChildren<Button>(true).Length;
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    yield return Measure("shop section " + group + "[" + i + "]", () =>
                    {
                        var r = _app.Router.Current.Root.GetComponentsInChildren<Transform>(true).First(t => t.name == group);
                        var b = r.GetComponentsInChildren<Button>(true)[idx];
                        b.onClick.Invoke();
                    }, 3);
                    if (!(_app.Router.Current is ShopScreen)) { _app.Router.Pop(); yield return null; }
                }
            }

            // ---- the run ---------------------------------------------------------------------------------------------
            _app.Router.Reset(new HubScreen());
            yield return null;
            var level = _svc.Content.MainLevel(1);
            var start = new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal };
            yield return Measure("RunStart command + push RunScreen", () =>
                SignaturePick.StartRun(_app, new RunStart { PortalId = start.PortalId, LevelId = start.LevelId, Mode = start.Mode }, r => { if (r.Ok) _app.Router.Push(new RunScreen()); }), 1);
            var run = _app.Router.Current as RunScreen;
            if (run == null) { Line("no run screen: " + _app.Session.LastResult?.Error); }
            else
            {
                Census("run census");
                yield return Idle("run board");
                int rolls = 0, picks = 0, books = 0;
                for (int step = 0; step < 80 && rolls < 6; step++)
                {
                    // wait for animations (dice, hops) to settle
                    for (int w = 0; w < 600 && run.Animating; w++) yield return null;
                    for (int w = 0; w < 30; w++) yield return null;
                    var ar = _svc.State.Run.Active;
                    if (ar == null) break;
                    var pick = _app.ModalLayer.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Button:Pick");
                    var book = _app.ModalLayer.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "SkillsBook");
                    if (book != null && books < 2)
                    {
                        books++;
                        yield return Measure("skills book (open)", () => book.onClick.Invoke(), 1);
                        // close the book modal (top-most)
                        Ui.DestroyGo(_app.ModalLayer.GetChild(_app.ModalLayer.childCount - 1).gameObject);
                        yield return null;
                    }
                    if (pick != null && picks < 3)
                    {
                        picks++;
                        yield return Measure("draft pick (tap → command → rebuild)", () => pick.onClick.Invoke(), 1);
                        continue;
                    }
                    if (ar.Phase == RunPhase.AwaitRoll)
                    {
                        rolls++;
                        yield return Measure("roll (tap → command → rebuild)", () => _app.Session.Run(new RunRoll()), 1);
                        continue;
                    }
                    if (ar.Phase == RunPhase.Encounter) { _app.Session.Run(new RunEncounter()); continue; }
                    if (ar.Phase == RunPhase.Signature) { _app.Session.Run(new RunPickSignature()); continue; }
                    if (ar.Phase == RunPhase.ChooseBranch) { _app.Session.Run(new RunChooseBranch { Index = 0 }); continue; }
                    if (ar.Phase == RunPhase.Event) { _app.Session.Run(new RunChooseEventOption { Index = 0 }); continue; }
                    if (ar.Phase == RunPhase.Shop) { _app.Session.Run(new RunShopLeave()); continue; }
                    if (ar.Phase == RunPhase.Quest) { _app.Session.Run(new RunQuestChoose { Index = 0 }); continue; }
                    if (ar.Phase == RunPhase.Draft && pick == null)
                    {
                        var sid = ar.DraftOffer != null && ar.DraftOffer.Count > 0 ? ar.DraftOffer[0] : null;
                        _app.Session.Run(new RunDraftPick { SkillId = sid });
                        continue;
                    }
                    Line("run phase " + ar.Phase + ", stopping");
                    break;
                }
                Line("run: rolls " + rolls + ", picks " + picks + ", books " + books);
            }

            string outPath = Environment.GetEnvironmentVariable("ROGUESTARS_PROBE_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ui_probe.txt"));
            File.WriteAllText(outPath, _out.ToString());
            Debug.Log("[probe] written " + outPath);
        }
    }
}
