using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RogueStars.Client.World.Anim;
using UnityEditor;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// Writes <c>Resources/Art/anim_bounds.json</c>: the alpha bounds of every creature sprite (hero, enemies, aliens, sidekicks,
    /// run characters), read from the source PNGs (the imported textures are not CPU-readable). Re-run after new creature art
    /// (menu <c>Tools/Art/Build Anim Bounds</c>, batch <c>-executeMethod RogueStars.Client.Editor.AnimBoundsBuilder.Build</c>); it also
    /// runs by itself when art in those folders is imported. A test fails when a creature sprite is missing from the table.
    /// </summary>
    public sealed class AnimBoundsBuilder : AssetPostprocessor
    {
        public static readonly string[] Folders =
        {
            "Assets/Art/Resources/Art/Hero", "Assets/Art/Resources/Art/Enemies", "Assets/Art/Resources/Art/Aliens",
            "Assets/Art/Resources/Art/Sidekicks", "Assets/Art/Resources/Art/Run/Characters",
        };
        public const string TableFile = "Assets/Art/Resources/Art/anim_bounds.json";

        [MenuItem("Tools/Art/Build Anim Bounds")]
        public static void Build()
        {
            var table = new SortedDictionary<string, int[]>();
            foreach (var folder in Folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(File.ReadAllBytes(file))) { Object.DestroyImmediate(tex); continue; }
                    var b = AnimBounds.Bounds(tex.GetPixels32(), tex.width, tex.height);
                    table[Path.GetFileNameWithoutExtension(file)] = new[] { b.xMin, b.yMin, b.xMax - 1, b.yMax - 1, tex.width, tex.height };
                    Object.DestroyImmediate(tex);
                }
            }
            // one line per sprite: [minX, minY, maxX, maxY, width, height]
            var sb = new System.Text.StringBuilder("{\n");
            int n = 0;
            foreach (var kv in table) sb.Append("  ").Append(JsonConvert.ToString(kv.Key)).Append(": [").Append(string.Join(", ", kv.Value)).Append(++n < table.Count ? "],\n" : "]\n");
            var json = sb.Append("}\n").ToString();
            if (File.Exists(TableFile) && File.ReadAllText(TableFile) == json) return;
            File.WriteAllText(TableFile, json);
            AssetDatabase.ImportAsset(TableFile);
            Debug.Log("AnimBoundsBuilder: " + table.Count + " creature sprites -> " + TableFile);
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touched = false;
            foreach (var p in imported)
            {
                if (!p.EndsWith(".png")) continue;
                foreach (var f in Folders) if (p.StartsWith(f)) touched = true;
            }
            if (touched) EditorApplication.delayCall += Build;
        }
    }
}
