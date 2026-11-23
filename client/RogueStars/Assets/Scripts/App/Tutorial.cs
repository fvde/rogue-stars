using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    /// <summary>
    /// Non-intrusive tutorial: content tutorial steps (Meta onboarding) are completed automatically when the
    /// player has done the thing (TutorialAdvance grants the step reward); the hub shows the next goal that needs another
    /// screen as a card with a "Go" button (PLAY covers the run goals). Steps follow the onboarding v2 order: the first
    /// run before anything else, then equip the starter parts (Parts unlock after level 1), City, worm level, laboratory,
    /// aliens, heroes. No forced popups.
    /// </summary>
    public static class Tutorial
    {
        public sealed class Goal
        {
            public string StepId;
            public string Text;
            /// <summary>ScreenCatalog id to open ("portals", "worm", ...).</summary>
            public string ScreenId;
            public Func<GameState, bool> Done;
        }

        public static readonly List<Goal> Goals = new List<Goal>
        {
            new Goal { StepId = "wake_up", Text = "Wake the worm", ScreenId = "portals", Done = s => true },
            new Goal { StepId = "first_roll", Text = "Open a portal and roll the dice", ScreenId = "portals", Done = s => s.Counter("run_rolls") > 0 },
            new Goal { StepId = "first_battle", Text = "Win your first battle", ScreenId = "portals", Done = s => s.Counter("run_battles_won") > 0 },
            new Goal { StepId = "first_run", Text = "Beat the boss to finish your first run", ScreenId = "portals", Done = s => s.Counter("runs_completed") > 0 },
            new Goal { StepId = "equip_part", Text = "Equip a part on the worm", ScreenId = "worm", Done = s => s.Parts.Equipped.Count > 0 },
            new Goal { StepId = "build_biomass_farm", Text = "Build the Biomass Farm in the City", ScreenId = "city", Done = s => s.Counter("city_buildings_built") > 0 },
            new Goal { StepId = "level_worm", Text = "Level up the worm with Biomass", ScreenId = "worm", Done = s => s.Counter("worm_levels") > 0 },
            new Goal { StepId = "research_skill", Text = "Research a skill in the Laboratory", ScreenId = "laboratory", Done = s => s.Counter("lab_research_started") > 0 },
            new Goal { StepId = "meet_alien", Text = "Meet your first alien", ScreenId = "aliens", Done = s => s.Aliens.Owned.Count > 0 },
            new Goal { StepId = "equip_hero", Text = "Choose a hero persona", ScreenId = "heroes", Done = s => !string.IsNullOrEmpty(s.Heroes.Equipped) },
        };

        private static bool _checking;
        private static readonly HashSet<string> InFlight = new HashSet<string>();

        /// <summary>Completable = not done, previous step done, feature unlocked (mirrors the rules' checks).</summary>
        public static bool Completable(GameState s, ContentDb c, string stepId)
        {
            if (!c.TutorialSteps.TryGetValue(stepId, out var def)) return false;
            if (s.Onboarding.TutorialDone.Contains(stepId)) return false;
            if (def.After != null && !s.Onboarding.TutorialDone.Contains(def.After)) return false;
            return def.FeatureId == null || Features.IsUnlocked(s, c, def.FeatureId);
        }

        /// <summary>The first goal not done yet whose prerequisites are met (null when the tutorial is over).</summary>
        public static Goal Next(GameState s, ContentDb c, long nowMs = 0)
        {
            if (s == null) return null;
            if (Features.IsUnlocked(s, c, Features.Parts) && HasEquippablePart(s, c))
                return new Goal { StepId = null, Text = "Equip your starter parts on the worm", ScreenId = "worm", Done = st => !HasEquippablePart(st, c) };
            foreach (var g in Goals)
                if (!s.Onboarding.TutorialDone.Contains(g.StepId) && Completable(s, c, g.StepId)) return g;
            // Nothing to do right now: point at the next feature unlock ("clear level N" / "unlocks tomorrow").
            var next = Features.Next(s, c, nowMs);
            if (next != null)
                return new Goal { StepId = null, Text = "Next unlock: " + next.Name + " — " + next.Text, ScreenId = "portals", Done = st => false };
            return null;
        }

        /// <summary>An unequipped part fits an empty body slot (client hint; equipping is not gated).</summary>
        public static bool HasEquippablePart(GameState s, ContentDb c)
        {
            foreach (var p in s.Parts.Items.Values)
            {
                if (PartsSystem.IsEquipped(s, p.Uid) || !c.PartsConfig.Defs.TryGetValue(p.DefId, out var def)) continue;
                if (def.Kind == PartKind.Arm)
                {
                    if (!s.Parts.Equipped.ContainsKey(PartSlot.LeftArm) || !s.Parts.Equipped.ContainsKey(PartSlot.RightArm)) return true;
                }
                else if (!s.Parts.Equipped.ContainsKey((PartSlot)(int)def.Kind)) return true;
            }
            return false;
        }

        /// <summary>Advance every step whose action is done (one command per step; re-entrancy safe).</summary>
        public static void Check(GameSession session)
        {
            if (_checking || !session.IsReady || session.State == null) return;
            _checking = true;
            try
            {
                foreach (var g in Goals)
                {
                    var s = session.State;
                    if (InFlight.Contains(g.StepId) || !Completable(s, session.Content, g.StepId) || !g.Done(s)) continue;
                    string id = g.StepId;
                    InFlight.Add(id);
                    session.Run(new TutorialAdvance { Step = id }, _ => InFlight.Remove(id));
                }
            }
            finally { _checking = false; }
        }
    }
}
