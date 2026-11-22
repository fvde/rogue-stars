using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Client.Services
{
    /// <summary>
    /// The client's single gateway to game state. Two implementations share the same command objects:
    /// <see cref="LocalGameService"/> runs the shared rules engine on device (offline mode) and
    /// <see cref="RemoteGameService"/> sends commands to the authoritative server (docs/server.md).
    /// All callbacks are invoked on the Unity main thread (local: synchronously).
    /// </summary>
    public interface IGameService
    {
        bool IsOnline { get; }
        /// <summary>"offline" or "online"; shown in the UI.</summary>
        string ModeName { get; }
        bool IsConnected { get; }
        /// <summary>Local engine instance: queries (loadout, costs) and, offline, execution.</summary>
        GameEngine Engine { get; }
        ContentDb Content { get; }
        /// <summary>Latest known player state (authoritative copy from the server when online). Treat as read-only.</summary>
        GameState State { get; }
        /// <summary>Current time in the rules' clock (server time when online).</summary>
        long NowMs { get; }

        /// <summary>Raised whenever <see cref="State"/> was replaced or changed.</summary>
        event Action StateChanged;

        /// <summary>Create/load the player (offline: save file; online: dev auth + /v1/session).</summary>
        void Connect(Action<ServiceResult> done);

        /// <summary>Re-sync time-based state (offline: settle; online: GET /v1/state).</summary>
        void Refresh(Action<ServiceResult> done);

        /// <summary>Execute a player command (offline: locally; online: POST /v1/command with a requestId).</summary>
        void Execute(GameCommand command, Action<CommandResult> done);

        /// <summary>Complete a store purchase. Offline: local PurchaseComplete with a dev receipt; online: POST /v1/iap/verify.</summary>
        void VerifyPurchase(string productId, string purchaseToken, Action<CommandResult> done);

        // ---- cross-player features -----------------------------------------------------------
        void ArenaOpponents(Action<ServiceResult<List<ArenaOpponent>>> done);
        void ArenaFight(string opponentId, Action<ServiceResult<ArenaFightOutcome>> done);
        void ArenaLeaderboard(Action<ServiceResult<ArenaLeaderboard>> done);

        void Invasions(Action<ServiceResult<List<InvasionInfo>>> done);
        void InvasionAttack(string invasionId, Action<ServiceResult<InvasionAttackOutcome>> done);

        /// <summary>Guild endpoints; null when the mode has no guild support (offline).</summary>
        IGuildApi Guilds { get; }
    }

    /// <summary>Server guild endpoints (/v1/guilds/*).</summary>
    public interface IGuildApi
    {
        void Search(string query, Action<ServiceResult<List<GuildInfo>>> done);
        void Mine(Action<ServiceResult<MyGuild>> done);
        void Create(string name, string description, Action<ServiceResult<GuildInfo>> done);
        void Join(string guildId, Action<ServiceResult<GuildInfo>> done);
        void Leave(Action<ServiceResult> done);
        void Chat(long sinceMs, Action<ServiceResult<List<ChatMessage>>> done);
        void Post(string text, Action<ServiceResult<ChatMessage>> done);
    }

    /// <summary>A completed store transaction waiting for server verification and consumption.</summary>
    public sealed class StorePurchase
    {
        public string ProductId;
        public string StoreProductId;
        public string PurchaseToken;
        public string OrderId;
    }

    /// <summary>
    /// Platform store. TODO(billing): GooglePlayStoreGateway backed by the Play Billing Library
    /// (launch billing flow, then consume/acknowledge only after the server confirmed /v1/iap/verify).
    /// </summary>
    public interface IStoreGateway
    {
        string Name { get; }
        void Purchase(string productId, string storeProductId, Action<ServiceResult<StorePurchase>> done);
        /// <summary>Consume/acknowledge after the server granted the product.</summary>
        void Finish(StorePurchase purchase);
    }

    /// <summary>Completes every purchase instantly with a "fake-" token (accepted by the server's fake IAP mode).</summary>
    public sealed class FakeStoreGateway : IStoreGateway
    {
        public string Name => "fake";
        public bool FailNext;
        public readonly List<StorePurchase> Finished = new List<StorePurchase>();

        public void Purchase(string productId, string storeProductId, Action<ServiceResult<StorePurchase>> done)
        {
            if (FailNext) { FailNext = false; done(ServiceResult<StorePurchase>.Fail("purchase_cancelled")); return; }
            string id = Guid.NewGuid().ToString("N");
            done(ServiceResult<StorePurchase>.Success(new StorePurchase
            {
                ProductId = productId, StoreProductId = storeProductId, PurchaseToken = "fake-" + id, OrderId = "fake-order-" + id,
            }));
        }

        public void Finish(StorePurchase purchase) => Finished.Add(purchase);
    }

    /// <summary>
    /// Rewarded ads. TODO(ads): AppLovin MAX gateway; online rewards arrive via the server's /v1/ads/ssv callback.
    /// </summary>
    public interface IAdGateway
    {
        void Show(string placementId, Action<ServiceResult<string>> done);
    }

    public sealed class FakeAdGateway : IAdGateway
    {
        public void Show(string placementId, Action<ServiceResult<string>> done) =>
            done(ServiceResult<string>.Success("fake-ad-" + Guid.NewGuid().ToString("N")));
    }
}
