namespace RogueStars.Client.World
{
    /// <summary>
    /// Which way the worm faces. The hero worm art (<c>Art/Hero/hero_worm</c>) faces LEFT, but battles put the enemies on the
    /// right and the board path climbs to the right: the art is mirrored (<c>flip</c> = true) while the worm looks right.
    /// </summary>
    public static class WormFacing
    {
        /// <summary>Horizontal distance below which the worm keeps its current facing (a vertical move or an overlapping target).</summary>
        public const float Dead = 0.01f;

        /// <summary>The mirror state after looking from <paramref name="fromX"/> to <paramref name="toX"/> (screen / board x).</summary>
        public static bool Flip(float fromX, float toX, bool current)
        {
            if (toX > fromX + Dead) return true;   // target on the right: mirror the left-facing art
            if (toX < fromX - Dead) return false;  // target on the left: the art as drawn
            return current;
        }

        /// <summary>The x scale of a UI image carrying the worm art for a mirror state (-1 mirrors about the image's centre).</summary>
        public static float ScaleX(bool flip) => flip ? -1f : 1f;
    }
}
