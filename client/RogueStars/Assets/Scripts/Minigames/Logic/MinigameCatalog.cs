using System;
using System.Collections.Generic;

namespace RogueStars.Client.Minigames
{
    /// <summary>Client class per rules mini-game id (ContentDb.Minigames).</summary>
    public static class MinigameCatalog
    {
        private static readonly Dictionary<string, Func<int, MinigameLogic>> Games = new Dictionary<string, Func<int, MinigameLogic>>
        {
            { "portal_slip", s => new PortalSlip(s) },
            { "comet_catch", s => new CometCatch(s) },
            { "saucer_landing", s => new SaucerLanding(s) },
            { "clock_stopper", s => new ClockStopper(s) },
            { "wormhole_dash", s => new WormholeDash(s) },
            { "shield_parry", s => new ShieldParry(s) },
            { "slingshot_spore", s => new SlingshotSpore(s) },
            { "asteroid_slice", s => new AsteroidSlice(s) },
            { "jelly_stack", s => new JellyStack(s) },
            { "signal_echo", s => new SignalEcho(s) },
            { "fossil_flip", s => new FossilFlip(s) },
            { "codebreaker_hatch", s => new CodebreakerHatch(s) },
            { "odd_alien_out", s => new OddAlienOut(s) },
            { "slot_pod", s => new SlotPod(s) },
        };

        public static IEnumerable<string> Ids => Games.Keys;

        public static bool Has(string id) => id != null && Games.ContainsKey(id);

        /// <summary>The game for a rules id and seed; unknown ids fall back to Portal Slip (newer rules, older client).</summary>
        public static MinigameLogic Create(string id, int seed) =>
            id != null && Games.TryGetValue(id, out var make) ? make(seed) : new PortalSlip(seed);
    }
}
