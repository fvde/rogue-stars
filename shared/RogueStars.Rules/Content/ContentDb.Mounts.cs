using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    [Serializable]
    public sealed class MountDef
    {
        public string Id;
        public string Name;
        /// <summary>One line: what the mount does ("Spotter: marks one enemy …").</summary>
        public string Tagline;
        /// <summary>Mounts are not tied to a synergy (hero &amp; mount kits): None. Kept for saves and old screens.</summary>
        public SkillFamily Family;
        public SkillFamily SupportFamily;
        /// <summary>Kit skills by step: the passive and the active at 1★ / 3★ / 5★ / 8★ / 10★, each step replacing the one before.</summary>
        public List<SkillStep> Weapons = new List<SkillStep>();
        /// <summary>The kit as the screens show it: Passive and Active lines per step.</summary>
        public List<KitText> Kit = new List<KitText>();
        /// <summary>The active's reload (rounds) at each kit step (1★, 3★, 5★, 8★, 10★).</summary>
        public List<int> Reloads = new List<int>();
    }

    [Serializable]
    public sealed class MountConfig
    {
        public Dictionary<string, MountDef> Defs = new Dictionary<string, MountDef>();
        /// <summary>
        /// Max stars (pull system, docs/design/pull-system.md): 10★ on the shared shard table (ContentDb.ShardStars) like every Mythic;
        /// the former awakening 1–5 are stars 6★–10★ (old saves: stars + awakening).
        /// </summary>
        public int MaxStars = 10;
        /// <summary>What a mount batch (RewardKinds.Mount) of a 10★ mount becomes (summon event exchange tokens).</summary>
        public Reward CopyOverflow;
        /// <summary>StarCoreCosts[s] = Mount Cores to reach star s besides the shards (the former awakening cores, stars 6–10; 0 below).</summary>
        public List<long> StarCoreCosts = new List<long>();
        public int MaxLevelBase = 20;
        public int MaxLevelPerStep = 10;
        /// <summary>LevelFuelCosts[L] = Fuel from level L to L+1.</summary>
        public List<long> LevelFuelCosts = new List<long>();
        /// <summary>Permanent worm stats per star of every owned mount (collection bonus: +1% mount damage).</summary>
        public List<StatMod> CollectionPerStep = new List<StatMod>();
        /// <summary>Mount damage % per level above 1 (Fuel keeps a sink; no mount stats).</summary>
        public double DamagePctPerLevel = 1;
        /// <summary>Mount damage % at each in-between step (2★, 4★, 6★, 7★, 9★): the kit steps change texts, these a number.</summary>
        public double DamagePctPerStep = 5;
        /// <summary>The kit steps: 1★, 3★, 5★, 8★, 10★ (8★ / 10★ were awakening 3 / 5).</summary>
        public int[] KitSteps = { 1, 3, 5, 8, 10 };
        /// <summary>The in-between steps (2★, 4★, 6★, 7★, 9★; were 2★, 4★, A1, A2, A4).</summary>
        public int[] BetweenStars = { 2, 4, 6, 7, 9 };

        public int MaxLevel(int stars) => Math.Min(LevelFuelCosts.Count, MaxLevelBase + MaxLevelPerStep * stars);

        /// <summary>The kit step index (0 = 1★ … 4 = 10★) a mount at these stars has reached.</summary>
        public int KitStep(int stars)
        {
            int k = 0;
            for (int i = 0; i < KitSteps.Length; i++) if (stars >= KitSteps[i]) k = i;
            return k;
        }

        /// <summary>In-between steps reached: each gives DamagePctPerStep mount damage.</summary>
        public int BetweenSteps(int stars)
        {
            int n = 0;
            foreach (var s in BetweenStars) if (stars >= s) n++;
            return n;
        }
    }

    public sealed partial class ContentDb
    {
        /// <summary>Mounts (spaceships): vehicles on a reload, one passive + one active per step (Progression; hero &amp; mount kits).</summary>
        public readonly MountConfig MountConfig = new MountConfig();

        partial void BuildMounts()
        {
            var m = MountConfig;
            m.MaxStars = 10;
            m.CopyOverflow = Reward.Cur(Currencies.MountBolts, 300);
            // The former awakening Mount Cores stay on stars 6–10 (Mount Cores keep their sources: events, Burrow, Dark Side, guild shop).
            m.StarCoreCosts.AddRange(new long[] { 0, 0, 0, 0, 0, 0, 100, 200, 400, 700, 1100 });
            m.MaxLevelBase = 20; m.MaxLevelPerStep = 10;
            m.LevelFuelCosts.Add(0);
            for (int l = 1; l < m.MaxLevelBase + m.MaxLevelPerStep * m.MaxStars; l++) m.LevelFuelCosts.Add((long)Math.Round(50 * Math.Pow(l, 1.5)));
            m.CollectionPerStep.Add(new StatMod(StatId.MountPower, 1));
            m.DamagePctPerLevel = 1; m.DamagePctPerStep = 5;

            const TriggerKind V = TriggerKind.OnVolley;
            const TargetKind Worm = TargetKind.Worm, Front = TargetKind.Enemy;
            const string P = "Passive", A = "Active";

            // ---- Scout Saucer (starter): Spotlight + Scan Pulse ---------------------------------------------------------------
            var d = NewMount("mount_scout_saucer", "Scout Saucer", "Spotter: marks one enemy, feeds it a status you own, and passes its stacks on when it dies.");
            int[] spot = { 10, 15, 20, 25, 30 }, contagion = { 0, 0, 50, 50, 100 };
            string[] spotText =
            {
                "Marks the healthiest enemy. Your hits on the mark get +10% crit chance.",
                "Marks the healthiest enemy. Your hits on the mark get +15% crit chance.",
                "Mark: +20% crit chance. When the mark dies, half its statuses jump to the next mark.",
                "Mark: +25% crit chance. When the mark dies, half its statuses jump to the next mark.",
                "Mark: +30% crit chance. When the mark dies, all its statuses jump to the next mark.",
            };
            int[] pulse = { 80, 100, 120, 140, 160 }, pulseStacks = { 1, 2, 3, 4, 5 }, reload = { 3, 3, 3, 2, 2 };
            string[] pulseText =
            {
                "Every 3 rounds: zap the mark for 80% ATK and 1 stack of a random status you own.",
                "Every 3 rounds: zap the mark for 100% ATK and 2 stacks of a random status you own.",
                "Every 3 rounds: 120% ATK on the mark, 3 stacks of a random owned status. Always crits.",
                "Every 2 rounds: 140% ATK on the mark, 4 stacks of a random owned status. Always crits.",
                "Every 2 rounds: 160% ATK, 5 stacks of a random owned status, always crits; others take 40%.",
            };
            for (int k = 0; k < 5; k++)
            {
                var pt = new List<SkillTrigger> { KRule("spotlight", spot[k]) };
                if (contagion[k] > 0) pt.Add(KRule("contagion", contagion[k]));
                MountStep(d, k, P, "Spotlight", spotText[k], pt.ToArray());
                var at = new List<SkillTrigger>
                {
                    KRule("reload:" + reload[k]),
                    K(V, EffectKind.Damage, pulse[k], Front, k >= 2 ? "crit" : null),
                    K(V, EffectKind.ApplyStatus, 0, Front, "owned", stacks: pulseStacks[k]),
                };
                if (k == 4) at.Add(K(V, EffectKind.Damage, 40, TargetKind.AllOtherEnemies));
                MountStep(d, k, A, "Scan Pulse", pulseText[k], at.ToArray());
            }

            // ---- Magma Barge: Pressure Valve + Eruption -------------------------------------------------------------------
            d = NewMount("mount_magma_barge", "Magma Barge", "Pressure boiler: every status stack builds steam (rage); the bomb can pop a free rage attack.");
            int[] valve = { 10, 15, 20, 25, 30 }, refund = { 0, 0, 10, 15, 20 }, bomb = { 120, 140, 160, 180, 200 }, fillAt = { 0, 0, 70, 60, 60 };
            string[] valveText =
            {
                "Every 10 status stacks the worm applies: +10 rage.",
                "Every 10 status stacks the worm applies: +15 rage.",
                "Every 10 stacks: +20 rage. Each rage attack refunds 10 rage.",
                "Every 10 stacks: +25 rage. Each rage attack refunds 15 rage.",
                "Every 10 stacks: +30 rage. Each rage attack refunds 20 rage.",
            };
            string[] bombText =
            {
                "Every 3 rounds: magma bomb on the front enemy, 120% ATK.",
                "Every 3 rounds: magma bomb on the front enemy, 140% ATK.",
                "Every 3 rounds: bomb for 160% ATK. If the worm has 70+ rage, it fills to full.",
                "Every 2 rounds: bomb for 180% ATK. At 60+ rage, the worm's rage fills.",
                "Every 2 rounds: 200% ATK. At full rage, the worm makes a free rage attack now.",
            };
            for (int k = 0; k < 5; k++)
            {
                var pt = new List<SkillTrigger>
                {
                    K(TriggerKind.OnStatusApplied, EffectKind.GainRage, valve[k], Worm, "round_cap:30", everyN: 10, rider: true),
                };
                if (refund[k] > 0) pt.Add(K(TriggerKind.OnRageAttack, EffectKind.GainRage, refund[k], Worm, rider: true));
                MountStep(d, k, P, "Pressure Valve", valveText[k], pt.ToArray());
                var at = new List<SkillTrigger> { KRule("reload:" + reload[k]), K(V, EffectKind.Damage, bomb[k], Front) };
                if (k == 4) at.Add(K(V, EffectKind.FreeRageAttack, 0, Front, "worm", @if: "worm_rage:100")); // full before the bomb: a free rage attack
                if (fillAt[k] > 0) at.Add(K(V, EffectKind.GainRage, 0, Worm, "fill", @if: "worm_rage:" + fillAt[k]));
                MountStep(d, k, A, "Eruption", bombText[k], at.ToArray());
            }

            // ---- Siphon Hauler (was Reactor / Toxic Hauler; id kept): Siphon Line + Drain Hose ------------------------------------
            d = NewMount("mount_toxic_hauler", "Siphon Hauler", "Siphon tanker: drinks a share of all status damage back into the worm.");
            int[] siphon = { 5, 8, 10, 12, 15 }, siphonCap = { 0, 0, 10, 15, 20 }, hose = { 60, 80, 100, 120, 140 }, tick = { 100, 100, 150, 175, 200 };
            string[] siphonText =
            {
                "The worm heals 5% of the damage its statuses deal (ticks, Supernovas, consumes, discharges).",
                "The worm heals 8% of the damage its statuses deal.",
                "Heals 10% of status damage. Healing past full HP becomes a shield (max 10% max HP).",
                "Heals 12% of status damage. Overheal becomes a shield (max 15% max HP).",
                "Heals 15%; overheal shield (max 20%). While that shield is full, Drain Hose hits every enemy.",
            };
            string[] hoseText =
            {
                "Every 3 rounds: 60% ATK to the front enemy, then its Radiation and Bleed tick once now.",
                "Every 3 rounds: 80% ATK to the front enemy, then its Radiation and Bleed tick once now.",
                "Every 3 rounds: 100% ATK, then its Radiation and Bleed tick once now, at 150%.",
                "Every 2 rounds: 120% ATK, then its Radiation and Bleed tick once now, at 175%.",
                "Every 2 rounds: 140% ATK, ticks at 200%. With a full siphon shield, it hits every enemy.",
            };
            for (int k = 0; k < 5; k++)
            {
                var pt = new List<SkillTrigger>
                {
                    K(TriggerKind.OnStatusDamage, EffectKind.Heal, siphon[k], Worm, siphonCap[k] > 0 ? "of_amount;overheal_shield;siphon" : "of_amount", rider: true),
                };
                if (siphonCap[k] > 0) pt.Add(KRule("siphon_cap", siphonCap[k]));
                MountStep(d, k, P, "Siphon Line", siphonText[k], pt.ToArray());
                string wide = k == 4 ? "wide_siphon" : null;
                MountStep(d, k, A, "Drain Hose", hoseText[k],
                    KRule("reload:" + reload[k]), K(V, EffectKind.Damage, hose[k], Front, wide),
                    K(V, EffectKind.TickStatus, tick[k], Front, wide, StatusId.Poison), K(V, EffectKind.TickStatus, tick[k], Front, wide, StatusId.Bleed));
            }

            // ---- Star Lancer: Lance Sight + Impale ----------------------------------------------------------------------------
            d = NewMount("mount_star_lancer", "Star Lancer", "Crit hunter: worm crits charge the lance; Impale always crits and pierces the line.");
            // a slow heavy lance: every 3 rounds at every step (8★ / 10★ keep the reload; tuned to ~20% / ~30% of the side's damage at 5★ / 10★)
            int[] charge = { 15, 20, 25, 30, 35 }, chargeCap = { 30, 40, 50, 60, 70 }, lance = { 120, 125, 130, 140, 140 }, lanceReload = { 3, 3, 3, 3, 3 };
            string[] sightText =
            {
                "Each worm crit charges the lance: the next Impale deals +15% per crit (max +30%).",
                "Each worm crit charges the lance: the next Impale deals +20% per crit (max +40%).",
                "Each crit charges +25% (max +50%). Crits on enemies below 30% HP charge double.",
                "Each crit charges +30% (max +60%). Crits on enemies below 30% HP charge double.",
                "Each crit charges +35% (max +70%). An Impale kill reloads the Lancer at once.",
            };
            string[] impaleText =
            {
                "Every 3 rounds: lance the front enemy, 120% ATK. This hit always crits.",
                "Every 3 rounds: lance the front enemy, 125% ATK. Always crits.",
                "Every 3 rounds: 130% ATK, always crits, and pierces the enemy behind for half.",
                "Every 3 rounds: 140% ATK, always crits, pierces the enemy behind for half.",
                "Every 3 rounds: 140% ATK, always crits, pierces the whole line for half.",
            };
            for (int k = 0; k < 5; k++)
            {
                var pt = new List<SkillTrigger>
                {
                    K(TriggerKind.OnCrit, EffectKind.Charge, charge[k], Self(), "cap:" + chargeCap[k] + (k >= 2 ? ";double_below:30" : ""), rider: true),
                };
                if (k == 4) pt.Add(K(TriggerKind.OnKill, EffectKind.AdvanceTimer, 0, Self(), "reload", cooldown: 1));
                MountStep(d, k, P, "Lance Sight", sightText[k], pt.ToArray());
                var at = new List<SkillTrigger> { KRule("reload:" + lanceReload[k]), K(V, EffectKind.Damage, lance[k], Front, "crit") };
                if (k == 2 || k == 3) at.Add(K(V, EffectKind.Damage, lance[k] / 2.0, TargetKind.SecondEnemy, "crit"));
                if (k == 4) at.Add(K(V, EffectKind.Damage, lance[k] / 2.0, TargetKind.AllOtherEnemies, "crit"));
                MountStep(d, k, A, "Impale", impaleText[k], at.ToArray());
            }

            // ---- Frost Frigate: Coolant Loop + Ice Wall -----------------------------------------------------------------------
            d = NewMount("mount_frost_frigate", "Frost Frigate", "Cryo bay: coolant shields every round, a cryo pod against death, ice walls that burst.");
            int[] coolant = { 2, 3, 4, 5, 6 }, pod = { 0, 0, 25, 30, 40 }, wallHit = { 60, 70, 80, 90, 100 }, wallShield = { 6, 8, 10, 12, 15 }, burst = { 0, 0, 100, 125, 150 };
            string[] loopText =
            {
                "Each round: remove 1 status stack from the worm and gain a shield of 2% max HP.",
                "Each round: remove 1 status stack from the worm; shield of 3% max HP.",
                "Shield 4%. Cryo pod: once per battle, a fatal hit skips your next turn instead and heals 25%.",
                "Shield 5% per round. Cryo pod once per battle: skip a turn, heal 30%.",
                "Shield 6% per round. Cryo pod heals 40%; removed stacks go onto the front enemy.",
            };
            string[] wallText =
            {
                "Every 3 rounds: 60% ATK to the front enemy and a shield of 6% max HP.",
                "Every 3 rounds: 70% ATK and a shield of 8% max HP.",
                "Every 3 rounds: 80% ATK, shield 10%. When this shield breaks, it bursts for 100% ATK.",
                "Every 2 rounds: 90% ATK, shield 12%, bursts for 125% ATK when broken.",
                "Every 2 rounds: 100% ATK, shield 15%; a 150% burst hits every enemy with your signatures' on-hit.",
            };
            for (int k = 0; k < 5; k++)
            {
                var pt = new List<SkillTrigger>
                {
                    K(TriggerKind.OnRoundStart, EffectKind.RemoveStatus, 0, Worm, k == 4 ? "largest;to_front" : "largest"),
                    K(TriggerKind.OnRoundStart, EffectKind.Shield, coolant[k], Worm),
                };
                if (pod[k] > 0) pt.Add(K(TriggerKind.OnFatal, EffectKind.CheatDeath, pod[k], Worm, "stasis", maxPerBattle: 1, rider: true));
                MountStep(d, k, P, "Coolant Loop", loopText[k], pt.ToArray());
                string wp = burst[k] > 0 ? "wall:" + burst[k] + (k == 4 ? ";wall_all" : "") : null;
                MountStep(d, k, A, "Ice Wall", wallText[k],
                    KRule("reload:" + reload[k]), K(V, EffectKind.Damage, wallHit[k], Front), K(V, EffectKind.Shield, wallShield[k], Worm, wp));
            }

            // ---- Rift Racer (new): Slipstream + Rift Dash ---------------------------------------------------------------------
            d = NewMount("mount_rift_racer", "Rift Racer", "Tempo racer: the worm opens every battle, dashes for extra attacks, and kills speed up every clock.");
            int[] slip = { 0, 30, 40, 50, 60 }, dash = { 60, 70, 80, 90, 70 }, dashes = { 1, 1, 1, 1, 2 };
            string[] slipText =
            {
                "Round 1: the worm acts before every enemy.",
                "Round 1: the worm acts first, and its first attack deals +30%.",
                "Acts first in round 1 (+40%). Each kill brings the Racer's next dash 1 round closer.",
                "Acts first in round 1 (+50%). Each kill brings the next dash 1 round closer.",
                "First in round 1 (+60%). Each kill advances every timer you own by 1 round.",
            };
            string[] dashText =
            {
                "Every 3 rounds: the worm makes an extra attack at 60% damage.",
                "Every 3 rounds: the worm makes an extra attack at 70% damage.",
                "Every 3 rounds: the worm makes an extra attack at 80%. It can combo.",
                "Every 2 rounds: the worm makes an extra attack at 90%. It can combo.",
                "Every 2 rounds: the worm makes two extra attacks at 70%. They can combo.",
            };
            for (int k = 0; k < 5; k++)
            {
                var pt = new List<SkillTrigger> { KRule("slipstream", slip[k]) };
                if (k >= 2) pt.Add(K(TriggerKind.OnEnemyDeath, EffectKind.AdvanceTimer, 0, Self(), k == 4 ? "all" : "mount", cooldown: 1));
                MountStep(d, k, P, "Slipstream", slipText[k], pt.ToArray());
                MountStep(d, k, A, "Rift Dash", dashText[k],
                    KRule("reload:" + reload[k]), K(V, EffectKind.RiderAttack, dash[k], Front, k >= 2 ? "combo" : null, stacks: dashes[k]));
            }

            // ---- Aegis Walker (new): Payback Coil + Payback Cannon -----------------------------------------------------------
            d = NewMount("mount_aegis_walker", "Aegis Walker", "Payback walker: banks the damage the worm takes, then fires it back.");
            int[] coil = { 20, 25, 30, 35, 40 }, coilCap = { 300, 300, 400, 450, 500 }, cannon = { 60, 80, 100, 100, 100 };
            string[] coilText =
            {
                "Stores 20% of the damage the worm takes, shielded hits included (max 300% ATK).",
                "Stores 25% of the damage the worm takes, shielded hits included (max 300% ATK).",
                "Stores 30% (max 400% ATK). Supernova recoil counts double; counterattacks store half their damage.",
                "Stores 35% (max 450% ATK). Recoil counts double; counterattacks store half their damage.",
                "Stores 40% (max 500% ATK). A full coil makes the worm counter every hit until its next turn.",
            };
            string[] cannonText =
            {
                "Every 3 rounds: hit the last attacker for 60% ATK plus all stored damage. Empties the coil.",
                "Every 3 rounds: last attacker, 80% ATK plus stored damage. Empties the coil.",
                "Every 3 rounds: 100% ATK + stored damage on the last attacker, with your signatures' on-hit twice.",
                "Every 2 rounds: 100% ATK + stored damage, signatures' on-hit twice.",
                "Every 2 rounds: 100% ATK on every enemy that hit the worm since the last shot, sharing the stored damage.",
            };
            for (int k = 0; k < 5; k++)
            {
                var pt = new List<SkillTrigger> { KRule("coil", coil[k]), KRule("coil_cap", coilCap[k]) };
                if (k >= 2) { pt.Add(KRule("coil_recoil:2")); pt.Add(KRule("coil_counter", 50)); }
                if (k == 4) pt.Add(KRule("coil_window"));
                MountStep(d, k, P, "Payback Coil", coilText[k], pt.ToArray());
                var tgt = k == 4 ? TargetKind.Attackers : TargetKind.LastAttacker;
                var at = new List<SkillTrigger> { KRule("reload:" + reload[k]), K(V, EffectKind.Damage, cannon[k], tgt, k == 4 ? "coil;coil_split" : "coil") };
                if (k >= 2) at.Add(K(V, EffectKind.CarrySignatures, 0, tgt, stacks: 2));
                MountStep(d, k, A, "Payback Cannon", cannonText[k], at.ToArray());
            }

            // ---- Hive Carrier: Wingman + Launch Bay ----------------------------------------------------------------------------
            d = NewMount("mount_hive_carrier", "Hive Carrier", "Drone deck: drones follow every extra hit and guard the worm.");
            int[] wing = { 30, 35, 40, 45, 50 }, wingCap = { 1, 1, 1, 2, 2 }, drone = { 20, 25, 30, 35, 40 }, droneMax = { 2, 2, 3, 3, 3 };
            string[] wingText =
            {
                "Once per turn, when the worm lands an extra hit (combo, counter, Echo), a drone strikes for 30% ATK.",
                "Once per turn, when the worm lands an extra hit, a drone strikes for 35% ATK.",
                "Once per turn, an extra hit brings a 40% ATK drone strike with your signatures' on-hit.",
                "Twice per turn: extra hits bring a 45% ATK drone strike with your signatures' on-hit.",
                "Twice per turn, 50% ATK with your on-hit; each strike gives every sidekick 10 charge.",
            };
            string[] bayText =
            {
                "Every 3 rounds (from round 1): launch a Drone sidekick that zaps for 20% ATK. Max 2.",
                "Every 3 rounds: launch a Drone (zaps for 25% ATK). Max 2.",
                "Every 3 rounds: launch a Drone (30% ATK), max 3. A Drone can guard one hit (−50%), then breaks.",
                "Every 2 rounds: launch a Drone (35% ATK), max 3. Drones guard one hit, then break.",
                "Every 2 rounds, 40% ATK, max 3, guard; Drones trigger your signatures when they act.",
            };
            for (int k = 0; k < 5; k++)
            {
                string cap = "turn_cap:" + wingCap[k];
                var pt = new List<SkillTrigger> { K(TriggerKind.OnExtraHit, EffectKind.Damage, wing[k], Front, cap, rider: true) };
                if (k >= 2) pt.Add(K(TriggerKind.OnExtraHit, EffectKind.CarrySignatures, 0, Front, cap, rider: true));
                if (k == 4) pt.Add(K(TriggerKind.OnExtraHit, EffectKind.ChargeAllies, 10, Self(), cap, rider: true));
                MountStep(d, k, P, "Wingman", wingText[k], pt.ToArray());
                string sp = "sk_drone;max:" + droneMax[k] + (k == 4 ? ";carry" : ";nosig") + (k >= 2 ? ";guard:50" : "");
                MountStep(d, k, A, "Launch Bay", bayText[k],
                    KRule("reload:" + reload[k]), K(V, EffectKind.Summon, drone[k], Self(), sp));
            }

            // ---- Chrono Skiff (new): Time Debt + Echo Shot ------------------------------------------------------------------
            d = NewMount("mount_chrono_skiff", "Chrono Skiff", "Time thief: steals one enemy turn at the start, then makes the worm repeat its last attack.");
            int[] debt = { 20, 25, 30, 35, 40 }, skipRage = { 0, 0, 15, 20, 25 }, echoPct = { 60, 70, 80, 90, 100 }, echoes = { 1, 1, 1, 1, 2 }, skiffReload = { 4, 4, 3, 3, 3 };
            string[] debtText =
            {
                "Battle start: Delay the fastest non-boss enemy once. Bosses lose 20 rage instead.",
                "Battle start: Delay the fastest non-boss enemy once. Bosses lose 25 rage instead.",
                "Battle start Delay (bosses −30 rage). Each enemy turn you skip gives the worm 15 rage.",
                "Battle start Delay (bosses −35 rage). Each skipped enemy turn gives the worm 20 rage.",
                "Bosses −40 rage, skips give 25 rage. Once per battle below 50% HP: Delay the attacker.",
            };
            string[] echoText =
            {
                "Every 4 rounds: the worm repeats its last attack at 60%.",
                "Every 4 rounds: the worm repeats its last attack at 70%.",
                "Every 3 rounds: the worm repeats its last attack at 80%. It counts as an Echo.",
                "Every 3 rounds: the worm repeats its last attack at 90%. It counts as an Echo.",
                "Every 3 rounds: the worm repeats its last attack twice, at 100%.",
            };
            for (int k = 0; k < 5; k++)
            {
                var pt = new List<SkillTrigger>
                {
                    K(TriggerKind.OnBattleStart, EffectKind.ApplyStatus, 0, TargetKind.FastestNonBoss, status: StatusId.Delay),
                    K(TriggerKind.OnBattleStart, EffectKind.GainRage, -debt[k], TargetKind.AllEnemies, "bosses;debt"),
                };
                if (skipRage[k] > 0) pt.Add(K(TriggerKind.OnSkipTurn, EffectKind.GainRage, skipRage[k], Worm));
                if (k == 4) pt.Add(K(TriggerKind.OnDamaged, EffectKind.ApplyStatus, 0, Front, "boss_rage:" + debt[k], StatusId.Delay, @if: "self_hp_below:50", maxPerBattle: 1, rider: true));
                MountStep(d, k, P, "Time Debt", debtText[k], pt.ToArray());
                MountStep(d, k, A, "Echo Shot", echoText[k],
                    KRule("reload:" + skiffReload[k]), KRule("needs_attack"),
                    K(V, EffectKind.RiderAttack, echoPct[k], Front, k >= 2 ? "echo;as_echo" : "echo", stacks: echoes[k]));
            }
        }

        private static TargetKind Self() => TargetKind.Self;

        private MountDef NewMount(string id, string name, string tagline)
        {
            var d = new MountDef { Id = id, Name = name, Tagline = tagline, Family = SkillFamily.None, SupportFamily = SkillFamily.None };
            MountConfig.Defs[id] = d;
            return d;
        }

        /// <summary>The passive or active at kit step k (0 = 1★ … 4 = 10★): <c>mountId_passive_k+1</c>, replacing the step before.</summary>
        private void MountStep(MountDef d, int k, string slot, string name, string text, params SkillTrigger[] triggers)
        {
            string kind = slot == "Passive" ? "passive" : "active";
            var s = KitSkill(d.Id + "_" + kind + "_" + (k + 1), name, SkillSource.Mount, text, triggers);
            int star = MountConfig.KitSteps[k];
            d.Weapons.Add(new SkillStep(star, 0, s.Id, k > 0 ? d.Id + "_" + kind + "_" + k : null));
            d.Kit.Add(new KitText(star, 0, slot, name, text));
            if (slot != "Passive")
                foreach (var t in triggers)
                    if (t.Effect == EffectKind.Rule && t.Param != null && t.Param.StartsWith("reload:", StringComparison.Ordinal))
                        d.Reloads.Add(int.Parse(t.Param.Substring(7), System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
