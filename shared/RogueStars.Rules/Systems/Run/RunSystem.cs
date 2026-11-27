using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Dice-board runs (GDD §3). State: GameState.Run (ActiveRun while a run is in progress).
    /// Commands: RunStart, RunRoll, RunChooseBranch, RunChooseEventOption, RunDraftPick, RunDraftReroll,
    /// RunShopBuy/Reroll/Leave, RunRevive, RunAbandon, RunAutoStep, RunBurrowDescend, RunBurrowCashOut, RunQuestChoose.
    /// Battles are resolved inside the handlers with BattleSimulator; the last log is in ActiveRun.LastBattle.
    /// Rewards are pending and granted only on completion (ctx.Grant), failure loses everything.
    /// </summary>
    public static partial class RunSystem
    {
        public static void Register(GameEngine e)
        {
            e.Register<RunStart>(HandleStart);
            e.Register<RunPickSignature>((c, ctx) => { var r = Need(ctx); PickStartSignature(ctx, r, c.SignatureId); return Ok(ctx); });
            e.Register<RunRoll>((c, ctx) => { var r = Need(ctx); Roll(ctx, r); return Ok(ctx); });
            e.Register<RunChooseBranch>((c, ctx) => { var r = Need(ctx); ChooseBranch(ctx, r, c.Index); return Ok(ctx); });
            e.Register<RunChooseEventOption>((c, ctx) => { var r = Need(ctx); ChooseEventOption(ctx, r, c.Index, c.SkillId); return Ok(ctx); });
            e.Register<RunDraftPick>((c, ctx) => { var r = Need(ctx); DraftPick(ctx, r, c.SkillId); return Ok(ctx); });
            e.Register<RunDraftReroll>((c, ctx) => { var r = Need(ctx); DraftReroll(ctx, r); return Ok(ctx); });
            e.Register<RunShopBuy>((c, ctx) => { var r = Need(ctx); ShopBuy(ctx, r, c.Index); return Ok(ctx); });
            e.Register<RunShopReroll>((c, ctx) => { var r = Need(ctx); ShopReroll(ctx, r); return Ok(ctx); });
            e.Register<RunShopLeave>((c, ctx) => { var r = Need(ctx); ShopLeave(ctx, r); return Ok(ctx); });
            e.Register<RunEncounter>((c, ctx) => { var r = Need(ctx); Encounter(ctx, r); return Ok(ctx); });
            e.Register<RunRevive>((c, ctx) => { var r = Need(ctx); Revive(ctx, r, c.Method, c.AdToken); return Ok(ctx); });
            e.Register<RunAbandon>((c, ctx) => { var r = Need(ctx); Finish(ctx, r, false); return Ok(ctx); });
            e.Register<RunAutoStep>((c, ctx) => { var r = Need(ctx); AutoStep(ctx, r, c); return Ok(ctx); });
            e.Register<RunBurrowDescend>((c, ctx) => { var r = Need(ctx); BurrowDescend(ctx, r); return Ok(ctx); });
            e.Register<RunDoubleRewards>(HandleDoubleRewards);
            RegisterMinigames(e);
            RegisterQuests(e);
            e.Register<RunBurrowCashOut>((c, ctx) => { var r = Need(ctx); Require(r.Phase == RunPhase.BurrowChoice, "not at a burrow cash-out"); Finish(ctx, r, true); return Ok(ctx); });

            e.RegisterNewPlayer(ctx =>
            {
                ctx.State.Run.SignatureSeed = ctx.Rng.Fork("signature").NextULong(); // a fork: the player stream is not consumed
                foreach (var cur in new[] { Currencies.BurrowKeys }) // stamina: Progression's starting wallet
                {
                    var rule = ctx.Content.Regen(cur);
                    if (rule == null) continue;
                    long have = ctx.State.Wallet.Get(cur);
                    if (have < rule.BaseCap) ctx.Grant(Reward.Cur(cur, rule.BaseCap - have)); // stashed until the Burrow unlocks
                    ctx.State.Wallet.SettleRegen(rule, ctx.NowMs);
                }
            });
        }

        // ---- public queries (client/server) ----------------------------------------------------

        /// <summary>Entry cost of a level in a mode (Stamina; Burrow Keys for the Burrow; Overcharged ×2).</summary>
        public static Cost EntryCost(ContentDb content, string levelId, RunMode mode)
        {
            var level = content.RunLevel(levelId);
            var portal = level != null ? content.Portal(level.PortalId) : null;
            if (level == null || portal == null) return new Cost(Currencies.Stamina, 0);
            if (mode == RunMode.Burrow) return new Cost(portal.EntryCurrency, portal.EntryCost);
            double mult = mode == RunMode.Overcharged ? portal.OverchargedStaminaMultiplier : 1;
            return new Cost(portal.EntryCurrency, (long)Math.Round(level.StaminaCost * mult));
        }

        /// <summary>Whether the level can be started in this mode (progression only; feature gates via Features).</summary>
        public static bool IsLevelUnlocked(GameState state, ContentDb content, string levelId, RunMode mode)
        {
            var level = content.RunLevel(levelId);
            var portal = level != null ? content.Portal(level.PortalId) : null;
            if (level == null || portal == null) return false;
            switch (mode)
            {
                case RunMode.Burrow:
                    return portal.Kind == PortalKind.Burrow && level.Index == 1 && Features.IsUnlocked(state, content, Features.Burrow);
                case RunMode.DarkSide:
                    if (portal.Kind != PortalKind.DarkSide || !Features.IsUnlocked(state, content, Features.DarkSide)) return false;
                    return level.Index == 1 || Progress(state, content, PrevLevelId(content, portal, level)).Cleared;
                case RunMode.Overcharged:
                    if (portal.Kind != PortalKind.Main || !Features.IsUnlocked(state, content, Features.Overcharged)) return false;
                    if (!Progress(state, content, level.Id).Cleared) return false;
                    return level.Index == 1 || Progress(state, content, PrevLevelId(content, portal, level)).OverchargedCleared;
                default:
                    if (portal.Kind != PortalKind.Main) return false;
                    // The previous level in the endless sequence (the last level of the previous portal for a level 1).
                    return level.Number <= 1 || Progress(state, content, content.MainLevelId(level.Number - 1)).Cleared;
            }
        }

        /// <summary>
        /// Global number of a main-portal level (portals by Order, 1-based: portal 1 = 1..10, portal 2 = 11..20, endless);
        /// 0 for other portals. Run keeps the highest cleared one in the counter main_level_max (feature gates).
        /// </summary>
        public static int MainLevelNumber(ContentDb content, string levelId)
        {
            var level = content.RunLevel(levelId);
            if (level == null || level.PortalId == ContentDb.DarkSidePortalId) return 0;
            return level.Number;
        }

        /// <summary>The next main level to clear (main_level_max + 1): the campaign never ends.</summary>
        public static LevelDef NextMainLevel(GameState state, ContentDb content) =>
            content.MainLevel((int)state.Counter(Requirements.MainLevelCounter) + 1);

        /// <summary>Level progress (feats, clears); returns an empty record if never played. Uses ContentDb.Default for archived levels.</summary>
        public static LevelProgress Progress(GameState state, string levelId) => Progress(state, ContentDb.Default, levelId);

        /// <summary>Level progress (feats, clears), also for levels folded into the compact archives; an empty record if never played.</summary>
        public static LevelProgress Progress(GameState state, ContentDb content, string levelId)
        {
            if (levelId == null) return new LevelProgress();
            if (state.Run.Levels.TryGetValue(levelId, out var p)) return p;
            var archived = Archived(state, content, levelId);
            return archived ?? new LevelProgress();
        }

        /// <summary>Worm combat spec for the current run (loadout + run skills + run stats).</summary>
        public static CombatantSpec WormSpec(ContentDb content, ActiveRun run)
        {
            var side = BattleSimulator.BuildPlayerSide(run.Loadout, content, run.Skills, WormExtraStats(content, run), run.SkillLevels);
            return side[side.Count - 1];
        }

        /// <summary>Skills active for the worm in this run (meta skills + drafted run skills).</summary>
        public static List<string> ActiveSkillIds(ActiveRun run)
        {
            var list = new List<string>(run.Loadout.MetaSkillIds);
            list.AddRange(run.Skills);
            return list;
        }

        // ---- handlers -------------------------------------------------------------------------

        private static CommandResult Ok(CommandContext ctx) => CommandResult.Success(ctx.State.Run.Active != null ? ctx.State.Run.Active.LastBattle : null);

        private static void Require(bool cond, string error) => GameEngine.Require(cond, error);

        private static ActiveRun Need(CommandContext ctx)
        {
            var r = ctx.State.Run.Active;
            Require(r != null, "no active run");
            return r;
        }

        private static CommandResult HandleStart(RunStart cmd, CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content;
            Require(s.Run.Active == null, "a run is already in progress");
            var level = c.RunLevel(cmd.LevelId);
            Require(level != null, "unknown level");
            Require(string.IsNullOrEmpty(cmd.PortalId) || cmd.PortalId == level.PortalId, "level is not in that portal");
            var portal = c.Portal(level.PortalId);
            Require(portal != null, "unknown portal");
            if (cmd.Mode == RunMode.Overcharged) Require(Features.IsUnlocked(s, c, Features.Overcharged), "overcharged locked");
            if (cmd.Mode == RunMode.Burrow) Require(Features.IsUnlocked(s, c, Features.Burrow), "burrow locked");
            if (cmd.Mode == RunMode.DarkSide) Require(Features.IsUnlocked(s, c, Features.DarkSide), "dark side locked");
            Require(IsLevelUnlocked(s, c, level.Id, cmd.Mode), "level locked");

            var cost = EntryCost(c, level.Id, cmd.Mode);
            // Regenerating entry currencies were settled by EconomySystem's settler before this handler.
            Require(s.Wallet.CanAfford(cost.Currency, cost.Amount), "not enough " + cost.Currency);
            s.Wallet.TrySpend(cost.Currency, cost.Amount);
            if (cost.Currency == Currencies.Stamina) s.Count("stamina_spent", cost.Amount);

            var run = new ActiveRun
            {
                RunId = s.PlayerId + ":" + s.Run.RunsStarted,
                PortalId = portal.Id, LevelId = level.Id, Mode = cmd.Mode, StartedMs = ctx.NowMs,
                Rng = ctx.Rng.Fork("run:" + s.Run.RunsStarted + ":" + level.Id),
                Loadout = ctx.Engine.BuildLoadout(s, ctx.NowMs),
            };
            var signatures = SignatureOffer(s, c, run.Loadout); // before RunsStarted moves on: the offer the client showed
            s.Run.RunsStarted++;
            if (run.Loadout.WormStats.Value(StatId.Hp) <= 0) run.Loadout.WormStats.AddAll(c.RunTuning.FallbackWormStats);
            run.DraftRerolls = c.RunTuning.DraftRerollsPerRun + run.Loadout.DraftRerolls;
            ScheduleQuest(ctx, run, level); // before the board: a scheduled run gets the quest tile (RunBoard)
            SetupLevel(c, run, level);
            run.Coins = StartCoins(c, level) + run.Loadout.RunStartCoins; // enough for a purchase at the first shop (+ City boons)
            run.SkipNegativeEvents += run.Loadout.SkipNegativeEvents;
            run.AwakeningReductionPct += run.Loadout.AwakeningSlowPct;
            s.Run.Active = run;
            bool pickOnBoard = cmd.PickOnBoard && string.IsNullOrEmpty(cmd.SignatureId) && signatures.Count > 1;
            if (pickOnBoard) run.StartSignatures.AddRange(signatures); // picked on the board before the first roll (RunPickSignature)
            else GrantSignature(ctx, run, cmd.SignatureId, signatures);
            RefreshMaxHp(c, run);

            RunTriggers(ctx, run, TriggerKind.OnRunStart, null);
            RefreshMaxHp(c, run);

            s.Count("runs_started");
            s.Count("runs_started_" + cmd.Mode.ToString().ToLowerInvariant());
            ctx.Emit("run_started", run.RunId, level.Id, cmd.Mode.ToString());
            if (pickOnBoard) run.Phase = RunPhase.Signature; // the pick's Continue moves on
            else Continue(ctx, run);
            return Ok(ctx);
        }

        /// <summary>Builds the board for a level (also used per Burrow floor).</summary>
        private static void SetupLevel(ContentDb c, ActiveRun run, LevelDef level)
        {
            var portal = c.Portal(level.PortalId);
            run.LevelId = level.Id;
            run.Threat = level.Threat + (run.Mode == RunMode.Overcharged ? portal.OverchargedThreatBonus : 0);
            run.MinibossOrder = MinibossOrder(level, run.Rng); // a fork: the run's stream (and so its board) is untouched
            var board = RunBoard.Generate(c, level, run.Rng, run);
            run.Tiles = board.Tiles;
            run.BoardEnd = board.EndTile;
            run.BoardSegments = board.Segments;
            run.SetPieces = board.SetPieces;
            run.BossGateTile = -1;
            run.StepPenaltyNext = 0;
            run.EncounterKind = null;
            run.Position = 0;
            run.Trail.Clear();
            run.Trail.Add(0);
            run.StepsLeft = 0;
            run.ChosenNext = -1;
            run.BranchOptions.Clear();
            run.Rolls = 0;
            run.AccountLootOffers = 0;
            run.MaxRolls = level.MaxRolls;
            run.Dice = Math.Max(1, level.Dice);
            run.BossId = level.BossId;
            run.Awakening = 0;
            run.ActionsTriggered = 0;
            run.BossStatPct = 0;
            run.BossFought = false;
            run.BossRounds = 0;
            run.Phase = RunPhase.AwaitRoll;
        }

        private static void Roll(CommandContext ctx, ActiveRun run)
        {
            Require(run.Phase == RunPhase.AwaitRoll && run.StepsLeft == 0, "cannot roll now");
            var c = ctx.Content;
            int n = Math.Max(1, run.Dice + run.ExtraDicePermanent + run.ExtraDiceNext);
            run.ExtraDiceNext = 0;
            EnsureBoardAhead(c, run, 6 * n);
            run.LastDice.Clear();
            int sum = 0;
            for (int i = 0; i < n; i++) { int d = run.Rng.Range(1, 7); run.LastDice.Add(d); sum += d; }
            if (run.StepPenaltyNext > 0) { sum = Math.Max(1, sum - run.StepPenaltyNext); run.StepPenaltyNext = 0; }
            run.Rolls++;
            run.TileOutcomes.Clear(); // a new move: the previous move's tile outcomes were shown
            ctx.State.Count("run_rolls");
            ctx.Emit("run_roll", sum.ToString(), string.Join("+", run.LastDice));
            var boss = Boss(c, run);
            // Per-roll skills first (Calm Mind drains the meter), then the boss's pace: draining after the clamp at 100 kept a
            // full meter at 99 for ever, so with Calm Mind the boss only came at the roll limit.
            RunTriggers(ctx, run, TriggerKind.OnRoll, null);
            if (boss != null) AddAwakening(ctx, run, boss.AwakenPerRoll);
            LevelUpFromBank(ctx, run); // carried XP pays one level per roll (never several at once)
            run.StepsLeft = sum;
            Continue(ctx, run);
        }

        private static void ChooseBranch(CommandContext ctx, ActiveRun run, int index)
        {
            Require(run.Phase == RunPhase.ChooseBranch, "no branch to choose");
            Require(index >= 0 && index < run.BranchOptions.Count, "invalid branch");
            run.ChosenNext = run.BranchOptions[index];
            run.BranchOptions.Clear();
            run.Phase = RunPhase.AwaitRoll;
            Continue(ctx, run);
        }

        private static void DraftPick(CommandContext ctx, ActiveRun run, string skillId)
        {
            Require(run.Phase == RunPhase.Draft, "no draft open");
            Require(skillId != null && run.DraftOffer.Contains(skillId), "skill not offered");
            var s = ctx.Content.Skill(skillId);
            bool mythic = run.MythicDraft;
            if (s != null && s.Signature) AddSignature(ctx, run, skillId, "mythic"); // a miniboss offered a signature in a Mythic's place
            else GrantSkill(ctx, run, skillId, 0, mythic ? "mythic" : run.Minigame.DraftOpen ? "minigame" : "draft");
            run.Minigame.DraftOpen = false;
            if (mythic) run.PendingMythicDrafts = Math.Max(0, run.PendingMythicDrafts - 1);
            else
            {
                run.DraftsWithoutLegendary = s.Rarity >= Rarity.Legendary ? 0 : run.DraftsWithoutLegendary + 1;
                run.PendingDrafts = Math.Max(0, run.PendingDrafts - 1);
            }
            run.MythicDraft = false;
            run.DraftOffer.Clear();
            run.Phase = RunPhase.AwaitRoll;
            ctx.State.Count("run_skills_drafted");
            if (mythic) ctx.State.Count("run_mythic_picks");
            ctx.Emit("run_draft_pick", skillId);
            Continue(ctx, run);
        }

        private static void DraftReroll(CommandContext ctx, ActiveRun run)
        {
            Require(run.Phase == RunPhase.Draft, "no draft open");
            Require(run.DraftRerolls > 0, "no rerolls left");
            run.DraftRerolls--;
            var offer = run.MythicDraft ? RunDraft.GenerateMythic(ctx.Content, run, run.Rng) : RunDraft.Generate(ctx.Content, run, run.Rng);
            if (run.MythicDraft) MaybeOfferSignature(ctx, run, offer);
            if (offer.Count > 0) { run.DraftOffer.Clear(); run.DraftOffer.AddRange(offer); }
        }

        private static void ShopBuy(CommandContext ctx, ActiveRun run, int index)
        {
            Require(run.Phase == RunPhase.Shop, "no shop open");
            Require(index >= 0 && index < run.Shop.Count, "invalid offer");
            var o = run.Shop[index];
            Require(!o.Sold, "sold out");
            Require(run.Coins >= o.Price, "not enough run coins");
            if (o.Kind == ShopOfferKind.Skill) Require(RunDraft.IsEligible(ctx.Content, run, ctx.Content.Skill(o.SkillId)), "skill no longer available");
            run.Coins -= o.Price;
            o.Sold = true;
            run.ShopBuys++;
            switch (o.Kind)
            {
                case ShopOfferKind.Skill: GrantSkill(ctx, run, o.SkillId, 0, "shop"); break;
                case ShopOfferKind.Heal: HealWorm(ctx.Content, run, o.Value); break;
                case ShopOfferKind.Stat:
                    run.RunStats.Add(new StatMod(o.Stat.Stat, o.Stat.Flat, o.Stat.Percent));
                    run.Gains.Add(new RunGain { Source = "shop", Stat = new StatMod(o.Stat.Stat, o.Stat.Flat, o.Stat.Percent) });
                    RefreshMaxHp(ctx.Content, run);
                    break;
            }
            ctx.State.Count("run_shop_buys");
            ctx.Emit("run_shop_buy", index.ToString(), o.Kind.ToString(), o.Kind == ShopOfferKind.Skill ? o.SkillId : o.Kind == ShopOfferKind.Stat ? o.Stat.Stat.ToString() : "heal");
        }

        /// <summary>
        /// The offer auto-pick (farming) buys next, or −1 to leave: the heal below half HP, otherwise the affordable skill or
        /// stat perk the default draft logic wants most (a stat perk counts like a Rare skill; rarer skills, family synergy and
        /// chain upgrades rank higher, level-ups a little lower). Never rerolls.
        /// </summary>
        public static int FarmShopPick(ContentDb content, ActiveRun run)
        {
            if (run == null || run.Phase != RunPhase.Shop) return -1;
            for (int i = 0; i < run.Shop.Count; i++)
            {
                var o = run.Shop[i];
                if (o.Kind == ShopOfferKind.Heal && !o.Sold && run.Coins >= o.Price && run.WormHp < run.WormMaxHp * 0.5) return i;
            }
            int best = -1; double bestScore = double.MinValue;
            for (int i = 0; i < run.Shop.Count; i++)
            {
                var o = run.Shop[i];
                if (o.Sold || o.Kind == ShopOfferKind.Heal || run.Coins < o.Price) continue;
                double score;
                if (o.Kind == ShopOfferKind.Skill)
                {
                    var s = content.Skill(o.SkillId);
                    if (!RunDraft.IsEligible(content, run, s)) continue;
                    score = RunDraft.PickScore(content, run, s);
                }
                else score = (int)Rarity.Rare * 10 + 2;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private static void ShopReroll(CommandContext ctx, ActiveRun run)
        {
            Require(run.Phase == RunPhase.Shop, "no shop open");
            long price = ShopRerollCost(ctx.Content, run);
            Require(run.Coins >= price, "not enough run coins");
            run.Coins -= price;
            run.ShopRerolls++;
            FillShop(ctx.Content, run, true);
        }

        /// <summary>
        /// The next reroll's price in the open shop: the level's shop table base + step × rerolls in this visit. Every shop
        /// visit starts again at the base price (owner playtest: the raised price carried over to the next shop).
        /// </summary>
        public static long ShopRerollCost(ContentDb content, ActiveRun run)
        {
            if (run == null) return 0;
            var table = ShopTable(content, run);
            return table.RerollBase + table.RerollStep * run.ShopRerolls;
        }

        private static void ShopLeave(CommandContext ctx, ActiveRun run)
        {
            Require(run.Phase == RunPhase.Shop, "no shop open");
            run.Shop.Clear();
            run.Phase = RunPhase.AwaitRoll; // no result screen after a shop: the purchases were seen in the shop (playtest)
            Continue(ctx, run);
        }

        public const string ReviveAdPlacement = "revive";
        public const string DoubleRewardsAdPlacement = "double_run_rewards";

        /// <summary>
        /// Result screen ad: the last won run pays its doublable rewards (RunRewardLine.IsDoublable: everything the result screen
        /// lists, as granted: Biomass surge and run_reward modifiers included) once more. Once per run, daily
        /// cap via the ad placement. The summary reflects it: Granted totals grow (currencies doubled in place, a second copy of
        /// each item drop), Rewards lines are marked Doubled, DoubledGranted lists what this paid.
        /// </summary>
        private static CommandResult HandleDoubleRewards(RunDoubleRewards cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var summary = s.Run.LastSummary;
            Require(s.Run.Active == null && summary != null && summary.Won, "no won run to double");
            Require(!summary.Doubled, "rewards already doubled");
            Require(summary.Doublable.Count > 0, "nothing to double");
            ShopSystem.ConsumeAdView(ctx, DoubleRewardsAdPlacement, cmd.AdToken);
            summary.Doubled = true;
            foreach (var line in summary.Rewards)
            {
                if (!line.Doublable) continue;
                var r = line.Reward;
                if (!ctx.Grant(new Reward(r.Kind, r.Id, r.Amount, r.Rarity))) continue; // granted at completion, so it has a use
                line.Doubled = true;
                summary.DoubledGranted.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
            }
            // the result screen's totals: each doubled currency line counts twice, each doubled item drop is listed again
            summary.Granted.Clear();
            foreach (var line in summary.Rewards)
            {
                var r = line.Reward;
                bool currency = r.Kind == RewardKinds.Currency;
                summary.Granted.Add(new Reward(r.Kind, r.Id, line.Doubled && currency ? r.Amount * 2 : r.Amount, r.Rarity));
                if (line.Doubled && !currency) summary.Granted.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
            }
            s.Count("run_rewards_doubled");
            ctx.Emit("run_rewards_doubled", summary.RunId);
            return CommandResult.Success(summary);
        }

        private static void Revive(CommandContext ctx, ActiveRun run, ReviveMethod method, string adToken)
        {
            Require(run.Phase == RunPhase.Defeated, "nothing to revive from");
            Require(!run.ReviveUsed, "revive already used this run");
            if (method == ReviveMethod.Ad) ShopSystem.ConsumeAdView(ctx, ReviveAdPlacement, adToken);
            if (method == ReviveMethod.Diamonds)
            {
                long cost = ctx.Content.RunTuning.ReviveDiamondCost;
                Require(ctx.State.Wallet.CanAfford(Currencies.Diamonds, cost), "not enough diamonds");
                ctx.State.Wallet.TrySpend(Currencies.Diamonds, cost);
            }
            run.ReviveUsed = true;
            ctx.State.Count("run_revives");
            run.WormHp = Math.Max(1, run.WormMaxHp * ctx.Content.RunTuning.ReviveHpPct / 100.0);
            run.Phase = RunPhase.AwaitRoll;
            ctx.Emit("run_revived", method.ToString());

            var enemies = new List<CombatantSpec>(run.DefeatEnemies);
            var followUp = new List<RunEffect>(run.DefeatFollowUp);
            string kind = run.DefeatKind;
            int xp = run.DefeatXp; long coins = run.DefeatCoins;
            run.DefeatEnemies.Clear(); run.DefeatFollowUp.Clear();
            var before = Snap.Of(run);
            bool won = enemies.Count == 0 ? true : Fight(ctx, run, enemies, kind, xp, coins, followUp);
            if (enemies.Count == 0) OnBattleWin(ctx, run, kind, xp, coins);
            if (kind != "boss") TileOutcome(ctx, run, run.Tiles[run.Position], null, before, "battle", false); // the retried fight
            if (!won) return;
            AfterWin(ctx, run, kind, followUp);
            Continue(ctx, run);
        }

        private static void BurrowDescend(CommandContext ctx, ActiveRun run)
        {
            Require(run.Phase == RunPhase.BurrowChoice, "not at a burrow cash-out");
            var portal = ctx.Content.Portal(run.PortalId);
            int idx = portal.LevelIds.IndexOf(run.LevelId);
            Require(idx >= 0 && idx + 1 < portal.LevelIds.Count, "no deeper floor");
            run.Floor++;
            SetupLevel(ctx.Content, run, ctx.Content.RunLevel(portal.LevelIds[idx + 1]));
            ctx.Emit("run_burrow_descend", run.Floor.ToString());
            Continue(ctx, run);
        }

        /// <summary>The decision the run waits for (finer than <see cref="RunPhase"/>: events by tile kind, revive vs give up).</summary>
        public static RunDecision Decision(ContentDb content, ActiveRun run)
        {
            if (run == null) return RunDecision.None;
            switch (run.Phase)
            {
                case RunPhase.AwaitRoll: return RunDecision.Roll;
                case RunPhase.ChooseBranch: return RunDecision.Branch;
                case RunPhase.Draft: return RunDecision.Draft;
                case RunPhase.Shop: return RunDecision.Shop;
                case RunPhase.Defeated: return run.ReviveUsed ? RunDecision.GiveUp : RunDecision.Revive;
                case RunPhase.BurrowChoice: return RunDecision.BurrowChoice;
                case RunPhase.Encounter: return RunDecision.Encounter;
                case RunPhase.Minigame: return RunDecision.Minigame;
                case RunPhase.Quest: return RunDecision.Quest;
                case RunPhase.Signature: return RunDecision.Signature;
                case RunPhase.Event:
                {
                    var tile = run.EventTile >= 0 && run.EventTile < run.Tiles.Count ? run.Tiles[run.EventTile] : null;
                    var def = tile != null ? TileDefOf(content, tile) : null;
                    if (def != null && def.Category == TileCategory.Treasure) return RunDecision.Treasure;
                    if (def != null && def.Category == TileCategory.Campfire) return RunDecision.Campfire;
                    return RunDecision.Event;
                }
                default: return RunDecision.None;
            }
        }

        /// <summary>
        /// True if the current decision needs the player: auto mode (RunAutoStep without Farming) stops here and the
        /// client shows the decision UI. Clear steps (roll, a miniboss / boss encounter, give up without a revive left)
        /// are auto-resolvable; every event, chest and campfire needs the player (even a single option is shown). With <paramref name="farming"/> every decision is auto-resolvable (shops are
        /// left untouched, or use the heal heuristic with RunAutoStep.FarmShops).
        /// </summary>
        public static bool RequiresPlayer(ContentDb content, ActiveRun run, bool farming = false)
        {
            switch (Decision(content, run))
            {
                case RunDecision.None:
                case RunDecision.Roll:
                case RunDecision.GiveUp:
                case RunDecision.Encounter:
                    return false;
                default: // events, chests and campfires too, even with a single option: the player always sees them
                    return !farming;
            }
        }

        /// <summary>
        /// Auto mode: the next clear step, or — with Farming — the default action of the current decision. Refused at
        /// a player decision without Farming. Farming defaults: first branch, the event's default option (first
        /// affordable one otherwise), best draft / mythic pick, shops buy what <see cref="FarmShopPick"/> wants one offer
        /// per step and are left when nothing more is worth it, defeat → give up (no hidden spending), Burrow → cash out.
        /// </summary>
        private static void AutoStep(CommandContext ctx, ActiveRun run, RunAutoStep cmd)
        {
            var decision = Decision(ctx.Content, run);
            Require(decision != RunDecision.None, "nothing to do");
            Require(!RequiresPlayer(ctx.Content, run, cmd.Farming), "needs a player decision (" + decision + ")");
            switch (run.Phase)
            {
                case RunPhase.AwaitRoll: Roll(ctx, run); break;
                case RunPhase.Encounter: Encounter(ctx, run); break;
                case RunPhase.ChooseBranch: ChooseBranch(ctx, run, 0); break;
                case RunPhase.Event:
                {
                    var ev = ctx.Content.RunEvents[run.EventId];
                    int opt = ev.DefaultOption;
                    if (!CanAffordOption(run, ev.Options[opt]))
                        for (int i = 0; i < ev.Options.Count; i++) if (CanAffordOption(run, ev.Options[i])) { opt = i; break; }
                    ChooseEventOption(ctx, run, opt);
                    break;
                }
                case RunPhase.Draft: DraftPick(ctx, run, RunDraft.BestPick(ctx.Content, run)); break;
                case RunPhase.Shop:
                {
                    int pick = FarmShopPick(ctx.Content, run);
                    if (pick >= 0) ShopBuy(ctx, run, pick);
                    else ShopLeave(ctx, run);
                    break;
                }
                case RunPhase.Defeated: Finish(ctx, run, false); break;
                case RunPhase.BurrowChoice: Finish(ctx, run, true); break;
                case RunPhase.Minigame: MinigameAuto(ctx, run); break;
                case RunPhase.Quest: QuestChoose(ctx, run, QuestFarmPick(ctx.Content, ctx.State, run)); break;
                case RunPhase.Signature: PickStartSignature(ctx, run, null); break;
            }
        }
    }
}
