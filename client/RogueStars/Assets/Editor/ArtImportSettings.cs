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
            ti.SetTextureSettings(s);
            // slice textures are sampled by the code-drawn tile faces (IsoArt.Top), and tile across the ground
            if (assetPath.Contains("/Art/Slice/Textures/")) { ti.isReadable = true; ti.wrapMode = TextureWrapMode.Repeat; }
        }
    }
}
