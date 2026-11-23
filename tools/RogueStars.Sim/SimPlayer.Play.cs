using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

public sealed partial class SimPlayer
{
    // ---- runs ---------------------------------------------------------------------------------------------

    private static string Key(string levelId, RunMode mode) => mode + ":" + levelId;

    /// <summary>Global main level n (1-based) of the endless campaign.</summary>
    private LevelDef MainLevel(int n) => C.MainLevel(n);

    private int MainMax => (int)S.Counter(Requirements.MainLevelCounter);

    /// <summary>First main level not yet Overcharged-cleared (cached scan start; Overcharged clears go in order).</summary>
    private int _ocFrontier = 1;

    /// <summary>Frontier attempts: after a loss, retry once power grew a little, or the next day (two tries per day at most).</summary>
    private bool ShouldTry(string key)
    {
        if (!_failPower.TryGetValue(key, out var fp)) return true;
        if (_power >= fp * 1.01) return true;
        if (_failDay.GetValueOrDefault(key) < DayIndex) return true;
        return _failCount.GetValueOrDefault(key) < 2;
    }

    private readonly Dictionary<string, int> _failDay = new();

    private int _staminaDay = -1;
    private long _staminaToday, _darkStaminaToday;

    private double WinEstimate(string key) => _winEma.TryGetValue(key, out var p) ? p : 0.85;

    private double FarmValue(LevelDef l, RunMode mode)
    {
        var portal = C.Portal(l.PortalId);
        double mult = l.RewardMultiplier * (mode == RunMode.Overcharged ? portal.OverchargedRewardMultiplier : 1);
        double v = 0;
        foreach (var r in l.CompletionRewards) if (r.Kind == RewardKinds.Currency && r.Id != Currencies.Diamonds) v += r.Amount;
        v *= 1 + 0.35 * l.CompletionLootRolls; // loot rolls scale with the portal's loot table
        return v * mult * WinEstimate(Key(l.Id, mode)) / RunSystem.EntryCost(C, l.Id, mode).Amount;
    }

    /// <summary>
    /// Next run: Dark Side Data farming while the laboratory waits for Data (capped share of the day's stamina), then the
    /// frontier (normal, then Dark Side, then Overcharged) when promising, else the best farm level.
    /// </summary>
    private (string level, RunMode mode) ChooseRun(HashSet<string> failedThisSession)
    {
        // Data for the laboratory first: farm the best cleared Dark Side level while research waits for Data, with at most
        // DarkFarmShare of the day's run stamina (a player splits stamina between frontier pushes and lab Data).
        if (LabDataShort && Unlocked(Features.DarkSide) && _darkStaminaToday + 12 <= Time.DarkFarmShare * (_staminaToday + 12))
        {
            // Best cleared Dark Side level (the endless sequence is cleared in order; look back a few levels from the best).
            string bestDark = null;
            int darkMax = (int)S.Counter(RunSystem.DarkSideMaxCounter);
            for (int i = Math.Max(1, darkMax - 12); i <= darkMax; i++)
            {
                string id = C.DarkSideLevel(i).Id;
                if (!RunSystem.Progress(S, C, id).Cleared) break;
                if (WinEstimate(Key(id, RunMode.DarkSide)) >= 0.7 && !failedThisSession.Contains(Key(id, RunMode.DarkSide))) bestDark = id;
            }
            if (bestDark != null) return (bestDark, RunMode.DarkSide);
        }
        int max = MainMax;
        {
            var next = MainLevel(max + 1); // the campaign never ends
            string k = Key(next.Id, RunMode.Normal);
            if (!failedThisSession.Contains(k) && ShouldTry(k) && RunSystem.IsLevelUnlocked(S, C, next.Id, RunMode.Normal)) return (next.Id, RunMode.Normal);
        }
        if (Unlocked(Features.DarkSide))
        {
            string id = C.DarkSideLevel((int)S.Counter(RunSystem.DarkSideMaxCounter) + 1).Id; // Dark Side frontier (endless)
            string k = Key(id, RunMode.DarkSide);
            if (!failedThisSession.Contains(k) && ShouldTry(k) && RunSystem.IsLevelUnlocked(S, C, id, RunMode.DarkSide)) return (id, RunMode.DarkSide);
        }
        bool oc = Unlocked(Features.Overcharged);
        if (oc)
            for (int n = Math.Max(1, _ocFrontier); n <= max; n++)
            {
                var l = MainLevel(n);
                if (!RunSystem.Progress(S, C, l.Id).Cleared) break;
                if (RunSystem.Progress(S, C, l.Id).OverchargedCleared) { _ocFrontier = n + 1; continue; }
                string k = Key(l.Id, RunMode.Overcharged);
                if (!failedThisSession.Contains(k) && ShouldTry(k) && RunSystem.IsLevelUnlocked(S, C, l.Id, RunMode.Overcharged)) return (l.Id, RunMode.Overcharged);
                break;
            }
        // Farm: the best expected value per stamina among cleared levels (recent win-rate weighted).
        (string, RunMode) best = (MainLevel(1).Id, RunMode.Normal);
        double bestV = -1;
        for (int i = Math.Max(0, max - 12); i < max; i++)
        {
            var l = MainLevel(i + 1);
            double v = FarmValue(l, RunMode.Normal);
            if (WinEstimate(Key(l.Id, RunMode.Normal)) >= 0.7 && v > bestV) { bestV = v; best = (l.Id, RunMode.Normal); }
            if (oc && RunSystem.Progress(S, C, l.Id).OverchargedCleared)
            {
                double vo = FarmValue(l, RunMode.Overcharged);
                if (WinEstimate(Key(l.Id, RunMode.Overcharged)) >= 0.7 && vo > bestV) { bestV = vo; best = (l.Id, RunMode.Overcharged); }
            }
        }
        return best;
    }

    private bool EnsureStamina(long cost)
    {
        if (Bal(Currencies.Stamina) >= cost) return true;
        if (_minutesLeft < 8) return false;
        var info = EconomySystem.GetStaminaRefillInfo(S, C, Now);
        if (info.FreeRefillsLeft > 0 && Ok(new BuyStamina { Method = StaminaRefillMethod.DailyFree })) return Bal(Currencies.Stamina) >= cost;
        if (WillWatchAd(C.EconomyConfig.StaminaRefill.AdPlacementId) && Ok(new BuyStamina { Method = StaminaRefillMethod.Ad, AdToken = AdToken }))
        {
            UseTime(AdCost);
            return Bal(Currencies.Stamina) >= cost;
        }
        int boughtToday = C.EconomyConfig.StaminaRefill.DiamondPrices.Count - info.DiamondRefillsLeft;
        if (boughtToday < Spend.DiamondRefillsPerDay && info.NextDiamondPrice > 0 && Spendable(info.NextDiamondPrice)
            && Ok(new BuyStamina { Method = StaminaRefillMethod.Diamonds }))
            return Bal(Currencies.Stamina) >= cost;
        return false;
    }

    private void Runs()
    {
        if (_staminaDay != DayIndex) { _staminaDay = DayIndex; _staminaToday = 0; _darkStaminaToday = 0; }
        var failed = new HashSet<string>();
        int guard = 0;
        while (_minutesLeft >= 2.5 && guard++ < 60)
        {
            var (levelId, mode) = ChooseRun(failed);
            long cost = RunSystem.EntryCost(C, levelId, mode).Amount;
            if (!EnsureStamina(cost))
            {
                // Not enough for the frontier/Dark Side: fall back to the cheapest farm level if possible.
                if (mode == RunMode.Normal) break;
                failed.Add(Key(levelId, mode));
                continue;
            }
            var sum = PlayRun(levelId, mode, out double minutes);
            if (sum == null) { failed.Add(Key(levelId, mode)); continue; }
            _staminaToday += cost;
            if (mode == RunMode.DarkSide) _darkStaminaToday += cost;
            UseTime(minutes);
            MinutesInRuns += minutes;
            RunsPlayed++;
            string k = Key(levelId, mode);
            _winEma[k] = WinEstimate(k) * 0.7 + (sum.Won ? 0.3 : 0);
            if (sum.Won)
            {
                RunsWon++;
                _failPower.Remove(k); _failCount.Remove(k);
                if (WillWatchAd(RunSystem.DoubleRewardsAdPlacement) && Ok(new RunDoubleRewards { AdToken = AdToken })) UseTime(AdCost);
            }
            else
            {
                failed.Add(k);
                if (_failPower.TryGetValue(k, out var fp) && _power < fp * 1.01 && _failDay.GetValueOrDefault(k) == DayIndex) _failCount[k] = _failCount.GetValueOrDefault(k) + 1;
                else { _failPower[k] = _power; _failCount[k] = 1; _failDay[k] = DayIndex; }
            }
            if (RunsPlayed % 3 == 0) { UpgradeAll(); RefreshPower(); }
        }
    }

    /// <summary>Plays a run with RunAutoStep (auto mode), with the bot's revive and Burrow choices. Returns null if it could not start.</summary>
    private RunSummary PlayRun(string levelId, RunMode mode, out double minutes)
    {
        minutes = 0;
        var r = Exec(new RunStart { LevelId = levelId, Mode = mode });
        if (!r.Ok) return null;
        int rolls = 0, decisions = 0; double rounds = 0;
        void Tally(CommandResult res)
        {
            foreach (var ev in res.Events)
                switch (ev.Type)
                {
                    case "run_roll": rolls++; break;
                    case "run_draft": case "run_event": case "run_shop": decisions++; break;
                    case "run_battle": rounds += double.Parse(ev.Args[2]); break;
                }
        }
        Tally(r);
        for (int i = 0; i < 3000 && S.Run.Active != null; i++)
        {
            var a = S.Run.Active;
            CommandResult step;
            if (a.Phase == RunPhase.BurrowChoice)
            {
                var portal = C.Portal(a.PortalId);
                bool deeper = portal.LevelIds.IndexOf(a.LevelId) + 1 < portal.LevelIds.Count;
                bool healthy = a.WormMaxHp > 0 && a.WormHp / a.WormMaxHp >= 0.55;
                step = Exec(deeper && healthy ? new RunBurrowDescend() : new RunBurrowCashOut());
            }
            else if (a.Phase == RunPhase.Defeated && !a.ReviveUsed && a.PendingLoot.Count > 0 && WillWatchAd(RunSystem.ReviveAdPlacement))
            {
                step = Exec(new RunRevive { Method = ReviveMethod.Ad, AdToken = AdToken });
                if (step.Ok) UseTime(AdCost);
            }
            else if (a.Phase == RunPhase.Defeated && !a.ReviveUsed && Spend.MonthlyBudgetCents > 0 && Spendable(C.RunTuning.ReviveDiamondCost))
                step = Exec(new RunRevive { Method = ReviveMethod.Diamonds });
            else step = Exec(new RunAutoStep { Farming = true, FarmShops = true });
            if (!step.Ok) step = Exec(new RunAutoStep { Farming = true, FarmShops = true });
            if (!step.Ok) { Exec(new RunAbandon()); break; }
            Tally(step);
        }
        double speed = ShopSystem.HasBattleSpeed3(S, C, Now) ? 3 : 2;
        minutes = (Timing.RunOverheadSec + rolls * Timing.RollSec + decisions * Timing.DecisionSec + rounds * Timing.BattleRoundSec / speed) / 60.0;
        return S.Run.LastSummary;
    }

    private void Burrow()
    {
        if (!Unlocked(Features.Burrow)) return;
        var portal = C.Portals.Values.FirstOrDefault(p => p.Kind == PortalKind.Burrow);
        if (portal == null) return;
        while (Bal(portal.EntryCurrency) >= portal.EntryCost && _minutesLeft >= 4)
        {
            var sum = PlayRun(portal.LevelIds[0], RunMode.Burrow, out double minutes);
            if (sum == null) break;
            UseTime(minutes);
            MinutesInRuns += minutes;
        }
    }

    // ---- arena (snapshot ladder) & invasions ----------------------------------------------------------------

    private long _arenaFightsAtDayStart = -1;
    private int _arenaDay = -1;

    /// <summary>
    /// Arena opponents are a snapshot ladder: the player's own current loadout, scaled by a league factor (higher
    /// leagues field stronger players) and noise. The fight itself is BattleSimulator.PlayerVsSnapshot like on the server.
    /// </summary>
    private void Arena(int fightsWanted)
    {
        if (!Unlocked(Features.Arena)) return;
        ArenaWeekEnd();
        if (_arenaDay != DayIndex) { _arenaDay = DayIndex; _arenaFightsAtDayStart = S.Counter("arena_fights"); }
        while (S.Counter("arena_fights") - _arenaFightsAtDayStart < fightsWanted && Bal(Currencies.ArenaTickets) >= C.Arena.TicketsPerFight
               && _minutesLeft >= Timing.ArenaFight)
        {
            if (!Ok(new ArenaStartFight { OpponentId = "ladder" })) return;
            var me = E.BuildLoadout(S, Now);
            var opp = E.BuildLoadout(S, Now);
            double f = 0.86 + 0.055 * S.Arena.League + (_bot.NextDouble() - 0.5) * 0.3;
            double pct = (f - 1) * 100;
            foreach (var st in new[] { StatId.Hp, StatId.Atk, StatId.Def })
            {
                opp.WormStats.Add(new StatMod(st, 0, pct));
                opp.MountStats.Add(new StatMod(st, 0, pct));
                foreach (var a in opp.Aliens) a.Stats.Add(new StatMod(st, 0, pct));
            }
            var result = BattleSimulator.PlayerVsSnapshot(C, me, opp, Rng.FromSeed(Seed * 31 + S.Counter("arena_fights")));
            long oppPoints = S.Arena.Points + (long)((_bot.NextDouble() - 0.5) * 120);
            Exec(new ArenaFightResult { Won = result.PlayerWon(), OpponentPoints = Math.Max(0, oppPoints) });
            UseTime(Timing.ArenaFight);
        }
    }

    /// <summary>Weekly league close (server job): rank from points against a 150-player group N(StartPoints, 120).</summary>
    private void ArenaWeekEnd()
    {
        var a = S.Arena;
        long week = GameClock.WeekIndex(C, Now);
        if (a.Week < 0 || a.Week == week || a.WeekWins + a.WeekLosses == 0) return;
        double z = (a.Points - C.Arena.StartPoints) / 120.0;
        double above = 1 - Phi(z);
        int rank = Math.Clamp(1 + (int)Math.Floor(149 * above), 1, 150);
        Exec(new ArenaWeekEnd { Rank = rank, GroupSize = 150 });
    }

    private static double Phi(double z) => 0.5 * (1 + Erf(z / Math.Sqrt(2)));

    private static double Erf(double x)
    {
        double t = 1 / (1 + 0.3275911 * Math.Abs(x));
        double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return x >= 0 ? y : -y;
    }

    private readonly Dictionary<string, (string invasionId, long endMs, long score, int attempts)> _invasions = new();

    /// <summary>
    /// Invasion attempts during active invasions: InvasionStartAttempt, then the server's battle
    /// (BattleSimulator.PlayerVsBoss against the invader scaled to the player's frontier threat, HP pool) and
    /// InvasionRecordDamage. Outcomes are applied after the occurrence ends (see ResolveInvasionOutcomes).
    /// </summary>
    private void Invasions()
    {
        if (!Unlocked(Features.Invasions)) return;
        foreach (var ev in LiveOpsSystem.ActiveEvents(S, C, Now).Where(x => x.Kind == LiveOpsKind.Invasion))
        {
            while (Bal(Currencies.InvasionAttempts) >= 1 && _minutesLeft >= Timing.InvasionAttempt)
            {
                if (!Ok(new InvasionStartAttempt { OccurrenceId = ev.OccurrenceId })) break;
                var def = C.Invasion(ev.RefId);
                int frontier = Math.Max(0, MainMax - 1);
                double scale = C.ThreatMultiplier(MainLevel(frontier + 1).Threat) * 2.5;
                var boss = BattleSimulator.BuildEnemy(C, def.BossId, scale);
                boss.StartHp = 1e15;
                var res = BattleSimulator.PlayerVsBoss(C, E.BuildLoadout(S, Now), boss, Rng.FromSeed(Seed * 131 + S.Counter("invasion_battles")));
                var sc = Exec(new InvasionRecordDamage { OccurrenceId = ev.OccurrenceId, Damage = res.PlayerDamageDealt });
                long score = sc.Ok && sc.Payload is long l ? l : 0;
                _invasions.TryGetValue(ev.OccurrenceId, out var prev);
                _invasions[ev.OccurrenceId] = (ev.RefId, ev.EndMs, prev.score + score, prev.attempts + 1);
                UseTime(Timing.InvasionAttempt);
            }
        }
    }

    /// <summary>
    /// Group outcome (server): 19 other group members with attempts 0..6 and per-attempt scores log-normally spread
    /// around the player's own; repelled when the group used at least 40 attempts.
    /// </summary>
    private void ResolveInvasionOutcomes()
    {
        foreach (var kv in _invasions.Where(x => x.Value.endMs <= Now).OrderBy(x => x.Key, StringComparer.Ordinal).ToList())
        {
            var (inv, _, score, attempts) = kv.Value;
            double perAttempt = attempts > 0 ? (double)score / attempts : 0;
            int totalAttempts = attempts, rank = 1;
            for (int i = 0; i < 19; i++)
            {
                int at = _bot.Range(0, 7);
                double f = Math.Exp((_bot.NextDouble() + _bot.NextDouble() + _bot.NextDouble() - 1.5) * 0.9);
                totalAttempts += at;
                if (at * perAttempt * f > score) rank++;
            }
            Exec(new LiveOpsApplyOutcome { InvasionId = inv, OccurrenceId = kv.Key, Repelled = totalAttempts >= 40, ContributionRank = attempts > 0 ? rank : 0 });
            _invasions.Remove(kv.Key);
        }
    }
}
