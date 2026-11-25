using RogueStars.Client.World;
using UnityEditor;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// Every texture dropped under <c>Assets/Art/</c> (generated board backgrounds, tile sprites, the hero) imports as a
    /// single UI sprite without mip maps, full-rect mesh, alpha as transparency: no manual import settings when an asset is
    /// added next to a line in <c>ArtCatalog</c>.
    /// </summary>
    public sealed class ArtImportSettings : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/Art/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = 2048;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteAlignment = (int)SpriteAlignment.Center;
            s.spritePixelsPerUnit = 100;
            // art that stands on the board: the pivot is its ground contact (footprint centre over the lowest solid row,
            // transparent padding ignored), so the board seats it on its tile / slot / ground spot (SpriteContact)
            if (IsStanding(assetPath))
            {
                var c = ContactOfFile(assetPath, out int w, out int h);
                if (c.Valid)
                {
                    s.spriteAlignment = (int)SpriteAlignment.Custom;
                    s.spritePivot = SpriteContact.Pivot01(c, w, h);
                }
            }
            ti.SetTextureSettings(s);
            // trimmed props / NPCs / markers (≤ 768 px sources): capped at 1024, compressed (ASTC on Android) like the rest
            if (assetPath.Contains("/Art/Run/") || assetPath.Contains("/Art/Slice/Markers/") || assetPath.Contains("/Art/Slice/SetPieces/"))
            {
                ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.Compressed;
            }
            // slice textures are sampled by the code-drawn tile faces (IsoArt.Top), and tile across the ground
            if (assetPath.Contains("/Art/Slice/Textures/")) { ti.isReadable = true; ti.wrapMode = TextureWrapMode.Repeat; }
        }

        /// <summary>Bumped when the import rules change (Unity reimports the art): 2 = ground-contact pivots of standing art (3: footprint x over the lower quarter; 4: the hero worm stands too).</summary>
        public override uint GetVersion() => 4;

        /// <summary>Reimports the standing board art (after a change of the contact rules; batch: -executeMethod).</summary>
        [MenuItem("Tools/Art/Reimport Standing Art (contact pivots)")]
        public static void ReimportStanding()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Resources/Art/Slice", "Assets/Art/Resources/Art/Hero" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (IsStanding(p)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Board art that stands on the ground: tile markers, set pieces, scatter props and the hero worm (the board token).</summary>
        public static bool IsStanding(string path) =>
            path.Contains("/Art/Slice/Markers/") || path.Contains("/Art/Slice/SetPieces/") || path.Contains("/Art/Slice/Scatter/") || path.Contains("/Art/Hero/");

        /// <summary>The ground contact of a source PNG (read from disk, so the compressed import does not matter).</summary>
        public static ContactInfo ContactOfFile(string path, out int w, out int h)
        {
            w = h = 0;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(System.IO.File.ReadAllBytes(path))) { Object.DestroyImmediate(tex); return default; }
                w = tex.width; h = tex.height;
                var c = SpriteContact.Find(tex.GetPixels32(), w, h);
                Object.DestroyImmediate(tex);
                return c;
            }
            catch (System.IO.IOException) { return default; }
        }
    }
}
