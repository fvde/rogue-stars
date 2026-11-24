using UnityEngine;

namespace RogueStars.Client.World
{
    /// <summary>
    /// The board's iso projection (world units; 1 unit = 100 px of a 1080-px-wide portrait screen): grid +X runs up-right,
    /// +Y up-left, a cell's top-face diamond is <see cref="TileW"/> × <see cref="TileH"/> (a dimetric ~1.45 : 1, the look of
    /// the reference boards), one height level lifts a block by <see cref="Rise"/>. Painter's order: further up the screen
    /// (larger x + y) is drawn first.
    /// </summary>
    public static class Iso
    {
        /// <summary>Screen offset of one grid step: ±StepX across, +StepY up.</summary>
        public const float StepX = 0.84f, StepY = 0.58f;
        public const float TileW = 2 * StepX, TileH = 2 * StepY;
        /// <summary>One platform height level.</summary>
        public const float Rise = 0.34f;
        /// <summary>Thickness of a path slab over the platform.</summary>
        public const float Slab = 0.16f;
        /// <summary>Platform wall under a height-0 slab (the decorated trim).</summary>
        public const float Wall = 0.7f;
        /// <summary>Sprite pixels per world unit of the generated art.</summary>
        public const float Ppu = 100;

        /// <summary>Centre of a cell's top face.</summary>
        public static Vector2 Cell(float gx, float gy, float height = 0) => new Vector2((gx - gy) * StepX, (gx + gy) * StepY + height * Rise);

        /// <summary>Painter's order of something standing on (or at) cell gx, gy: further back (larger x + y) first.</summary>
        public static int Order(float gx, float gy) => 30000 - Mathf.RoundToInt((gx + gy) * 20);

        /// <summary>Grid offset of a heading index (0 +Y, 1 +X, 2 −Y, 3 −X; RunBoard.Heading).</summary>
        public static Vector2Int Step(int heading)
        {
            switch (heading & 3)
            {
                case 1: return new Vector2Int(1, 0);
                case 2: return new Vector2Int(0, -1);
                case 3: return new Vector2Int(-1, 0);
                default: return new Vector2Int(0, 1);
            }
        }
    }
}
