namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// The five cut-out clips, written once against part roles (root, body, eye, armF, armB, legL, legR, horn, ear) so every
    /// rig with those roles shares them. Forward is −x (the art faces the viewer, turned a little left); positive rotation
    /// leans forward. Rotation in degrees, offsets in units (100 source px), scales as factors.
    /// </summary>
    public static class CutoutClips
    {
        public static readonly string[] Names = { "idle", "attack", "hit", "death", "celebrate" };

        public static CutoutClip Get(string name)
        {
            switch (name)
            {
                case "attack": return Attack();
                case "hit": return Hit();
                case "death": return Death();
                case "celebrate": return Celebrate();
                default: return Idle();
            }
        }

        public static CutoutClip Idle()
        {
            var c = new CutoutClip { Name = "idle", Length = 2.4f, Loop = true };
            c.Key("body", CutoutChannel.SY, 0, 1, 1.2f, 1.045f, 2.4f, 1);
            c.Key("body", CutoutChannel.SX, 0, 1, 1.2f, 0.975f, 2.4f, 1);
            c.Key("body", CutoutChannel.Rot, 0, 0, 0.6f, 1.5f, 1.8f, -1.5f, 2.4f, 0);
            // a blink near the end of the loop
            c.Key("eye", CutoutChannel.SY, 0, 1, 1.85f, 1, 1.93f, 0.08f, 2.02f, 1, 2.4f, 1);
            c.Key("armF", CutoutChannel.Rot, 0, 0, 1.2f, -6, 2.4f, 0);
            c.Key("armB", CutoutChannel.Rot, 0, 0, 1.2f, 7, 2.4f, 0);
            c.Key("ear", CutoutChannel.Rot, 0, 0, 0.5f, 8, 0.9f, -3, 1.4f, 0, 2.4f, 0);
            c.Key("horn", CutoutChannel.Rot, 0, 0, 1.2f, 2, 2.4f, 0);
            c.Key("legL", CutoutChannel.Rot, 0, 0, 1.2f, 2, 2.4f, 0);
            c.Key("legR", CutoutChannel.Rot, 0, 0, 1.2f, -2, 2.4f, 0);
            return c;
        }

        public static CutoutClip Attack()
        {
            var c = new CutoutClip { Name = "attack", Length = 1.1f };
            // wind up (back + down), lunge forward, recover
            c.Key("root", CutoutChannel.X, 0, 0, 0.3f, 0.14f, 0.45f, -0.55f, 0.6f, -0.5f, 1.1f, 0);
            c.Key("body", CutoutChannel.Rot, 0, 0, 0.3f, -10, 0.45f, 14, 0.62f, 10, 1.1f, 0);
            c.Key("body", CutoutChannel.SY, 0, 1, 0.3f, 0.86f, 0.45f, 1.1f, 0.62f, 1.04f, 1.1f, 1);
            c.Key("body", CutoutChannel.SX, 0, 1, 0.3f, 1.12f, 0.45f, 0.94f, 0.62f, 0.98f, 1.1f, 1);
            c.Key("eye", CutoutChannel.SY, 0, 1, 0.3f, 0.55f, 0.45f, 1.15f, 0.7f, 1, 1.1f, 1);
            c.Key("armB", CutoutChannel.Rot, 0, 0, 0.3f, 45, 0.46f, -75, 0.65f, -60, 1.1f, 0);
            c.Key("armF", CutoutChannel.Rot, 0, 0, 0.3f, -25, 0.46f, 35, 0.65f, 25, 1.1f, 0);
            c.Key("horn", CutoutChannel.Rot, 0, 0, 0.3f, -6, 0.46f, 10, 1.1f, 0);
            c.Key("ear", CutoutChannel.Rot, 0, 0, 0.3f, -12, 0.5f, 15, 1.1f, 0);
            c.Key("legL", CutoutChannel.Rot, 0, 0, 0.3f, -6, 0.46f, 14, 1.1f, 0);
            c.Key("legR", CutoutChannel.Rot, 0, 0, 0.3f, 6, 0.46f, -10, 1.1f, 0);
            return c;
        }

        public static CutoutClip Hit()
        {
            var c = new CutoutClip { Name = "hit", Length = 0.7f };
            c.Key("root", CutoutChannel.X, 0, 0, 0.08f, 0.3f, 0.35f, 0.08f, 0.7f, 0);
            c.Key("body", CutoutChannel.Rot, 0, 0, 0.08f, -14, 0.22f, 6, 0.38f, -3, 0.7f, 0);
            c.Key("body", CutoutChannel.SX, 0, 1, 0.08f, 1.12f, 0.22f, 0.95f, 0.4f, 1.02f, 0.7f, 1);
            c.Key("body", CutoutChannel.SY, 0, 1, 0.08f, 0.88f, 0.22f, 1.05f, 0.4f, 0.98f, 0.7f, 1);
            c.Key("eye", CutoutChannel.SY, 0, 1, 0.06f, 0.2f, 0.4f, 0.3f, 0.6f, 1, 0.7f, 1);
            c.Key("armB", CutoutChannel.Rot, 0, 0, 0.1f, 30, 0.4f, -8, 0.7f, 0);
            c.Key("armF", CutoutChannel.Rot, 0, 0, 0.1f, -30, 0.4f, 8, 0.7f, 0);
            c.Key("ear", CutoutChannel.Rot, 0, 0, 0.1f, -20, 0.3f, 12, 0.7f, 0);
            c.Key("", CutoutChannel.Flash, 0, 0, 0.03f, 0.9f, 0.18f, 0, 0.7f, 0);
            return c;
        }

        public static CutoutClip Death()
        {
            var c = new CutoutClip { Name = "death", Length = 1.6f };
            // stagger back, tip over a little, collapse into a squashed heap, eyes shut, fade (stays in its lane)
            c.Key("root", CutoutChannel.X, 0, 0, 0.2f, 0.18f, 0.7f, 0.3f, 1.6f, 0.3f);
            c.Key("root", CutoutChannel.Rot, 0, 0, 0.2f, -10, 0.4f, 5, 0.75f, -28, 0.85f, -22, 1.6f, -24);
            c.Key("body", CutoutChannel.SY, 0, 1, 0.2f, 0.9f, 0.45f, 1.05f, 0.8f, 0.6f, 0.9f, 0.7f, 1.0f, 0.62f, 1.6f, 0.62f);
            c.Key("body", CutoutChannel.SX, 0, 1, 0.2f, 1.08f, 0.45f, 0.97f, 0.8f, 1.22f, 0.9f, 1.15f, 1.6f, 1.18f);
            c.Key("eye", CutoutChannel.SY, 0, 1, 0.3f, 0.6f, 0.6f, 0.08f, 1.6f, 0.08f);
            c.Key("armB", CutoutChannel.Rot, 0, 0, 0.4f, 25, 0.85f, -55, 1.6f, -50);
            c.Key("armF", CutoutChannel.Rot, 0, 0, 0.4f, -20, 0.85f, 50, 1.6f, 45);
            c.Key("legL", CutoutChannel.Rot, 0, 0, 0.8f, 25, 1.6f, 22);
            c.Key("legR", CutoutChannel.Rot, 0, 0, 0.8f, -20, 1.6f, -18);
            c.Key("horn", CutoutChannel.Rot, 0, 0, 0.8f, -10, 1.6f, -10);
            c.Key("ear", CutoutChannel.Rot, 0, 0, 0.9f, -35, 1.6f, -30);
            c.Key("", CutoutChannel.Flash, 0, 0, 0.04f, 0.8f, 0.2f, 0, 1.6f, 0);
            c.Key("", CutoutChannel.Alpha, 0, 1, 1.0f, 1, 1.6f, 0);
            return c;
        }

        public static CutoutClip Celebrate()
        {
            var c = new CutoutClip { Name = "celebrate", Length = 1.2f, Loop = true };
            // two hops per loop, squash on each landing, arms up and waving
            c.Key("root", CutoutChannel.Y, 0, 0, 0.15f, 0.32f, 0.3f, 0, 0.45f, 0.32f, 0.6f, 0, 1.2f, 0);
            c.Key("body", CutoutChannel.SY, 0, 0.88f, 0.08f, 1.08f, 0.3f, 0.86f, 0.38f, 1.08f, 0.6f, 0.86f, 0.75f, 1.02f, 1.2f, 0.88f);
            c.Key("body", CutoutChannel.SX, 0, 1.1f, 0.08f, 0.95f, 0.3f, 1.1f, 0.38f, 0.95f, 0.6f, 1.1f, 0.75f, 1, 1.2f, 1.1f);
            c.Key("body", CutoutChannel.Rot, 0, 0, 0.3f, 6, 0.6f, -6, 0.9f, 4, 1.2f, 0);
            c.Key("eye", CutoutChannel.SY, 0, 0.55f, 1.2f, 0.55f);
            c.Key("armB", CutoutChannel.Rot, 0, 40, 0.3f, 60, 0.6f, 35, 0.9f, 60, 1.2f, 40);
            c.Key("armF", CutoutChannel.Rot, 0, -50, 0.3f, -75, 0.6f, -45, 0.9f, -75, 1.2f, -50);
            c.Key("ear", CutoutChannel.Rot, 0, 0, 0.15f, 15, 0.3f, -5, 0.45f, 15, 0.6f, -5, 1.2f, 0);
            c.Key("legL", CutoutChannel.Rot, 0, 0, 0.15f, -12, 0.3f, 0, 0.45f, -12, 0.6f, 0, 1.2f, 0);
            c.Key("legR", CutoutChannel.Rot, 0, 0, 0.15f, 12, 0.3f, 0, 0.45f, 12, 0.6f, 0, 1.2f, 0);
            return c;
        }
    }
}
