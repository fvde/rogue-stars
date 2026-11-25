using System.Collections.Generic;
using UnityEngine;

namespace RogueStars.Client.World
{
    /// <summary>Where a standing sprite touches the ground (pixels of its rect, origin bottom-left).</summary>
    public struct ContactInfo
    {
        /// <summary>Centre of the footprint: the point that goes on the tile / slot / ground spot.</summary>
        public float X, Y;
        /// <summary>The lowest solid row (the front edge of the base).</summary>
        public float Bottom;
        /// <summary>Width of the base (the solid pixels of the bottom band).</summary>
        public float Width;
        public bool Valid;
    }

    /// <summary>
    /// The ground contact of standing art (owner playtest 0.1.224: "background assets float above the ground"). Imported and
    /// drawn sprites are cut-outs with a little transparent padding and a 3/4 view: the lowest solid row is the front edge of
    /// the base, the footprint's centre lies above it by half the footprint's depth (an iso ellipse: <see cref="DepthShare"/>
    /// × the base width). Placing that centre on the ground point — and the contact shadow around it — seats a prop on its
    /// tile instead of hanging its front edge on the tile's middle with the shadow sticking out under it.
    /// Imported art gets the contact as its sprite pivot at import (ArtImportSettings); code-drawn sprites register theirs
    /// when painted (IsoPaint.ToSprite). Pure finder: <see cref="Find"/>.
    /// </summary>
    public static class SpriteContact
    {
        /// <summary>Half the depth of a footprint as a share of its width (the board's iso ratio StepY / StepX, halved).</summary>
        public const float DepthShare = Iso.StepY / Iso.StepX * 0.5f;
        /// <summary>A pixel counts as solid above this alpha (soft edges and glow are not ground).</summary>
        public const byte SolidAlpha = 128;
        /// <summary>The bottom band measured for the base width: this share of the solid height.</summary>
        public const float BandShare = 0.08f;
        /// <summary>The lower share of the solid height whose extent centres the footprint horizontally.</summary>
        public const float XBandShare = 0.25f;

        private static readonly Dictionary<Sprite, ContactInfo> Registered = new Dictionary<Sprite, ContactInfo>();

        /// <summary>
        /// The contact of an RGBA image (row-major, bottom row first, as Texture2D / IsoPaint store it): the lowest row with
        /// at least 3 % of the solid width (stray specks and thin glow do not count), the base width over the bottom band and
        /// the footprint centre above the front edge.
        /// </summary>
        public static ContactInfo Find(Color32[] px, int w, int h) => Find(i => px[i].a, w, h);

        public static ContactInfo Find(Color[] px, int w, int h) => Find(i => (byte)(px[i].a * 255), w, h);

        private static ContactInfo Find(System.Func<int, byte> alpha, int w, int h)
        {
            var info = new ContactInfo();
            if (w <= 0 || h <= 0) return info;
            int minX = w, maxX = -1, top = -1;
            var rowCount = new int[h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (alpha(y * w + x) > SolidAlpha) { rowCount[y]++; if (x < minX) minX = x; if (x > maxX) maxX = x; top = y; }
            if (maxX < 0) return info;
            int solidW = maxX - minX + 1;
            int need = Mathf.Max(2, Mathf.RoundToInt(solidW * 0.03f));
            int bottom = 0;
            while (bottom < h && rowCount[bottom] < need) bottom++;
            if (bottom >= h) return info;
            int band = Mathf.Max(2, Mathf.RoundToInt((top - bottom + 1) * BandShare));
            int bMin = w, bMax = -1;
            for (int y = bottom; y < Mathf.Min(h, bottom + band); y++)
                for (int x = 0; x < w; x++)
                    if (alpha(y * w + x) > SolidAlpha) { if (x < bMin) bMin = x; if (x > bMax) bMax = x; }
            float bw = bMax - bMin + 1;
            // the footprint's horizontal centre: the lower quarter's extent (a skull resting on its jaw and the back of its
            // cranium centres between both, not under the jaw alone)
            int quarter = Mathf.Max(band, Mathf.RoundToInt((top - bottom + 1) * XBandShare));
            int qMin = w, qMax = -1;
            for (int y = bottom; y < Mathf.Min(h, bottom + quarter); y++)
                for (int x = 0; x < w; x++)
                    if (alpha(y * w + x) > SolidAlpha) { if (x < qMin) qMin = x; if (x > qMax) qMax = x; }
            info.Valid = true;
            info.Bottom = bottom;
            info.Width = bw;
            info.X = (qMin + qMax + 1) * 0.5f;
            info.Y = bottom + bw * DepthShare;
            return info;
        }

        /// <summary>Registers the contact of a code-drawn sprite (its pixels are gone once uploaded).</summary>
        public static void Register(Sprite s, ContactInfo c) { if (s != null && c.Valid) Registered[s] = c; }

        /// <summary>
        /// The contact of a sprite in its rect's pixels: registered (code-drawn), else its pivot when the import set one
        /// (imported art: the pivot is the footprint centre, the base width follows from its height over the front edge),
        /// else the bottom centre of the rect with a base of 60 % of its width.
        /// </summary>
        public static ContactInfo Of(Sprite s)
        {
            if (s == null) return default;
            if (Registered.TryGetValue(s, out var c)) return c;
            var r = s.rect;
            var p = s.pivot; // pixels from the rect's bottom-left
            bool centred = Mathf.Abs(p.x - r.width / 2) < 0.5f && Mathf.Abs(p.y - r.height / 2) < 0.5f;
            if (!centred)
                return new ContactInfo { Valid = true, X = p.x, Y = p.y, Bottom = 0, Width = Mathf.Max(1, p.y / DepthShare) };
            return new ContactInfo { Valid = true, X = r.width / 2, Y = 0, Bottom = 0, Width = r.width * 0.6f };
        }

        /// <summary>Normalised pivot (0..1 of the image) for an import: the footprint centre.</summary>
        public static Vector2 Pivot01(ContactInfo c, int w, int h) =>
            c.Valid ? new Vector2(c.X / w, Mathf.Clamp01(c.Y / h)) : new Vector2(0.5f, 0.5f);
    }
}
