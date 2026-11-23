using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// Run mini-games (GDD §3.6): 14 short skill games on GAME tiles. The client plays a game and decides the tier itself
    /// (thresholds live in each game's client code); the rules only pick the game, its seed and the reward type, and pay
    /// the reward of the tier from <see cref="MinigameTuning.Rewards"/> — the one table to retune.
    /// </summary>
    public sealed partial class ContentDb
    {
        /// <summary>Mini-game definitions in a fixed order (deterministic picks).</summary>
        public readonly List<MinigameDef> Minigames = new List<MinigameDef>();
        public readonly MinigameTuning MinigameTuning = new MinigameTuning();

        public const string MinigameTileId = "minigame";

        public MinigameDef Minigame(string id)
        {
            if (id == null) return null;
            foreach (var m in Minigames) if (m.Id == id) return m;
            return null;
        }

        partial void BuildMinigames()
        {
            var tile = new TileDef
            {
                Id = MinigameTileId, Name = "Alien Arcade", Text = "Play a short mini-game for run coins or a skill roll.",
                Category = TileCategory.Minigame, Valence = TileValence.Special, Once = true,
            };
            BoardTiles[tile.Id] = tile;

            Game("portal_slip", "Portal Slip", MinigameInput.Tap, "Tap when the portal is fully open — 5 slips, faster each time.");
            Game("comet_catch", "Comet Catch", MinigameInput.Drag, "Drag the worm into the comets' paths. Dodge the fiery ones!");
            Game("saucer_landing", "Saucer Landing", MinigameInput.Drag, "Drag the saucer gently onto the pad — mind the solar wind.");
            Game("clock_stopper", "Clock Stopper", MinigameInput.Tap, "Tap to stop the hand on the glowing time.");
            Game("wormhole_dash", "Wormhole Dash", MinigameInput.Tap, "Tap to switch lanes. Dodge rocks, grab coins.");
            Game("shield_parry", "Shield Parry", MinigameInput.Tap, "Tap the shield of the same colour as the shot hits.");
            Game("slingshot_spore", "Slingshot Spore", MinigameInput.Drag, "Pull back and release to fling spores at the aliens.");
            Game("asteroid_slice", "Asteroid Slice", MinigameInput.Swipe, "Swipe to slice rocks. Spare the friendly aliens!");
            Game("jelly_stack", "Jelly Stack", MinigameInput.Tap, "Tap to drop each jelly block onto the tower.");
            Game("signal_echo", "Signal Echo", MinigameInput.Tap, "Watch the aliens' signal, then tap it back.");
            Game("fossil_flip", "Fossil Flip", MinigameInput.Tap, "Flip two cards at a time and match the fossils.");
            Game("codebreaker_hatch", "Codebreaker Hatch", MinigameInput.Tap, "Crack the 4-colour code in 6 tries.");
            Game("odd_alien_out", "Odd Alien Out", MinigameInput.Tap, "Tap the alien that looks different.");
            Game("slot_pod", "Slot Pod", MinigameInput.Tap, "Tap to stop each reel. Match the symbols!");

            var t = MinigameTuning;
            // Coins ≈ shop purchases (shop_standard: Common skill 25, Rare 45, heal 30, stat 40): gold ≈ a good purchase or two.
            t.Rewards.Add(Tier(MinigameTier.Miss, 5, false, 0, 0, 0, 0, 0));
            t.Rewards.Add(Tier(MinigameTier.Bronze, 30, true, 55, 28, 12, 4, 1));
            t.Rewards.Add(Tier(MinigameTier.Silver, 60, true, 30, 38, 22, 8, 2));
            t.Rewards.Add(Tier(MinigameTier.Gold, 100, true, 8, 30, 36, 20, 6));
        }

        private void Game(string id, string name, MinigameInput input, string instruction) =>
            Minigames.Add(new MinigameDef { Id = id, Name = name, Input = input, Instruction = instruction });

        private static MinigameTierReward Tier(MinigameTier tier, long coins, bool draft, double common, double rare, double epic, double legendary, double mythic)
        {
            var r = new MinigameTierReward { Tier = tier, Coins = coins, Draft = draft };
            if (draft)
            {
                r.DraftRarityWeights[Rarity.Common] = common;
                r.DraftRarityWeights[Rarity.Rare] = rare;
                r.DraftRarityWeights[Rarity.Epic] = epic;
                r.DraftRarityWeights[Rarity.Legendary] = legendary;
                r.DraftRarityWeights[Rarity.Mythic] = mythic;
            }
            return r;
        }
    }

    public enum MinigameInput { Tap, Drag, Swipe }

    /// <summary>One mini-game: id (client class), name and the one-line instruction shown before it starts.</summary>
    [Serializable]
    public sealed class MinigameDef
    {
        public string Id;
        public string Name;
        public MinigameInput Input;
        public string Instruction;
        public double Weight = 1;
    }

    /// <summary>What one tier pays: run coins (money) or a skill roll (a 1-of-3 draft with these rarity odds).</summary>
    [Serializable]
    public sealed class MinigameTierReward
    {
        public MinigameTier Tier;
        /// <summary>Money reward (scaled by the level's shop prices, see MinigameTuning.ScaleCoinsWithShop). A Miss pays these as consolation for both reward types.</summary>
        public long Coins;
        /// <summary>Skill roll: whether this tier opens a draft (Miss: no draft, consolation coins instead).</summary>
        public bool Draft;
        public Dictionary<Rarity, double> DraftRarityWeights = new Dictionary<Rarity, double>();
    }

    /// <summary>Placement, reward choice and the reward table of run mini-games.</summary>
    [Serializable]
    public sealed class MinigameTuning
    {
        /// <summary>GAME tiles per board (Normal, Overcharged, Dark Side). Each resolves once (then it is quiet ground).</summary>
        public int TilesPerBoard = 2;
        /// <summary>GAME tiles per Burrow floor (short floors).</summary>
        public int TilesPerBurrowFloor = 1;
        /// <summary>Where the GAME tiles sit along the main path (0..1), away from the campfire / shop / miniboss spots.</summary>
        public List<double> TileFractions = new List<double> { 0.36, 0.88 };
        /// <summary>Chance (%) that a GAME tile pays a skill roll instead of run coins.</summary>
        public double SkillRollChancePct = 50;
        /// <summary>Tier auto mode uses when farming (no player): a Miss = consolation coins only, no skill roll.</summary>
        public MinigameTier FarmingTier = MinigameTier.Miss;
        /// <summary>Coins scale with the level's shop (its Rare skill price / shop_standard's), so dear shops pay more.</summary>
        public bool ScaleCoinsWithShop = true;
        /// <summary>Rewards per tier (Miss, Bronze, Silver, Gold).</summary>
        public List<MinigameTierReward> Rewards = new List<MinigameTierReward>();

        public MinigameTierReward For(MinigameTier tier)
        {
            foreach (var r in Rewards) if (r.Tier == tier) return r;
            return Rewards.Count > 0 ? Rewards[0] : new MinigameTierReward { Tier = tier };
        }
    }
}
