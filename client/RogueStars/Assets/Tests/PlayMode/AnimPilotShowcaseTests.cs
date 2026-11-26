using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Client.World.Anim;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Animation pilot: the four techniques side by side, recorded as a frame sequence (10 s at 30 fps, 2400×720) —
    /// 1 the worm bent along a code spine (idle, hops, attack, hit, happy), 2 two cut-out rigs (blob, biped) playing idle,
    /// attack, hit, celebrate, death, 3 the campfire's flame shader + embers + glow and the sway shader on the alien tree,
    /// 4 the portal marker's AI-video flipbook. Writes <c>frames/f_###.png</c> and <c>stats.txt</c> (vertex / renderer
    /// counts, CPU cost per update) to $ROGUESTARS_SHOTS_DIR (default client/RogueStars/Screenshots/anim-pilot).
    /// Explicit: <c>-testFilter AnimPilotShowcaseTests</c>, batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("records the animation pilot; run with -testFilter AnimPilotShowcaseTests")]
    public sealed class AnimPilotShowcaseTests
    {
        private const int W = 2400, H = 720, Fps = 30, Seconds = 10;
        /// <summary>Panel edges in world units (100 px per unit): worm | aliens | props | portal.</summary>
        public static readonly float[] PanelEdges = { -12f, -7.2f, 0f, 7.2f, 12f };
        private const float Ground = -2.85f;
        private const int Layer = 0;

        [UnityTest, Timeout(900000)]
        public IEnumerator Record()
        {
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            string dir = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "anim-pilot"));
            string frames = Path.Combine(dir, "frames");
            Directory.CreateDirectory(frames);
            foreach (var f in Directory.GetFiles(frames, "*.png")) File.Delete(f);

            var root = new GameObject("AnimPilot");
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = H / 200f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.2f, 0.24f);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.enabled = false;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);

            var spriteMat = AnimProp.LoadMaterial("AnimSprite");
            Assert.IsNotNull(spriteMat, "run AnimPilotAssets.Create first (materials)");
            BuildBackdrop(root.transform);

            // ---- 1 worm ----------------------------------------------------------------------------------------------
            float wormX = Mid(0);
            var wormGo = new GameObject("Worm");
            wormGo.transform.SetParent(root.transform, false);
            wormGo.transform.localPosition = new Vector3(wormX, Ground, 0);
            Shadow(wormGo.transform, 1.7f, 9);
            var spine = wormGo.AddComponent<SpineWorm>();
            var hero = ArtCatalog.Hero();
            Assert.IsNotNull(hero, "hero_worm sprite");
            spine.Init(hero, spriteMat, 3.4f, 10);
            var motion = wormGo.AddComponent<WormMotion>();

            // ---- 2 cut-out rigs ----------------------------------------------------------------------------------------
            var blob = Rig(root.transform, "alien_blorp", Mid(1) - 1.8f, 0.66f, spriteMat, 20);
            var biped = Rig(root.transform, "alien_pebblehorn", Mid(1) + 1.75f, 0.7f, spriteMat, 40);

            // ---- 3 shader props --------------------------------------------------------------------------------------
            var fireGo = new GameObject("Campfire");
            fireGo.transform.SetParent(root.transform, false);
            fireGo.transform.localPosition = new Vector3(Mid(2) - 2.0f, Ground - 0.1f, 0);
            Shadow(fireGo.transform, 2.5f, 59);
            var glow = AnimProp.Quad(fireGo.transform, "Glow", AnimProp.LoadMaterial("FireGlow"), 1, 5.2f, 0, 58, 0.25f);
            glow.transform.localPosition = new Vector3(0, 0.2f, 0);
            AnimProp.Quad(fireGo.transform, "Fire", AnimProp.LoadMaterial("CampfireFlame"), 421f / 562f, 3.4f, 0.05f, 60);
            var embers = AnimProp.Embers(fireGo.transform, AnimProp.LoadMaterial("Ember"), 0.9f, 61);
            embers.transform.localPosition = new Vector3(0, 1.5f, 0);
            var treeGo = new GameObject("Tree");
            treeGo.transform.SetParent(root.transform, false);
            treeGo.transform.localPosition = new Vector3(Mid(2) + 1.5f, Ground, 0);
            Shadow(treeGo.transform, 2.4f, 69);
            AnimProp.Quad(treeGo.transform, "Tree", AnimProp.LoadMaterial("SwayTree"), 578f / 561f, 3.5f, 0.06f, 70);

            // ---- 4 flipbook portal -----------------------------------------------------------------------------------
            var portalGo = new GameObject("Portal");
            portalGo.transform.SetParent(root.transform, false);
            portalGo.transform.localPosition = new Vector3(Mid(3), Ground - 0.1f, 0);
            Shadow(portalGo.transform, 3.4f, 79);
            var portalMat = AnimProp.LoadMaterial("PortalLoop");
            Assert.IsNotNull(portalMat, "PortalLoop material");
            var pglow = AnimProp.Quad(portalGo.transform, "Glow", AnimProp.LoadMaterial("PortalGlow"), 1, 4.6f, 0, 81, 0.5f);
            pglow.transform.localPosition = new Vector3(0, 1.9f, 0);
            AnimProp.Quad(portalGo.transform, "Portal", portalMat, PortalAspect(portalMat), 3.8f, 0, 80);

            WriteStats(dir, spine, blob, biped);

            // ---- record ----------------------------------------------------------------------------------------------
            Time.captureFramerate = Fps;
            try
            {
                int total = Fps * Seconds;
                for (int i = 0; i < total; i++)
                {
                    float t = i / (float)Fps;
                    Script(t, motion, wormX, blob, biped);
                    yield return null;
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(frames, $"f_{i:000}.png"), tex.EncodeToPNG());
                }
            }
            finally { Time.captureFramerate = 0; }
            Debug.Log("AnimPilot: recorded " + Fps * Seconds + " frames to " + frames);
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(camGo);
            rt.Release();
        }

        private static float Mid(int panel) => (PanelEdges[panel] + PanelEdges[panel + 1]) / 2;

        private static float _lastT = -1;

        /// <summary>The 10-second timeline: fires each beat once when its time is crossed.</summary>
        private static void Script(float t, WormMotion worm, float wormX, CutoutRig blob, CutoutRig biped)
        {
            bool At(float s) => _lastT < s && t >= s;
            if (t == 0) _lastT = -1;
            if (At(0)) { worm.Play(WormState.Idle); Play(blob, "idle"); Play(biped, "idle"); }
            if (At(1.4f)) worm.HopTo(new Vector3(wormX - 0.8f, Ground, 0), 0.42f);
            if (At(2.0f)) worm.HopTo(new Vector3(wormX + 0.8f, Ground, 0), 0.42f);
            if (At(2.6f)) worm.HopTo(new Vector3(wormX, Ground, 0), 0.42f);
            if (At(3.6f)) worm.Play(WormState.Attack);
            if (At(5.2f)) worm.Play(WormState.Hit);
            if (At(6.4f)) worm.Play(WormState.Happy);
            if (At(8.2f)) worm.Play(WormState.Attack);

            if (At(1.4f)) { Play(blob, "attack"); }
            if (At(1.7f)) { Play(biped, "attack"); }
            if (At(2.6f)) Play(blob, "idle");
            if (At(2.9f)) { Play(biped, "idle"); }
            if (At(3.3f)) { Play(blob, "hit"); Play(biped, "hit"); }
            if (At(4.1f)) { Play(blob, "celebrate"); Play(biped, "celebrate"); }
            if (At(6.5f)) { Play(blob, "death"); }
            if (At(6.8f)) { Play(biped, "death"); }
            if (At(8.7f)) { Play(blob, "idle"); Play(biped, "idle"); }
            _lastT = t;
        }

        private static void Play(CutoutRig rig, string clip) => rig.Play(CutoutClips.Get(clip));

        private static CutoutRig Rig(Transform parent, string id, float x, float scale, Material mat, int order)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, Ground, 0);
            Shadow(go.transform, 2.3f * scale / 0.55f, order - 1);
            var rigGo = new GameObject("rig");
            rigGo.transform.SetParent(go.transform, false);
            rigGo.transform.localScale = Vector3.one * scale;
            var rig = rigGo.AddComponent<CutoutRig>();
            Assert.IsTrue(rig.Build(id, mat, order), "rig " + id);
            return rig;
        }

        private static void Shadow(Transform parent, float width, int order)
        {
            var go = new GameObject("Shadow");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = IsoArt.SoftShadow();
            sr.sortingOrder = order;
            float w = sr.sprite.bounds.size.x;
            go.transform.localScale = new Vector3(width / w, width / w * 0.9f, 1);
        }

        private static float PortalAspect(Material m)
        {
            var t = m.mainTexture;
            if (t == null) return 1;
            return t.width / m.GetFloat("_Cols") / (t.height / m.GetFloat("_Rows"));
        }

        /// <summary>A ground band per panel and thin dividers (plain sprite quads).</summary>
        private static void BuildBackdrop(Transform root)
        {
            var white = Texture2D.whiteTexture;
            var sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
            Color[] tints = { new Color(0.22f, 0.27f, 0.22f), new Color(0.24f, 0.22f, 0.28f), new Color(0.27f, 0.24f, 0.2f), new Color(0.2f, 0.2f, 0.3f) };
            for (int p = 0; p < 4; p++)
            {
                var go = new GameObject("Ground" + p);
                go.transform.SetParent(root, false);
                float w = PanelEdges[p + 1] - PanelEdges[p];
                go.transform.localPosition = new Vector3(Mid(p), Ground - 1.2f, 0);
                go.transform.localScale = new Vector3(w, 2.6f, 1);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite; sr.color = tints[p]; sr.sortingOrder = -10;
                if (p == 0) continue;
                var d = new GameObject("Divider" + p);
                d.transform.SetParent(root, false);
                d.transform.localPosition = new Vector3(PanelEdges[p], 0, 0);
                d.transform.localScale = new Vector3(0.04f, H / 100f, 1);
                var dr = d.AddComponent<SpriteRenderer>();
                dr.sprite = sprite; dr.color = new Color(0.05f, 0.06f, 0.08f); dr.sortingOrder = 200;
            }
        }

        /// <summary>Renderer / vertex counts and the CPU cost of one animation update per technique.</summary>
        private static void WriteStats(string dir, SpineWorm spine, CutoutRig blob, CutoutRig biped)
        {
            var sb = new StringBuilder();
            const int N = 2000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) { spine.Pose.WavePhase = i * 0.01f; spine.Apply(); }
            double wormUs = sw.Elapsed.TotalMilliseconds * 1000 / N;
            var clip = CutoutClips.Celebrate();
            biped.Play(clip);
            sw.Restart();
            for (int i = 0; i < N; i++) biped.Sample(i * 0.001f);
            double rigUs = sw.Elapsed.TotalMilliseconds * 1000 / N;
            sb.AppendLine($"worm: 1 MeshRenderer, {spine.VertexCount} vertices, {SpineWorm.Cols * SpineWorm.Rows * 2} triangles, Apply {wormUs:0.0} us/frame (editor, M2 Pro)");
            sb.AppendLine($"blob rig: {blob.PartCount} SpriteRenderers (full-rect quads, 4 verts each)");
            sb.AppendLine($"biped rig: {biped.PartCount} SpriteRenderers, Sample {rigUs:0.0} us/frame (editor, M2 Pro)");
            foreach (var id in new[] { "alien_blorp", "alien_pebblehorn" })
            {
                long px = 0;
                foreach (var s in Resources.LoadAll<Sprite>("Art/Anim/Rigs/" + id)) px += (long)(s.rect.width * s.rect.height);
                sb.AppendLine($"{id}: part texels {px} (source 512x512 = 262144)");
            }
            File.WriteAllText(Path.Combine(dir, "stats.txt"), sb.ToString());
            Debug.Log("AnimPilot stats\n" + sb);
        }
    }
}
