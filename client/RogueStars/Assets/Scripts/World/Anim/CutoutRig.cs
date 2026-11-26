using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// Animation pilot, technique 2 (cut-out rig): an alien split into part sprites (body, eye, arms, legs, …) under a small
    /// transform hierarchy of code bones, built from the rig file the cut-out tool writes
    /// (<c>Art/Anim/Rigs/&lt;id&gt;/rig.json</c>: per part its sprite, role, parent, rect centre and joint pivot in source
    /// pixels). Clips (<see cref="CutoutClips"/>) address parts by <b>role</b>, not by name, so one clip set drives every
    /// alien whose rig names the same roles; roles a rig lacks are skipped.
    /// </summary>
    public sealed class CutoutRig : MonoBehaviour
    {
        [Serializable]
        public sealed class PartDef
        {
            public string name, role, parent;
            public float cx, cy, px, py;
            public int order;
        }

        [Serializable]
        public sealed class RigDef
        {
            public string id;
            public float originX, originY;
            public PartDef[] parts;
        }

        private sealed class Bone
        {
            public Transform T;
            public Vector3 RestPos, RestScale;
            public float RestRot;
        }

        private readonly Dictionary<string, Bone> _byRole = new Dictionary<string, Bone>();
        private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        private Bone _root;
        private MaterialPropertyBlock _block;
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private CutoutClip _clip;
        private float _t;
        public string ClipName => _clip != null ? _clip.Name : null;
        public int PartCount => _renderers.Count;
        public IReadOnlyList<SpriteRenderer> Renderers => _renderers;

        /// <summary>Loads <c>Art/Anim/Rigs/{id}</c> (rig.json + part sprites) and builds the bones; false when missing.</summary>
        public bool Build(string id, Material material, int sortingOrder)
        {
            var json = Resources.Load<TextAsset>("Art/Anim/Rigs/" + id + "/rig");
            if (json == null) return false;
            var def = JsonUtility.FromJson<RigDef>(json.text);
            _block = new MaterialPropertyBlock();
            var rootGo = new GameObject("root");
            rootGo.layer = gameObject.layer;
            rootGo.transform.SetParent(transform, false);
            _root = new Bone { T = rootGo.transform, RestPos = Vector3.zero, RestScale = Vector3.one };
            _byRole["root"] = _root;
            var pivots = new Dictionary<string, Vector2> { ["root"] = new Vector2(def.originX, def.originY) };
            var bones = new Dictionary<string, Transform> { ["root"] = rootGo.transform };
            // parents first: the file lists them in order
            foreach (var p in def.parts)
            {
                string parent = string.IsNullOrEmpty(p.parent) ? "root" : p.parent;
                var pp = pivots[parent];
                var go = new GameObject(p.name) { layer = gameObject.layer };
                go.transform.SetParent(bones[parent], false);
                go.transform.localPosition = Px(p.px - pp.x, p.py - pp.y);
                var art = new GameObject(p.name + "_art") { layer = gameObject.layer };
                art.transform.SetParent(go.transform, false);
                art.transform.localPosition = Px(p.cx - p.px, p.cy - p.py);
                var sr = art.AddComponent<SpriteRenderer>();
                sr.sprite = Resources.Load<Sprite>("Art/Anim/Rigs/" + id + "/" + p.name);
                sr.sharedMaterial = material;
                sr.sortingOrder = sortingOrder + p.order;
                _renderers.Add(sr);
                pivots[p.name] = new Vector2(p.px, p.py);
                bones[p.name] = go.transform;
                var b = new Bone { T = go.transform, RestPos = go.transform.localPosition, RestScale = Vector3.one };
                if (!string.IsNullOrEmpty(p.role)) _byRole[p.role] = b;
            }
            return true;
        }

        /// <summary>Source pixels (y down) → local units (y up), 100 px per unit like the imported sprites.</summary>
        private static Vector3 Px(float dx, float dy) => new Vector3(dx / Iso.Ppu, -dy / Iso.Ppu, 0);

        public void Play(CutoutClip clip) { _clip = clip; _t = 0; Sample(0); }

        private void Update()
        {
            if (_clip == null) return;
            _t += Time.deltaTime;
            float t = _clip.Loop ? Mathf.Repeat(_t, _clip.Length) : Mathf.Min(_t, _clip.Length);
            Sample(t);
        }

        /// <summary>Poses every bone at clip time <paramref name="t"/>: rest pose + the clip's offsets per role.</summary>
        public void Sample(float t)
        {
            foreach (var b in _byRole.Values)
            {
                b.T.localPosition = b.RestPos;
                b.T.localRotation = Quaternion.Euler(0, 0, b.RestRot);
                b.T.localScale = b.RestScale;
            }
            float alpha = 1, flash = 0;
            if (_clip != null)
                foreach (var tr in _clip.Tracks)
                {
                    float v = tr.Curve.Evaluate(t);
                    if (tr.Channel == CutoutChannel.Alpha) { alpha = v; continue; }
                    if (tr.Channel == CutoutChannel.Flash) { flash = v; continue; }
                    if (!_byRole.TryGetValue(tr.Role, out var b)) continue;
                    var T = b.T;
                    switch (tr.Channel)
                    {
                        case CutoutChannel.Rot: T.localRotation = Quaternion.Euler(0, 0, T.localEulerAngles.z + v); break;
                        case CutoutChannel.X: T.localPosition += new Vector3(v, 0, 0); break;
                        case CutoutChannel.Y: T.localPosition += new Vector3(0, v, 0); break;
                        case CutoutChannel.SX: T.localScale = new Vector3(T.localScale.x * v, T.localScale.y, 1); break;
                        case CutoutChannel.SY: T.localScale = new Vector3(T.localScale.x, T.localScale.y * v, 1); break;
                    }
                }
            foreach (var sr in _renderers)
            {
                var c = sr.color; c.a = alpha; sr.color = c;
                sr.GetPropertyBlock(_block);
                _block.SetFloat(FlashId, flash);
                if (sr.sprite != null) _block.SetTexture(MainTexId, sr.sprite.texture);
                sr.SetPropertyBlock(_block);
            }
        }
    }

    public enum CutoutChannel { Rot, X, Y, SX, SY, Alpha, Flash }

    public sealed class CutoutTrack
    {
        public string Role;
        public CutoutChannel Channel;
        public AnimationCurve Curve;
    }

    /// <summary>A role-addressed clip: curves of rotation (degrees), offset (units), scale factors, whole-rig alpha and flash.</summary>
    public sealed class CutoutClip
    {
        public string Name;
        public float Length;
        public bool Loop;
        public readonly List<CutoutTrack> Tracks = new List<CutoutTrack>();

        /// <summary>Adds a track from (time, value) pairs with smooth tangents.</summary>
        public CutoutClip Key(string role, CutoutChannel ch, params float[] tv)
        {
            var keys = new Keyframe[tv.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(tv[i * 2], tv[i * 2 + 1]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0);
            if (Loop) { curve.preWrapMode = WrapMode.Loop; curve.postWrapMode = WrapMode.Loop; }
            Tracks.Add(new CutoutTrack { Role = role, Channel = ch, Curve = curve });
            return this;
        }
    }
}
