using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Client.World.Anim;
using UnityEngine;
using UnityEngine.TestTools;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Captures the shader animation of every animated prop, set piece and City building stand-in for the art review page
    /// ("Animated props and buildings"): one transparent frame sequence per item (24 frames at 12 fps = a 2 s loop, plus a few
    /// extra frames that <c>tools/art/anim_review_pack.py</c> cross-fades into the start so the loop is seamless) into
    /// <c>$ROGUESTARS_SHOTS_DIR/&lt;name&gt;/f_###.png</c> (default Screenshots/anim-review). The packer writes the 220 px
    /// WebPs to ~/AI/out/anim-review. Explicit: <c>-testFilter AnimReviewCaptureTests</c>, batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("captures animated props for the art review page; run with -testFilter AnimReviewCaptureTests")]
    public sealed class AnimReviewCaptureTests
    {
        public const int Loop = 24, Extra = 6, Fps = 12, Width = 440;

        private sealed class Item { public string Name; public Sprite Sprite; public PropLook Look; }

        [UnityTest, Timeout(1800000)]
        public IEnumerator Capture()
        {
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            string dir = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "anim-review"));
            Directory.CreateDirectory(dir);
            AnimSettings.Override = false;

            var items = new List<Item>();
            foreach (var kv in PropFx.Props)
            {
                var s = ArtCatalog.RunProp(kv.Key) ?? ArtCatalog.SliceSetPiece(kv.Key) ?? ArtCatalog.SliceMarker(kv.Key);
                if (s != null && kv.Value.Kind != FxKind.None || s != null && kv.Value.Smoke) items.Add(new Item { Name = "prop_" + kv.Key, Sprite = s, Look = kv.Value });
            }
            foreach (var kv in CityArt.BuildingArtKey)
            {
                var s = CityArt.LoadKey(kv.Value);
                var look = PropFx.LookOfBuilding(kv.Key);
                if (s != null && look != null) items.Add(new Item { Name = "building_" + kv.Key.Replace("bp_", ""), Sprite = s, Look = look });
            }
            Assert.That(items.Count, Is.GreaterThan(20), "props + buildings");

            var camGo = new GameObject("ReviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.enabled = false;
            cam.transform.position = new Vector3(0, 0, -10);
            Time.captureFramerate = Fps;
            var names = new List<string>();
            try
            {
                foreach (var it in items)
                {
                    float k = 3f / Mathf.Max(0.01f, it.Sprite.bounds.size.y);
                    var go = new GameObject(it.Name);
                    go.transform.localScale = new Vector3(k, k, 1);
                    go.transform.position = new Vector3(-it.Sprite.bounds.center.x * k, -it.Sprite.bounds.center.y * k, 0);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = it.Sprite;
                    PropFx.Apply(sr, it.Look);
                    float w = it.Sprite.bounds.size.x * k + 0.5f;
                    float top = (it.Look.Smoke || it.Look.Embers ? 1.3f : 0.3f), h = 3f + 0.3f + top;
                    // the frame: the sprite with margins, extra room above for smoke / embers
                    float cy = (top - 0.3f) / 2;
                    float rw = Mathf.Max(w, h * 0.8f);
                    cam.orthographicSize = h / 2;
                    cam.transform.position = new Vector3(0, cy, -10);
                    int rtW = Width, rtH = Mathf.RoundToInt(Width * h / rw);
                    cam.aspect = rw / h;
                    // widen the ortho view to rw
                    cam.orthographicSize = rw / cam.aspect / 2;
                    var rt = new RenderTexture(rtW, rtH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    cam.targetTexture = rt;
                    var tex = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
                    string folder = Path.Combine(dir, it.Name);
                    Directory.CreateDirectory(folder);
                    foreach (var f in Directory.GetFiles(folder, "*.png")) File.Delete(f);
                    for (int i = 0; i < 8; i++) yield return null; // warm-up (particles, material instance)
                    for (int i = 0; i < Loop + Extra; i++)
                    {
                        yield return null;
                        cam.Render();
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                        tex.Apply(false);
                        RenderTexture.active = prev;
                        File.WriteAllBytes(Path.Combine(folder, $"f_{i:000}.png"), tex.EncodeToPNG());
                    }
                    names.Add(it.Name);
                    cam.targetTexture = null;
                    rt.Release();
                    UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(go);
                    yield return null;
                }
            }
            finally { Time.captureFramerate = 0; }
            File.WriteAllLines(Path.Combine(dir, "items.txt"), names);
            UnityEngine.Object.Destroy(camGo);
            Debug.Log("AnimReviewCapture: " + names.Count + " items to " + dir);
        }
    }
}
