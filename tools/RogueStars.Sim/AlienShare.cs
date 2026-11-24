using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Alien contribution probe (--alien-share, aliens pass): the worm of an Active F2P bot at day N (its real loadout, squad removed)
/// with its starting signature set to each alien's synergy, alone and with one alien at 1★ and at 10★, against three sturdy
/// synthetic enemies for 20 rounds. Reports the alien's damage contribution = extra player-side damage per round in % of the worm's
/// damage without it (hits, statuses, buffs, echoes all count), and its direct share (damage credited to it). Seeds averaged.
/// </summary>
public static class AlienShare
{
    public static string Markdown(int day, int seeds)
    {
        var bot = new SimPlayer(TimeProfile.Active, SpendProfile.F2P, 1);
        for (int d = 0; d < day; d++) bot.PlayDay(d);
        var c = bot.C;
        var lo = bot.E.BuildLoadout(bot.S, bot.Now);
        lo.Aliens.Clear();
        double atk = lo.WormStats.Value(StatId.Atk), hp = lo.WormStats.Value(StatId.Hp);
        var sb = new StringBuilder();
        sb.AppendLine($"Alien contribution at day {day} (Active F2P worm: ATK {atk:0}, HP {hp:0}; 3 enemies, 20 rounds, {seeds} seeds; contribution = extra player damage per round vs the worm alone):");
        sb.AppendLine();
        sb.AppendLine("| Alien | rarity | signature | 1★ contribution | 1★ direct | 10★ contribution | 10★ direct |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var def in c.AlienTypes.Values.OrderBy(a => (int)a.Rarity).ThenBy(a => a.Id, StringComparer.Ordinal))
        {
            string sig = Signature(def.Family);
            double baseDpr = 0;
            var res = new List<(double contrib, double direct)>();
            foreach (int star in new[] { 1, 10 })
            {
                double with = 0, direct = 0, without = 0;
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var b0 = Fight(c, lo, null, sig, atk, hp, seed);
                    without += b0.PlayerDamageDealt / (double)Math.Max(1, b0.Rounds);
                    // before the aliens pass an alien also had levels: 1★ at level 20, 10★ at level 100 (ignored after it)
                    var owned = new OwnedAlien { TypeId = def.Id, Stars = star, Level = star == 1 ? 20 : 100 };
                    var ca = new CombatAlien { AlienTypeId = def.Id, Name = def.Name, Stats = AlienSystem.CombatStats(c, owned) };
                    ca.SkillIds.AddRange(AlienSystem.SkillIds(c, owned));
                    var b1 = Fight(c, lo, ca, sig, atk, hp, seed);
                    with += b1.PlayerDamageDealt / (double)Math.Max(1, b1.Rounds);
                    direct += b1.Fighters.Where(f => f.Role == CombatRole.Alien || f.Role == CombatRole.Summon).Sum(f => (double)f.DamageDealt) / Math.Max(1, b1.Rounds);
                }
                baseDpr = without;
                res.Add((100.0 * (with - without) / Math.Max(1, without), 100.0 * direct / Math.Max(1, without)));
            }
            sb.AppendLine($"| {def.Name} | {def.Rarity} | {sig} | {res[0].contrib:0.0}% | {res[0].direct:0.0}% | {res[1].contrib:0.0}% | {res[1].direct:0.0}% |");
        }
        return sb.ToString();
    }

    private static string Signature(SkillFamily f) => f switch
    {
        SkillFamily.Fire => "sig_fire", SkillFamily.Poison => "sig_radiation", SkillFamily.Bleed => "sig_bleed", SkillFamily.Ice => "sig_ice",
        SkillFamily.Lightning => "sig_lightning", SkillFamily.AlienSynergy => "sig_alien", SkillFamily.Counter => "sig_counter",
        SkillFamily.Combo => "sig_combo", SkillFamily.Time => "sig_time", _ => "sig_bleed",
    };

    private static BattleResult Fight(ContentDb c, CombatLoadout lo, CombatAlien alien, string sig, double atk, double hp, int seed)
    {
        var l = new CombatLoadout { DisplayName = lo.DisplayName, MountId = lo.MountId, HeroId = lo.HeroId };
        l.WormStats.AddBlock(lo.WormStats);
        l.MountStats.AddBlock(lo.MountStats);
        l.ArmStyles.AddRange(lo.ArmStyles);
        l.MetaSkillIds.AddRange(lo.MetaSkillIds);
        if (alien != null) l.Aliens.Add(alien);
        var setup = new BattleSetup { RoundCap = 20 };
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(l, c, new List<string> { sig }));
        for (int i = 0; i < 3; i++)
        {
            var e = new CombatantSpec { Id = "foe" + i, Name = "Foe " + i, Role = i == 2 ? CombatRole.Boss : CombatRole.Enemy, Targetable = true };
            e.Stats.Add(new StatMod(StatId.Hp, atk * 400)).Add(new StatMod(StatId.Atk, hp / 120)).Add(new StatMod(StatId.Def, atk * 0.15)).Add(new StatMod(StatId.Speed, 50));
            setup.Enemy.Add(e);
        }
        return BattleSimulator.Simulate(setup, c, new Rng((ulong)(7000 + seed)));
    }
}
