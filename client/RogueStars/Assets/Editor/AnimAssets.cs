using UnityEditor;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// Writes the base materials of the animation system into <c>Assets/Art/Resources/Art/Anim/Materials</c> (batch:
    /// <c>-executeMethod RogueStars.Client.Editor.AnimAssets.Create</c>): the creature mesh material, one per prop shader
    /// (<c>FxFlame</c>, <c>FxSway</c>, <c>FxPulse</c>, <c>FxShimmer</c>, <c>FxSwirl</c>; <see cref="RogueStars.Client.World.Anim.PropFx"/>
    /// instances them per prop with the table's parameters) and the particle / glow materials. Materials in Resources keep their
    /// shaders in builds.
    /// </summary>
    public static class AnimAssets
    {
        private const string Dir = "Assets/Art/Resources/Art/Anim/Materials/";

        [MenuItem("Tools/Art/Anim Materials")]
        public static void Create()
        {
            AssetDatabase.Refresh();
            Make("AnimSprite", "RogueStars/Anim/Sprite", null, m => { });
            Make("FxFlame", "RogueStars/Anim/FlameFlicker", "Assets/Art/Resources/Art/Run/Props/campfire_big.png", m =>
            {
                m.SetTexture("_FlameMask", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Art/Anim/Props/campfire_flame_mask.png"));
                m.SetFloat("_Distort", 0.022f); m.SetFloat("_NoiseScale", 7); m.SetFloat("_Speed", 1.5f); m.SetFloat("_Flicker", 0.22f); m.SetFloat("_BaseY", 0.3f);
            });
            Make("FxSway", "RogueStars/Anim/Sway", null, m =>
            {
                m.SetFloat("_Amount", 0.025f); m.SetFloat("_Speed", 1.2f); m.SetFloat("_Gust", 0.3f); m.SetFloat("_Base", 0.25f); m.SetFloat("_Power", 1.9f);
            });
            Make("FxPulse", "RogueStars/Anim/Pulse", null, m => { m.SetColor("_Glow", new Color(0.5f, 0.9f, 1f, 1)); });
            Make("FxShimmer", "RogueStars/Anim/Shimmer", null, m => { });
            Make("FxSwirl", "RogueStars/Anim/Swirl", null, m => { });
            Make("FireGlow", "RogueStars/Anim/GlowPulse", null, m =>
            {
                m.SetColor("_Color", new Color(1f, 0.55f, 0.2f, 1)); m.SetFloat("_Intensity", 0.55f); m.SetFloat("_Falloff", 2.2f);
                m.SetFloat("_Pulse", 0.18f); m.SetFloat("_PulseSpeed", 2.6f); m.SetFloat("_Flicker", 0.3f);
            });
            Make("PortalGlow", "RogueStars/Anim/GlowPulse", null, m =>
            {
                m.SetColor("_Color", new Color(0.7f, 0.35f, 1f, 1)); m.SetFloat("_Intensity", 0.45f); m.SetFloat("_Falloff", 2.5f);
                m.SetFloat("_Pulse", 0.3f); m.SetFloat("_PulseSpeed", 1.6f); m.SetFloat("_Flicker", 0.05f);
            });
            Make("Ember", "RogueStars/Anim/Ember", null, m => m.SetFloat("_Intensity", 1.8f));
            Make("Puff", "RogueStars/Anim/Puff", null, m => m.SetFloat("_Soft", 1.6f));
            AssetDatabase.SaveAssets();
            Debug.Log("AnimAssets: materials written to " + Dir);
        }

        private static void Make(string name, string shader, string texPath, System.Action<Material> setup)
        {
            var sh = Shader.Find(shader);
            if (sh == null) { Debug.LogError("AnimAssets: shader missing " + shader); return; }
            string path = Dir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh); else m.shader = sh;
            if (texPath != null) m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            setup(m);
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        }
    }
}
