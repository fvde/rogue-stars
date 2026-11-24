using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Screens
{
    /// <summary>One firing of a player-side skill in the battle log: which skill and what it did ("Burn ×2").</summary>
    public struct SkillFire
    {
        public string SkillId;
        public string Effect;
        /// <summary>"Magma Tail · Burn ×2": the skill's name and the effect (the label over its tile).</summary>
        public string Label;
    }

    /// <summary>
    /// What the battle's skill strip shows (pure; tested): which log entries are a skill of the player's side firing — the
    /// rules log every skill effect with its source (<c>BattleLogEntry.SkillId</c>: skill damage, statuses applied or resisted,
    /// heals, shields, rage, buffs, summons) — the effect text, and the order of the strip.
    /// </summary>
    public static class BattleSkillFx
    {
        /// <summary>The skill firing of a log entry, or null (no skill, an enemy's skill, a status expiring).</summary>
        public static SkillFire? FireOf(BattleLogEntry e, BattleResult battle, ContentDb c)
        {
            if (e == null || string.IsNullOrEmpty(e.SkillId) || battle == null) return null;
            if (e.Action == BattleAction.StatusExpired) return null;
            if (e.Actor < 0 || e.Actor >= battle.Fighters.Count || battle.Fighters[e.Actor].Side != BattleSide.Player) return null;
            // a sidekick's ability (aliens, drones) plays on the sidekick itself (BattlePlaybackScreen: hop + name float)
            var who = battle.Fighters[e.Actor];
            if (who.Role == CombatRole.Alien || who.Role == CombatRole.Summon) return null;
            string effect = EffectText(e);
            // Command / Rally Cry: the 0-buff on a sidekick marks the extra action it grants
            if (e.Action == BattleAction.Buff && e.Amount == 0 && e.Target >= 0 && e.Target < battle.Fighters.Count
                && (battle.Fighters[e.Target].Role == CombatRole.Alien || battle.Fighters[e.Target].Role == CombatRole.Summon)) effect = "Extra action";
            string name = c?.Skill(e.SkillId)?.Name ?? e.SkillId;
            return new SkillFire { SkillId = e.SkillId, Effect = effect, Label = name + " · " + effect };
        }

        /// <summary>"Burn ×2", "Crit 240", "120 dmg", "+80 HP", "+50 Shield", "+20 Rage", "Buff +15%", "Summon", "Resisted", "Dodged".</summary>
        public static string EffectText(BattleLogEntry e)
        {
            switch (e.Action)
            {
                case BattleAction.StatusApplied: return Status(e.Status) + (e.Stacks > 1 ? " ×" + e.Stacks : "");
                case BattleAction.StatusResisted: return Status(e.Status) + " resisted";
                case BattleAction.Heal:
                case BattleAction.Regen:
                case BattleAction.LifeSteal: return "+" + Fmt.Num(e.Amount) + " HP";
                case BattleAction.Shield: return "+" + Fmt.Num(e.Amount) + " Shield";
                case BattleAction.GainRage: return "+" + Fmt.Num(e.Amount) + " Rage";
                case BattleAction.Buff: return e.Amount != 0 ? "Buff +" + Fmt.Num(e.Amount) + "%" : "Buff";
                case BattleAction.Summon: return "Summon";
                default:
                    if (e.Dodge) return "Dodged";
                    if (e.Crit) return "Crit " + Fmt.Num(e.Amount);
                    return Fmt.Num(e.Amount) + " dmg";
            }
        }

        private static string Status(StatusId s) => s == StatusId.None ? "Status" : SkillText.StatusName(s);

        /// <summary>Every skill firing of the battle, in log order.</summary>
        public static List<SkillFire> Fires(BattleResult battle, ContentDb c)
        {
            var list = new List<SkillFire>();
            if (battle == null) return list;
            foreach (var e in battle.Log) { var f = FireOf(e, battle, c); if (f.HasValue) list.Add(f.Value); }
            return list;
        }

        /// <summary>
        /// The strip's skills: the run's skills plus any other player-side skill that fires (part / mount skills). The ones
        /// that fire in this battle come first, the most active first (ties keep the run's order), so the tiles that animate
        /// are the visible ones; then the silent ones in the run's order.
        /// </summary>
        public static List<string> StripOrder(IList<string> runSkills, BattleResult battle, ContentDb c)
        {
            var counts = new Dictionary<string, int>();
            var firstSeen = new List<string>();
            foreach (var f in Fires(battle, c))
            {
                if (!counts.ContainsKey(f.SkillId)) { counts[f.SkillId] = 0; firstSeen.Add(f.SkillId); }
                counts[f.SkillId]++;
            }
            var all = new List<string>();
            if (runSkills != null) foreach (var id in runSkills) if (!all.Contains(id)) all.Add(id);
            foreach (var id in firstSeen) if (!all.Contains(id)) all.Add(id);
            var fired = all.FindAll(id => counts.ContainsKey(id));
            // stable sort by fire count (List.Sort is not stable: break ties by the run's order)
            fired.Sort((a, b) => counts[a] != counts[b] ? counts[b].CompareTo(counts[a]) : all.IndexOf(a).CompareTo(all.IndexOf(b)));
            var order = new List<string>(fired);
            foreach (var id in all) if (!counts.ContainsKey(id)) order.Add(id);
            return order;
        }
    }

    /// <summary>
    /// Queues skill-fire animations per strip tile (pure; tested): a tile plays one fire at a time for <see cref="Duration"/>
    /// seconds, later fires wait in line; the same effect queued twice in a row is shown once with "×n", and a tile never
    /// holds more than <see cref="MaxPending"/> waiting fires (the oldest is dropped — the battle moves on).
    /// </summary>
    public sealed class SkillFireQueue
    {
        public const int MaxPending = 3;

        private sealed class Item { public string Label; public int Count = 1; public string Text => Count > 1 ? Label + " ×" + Count : Label; }
        private sealed class Lane { public float Left; public Item Active; public readonly List<Item> Pending = new List<Item>(); }

        private readonly Dictionary<string, Lane> _lanes = new Dictionary<string, Lane>();
        private readonly List<string> _keys = new List<string>();

        /// <summary>Seconds one fire plays on its tile (the screen sets it from the playback speed).</summary>
        public float Duration = 0.6f;
        /// <summary>A fire starts playing on tile <c>key</c> with this label.</summary>
        public event Action<string, string> Started;

        public void Push(string key, string label)
        {
            if (!_lanes.TryGetValue(key, out var lane)) { lane = new Lane(); _lanes[key] = lane; _keys.Add(key); }
            if (lane.Active == null) { Start(key, lane, new Item { Label = label }); return; }
            if (lane.Pending.Count > 0 && lane.Pending[lane.Pending.Count - 1].Label == label) { lane.Pending[lane.Pending.Count - 1].Count++; return; }
            if (lane.Pending.Count >= MaxPending) lane.Pending.RemoveAt(0);
            lane.Pending.Add(new Item { Label = label });
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _keys.Count; i++)
            {
                var lane = _lanes[_keys[i]];
                if (lane.Active == null) continue;
                lane.Left -= dt;
                if (lane.Left > 0) continue;
                lane.Active = null;
                if (lane.Pending.Count > 0)
                {
                    var next = lane.Pending[0];
                    lane.Pending.RemoveAt(0);
                    Start(_keys[i], lane, next);
                }
            }
        }

        private void Start(string key, Lane lane, Item item)
        {
            lane.Active = item;
            lane.Left = Math.Max(0.01f, Duration);
            Started?.Invoke(key, item.Text);
        }

        /// <summary>The label playing on a tile now, or null.</summary>
        public string Playing(string key) => _lanes.TryGetValue(key, out var l) ? l.Active?.Text : null;
        /// <summary>Fires waiting on a tile.</summary>
        public int Waiting(string key) => _lanes.TryGetValue(key, out var l) ? l.Pending.Count : 0;
        /// <summary>Any tile still playing or waiting.</summary>
        public bool Busy
        {
            get
            {
                foreach (var l in _lanes.Values) if (l.Active != null || l.Pending.Count > 0) return true;
                return false;
            }
        }
    }
}
