using RogueStars.Rules.Systems;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// Which clip a battle log entry plays on whom (pure mapping; the playback screen feeds the clips to its creatures).
    /// Attack-type entries (basic, combo, counter, skill, rage) play Attack on the actor and Hit on the target (Dodge: the
    /// target sidesteps with a short Move); a sidekick's turn, a guard or a hero / mount ability hops (Move, one hop); a death
    /// plays Death; the victory banner plays Celebrate on the player's side.
    /// </summary>
    public static class CreatureEvents
    {
        public static bool IsAttack(BattleAction a) =>
            a == BattleAction.Basic || a == BattleAction.Combo || a == BattleAction.Counter || a == BattleAction.Skill || a == BattleAction.Rage;

        /// <summary>The clip the actor of an entry plays (null = none).</summary>
        public static CreatureClip? Actor(BattleAction a)
        {
            if (IsAttack(a)) return CreatureClip.Attack;
            switch (a)
            {
                case BattleAction.SidekickAct:
                case BattleAction.Guard:
                case BattleAction.Volley:
                case BattleAction.Ability: return CreatureClip.Move;
                case BattleAction.Sacrifice: return CreatureClip.Death;
                default: return null;
            }
        }

        /// <summary>The clip the target of an entry plays (null = none). <paramref name="dodge"/>: the hit missed.</summary>
        public static CreatureClip? Target(BattleAction a, bool dodge)
        {
            switch (a)
            {
                case BattleAction.Basic:
                case BattleAction.Combo:
                case BattleAction.Counter:
                case BattleAction.Skill:
                case BattleAction.Rage:
                case BattleAction.Execute:
                case BattleAction.StatusTick:
                case BattleAction.Supernova:
                    return dodge ? CreatureClip.Move : CreatureClip.Hit;
                case BattleAction.Death: return CreatureClip.Death;
                case BattleAction.Ability: return CreatureClip.Move;
                default: return null;
            }
        }

        /// <summary>The clip of the survivors when the fight ends: the winning side celebrates.</summary>
        public static CreatureClip? End(bool playerWon, bool playerSide) => playerWon && playerSide ? CreatureClip.Celebrate : (CreatureClip?)null;

        /// <summary>Length in seconds of a single hop (sidekick turns, abilities).</summary>
        public const float HopSeconds = 0.55f;
    }
}
