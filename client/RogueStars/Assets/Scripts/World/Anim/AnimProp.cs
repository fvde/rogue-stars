using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// Builders for the extras of an animated prop: a quad with a shader material (the additive fire / portal glow), an ember
    /// and a smoke particle system. All motion is in the shaders (<c>RogueStars/Anim/*</c>, time-driven) and the particle
    /// systems: no script runs per frame. Base materials live in <c>Art/Anim/Materials</c>; <see cref="PropFx"/> makes one
    /// instance per prop (its own phase).
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
            main.cullingMode = ParticleSystemCullingMode.Pause;
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

        /// <summary>Smoke rising from a chimney, wreck or vent: ~10 soft grey puffs alive, drifting up and sideways, growing and fading.</summary>
        public static ParticleSystem Smoke(Transform parent, Material material, float width, int order, Color tint, float rise = 0.55f)
        {
            var go = new GameObject("Smoke") { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 3;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(rise * 0.6f, rise);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.36f);
            main.startColor = tint;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 18;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            var em = ps.emission;
            em.rateOverTime = 5.5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, 0.05f, 0.05f);
            shape.rotation = new Vector3(-90, 0, 0);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.08f, 0.22f); // a light wind (all three axes in the same curve mode)
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.8f, 0.8f, 0.85f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.7f, 0.2f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.sortingOrder = order;
            main.prewarm = true;
            ps.Play();
            return ps;
        }
    }
}
