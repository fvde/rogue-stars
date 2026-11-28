using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>What an imported file is (decides its destination and whether it is trimmed to its opaque pixels).</summary>
    public enum ArtKind { Alien, Enemy, TileSet, Ground, Background, Decor, Slice }

    /// <summary>One file to copy: generated source → stable path under <c>Assets/Art/Resources/Art/</c>.</summary>
    public sealed class ArtImportItem
    {
        public ArtKind Kind;
        public string Id;
        public string Source;
        /// <summary>Destination relative to <c>Assets/Art/Resources/</c>, e.g. <c>Art/Enemies/ember_imp.png</c>.</summary>
        public string Dest;
        /// <summary>Trim transparent margins (tile slabs, decor props) so the art fills the box it is drawn in.</summary>
        public bool Trim;
        /// <summary>Copy only when the destination does not exist yet (curated full-frame backgrounds are never replaced).</summary>
        public bool KeepExisting;

        public override string ToString() => Kind + " " + Id + ": " + Source + " → " + Dest;
    }

    /// <summary>
    /// Which generated files go into the game (pure; tested). The generator (tools/art) writes
    /// <c>~/AI/assets/&lt;category&gt;/&lt;id&gt;/&lt;variant&gt;_s&lt;seed&gt;.png</c>; only the simple variants are eligible
    /// (<see cref="SpriteVariants"/>: C, then S — one per id, the preferred variant first, then the lowest seed), minus the
    /// <see cref="Skip"/> list. Full-frame board backgrounds come from <c>~/AI/out/pkg-boards/&lt;biome&gt;__BB__*.png</c>.
    /// </summary>
    public static class ArtImportRules
    {
        /// <summary>Eligible sprite variants in order of preference (C = Flux simple, S = Flux extra simple; A_ = too complicated).</summary>
        public static readonly List<string> SpriteVariants = new List<string> { "C", "S" };
        /// <summary>Ground texture variants in order of preference (any full-frame ground; tools/art/prompts/direction-ground.json).</summary>
        public static readonly List<string> GroundVariants = new List<string> { "G2", "G1", "C", "S" };
        /// <summary>Board background variants (the distinct "BB" set; the "BA" ones look alike).</summary>
        public static readonly List<string> BoardVariants = new List<string> { "BB" };
        /// <summary>Files never imported: "&lt;category&gt;/&lt;id&gt;/&lt;file without .png&gt;" (the owner rejected them).</summary>
        public static readonly HashSet<string> Skip = new HashSet<string>
        {
            "enemies/null_shade/C_s1301", // too featureless
            "enemies/magma_slug/C_s1301", // blurry
            "boards/moon_craters/BB_s3001", // not one of the distinct boards
        };

        /// <summary>"C_s1301.png" → C, 1301. False for anything else (review sheets, stray files).</summary>
        public static bool TryParse(string fileName, out string variant, out int seed)
        {
            variant = null;
            seed = 0;
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
            var stem = fileName.Substring(0, fileName.Length - 4);
            int i = stem.LastIndexOf("_s", StringComparison.Ordinal);
            if (i <= 0 || !int.TryParse(stem.Substring(i + 2), out seed)) return false;
            variant = stem.Substring(0, i);
            return true;
        }

        /// <summary>
        /// The file to import among <paramref name="files"/> (names, with their category/id prefix <paramref name="key"/> for the
        /// skip list): the first variant of <paramref name="variants"/> that has a file, lowest seed first; null if none.
        /// </summary>
        public static string Pick(IEnumerable<string> files, string key, IList<string> variants)
        {
            string best = null;
            int bestRank = int.MaxValue, bestSeed = int.MaxValue;
            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                if (!TryParse(name, out var v, out int seed)) continue;
                int rank = variants.IndexOf(v);
                if (rank < 0) continue;
                if (Skip.Contains(key + "/" + name.Substring(0, name.Length - 4))) continue;
                if (rank < bestRank || (rank == bestRank && seed < bestSeed)) { best = path; bestRank = rank; bestSeed = seed; }
            }
            return best;
        }

        /// <summary>Decor folder "lava_2" → biome "lava" (the part before the trailing number), else null.</summary>
        public static string DecorBiome(string folder)
        {
            int i = folder.LastIndexOf('_');
            if (i <= 0 || !int.TryParse(folder.Substring(i + 1), out _)) return null;
            return folder.Substring(0, i);
        }

        /// <summary>
        /// Everything to import from <paramref name="assetsRoot"/> (~/AI/assets) and <paramref name="boardsRoot"/>
        /// (~/AI/out/pkg-boards), sorted by destination. <paramref name="dirs"/> / <paramref name="files"/> list a folder's
        /// sub-folders / files (full paths; empty when missing) so tests can feed a fake tree.
        /// </summary>
        public static List<ArtImportItem> Plan(string assetsRoot, string boardsRoot, Func<string, IEnumerable<string>> dirs, Func<string, IEnumerable<string>> files)
        {
            var items = new List<ArtImportItem>();
            // aliens / enemies: their own folders plus the simple redo (one folder for both, routed by the alien_ prefix)
            var creatures = new SortedDictionary<string, List<(string key, string path)>>(StringComparer.Ordinal);
            void Collect(string category, Func<string, bool> accept)
            {
                foreach (var dir in dirs(Path.Combine(assetsRoot, category)))
                {
                    var id = Path.GetFileName(dir);
                    if (!accept(id)) continue;
                    if (!creatures.TryGetValue(id, out var list)) creatures[id] = list = new List<(string, string)>();
                    foreach (var f in files(dir)) list.Add((category + "/" + id, f));
                }
            }
            Collect("aliens", _ => true);
            Collect("enemies", _ => true);
            Collect("simple-redo", _ => true);
            foreach (var kv in creatures)
            {
                bool alien = kv.Key.StartsWith("alien_", StringComparison.Ordinal);
                string best = null;
                // the skip list is keyed by the file's own category folder
                var eligible = kv.Value.Where(e => Pick(new[] { e.path }, e.key, SpriteVariants) != null).Select(e => e.path).ToList();
                best = Pick(eligible, "", SpriteVariants);
                if (best == null) continue;
                items.Add(new ArtImportItem
                {
                    Kind = alien ? ArtKind.Alien : ArtKind.Enemy, Id = kv.Key, Source = best,
                    Dest = (alien ? "Art/Aliens/" : "Art/Enemies/") + kv.Key + ".png",
                });
            }
            // tile slabs: one generic slab per biome tile set
            foreach (var dir in dirs(Path.Combine(assetsRoot, "board-tile-sprites")))
            {
                var biome = Path.GetFileName(dir);
                var best = Pick(files(dir), "board-tile-sprites/" + biome, SpriteVariants);
                if (best != null) items.Add(new ArtImportItem { Kind = ArtKind.TileSet, Id = biome, Source = best, Dest = "Art/Boards/" + biome + "/tiles/slab.png", Trim = true });
            }
            // seamless ground textures (full-frame, not trimmed)
            foreach (var dir in dirs(Path.Combine(assetsRoot, "grounds")))
            {
                var biome = Path.GetFileName(dir);
                var best = Pick(files(dir), "grounds/" + biome, GroundVariants);
                if (best != null) items.Add(new ArtImportItem { Kind = ArtKind.Ground, Id = biome, Source = best, Dest = "Art/Boards/" + biome + "/ground.png" });
            }
            // decor props: ~/AI/assets/decor/<biome>_<n>/ → Art/Decor/<biome>/<biome>_<n>.png
            foreach (var dir in dirs(Path.Combine(assetsRoot, "decor")))
            {
                var folder = Path.GetFileName(dir);
                var biome = DecorBiome(folder);
                if (biome == null) continue;
                var best = Pick(files(dir), "decor/" + folder, SpriteVariants);
                if (best != null) items.Add(new ArtImportItem { Kind = ArtKind.Decor, Id = folder, Source = best, Dest = "Art/Decor/" + biome + "/" + folder + ".png", Trim = true });
            }
            // full-frame board backgrounds: <biome>__BB__<model>__s<seed>.png; existing (curated) backgrounds are kept
            var boards = new SortedDictionary<string, (string path, int rank, int seed)>(StringComparer.Ordinal);
            foreach (var f in files(boardsRoot))
            {
                var name = Path.GetFileName(f);
                if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = name.Substring(0, name.Length - 4).Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length != 4 || !parts[3].StartsWith("s") || !int.TryParse(parts[3].Substring(1), out int seed)) continue;
                int rank = BoardVariants.IndexOf(parts[1]);
                if (rank < 0 || Skip.Contains("boards/" + parts[0] + "/" + parts[1] + "_" + parts[3])) continue;
                if (!boards.TryGetValue(parts[0], out var cur) || rank < cur.rank || (rank == cur.rank && seed < cur.seed)) boards[parts[0]] = (f, rank, seed);
            }
            foreach (var kv in boards)
                items.Add(new ArtImportItem { Kind = ArtKind.Background, Id = kv.Key, Source = kv.Value.path, Dest = "Art/Boards/" + kv.Key + "/background.png", KeepExisting = true });
            items.Sort((a, b) => string.CompareOrdinal(a.Dest, b.Dest));
            return items;
        }

        /// <summary>
        /// Vertical-slice packages (tools/art/packages/slice-*.json) → their ArtCatalog folder and whether the art is trimmed
        /// to its opaque pixels (props, icons) or kept full-frame (textures, battle arenas).
        /// </summary>
        public static readonly Dictionary<string, (string folder, bool trim)> SliceDest = new Dictionary<string, (string, bool)>
        {
            { "slice-markers", ("Art/Slice/Markers", true) },
            { "slice-setpieces", ("Art/Slice/SetPieces", true) },
            { "slice-scatter", ("Art/Slice/Scatter", true) },
            { "slice-hud", ("Art/Slice/Hud", true) },
            { "slice-skills", ("Art/Skills", true) },
            { "slice-texture", ("Art/Slice/Textures", false) },
            { "slice-battle", ("Art/Slice/Battle", false) },
            // screens pass (rest, events, chest, shop): NPCs and scene props, trimmed
            { "run-characters", ("Art/Run/Characters", true) },
            { "run-props", ("Art/Run/Props", true) },
            { "run-icons", ("Art/Run/Icons", true) },
            // art import 2026-11-25: home icons / backdrops, run result emblems, iso ground chunks, the hero worm
            { "hub-icons", ("Art/Hub/Icons", true) },
            { "hub-scene", ("Art/Hub/Scene", false) },
            { "run-result", ("Art/Result", true) },
            { "board-ground", ("Art/Slice/Ground", true) },
            { "hero", ("Art/Hero", false) },
            // art import 2 (2026-11-26): the new alien / enemy sprites (aliens3, enemies3), skill icons, the home planet stages
            { "aliens", ("Art/Aliens", false) },
            { "enemies", ("Art/Enemies", false) },
            { "skills-v2", ("Art/Skills", true) },
            { "hub-planet", ("Art/Hub/Planet", false) },
            // art import 3 (2026-11-26): boss sprites (enemy defs, ArtCatalog.Enemy), battle status icons, summon / menu icons, the drone sidekick
            // (import 4 adds the cleaned magma_tyrant / void_maw / prism_tyrant / rust_queen / storm_hive)
            { "bosses", ("Art/Enemies", false) },
            { "status-icons", ("Art/Status", true) },
            { "summon-icons", ("Art/Summon/Icons", true) },
            { "sidekicks", ("Art/Sidekicks", false) },
            // art import 4 (2026-11-27): the City buildings (one per blueprint, ArtCatalog.Building), each on its foundation plate
            { "buildings", ("Art/City/Buildings", true) },
            // UI kit v2 batch 1 (2026-11-28): the owner-approved kit pieces, full-frame at their spec size (9-slice borders:
            // KitImportRules), plus approved icons reused as kit icons (trimmed). KitArt / KitV2 load them by id.
            { "ui-kit-v2", ("Art/Ui/Kit", false) },
            { "ui-kit-icons", ("Art/Ui/Kit", true) },
        };

        /// <summary>
        /// A picks category without its package generation suffix: "slice-hud2" → "slice-hud", "slice-markers4" →
        /// "slice-markers" (a redo package lands in <c>&lt;assets&gt;/slice-hud2/</c> but goes to the same Resources folder).
        /// </summary>
        public static string BaseCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return category;
            int n = category.Length;
            while (n > 0 && char.IsDigit(category[n - 1])) n--;
            return n > 0 && n < category.Length ? category.Substring(0, n) : category;
        }

        /// <summary>
        /// The source file of a pick: an absolute path ("/…") or a home path ("~/…", e.g. a full-frame original under
        /// ~/AI/out) as given; a value with a "/" is a path under <c>&lt;assets&gt;/&lt;category&gt;/</c> (an alternate, or
        /// "../hub-icons/tab_worm/S_s5001.png" for a file of another package); else a file in <c>&lt;assets&gt;/&lt;category&gt;/&lt;id&gt;/</c>.
        /// </summary>
        public static string PickSource(string assetsRoot, string category, string id, string value)
        {
            if (value.StartsWith("~/", StringComparison.Ordinal))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value.Substring(2));
            if (Path.IsPathRooted(value)) return value;
            return value.Contains("/") ? Path.Combine(assetsRoot, category, value) : Path.Combine(assetsRoot, category, id, value);
        }

        /// <summary>The generic boss marker is retired: an old pick of it is never imported.</summary>
        public const string RetiredBossMarker = "slice-markers/boss";

        /// <summary>
        /// The slice / run art the owner picked (no automatic pick: every file is approved by hand). <paramref name="picks"/> maps
        /// "&lt;category&gt;/&lt;id&gt;" (e.g. "slice-markers/enemy", "run-props/campfire_big") to the chosen file name in
        /// <c>&lt;assets&gt;/&lt;category&gt;/&lt;id&gt;/</c> ("C_s1301.png"), or to a path under <c>&lt;assets&gt;/&lt;category&gt;/</c>
        /// when it holds a "/" — an alternate of another subject ("slice-setpieces/giant_mushroom_2": "giant_mushroom/J_s5201.png");
        /// tools/art/slice-picks.json holds them.
        /// </summary>

        public static List<ArtImportItem> PlanSlice(string assetsRoot, IDictionary<string, string> picks)
        {
            var items = new List<ArtImportItem>();
            if (picks == null) return items;
            foreach (var kv in picks)
            {
                var parts = kv.Key.Split('/');
                if (parts.Length != 2 || string.IsNullOrEmpty(kv.Value)) continue;
                // a category maps as written ("skills-v2") or without its generation suffix ("slice-hud2" → "slice-hud")
                if (!SliceDest.TryGetValue(parts[0], out var dest) && !SliceDest.TryGetValue(BaseCategory(parts[0]), out dest)) continue;
                if (BaseCategory(parts[0]) + "/" + parts[1] == RetiredBossMarker) continue; // the boss gate shows the run's own boss sprite (IsoArt.BossMarker)
                items.Add(new ArtImportItem
                {
                    Kind = ArtKind.Slice, Id = kv.Key,
                    Source = PickSource(assetsRoot, parts[0], parts[1], kv.Value),
                    Dest = dest.folder + "/" + parts[1] + ".png", Trim = dest.trim,
                });
            }
            items.Sort((a, b) => string.CompareOrdinal(a.Dest, b.Dest));
            return items;
        }

        /// <summary>Reads a picks file: a flat JSON object of string → string (no JSON library needed).</summary>
        public static Dictionary<string, string> ReadPicks(string json)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return d;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\""))
                d[m.Groups[1].Value] = m.Groups[2].Value;
            return d;
        }
    }

    /// <summary>
    /// UI kit v2 sprites (docs/design/ui-kit-v2.md, <c>Assets/Art/Resources/Art/Ui/Kit/&lt;id&gt;.png</c>, picks "ui-kit-v2/…" and
    /// "ui-kit-icons/…" in tools/art/slice-picks.json): the imported ids and the 9-slice border of each stretchable piece at its
    /// spec size. <see cref="ArtImportSettings"/> writes the border into the sprite on import, scaled to the file's actual pixel
    /// size; fixed pieces and icons get none.
    /// </summary>
    public static class KitImportRules
    {
        public const string Folder = "/Art/Ui/Kit/";

        /// <summary>9-slice pieces: id → (spec width, spec height, border px at the spec size), from the doc's table.</summary>
        public static readonly Dictionary<string, (int w, int h, int border)> NineSlice = new Dictionary<string, (int, int, int)>
        {
            { "topbar", (1080, 230, 24) },
            { "currency_pill", (240, 70, 34) },
            { "season_bar", (640, 90, 40) },
            { "season_bar_fill", (640, 90, 40) },
            { "timer_chip", (140, 40, 20) },
            { "quick_band", (900, 170, 40) },
            { "start_button", (540, 210, 60) },
            { "power_plate", (460, 90, 40) },
            { "stat_bar", (340, 70, 34) },
            { "tab_active", (340, 110, 30) },
            { "tab_inactive", (340, 110, 30) },
            { "grid_panel", (1080, 900, 30) },
            { "grid_header", (1080, 130, 30) },
            { "button_small_blue", (260, 86, 30) },
            { "button_small_yellow", (260, 86, 30) },
            { "hp_frame", (600, 60, 14) },
            { "hp_red", (600, 60, 14) },
            { "hp_shield_blue", (600, 60, 14) },
            { "hp_rage_yellow", (600, 60, 14) },
            { "speed_button", (200, 110, 30) },
            { "nav_tile", (170, 170, 56) }, // also the home side rail: stretched, at 75% opacity (owner: no side_rail piece)
        };

        /// <summary>Fixed pieces (drawn whole, no border).</summary>
        public static readonly string[] Fixed =
        {
            "avatar_frame", "badge_red", "edge_tab_left", "edge_tab_right", "level_hex", "list_button", "nav_tile_active", "round_button",
            "pip_gem_blue", "pip_gem_gold", "pip_gem_purple", "pip_strip", "plus_button", "skill_tile", "skill_tile_active", "slot_badge",
            "tile_common", "tile_rare", "tile_epic", "tile_legendary", "tile_mythic",
        };

        /// <summary>Approved game icons reused as kit icons (currencies; the HUD cog, the settings glyph on <c>round_button</c>).</summary>
        public static readonly string[] Icons = { "icon_stamina", "icon_diamond", "icon_ore", "icon_settings" };

        /// <summary>Every imported kit id.</summary>
        public static IEnumerable<string> All => NineSlice.Keys.Concat(Fixed).Concat(Icons);

        /// <summary>The kit id of an asset path under <c>…/Art/Ui/Kit/</c>, else null.</summary>
        public static string IdOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            int i = assetPath.IndexOf(Folder, StringComparison.Ordinal);
            if (i < 0) return null;
            return Path.GetFileNameWithoutExtension(assetPath.Substring(i + Folder.Length));
        }

        /// <summary>Kit icons (downscaled on import: drawn at ≤ 160 px, no mip maps).</summary>
        public static bool IsIcon(string id) => id != null && id.StartsWith("icon_", StringComparison.Ordinal);

        /// <summary>Sprite border (left, bottom, right, top) of <paramref name="id"/> for a file of <paramref name="width"/> × <paramref name="height"/> px.</summary>
        public static Vector4 Border(string id, int width, int height)
        {
            if (id == null || !NineSlice.TryGetValue(id, out var spec)) return Vector4.zero;
            float bx = Mathf.Round(spec.border * (width > 0 ? width / (float)spec.w : 1f));
            float by = Mathf.Round(spec.border * (height > 0 ? height / (float)spec.h : 1f));
            return new Vector4(bx, by, bx, by);
        }
    }

    /// <summary>
    /// <c>Tools/Art/Import Generated Art</c>: copies the eligible generated art (<see cref="ArtImportRules"/>) into
    /// <c>Assets/Art/Resources/Art/…</c> under stable names, trims tile slabs and decor props to their opaque pixels, and
    /// writes only files whose bytes changed. Batchmode:
    /// <c>Unity -batchmode -nographics -quit -projectPath … -executeMethod RogueStars.Client.Editor.ArtImporter.ImportCli</c>
    /// (<c>ROGUESTARS_ART_ASSETS</c> / <c>ROGUESTARS_ART_BOARDS</c> override ~/AI/assets and ~/AI/out/pkg-boards).
    /// </summary>
    public static class ArtImporter
    {
        public const string ResourcesRoot = "Assets/Art/Resources";

        public static string DefaultAssetsRoot => Environment.GetEnvironmentVariable("ROGUESTARS_ART_ASSETS")
                                                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AI/assets");
        public static string DefaultBoardsRoot => Environment.GetEnvironmentVariable("ROGUESTARS_ART_BOARDS")
                                                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AI/out/pkg-boards");

        [MenuItem("Tools/Art/Import Generated Art")]
        public static void ImportMenu()
        {
            var log = Import(DefaultAssetsRoot, DefaultBoardsRoot);
            Debug.Log("[ArtImporter]\n" + string.Join("\n", log));
        }

        /// <summary>Batchmode entry point (-executeMethod); prints what was written.</summary>
        public static void ImportCli()
        {
            var log = Import(DefaultAssetsRoot, DefaultBoardsRoot);
            foreach (var l in log) Console.WriteLine("[ArtImporter] " + l);
        }

        private static IEnumerable<string> Dirs(string path) =>
            Directory.Exists(path) ? Directory.GetDirectories(path).OrderBy(p => p, StringComparer.Ordinal) : Enumerable.Empty<string>();

        private static IEnumerable<string> Files(string path) =>
            Directory.Exists(path) ? Directory.GetFiles(path).OrderBy(p => p, StringComparer.Ordinal) : Enumerable.Empty<string>();

        /// <summary>Imports everything eligible; returns one line per item ("written", "unchanged", "kept").</summary>
        public static List<string> Import(string assetsRoot, string boardsRoot)
        {
            var log = new List<string>();
            var plan = ArtImportRules.Plan(assetsRoot, boardsRoot, Dirs, Files);
            // the slice art: only the owner's picks (tools/art/slice-picks.json, env ROGUESTARS_SLICE_PICKS overrides)
            var picksPath = Environment.GetEnvironmentVariable("ROGUESTARS_SLICE_PICKS") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../tools/art/slice-picks.json"));
            if (File.Exists(picksPath))
                foreach (var item in ArtImportRules.PlanSlice(assetsRoot, ArtImportRules.ReadPicks(File.ReadAllText(picksPath))))
                {
                    if (File.Exists(item.Source)) plan.Add(item);
                    else log.Add("missing   " + item.Source);
                }
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var item in plan)
                {
                    var dest = Path.Combine(ResourcesRoot, item.Dest);
                    if (item.KeepExisting && File.Exists(dest)) { log.Add("kept      " + item.Dest); continue; }
                    var bytes = File.ReadAllBytes(item.Source);
                    if (item.Trim) bytes = TrimToOpaque(bytes) ?? bytes;
                    if (File.Exists(dest) && File.ReadAllBytes(dest).SequenceEqual(bytes)) { log.Add("unchanged " + item.Dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, bytes);
                    log.Add("written   " + item.Dest + "  ←  " + item.Source);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();
            return log;
        }

        /// <summary>The PNG cropped to its opaque pixels (alpha &gt; 8) plus a 2 px margin; null when it cannot be read.</summary>
        public static byte[] TrimToOpaque(byte[] png)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(png)) return null;
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height, x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x].a > 8) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                if (x1 < 0) return null;
                x0 = Math.Max(0, x0 - 2); y0 = Math.Max(0, y0 - 2); x1 = Math.Min(w - 1, x1 + 2); y1 = Math.Min(h - 1, y1 + 2);
                int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
                if (cw == w && ch == h) return png;
                var crop = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                crop.SetPixels(tex.GetPixels(x0, y0, cw, ch));
                crop.Apply();
                var bytes = crop.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(crop);
                return bytes;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
