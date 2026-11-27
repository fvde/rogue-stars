using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Hero and mount kit probe (--kit-share): the worm of an Active F2P bot at day N (its real loadout: parts, squad; its own hero and
/// mount removed, the shared hero stat budget kept for everyone) with each of the 9 start signatures, alone and with each hero
/// (1★ / 5★ / 10★) or each mount (1★ / 5★ / A5), against a losable 3-enemy fight (two hitters and a boss, 20 rounds). Reports the win
/// rate, damage per round relative to the worm alone, the mount's share of the side's damage, and flags any hero × signature or
/// mount × signature cell above 115% of its row median (the page's "no dominant pick" check). Seeds averaged.
/// </summary>
public static class KitShare
{
    private static readonly string[] Sigs = { "sig_fire", "sig_radiation", "sig_bleed", "sig_ice", "sig_lightning", "sig_combo", "sig_counter", "sig_alien", "sig_time" };

    private sealed class Cell { public double Win, Rel, Share; }

    // the losable fight (calibrated so the worm alone wins about half its fights at day 60)
    private static readonly double HpHit = Env("KS_HPHIT", 14), HpBoss = Env("KS_HPBOSS", 35), AtkHit = Env("KS_ATKHIT", 12), AtkBoss = Env("KS_ATKBOSS", 7);
    private static double Env(string k, double d) => double.TryParse(Environment.GetEnvironmentVariable(k), out var v) ? v : d;

    public static string Markdown(int day, int seeds)
    {
        var bot = new SimPlayer(TimeProfile.Active, SpendProfile.F2P, 1);
        for (int d = 0; d < day; d++) bot.PlayDay(d);
        var c = bot.C;
        var lo = bot.E.BuildLoadout(bot.S, bot.Now);
        // strip the bot's own hero and mount (and their stats): every probe adds its own; the shared hero budget at 5★ Lv 30 for all
        var heroBudget = HeroSystem.HeroStats(c, "hero_star_knight", new HeroEntry { Stars = 5, Level = 30 });
        var baseLo = new CombatLoadout { DisplayName = "Worm" };
        baseLo.WormStats.AddBlock(lo.WormStats);
        if (!string.IsNullOrEmpty(lo.HeroId) && HeroSystem.Get(bot.S, lo.HeroId) is HeroEntry own) baseLo.WormStats.AddBlock(Neg(HeroSystem.HeroStats(c, lo.HeroId, own)));
        baseLo.WormStats.AddBlock(heroBudget);
        baseLo.Aliens.AddRange(lo.Aliens);
        baseLo.UnlockedFamilies.AddRange(lo.UnlockedFamilies);
        foreach (var id in lo.MetaSkillIds) { var s = c.Skill(id); if (s != null && s.Source != SkillSource.Hero && s.Source != SkillSource.Mount) baseLo.MetaSkillIds.Add(id); }
        double atk = baseLo.WormStats.Value(StatId.Atk), hp = baseLo.WormStats.Value(StatId.Hp);

        var sb = new StringBuilder();
        sb.AppendLine($"Hero and mount kit probe at day {day} (Active F2P worm, own hero / mount removed, shared hero stats 5★ Lv 30 for all: ATK {atk:0}, HP {hp:0}; "
            + $"{baseLo.Aliens.Count} squad aliens; 2 hitters + a boss, 20 rounds, {seeds} seeds per cell). Win % / damage per round relative to the worm alone (1.00) "
            + "with the same signature; ⚑ = above 115% of the row median.");
        sb.AppendLine();
        var baseCells = Sigs.ToDictionary(sig => sig, sig => Run(c, baseLo, sig, null, null, 0, seeds, atk, hp, null));
        sb.AppendLine("| alone | " + string.Join(" | ", Sigs.Select(s => s.Substring(4) + " " + baseCells[s].Win.ToString("0") + "% (" + baseCells[s].Rel.ToString("0") + "/round)")) + " |");
        sb.AppendLine();

        foreach (int star in new[] { 1, 5, 10 })
        {
            sb.AppendLine($"Heroes at {star}★ (win % · dmg rel.):");
            sb.AppendLine();
            sb.AppendLine("| Hero | " + string.Join(" | ", Sigs.Select(s => s.Substring(4))) + " | avg win | avg rel |");
            sb.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", Sigs.Length + 2)));
            foreach (var h in c.HeroConfig.Defs.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var row = Sigs.Select(sig => Run(c, baseLo, sig, HeroSystem.TraitSkills(c, h, star), null, 0, seeds, atk, hp, baseCells[sig])).ToList();
                sb.AppendLine(Row(c.HeroConfig.Defs[h].Name.Replace(" Worm", ""), row));
            }
            sb.AppendLine();
        }
        foreach (var (stars, label) in new[] { (1, "1★"), (5, "5★"), (10, "10★") })
        {
            sb.AppendLine($"Mounts at {label} (win % · dmg rel. · mount share of side damage):");
            sb.AppendLine();
            sb.AppendLine("| Mount | " + string.Join(" | ", Sigs.Select(s => s.Substring(4))) + " | avg win | avg rel | avg share |");
            sb.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", Sigs.Length + 3)));
            foreach (var m in c.MountConfig.Defs.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                double mp = MountSystem.MountStats(c, m, new MountEntry { Stars = stars, Level = 1 }).Value(StatId.MountPower);
                var row = Sigs.Select(sig => Run(c, baseLo, sig, MountSystem.WeaponSkills(c, m, stars), m, mp, seeds, atk, hp, baseCells[sig])).ToList();
                sb.AppendLine(Row(c.MountConfig.Defs[m].Name, row, true));
            }
            sb.AppendLine();
        }
        // the page's reference pair with the starter signatures (Star Knight × Scout Saucer with Fire / Radiation / Bleed; no hero is given at the start)
        sb.AppendLine("Reference pair (Star Knight 1★ + Scout Saucer 1★) vs alone, win % · dmg rel.:");
        sb.AppendLine();
        foreach (var sig in new[] { "sig_fire", "sig_radiation", "sig_bleed" })
        {
            var kit = HeroSystem.TraitSkills(c, "hero_star_knight", 1).Concat(MountSystem.WeaponSkills(c, "mount_scout_saucer", 1)).ToList();
            var cell = Run(c, baseLo, sig, kit, "mount_scout_saucer", 0, seeds, atk, hp, baseCells[sig]);
            sb.AppendLine($"- {sig.Substring(4)}: alone {baseCells[sig].Win:0}% → {cell.Win:0}% · {cell.Rel:0.00} (mount {cell.Share:0.0}% of side damage)");
        }
        return sb.ToString();
    }

    private static StatBlock Neg(StatBlock b)
    {
        var n = new StatBlock();
        foreach (var kv in b.Flat) n.Add(new StatMod(kv.Key, -kv.Value));
        foreach (var kv in b.Percent) n.Add(new StatMod(kv.Key, 0, -kv.Value));
        return n;
    }

    private static string Row(string name, List<Cell> row, bool share = false)
    {
        var rels = row.Select(x => x.Rel).OrderBy(x => x).ToList();
        double med = rels[rels.Count / 2];
        var cells = row.Select(x => $"{x.Win:0}% · {x.Rel:0.00}{(x.Rel > 1.15 * med ? " ⚑" : "")}");
        return $"| {name} | {string.Join(" | ", cells)} | {row.Average(x => x.Win):0}% | {row.Average(x => x.Rel):0.00} |" + (share ? $" {row.Average(x => x.Share):0.0}% |" : "");
    }

    private static Cell Run(ContentDb c, CombatLoadout baseLo, string sig, List<string> kit, string mount, double mountPower, int seeds, double atk, double hp, Cell basis)
    {
        double wins = 0, dpr = 0, share = 0;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var l = new CombatLoadout { DisplayName = "Worm", MountId = mount };
            l.WormStats.AddBlock(baseLo.WormStats);
            l.Aliens.AddRange(baseLo.Aliens);
            l.MetaSkillIds.AddRange(baseLo.MetaSkillIds);
            if (kit != null) l.MetaSkillIds.AddRange(kit);
            if (mountPower > 0) l.MountStats.Add(new StatMod(StatId.MountPower, mountPower));
            var setup = new BattleSetup { RoundCap = 20 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(l, c, new List<string> { sig }));
            for (int i = 0; i < 3; i++)
            {
                var e = new CombatantSpec { Id = "foe" + i, Name = "Foe " + i, Role = i == 2 ? CombatRole.Boss : CombatRole.Enemy, Targetable = true };
                e.Stats.Add(new StatMod(StatId.Hp, atk * (i == 2 ? HpBoss : HpHit))).Add(new StatMod(StatId.Atk, hp / (i == 2 ? AtkBoss : AtkHit)))
                    .Add(new StatMod(StatId.Def, atk * 0.15)).Add(new StatMod(StatId.Speed, 50 + 20 * i));
                setup.Enemy.Add(e);
            }
            var r = BattleSimulator.Simulate(setup, c, new Rng((ulong)(9000 + seed)));
            if (r.PlayerWon()) wins++;
            dpr += r.PlayerDamageDealt / (double)Math.Max(1, r.Rounds);
            double side = r.Fighters.Where(f => f.Side == BattleSide.Player).Sum(f => (double)f.DamageDealt);
            share += side > 0 ? r.Fighters.Where(f => f.Role == CombatRole.Mount).Sum(f => (double)f.DamageDealt) / side : 0;
        }
        var cell = new Cell { Win = 100.0 * wins / seeds, Share = 100.0 * share / seeds };
        cell.Rel = basis == null ? 1 : (dpr / seeds) / Math.Max(1, basis.Rel);
        if (basis == null) cell.Rel = dpr / seeds; // the baseline keeps its raw damage per round for the ratio
        return cell;
    }
}
