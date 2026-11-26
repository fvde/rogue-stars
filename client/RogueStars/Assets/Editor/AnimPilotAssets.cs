using UnityEditor;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>
    /// Animation pilot: writes the reusable materials of the shader / flipbook techniques into
    /// <c>Assets/Art/Resources/Art/Anim/Materials</c> (batch: <c>-executeMethod RogueStars.Client.Editor.AnimPilotAssets.Create</c>).
    /// Each material is one look with its parameters exposed in the inspector; every copy of a prop shares it.
    /// </summary>
    public static class AnimPilotAssets
    {
        private const string Dir = "Assets/Art/Resources/Art/Anim/Materials/";

        [MenuItem("Tools/Art/Anim Pilot Materials")]
        public static void Create()
        {
            AssetDatabase.Refresh();
            Make("AnimSprite", "RogueStars/Anim/Sprite", null, m => { });
            Make("CampfireFlame", "RogueStars/Anim/FlameFlicker", "Assets/Art/Resources/Art/Run/Props/campfire_big.png", m =>
            {
                m.SetTexture("_FlameMask", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Art/Anim/Props/campfire_flame_mask.png"));
                m.SetFloat("_Distort", 0.022f);
                m.SetFloat("_NoiseScale", 7);
                m.SetFloat("_Speed", 1.5f);
                m.SetFloat("_Flicker", 0.22f);
                m.SetFloat("_BaseY", 0.3f);
                m.SetFloat("_Pad", 0.05f);
            });
            Make("SwayTree", "RogueStars/Anim/Sway", "Assets/Art/Resources/Art/Slice/SetPieces/alien_tree.png", m =>
            {
                m.SetFloat("_Amount", 0.03f); m.SetFloat("_Speed", 1.2f); m.SetFloat("_Gust", 0.35f);
                m.SetFloat("_Base", 0.25f); m.SetFloat("_Power", 1.8f); m.SetFloat("_Pad", 0.06f);
            });
            Make("SwayMushroom", "RogueStars/Anim/Sway", "Assets/Art/Resources/Art/Slice/SetPieces/giant_mushroom.png", m =>
            {
                m.SetFloat("_Amount", 0.015f); m.SetFloat("_Speed", 0.9f); m.SetFloat("_Gust", 0.2f);
                m.SetFloat("_Base", 0.35f); m.SetFloat("_Power", 2.4f); m.SetFloat("_Pad", 0.04f);
            });
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
            var sheet = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Art/Resources/Art/Anim/Portal/portal_loop.json");
            Make("PortalLoop", "RogueStars/Anim/Flipbook", "Assets/Art/Resources/Art/Anim/Portal/portal_loop.png", m =>
            {
                if (sheet == null) return;
                var s = JsonUtility.FromJson<Sheet>(sheet.text);
                m.SetFloat("_Cols", s.cols); m.SetFloat("_Rows", s.rows); m.SetFloat("_Frames", s.frames); m.SetFloat("_Fps", s.fps);
            });
            AssetDatabase.SaveAssets();
            Debug.Log("AnimPilotAssets: materials written to " + Dir);
        }

        [System.Serializable]
        private sealed class Sheet { public int cols, rows, frames; public float fps; }

        private static void Make(string name, string shader, string texPath, System.Action<Material> setup)
        {
            var sh = Shader.Find(shader);
            if (sh == null) { Debug.LogError("AnimPilotAssets: shader missing " + shader); return; }
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
