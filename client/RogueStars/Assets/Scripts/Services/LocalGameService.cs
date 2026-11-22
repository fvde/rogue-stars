using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Services
{
    /// <summary>
    /// Offline mode: runs the shared <see cref="GameEngine"/> on device and persists the state as JSON.
    /// Server-only commands ([ServerOnly], e.g. PurchaseComplete, ArenaFightResult) are executed locally here and only here.
    /// Content is a private <see cref="ContentDb"/> instance with dev flags (AllowDevReceipts) switched on.
    /// </summary>
    public sealed class LocalGameService : IGameService
    {
        public const string SaveFileName = "rogue_stars_save.json";

        private readonly string _savePath;
        private readonly IClock _clock;
        private readonly string _playerId;
        private readonly string _displayName;
        private readonly Random _random;
        private GameState _state;

        public bool IsOnline => false;
        public string ModeName => "offline";
        public bool IsConnected => _state != null;
        public GameEngine Engine { get; }
        public ContentDb Content { get; }
        public GameState State => _state;
        public long NowMs => _clock.NowMs;
        public IGuildApi Guilds => null;
        public string SavePath => _savePath;

        public event Action StateChanged;

        /// <param name="saveDirectory">Usually Application.persistentDataPath.</param>
        public LocalGameService(string saveDirectory, string playerId = "local_player", string displayName = "Worm", IClock clock = null, int? randomSeed = null)
        {
            _savePath = Path.Combine(saveDirectory, SaveFileName);
            _clock = clock ?? SystemClock.Instance;
            _playerId = playerId;
            _displayName = displayName;
            _random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            Content = new ContentDb();
            Content.Shop.AllowDevReceipts = true; // offline purchases complete with "dev:" receipts
            Engine = new GameEngine(Content) { Cloner = ClientJson.Clone };
        }

        // ---- lifecycle --------------------------------------------------------------------------

        public void Connect(Action<ServiceResult> done)
        {
            try
            {
                _state = File.Exists(_savePath) ? Load(_savePath) : null;
                if (_state == null)
                {
                    long seed = DateTime.UtcNow.Ticks ^ ((long)_random.Next() << 20);
                    _state = Engine.NewPlayer(_playerId, NowMs, seed);
                    _state.DisplayName = _displayName;
                    Save();
                }
                Engine.Settle(_state, NowMs);
                RaiseChanged();
                done?.Invoke(ServiceResult.Success());
            }
            catch (Exception ex)
            {
                done?.Invoke(ServiceResult.Fail("load_failed: " + ex.Message));
            }
        }

        public void Refresh(Action<ServiceResult> done)
        {
            if (_state == null) { done?.Invoke(ServiceResult.Fail(ServiceErrors.NotConnected)); return; }
            Engine.Settle(_state, NowMs);
            RaiseChanged();
            done?.Invoke(ServiceResult.Success());
        }

        /// <summary>Delete the save and start a fresh player.</summary>
        public void ResetSave(Action<ServiceResult> done)
        {
            if (File.Exists(_savePath)) File.Delete(_savePath);
            _state = null;
            Connect(done);
        }

        public void Save()
        {
            if (_state == null) return;
            string dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = _savePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_state, ClientJson.Storage));
            if (File.Exists(_savePath)) File.Delete(_savePath);
            File.Move(tmp, _savePath);
        }

        public static GameState Load(string path) => JsonConvert.DeserializeObject<GameState>(File.ReadAllText(path), ClientJson.Storage);

        // ---- commands ---------------------------------------------------------------------------

        public void Execute(GameCommand command, Action<CommandResult> done)
        {
            done?.Invoke(ExecuteNow(command));
        }

        /// <summary>Synchronous execution (offline only). Server-only commands are allowed here.</summary>
        public CommandResult ExecuteNow(GameCommand command)
        {
            if (_state == null) return CommandResult.Fail(ServiceErrors.NotConnected);
            if (string.IsNullOrEmpty(command.RequestId)) command.RequestId = Guid.NewGuid().ToString("N");
            CommandResult result;
            try { result = Engine.Execute(ref _state, command, NowMs); }
            catch (Exception ex) // non-rule failures (e.g. a reward kind whose system is not merged yet); state is untouched (Cloner)
            {
                UnityEngine.Debug.LogWarning("[LocalGameService] " + command.Type + " failed: " + ex.Message);
                return CommandResult.Fail("internal_error: " + ex.Message);
            }
            if (result.Ok)
            {
                Save();
                RaiseChanged();
            }
            return result;
        }

        public void VerifyPurchase(string productId, string purchaseToken, Action<CommandResult> done)
        {
            string receipt = Content.Shop.DevReceiptPrefix + purchaseToken;
            done?.Invoke(ExecuteNow(new PurchaseComplete { ProductId = productId, ReceiptId = receipt }));
        }

        // ---- dev cheats (offline only) ----------------------------------------------------------

        /// <summary>Dev/test kit: currencies, blueprints, aliens, parts, runes, hero shards, a mount and pull tokens.</summary>
        public void DevGrantStarterKit()
        {
            var rewards = new List<Reward>
            {
                Reward.Cur(Currencies.Biomass, 20000), Reward.Cur(Currencies.Ore, 20000), Reward.Cur(Currencies.Grain, 20000),
                Reward.Cur(Currencies.Diamonds, 2000), Reward.Cur(Currencies.InvasionAttempts, 3), Reward.Cur(Currencies.ArenaTickets, 5),
            };
            var bpIds = new List<string>(Content.Blueprints.Keys);
            bpIds.Sort(string.CompareOrdinal);
            foreach (var id in bpIds)
            {
                var def = Content.Blueprints[id];
                if (def.Rarity <= Rarity.Rare || def.Category != BuildingCategory.Boon) rewards.Add(new Reward(RewardKinds.Blueprint, id, 1));
            }
            var partIds = new List<string>(Content.PartsConfig.Defs.Keys);
            partIds.Sort(string.CompareOrdinal);
            for (int i = 0; i < partIds.Count && i < 6; i++) rewards.Add(new Reward(RewardKinds.Part, partIds[i], 3, 0));
            var runeIds = new List<string>(Content.PartsConfig.Runes.Keys);
            runeIds.Sort(string.CompareOrdinal);
            if (runeIds.Count > 0) rewards.Add(new Reward(RewardKinds.Rune, runeIds[0], 3, 0));
            var heroIds = new List<string>(Content.HeroConfig.Defs.Keys);
            heroIds.Sort(string.CompareOrdinal);
            if (heroIds.Count > 0) rewards.Add(new Reward(RewardKinds.HeroShards, heroIds[0], 120));
            var mountIds = new List<string>(Content.MountConfig.Defs.Keys);
            mountIds.Sort(string.CompareOrdinal);
            if (mountIds.Count > 0) rewards.Add(new Reward(RewardKinds.Mount, mountIds[0], 1));
            foreach (var cur in new[] { Currencies.PartTokens, Currencies.BlueprintTokens, Currencies.AlienTokens }) rewards.Add(Reward.Cur(cur, 20));
            rewards.Add(Reward.Cur(Currencies.GearCores, 2000));
            rewards.Add(Reward.Cur(Currencies.Fuel, 5000));
            rewards.Add(Reward.Cur(Currencies.HeroXp, 5000));
            rewards.Add(Reward.Cur(Currencies.AdvancementPlates, 50));
            var alienIds = new List<string>(Content.AlienTypes.Keys);
            alienIds.Sort(string.CompareOrdinal);
            for (int i = 0; i < alienIds.Count && i < 4; i++) rewards.Add(new Reward(RewardKinds.Alien, alienIds[i], 2));
            DevGrant(rewards);
        }

        public void DevGrant(IEnumerable<Reward> rewards)
        {
            if (_state == null) return;
            var ctx = new CommandContext { State = _state, Content = Content, Engine = Engine, NowMs = NowMs };
            ctx.GrantAll(rewards);
            Save();
            RaiseChanged();
        }

        public void DevSetFlag(string flag, bool on)
        {
            if (_state == null) return;
            if (on) _state.Flags.Add(flag); else _state.Flags.Remove(flag);
            Engine.Settle(_state, NowMs);
            Save();
            RaiseChanged();
        }

        // ---- arena (local bots; power comparison until the Run workstream's BattleSimulator lands) ----

        private static readonly string[] BotNames = { "Squirmlord", "Noodle Prime", "Glorp", "Wiggleton", "Coilbert", "Slinkster", "Vermax", "Dirt Duke", "Loopy", "Mudrunner" };

        private long MyPower() => Math.Max(100, Engine.BuildLoadout(_state, NowMs).Power);

        private List<ArenaOpponent> Bots(int count)
        {
            long week = MetaTime.WeekIndex(Content, NowMs);
            var rng = Rng.FromSeed(week * 7919).Fork("arena_bots:" + _playerId);
            long power = MyPower();
            var list = new List<ArenaOpponent>();
            for (int i = 0; i < count; i++)
            {
                list.Add(new ArenaOpponent
                {
                    PlayerId = "bot_" + week + "_" + i,
                    DisplayName = BotNames[i % BotNames.Length] + (i >= BotNames.Length ? " " + (i / BotNames.Length + 1) : ""),
                    Points = Math.Max(0, Content.Arena.StartPoints + rng.Range(-200, 201)),
                    Power = (long)(power * (0.6 + rng.NextDouble() * 0.8)),
                });
            }
            return list;
        }

        public void ArenaOpponents(Action<ServiceResult<List<ArenaOpponent>>> done)
        {
            if (_state == null) { done(ServiceResult<List<ArenaOpponent>>.Fail(ServiceErrors.NotConnected)); return; }
            var all = Bots(20);
            long me = _state.Arena.Points;
            all.Sort((a, b) => Math.Abs(a.Points - me).CompareTo(Math.Abs(b.Points - me)));
            done(ServiceResult<List<ArenaOpponent>>.Success(all.GetRange(0, 5)));
        }

        public void ArenaFight(string opponentId, Action<ServiceResult<ArenaFightOutcome>> done)
        {
            var opp = Bots(20).Find(o => o.PlayerId == opponentId);
            if (opp == null) { done(ServiceResult<ArenaFightOutcome>.Fail("unknown opponent")); return; }
            var start = ExecuteNow(new ArenaStartFight { OpponentId = opponentId });
            if (!start.Ok) { done(ServiceResult<ArenaFightOutcome>.Fail(start.Error)); return; }
            long mine = MyPower();
            bool won = _random.NextDouble() < (double)mine / (mine + Math.Max(1, opp.Power));
            long before = _state.Arena.Points;
            var res = ExecuteNow(new ArenaFightResult { Won = won, OpponentPoints = opp.Points });
            if (!res.Ok) { done(ServiceResult<ArenaFightOutcome>.Fail(res.Error)); return; }
            done(ServiceResult<ArenaFightOutcome>.Success(new ArenaFightOutcome
            {
                FightId = start.Events.Count > 0 ? start.Events[0].ToString() : Guid.NewGuid().ToString("N"),
                Won = won, PointsDelta = _state.Arena.Points - before, Points = _state.Arena.Points, Opponent = opp,
            }));
        }

        public void ArenaLeaderboard(Action<ServiceResult<ArenaLeaderboard>> done)
        {
            if (_state == null) { done(ServiceResult<ArenaLeaderboard>.Fail(ServiceErrors.NotConnected)); return; }
            var rows = new List<ArenaLeaderboardEntry>();
            foreach (var b in Bots(20)) rows.Add(new ArenaLeaderboardEntry { PlayerId = b.PlayerId, DisplayName = b.DisplayName, Points = b.Points, Power = b.Power });
            rows.Add(new ArenaLeaderboardEntry
            {
                PlayerId = _state.PlayerId, DisplayName = _state.DisplayName, Points = _state.Arena.Points, Power = MyPower(),
                Wins = _state.Arena.WeekWins, Losses = _state.Arena.WeekLosses,
            });
            rows.Sort((a, b) => b.Points != a.Points ? b.Points.CompareTo(a.Points) : string.CompareOrdinal(a.PlayerId, b.PlayerId));
            var board = new ArenaLeaderboard
            {
                Week = MetaTime.WeekIndex(Content, NowMs).ToString(), Joined = true, League = _state.Arena.League,
                GroupId = "local", GroupSize = rows.Count, Entries = rows,
            };
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Rank = i + 1;
                if (rows[i].PlayerId == _state.PlayerId) board.MyRank = i + 1;
            }
            done(ServiceResult<ArenaLeaderboard>.Success(board));
        }

        // ---- invasions (content calendar occurrences; local damage roll) --------------------------

        public void Invasions(Action<ServiceResult<List<InvasionInfo>>> done)
        {
            var list = new List<InvasionInfo>();
            foreach (var ev in LiveOpsSystem.ActiveEvents(_state, Content, NowMs))
                if (ev.Kind == LiveOpsKind.Invasion)
                    list.Add(new InvasionInfo { Id = ev.OccurrenceId, Name = ev.Name, StartsMs = ev.StartMs, EndsMs = ev.EndMs, Local = true });
            done(ServiceResult<List<InvasionInfo>>.Success(list));
        }

        public void InvasionAttack(string occurrenceId, Action<ServiceResult<InvasionAttackOutcome>> done)
        {
            var start = ExecuteNow(new InvasionStartAttempt { OccurrenceId = occurrenceId });
            if (!start.Ok) { done(ServiceResult<InvasionAttackOutcome>.Fail(start.Error)); return; }
            long damage = (long)(Math.Max(1000, MyPower() * 10) * (0.8 + _random.NextDouble() * 0.4));
            var rec = ExecuteNow(new InvasionRecordDamage { OccurrenceId = occurrenceId, Damage = damage });
            if (!rec.Ok) { done(ServiceResult<InvasionAttackOutcome>.Fail(rec.Error)); return; }
            _state.LiveOps.Contributions.TryGetValue(occurrenceId, out var c);
            done(ServiceResult<InvasionAttackOutcome>.Success(new InvasionAttackOutcome
            {
                AttackId = Guid.NewGuid().ToString("N"), Damage = damage, GroupDamage = c != null ? c.Damage : damage, GroupHp = 0,
            }));
        }

        private void RaiseChanged() => StateChanged?.Invoke();
    }
}
