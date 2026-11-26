using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// Animation pilot, techniques 3 and 4 (shader props, flipbook loops): builders for a textured quad with a shader
    /// material, an additive glow and an ember particle system. All motion is in the shaders (<c>RogueStars/Anim/*</c>,
    /// time-driven) and the particle system: no script runs per frame. Materials live in
    /// <c>Art/Anim/Materials</c> with their parameters exposed (one material per look, shared by every copy; per-object phase
    /// comes from the world position).
    /// </summary>
    public static class AnimProp
    {
        public static Material LoadMaterial(string name) => Resources.Load<Material>("Art/Anim/Materials/" + name);

        /// <summary>
        /// A quad showing <paramref name="material"/>'s texture <paramref name="height"/> units tall, bottom centre at the
        /// origin + <paramref name="pivotY"/> share of its height, with <paramref name="pad"/> share of transparent margin on
        /// each side (the shader's <c>_Pad</c> must match) so displaced pixels never clip at the rect.
        /// </summary>
        public static MeshRenderer Quad(Transform parent, string name, Material material, float aspect, float height, float pad, int order, float pivotY = 0)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            float w = height * aspect, h = height;
            float px = w * pad, py = h * pad;
            var mesh = new Mesh { name = name };
            float y0 = -h * pivotY;
            mesh.vertices = new[]
            {
                new Vector3(-w / 2 - px, y0 - py), new Vector3(w / 2 + px, y0 - py),
                new Vector3(-w / 2 - px, y0 + h + py), new Vector3(w / 2 + px, y0 + h + py),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.sortingOrder = order;
            return mr;
        }

        /// <summary>Embers rising from a fire: ~18 alive, upward drift with noise, orange → red, fading.</summary>
        public static ParticleSystem Embers(Transform parent, Material material, float width, int order)
        {
            var go = new GameObject("Embers") { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 2;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.rateOverTime = 11;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, 0.1f, 0.1f);
            shape.rotation = new Vector3(-90, 0, 0); // emit along +y
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.8f;
            noise.scrollSpeed = 0.5f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0), new GradientColorKey(new Color(1f, 0.35f, 0.1f), 0.6f), new GradientColorKey(new Color(0.6f, 0.1f, 0.05f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.sortingOrder = order;
            main.prewarm = true;
            ps.Play();
            return ps;
        }
    }
}
