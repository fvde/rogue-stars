using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace RogueStars.Client.Services
{
    /// <summary>Wall clock in UTC epoch milliseconds (injectable for tests).</summary>
    public interface IClock { long NowMs { get; } }

    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();
        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>Settable clock for tests and simulations.</summary>
    public sealed class ManualClock : IClock
    {
        public long NowMs { get; set; }
        public ManualClock(long nowMs) { NowMs = nowMs; }
        public void Advance(long ms) => NowMs += ms;
    }

    /// <summary>Outcome of a non-command service call.</summary>
    public class ServiceResult
    {
        public bool Ok;
        public string Error;
        public static ServiceResult Success() => new ServiceResult { Ok = true };
        public static ServiceResult Fail(string error) => new ServiceResult { Ok = false, Error = error };
        public override string ToString() => Ok ? "ok" : "error: " + Error;
    }

    public sealed class ServiceResult<T> : ServiceResult
    {
        public T Value;
        public static ServiceResult<T> Success(T value) => new ServiceResult<T> { Ok = true, Value = value };
        public static new ServiceResult<T> Fail(string error) => new ServiceResult<T> { Ok = false, Error = error };
    }

    public static class ServiceErrors
    {
        public const string RequiresOnline = "requires_online";
        public const string ServerOnlyCommand = "server_only_command";
        public const string NotConnected = "not_connected";
    }

    // ---- cross-player DTOs (shape of docs/server.md responses) ----------------------------------

    [Serializable]
    public sealed class ArenaOpponent
    {
        public string PlayerId;
        public string DisplayName;
        public long Points;
        public long Power;
    }

    [Serializable]
    public sealed class ArenaFightOutcome
    {
        public string FightId;
        public bool Won;
        public long PointsDelta;
        public long Points;
        public ArenaOpponent Opponent;
    }

    [Serializable]
    public sealed class ArenaLeaderboardEntry
    {
        public int Rank;
        public string PlayerId;
        public string DisplayName;
        public long Points;
        public long Power;
        public int Wins;
        public int Losses;
    }

    [Serializable]
    public sealed class ArenaLeaderboard
    {
        public string Week;
        public bool Joined;
        public int League;
        public string GroupId;
        public int GroupSize;
        public int MyRank;
        public List<ArenaLeaderboardEntry> Entries = new List<ArenaLeaderboardEntry>();
    }

    [Serializable]
    public sealed class InvasionInfo
    {
        /// <summary>Attack key (server instance id; offline: the occurrence id).</summary>
        public string Id;
        /// <summary>Rules live-ops occurrence id (matches LiveOpsSystem.ActiveEvents / player contributions).</summary>
        public string OccurrenceId;
        /// <summary>Content invasion id.</summary>
        public string TypeId;
        /// <summary>From the rules calendar (false: an extra server-pushed invasion).</summary>
        public bool Scheduled;
        public string Name;
        public long StartsMs;
        public long EndsMs;
        public bool Local;
    }

    [Serializable]
    public sealed class InvasionAttackOutcome
    {
        public string AttackId;
        public long Damage;
        public long GroupDamage;
        public long GroupHp;
    }

    [Serializable]
    public sealed class GuildInfo
    {
        public string Id;
        public string Name;
        public string Description;
        public int Level;
        public int Members;
        public int MaxMembers;
    }

    [Serializable]
    public sealed class GuildMemberContribution
    {
        public string PlayerId;
        public string DisplayName;
        public long Damage;
    }

    [Serializable]
    public sealed class MyGuild
    {
        public GuildInfo Guild;
        public int Level;
        public List<GuildMemberContribution> Members = new List<GuildMemberContribution>();
    }

    [Serializable]
    public sealed class ChatMessage
    {
        public string Id;
        public string PlayerId;
        public string DisplayName;
        public string Text;
        public long AtMs;
    }

    /// <summary>JSON settings: storage (C# member names, like the server's StateJson.Options) and wire (camelCase, keys untouched).</summary>
    public static class ClientJson
    {
        /// <summary>Save files: PascalCase fields, collections replaced (never appended to field initialisers).</summary>
        public static readonly JsonSerializerSettings Storage = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new DefaultContractResolver(),
        };

        /// <summary>HTTP wire: camelCase members, dictionary keys (currency ids, counters) never re-cased, enums as numbers.</summary>
        public static readonly JsonSerializerSettings Wire = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = true },
            },
        };

        public static readonly JsonSerializer WireSerializer = JsonSerializer.Create(Wire);

        public static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value, Storage), Storage);
    }
}
