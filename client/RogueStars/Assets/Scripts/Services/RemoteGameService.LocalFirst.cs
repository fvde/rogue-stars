using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Services
{
    /// <summary>
    /// Local-first commands (docs/server.md → Local-first commands). Every player command runs on the local rules engine
    /// first: the state changes and the UI updates at once (battles included: they are simulated here and played back from
    /// this device's log). The command then goes to the server in the background, strictly in order, one at a time, with
    /// expectedVersion, the time it ran at, the predicted state hash and the battle results (claims). The server stays the
    /// store of record:
    /// <list type="bullet">
    /// <item>Agreement (the usual case): a small ack (version + hash), nothing else to do.</item>
    /// <item>Disagreement (hash mismatch, rejection, version conflict): the response carries the server's state; the client
    /// adopts it and re-runs its not-yet-sent commands on it (a command that no longer applies is dropped and reported).</item>
    /// <item>Network trouble: the queue keeps the commands and retries with backoff (the player keeps playing offline).</item>
    /// </list>
    /// Server-first (busy until the server answers): commands paying with an ad token, purchases, and every non-command call
    /// that changes the player on the server (IAP verify, instant ads, arena fights, invasion attacks, guild membership, dev
    /// tools). They wait for the queue to drain and run in its order.
    /// </summary>
    public sealed partial class RemoteGameService
    {
        /// <summary>Commands that always wait for the server (besides ones carrying an ad token, see <see cref="IsServerFirst"/>).</summary>
        public static readonly HashSet<string> ServerFirstCommands = new HashSet<string> { "PurchaseStart", "AdWatched" };

        /// <summary>Retry delays (seconds) for a send that failed on the network; the last one repeats.</summary>
        public static readonly float[] RetryBackoff = { 1f, 2f, 4f, 8f, 15f, 30f };

        private sealed class Pending
        {
            public GameCommand Command;
            /// <summary>Null for local-first commands; server-first: performs the call and invokes its continuation when done.</summary>
            public Action<Action> ServerFirst;
            public long AtMs;
            public long ExpectedVersion;
            public string StateHash;
            public JArray Claims;
            /// <summary>Called once the server answered this command (Refresh waits for it).</summary>
            public Action<ServiceResult> Synced;
        }

        private readonly List<Pending> _queue = new List<Pending>();
        private bool _sending;
        private float _retryIn = -1;
        private int _failures;

        /// <summary>Commands applied locally that the server has not confirmed yet (including the one in flight).</summary>
        public int PendingCount => _queue.Count;

        /// <summary>True while sends fail on the network (the queue waits and retries).</summary>
        public bool SyncStalled => _failures > 0;

        /// <summary>
        /// The server disagreed with a local prediction and its state was adopted (arg: the reason: "mismatch",
        /// "version_conflict" or the server's rejection error). The UI just rebuilds from <see cref="State"/>.
        /// </summary>
        public event Action<string> Diverged;

        /// <summary>A locally applied command was rejected by the server (or no longer applies after a correction): the error.</summary>
        public event Action<string> Rejected;

        /// <summary>Dev servers report every re-simulated battle; a disagreement is described here (both results) for the developer.</summary>
        public event Action<string> BattleMismatch;

        /// <summary>Sends failed / recovered (true = stalled): the UI may show "Reconnecting…".</summary>
        public event Action<bool> SyncStateChanged;

        /// <summary>True for commands that must not be predicted: ad-paid (server-verified ad credits) and purchase steps.</summary>
        public static bool IsServerFirst(GameCommand command)
        {
            if (command == null) return true;
            if (ServerFirstCommands.Contains(command.Type)) return true;
            var adToken = command.GetType().GetField("AdToken");
            return adToken != null && adToken.FieldType == typeof(string) && !string.IsNullOrEmpty((string)adToken.GetValue(command));
        }

        /// <summary>Executes locally, shows the result at once, and queues the command for the server.</summary>
        private void ExecuteLocalFirst(GameCommand command, Action<CommandResult> done)
        {
            if (_state == null) { done?.Invoke(CommandResult.Fail(ServiceErrors.NotConnected)); return; }
            if (string.IsNullOrEmpty(command.RequestId)) command.RequestId = Guid.NewGuid().ToString("N");
            long at = Math.Max(NowMs, _state.LastCommandMs);
            long expected = _state.Version;
            var working = _state;
            CommandResult result;
            try { result = Engine.Execute(ref working, command, at); }
            catch (Exception ex) { result = CommandResult.Fail(ex.Message); }
            if (!result.Ok) { done?.Invoke(result); return; } // the server would reject it too: nothing to send
            _state = working;
            Version = working.Version;
            _queue.Add(Predict(new Pending { Command = command, AtMs = at, ExpectedVersion = expected }, result));
            StateChanged?.Invoke();
            done?.Invoke(result);
            Pump();
        }

        private Pending Predict(Pending p, CommandResult result)
        {
            p.StateHash = StateHash.Hex(_state);
            p.Claims = new JArray();
            foreach (var b in result.Battles) p.Claims.Add(JObject.FromObject(b.Result.Summary(), ClientJson.WireSerializer));
            return p;
        }

        /// <summary>Queues a server-first call; it runs after every earlier command was confirmed.</summary>
        private void ServerFirst(Action<Action> call)
        {
            _queue.Add(new Pending { ServerFirst = call });
            Pump();
        }

        /// <summary>Wire body of a local-first command.</summary>
        public static JObject LocalFirstBody(GameCommand command, long expectedVersion, long atMs, string stateHash, JArray claims)
        {
            var body = CommandBody(command, expectedVersion);
            body["atMs"] = atMs;
            body["stateHash"] = stateHash;
            body["battles"] = claims ?? new JArray();
            return body;
        }

        /// <summary>Sends the head of the queue unless one is in flight or a retry is pending.</summary>
        private void Pump()
        {
            if (_sending || _queue.Count == 0 || _retryIn > 0 || !IsConnected) return;
            var head = _queue[0];
            _sending = true;
            if (head.ServerFirst != null)
            {
                head.ServerFirst(() => { _sending = false; Remove(head); Pump(); });
                return;
            }
            Call("POST", "/v1/command", LocalFirstBody(head.Command, head.ExpectedVersion, head.AtMs, head.StateHash, head.Claims), (json, err) =>
            {
                _sending = false;
                OnReply(head, json, err);
            });
        }

        private void Remove(Pending p)
        {
            int i = _queue.IndexOf(p);
            if (i >= 0) _queue.RemoveAt(i);
        }

        private void OnReply(Pending head, JObject json, string err)
        {
            if (json == null || (err != null && IsTransient(err) && !(json["state"] is JObject)))
            {
                // Not applied (or unknown): keep it and retry the same request id (the server replays it if it did apply).
                _failures++;
                _retryIn = RetryBackoff[Math.Min(_failures - 1, RetryBackoff.Length - 1)];
                if (_failures == 1) SyncStateChanged?.Invoke(true);
                // a caller waiting for the sync (Refresh) learns it failed; the command itself stays queued
                var waiting = head.Synced;
                head.Synced = null;
                waiting?.Invoke(ServiceResult.Fail(err ?? "network_error"));
                return;
            }
            if (_failures > 0) { _failures = 0; SyncStateChanged?.Invoke(false); }
            Remove(head);
            ReportBattleChecks(json);
            var st = json["serverTimeMs"];
            if (st != null && st.Type == JTokenType.Integer) _serverOffsetMs = (long)st - _clock.NowMs;
            if (json.Value<bool?>("match") == true)
            {
                var v = json["version"];
                if (_queue.Count == 0 && v != null && v.Type == JTokenType.Integer) Version = (long)v;
                head.Synced?.Invoke(ServiceResult.Success());
                Pump();
                return;
            }
            string reason = err ?? "mismatch";
            if (json["state"] is JObject)
            {
                if (err != null && err != "version_conflict") Rejected?.Invoke(err);
                ApplyState(json); // adopt + re-run the rest of the queue
                Diverged?.Invoke(reason);
                head.Synced?.Invoke(ServiceResult.Success());
                Pump();
                return;
            }
            // Refused without a state (bad request, forbidden, …): the server never applied it. Re-sync from the server.
            Rejected?.Invoke(reason);
            Call("GET", "/v1/state", null, (s, e2) =>
            {
                if (s != null && e2 == null) { ApplyState(s); Diverged?.Invoke(reason); }
                head.Synced?.Invoke(e2 == null ? ServiceResult.Success() : ServiceResult.Fail(e2));
                Pump();
            });
        }

        /// <summary>Not applied by the server for a reason that may pass (network, overload, sign-in): keep and retry.</summary>
        private static bool IsTransient(string err) =>
            err.StartsWith("network_error", StringComparison.Ordinal) || err == "contention" ||
            err == "invalid_token" || err == "missing_token" || err == "unauthenticated" || err.StartsWith("auth", StringComparison.Ordinal) ||
            err.StartsWith("http_5", StringComparison.Ordinal) || err == "bad_response" || err == "internal_error";

        private void ReportBattleChecks(JObject json)
        {
            if (!(json["battleChecks"] is JArray checks)) return;
            foreach (var c in checks)
            {
                if (c.Value<bool?>("match") != false) continue;
                BattleMismatch?.Invoke("Battle mismatch (" + c.Value<string>("kind") + " #" + c.Value<int?>("index") + "): client " +
                                       c.Value<string>("client") + " | server " + c.Value<string>("server"));
            }
        }

        /// <summary>Drives retries (GameSession.Tick).</summary>
        public void Tick(float dt)
        {
            if (_retryIn <= 0) return;
            _retryIn -= dt;
            if (_retryIn <= 0) { _retryIn = -1; Pump(); }
        }

        /// <summary>Retry a stalled queue now (e.g. the app came back to the foreground).</summary>
        public void RetryNow()
        {
            _retryIn = -1;
            Pump();
        }

        /// <summary>
        /// The server's state replaced ours: re-run the commands not sent yet on top of it (new predictions). A command in
        /// flight is left alone: its reply decides (a version conflict then adopts again).
        /// </summary>
        private void Rebase()
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                var p = _queue[i];
                if (p.ServerFirst != null || (i == 0 && _sending)) continue;
                long at = Math.Max(p.AtMs, _state.LastCommandMs);
                long expected = _state.Version;
                var working = _state;
                CommandResult result;
                try { result = Engine.Execute(ref working, p.Command, at); }
                catch (Exception ex) { result = CommandResult.Fail(ex.Message); }
                if (!result.Ok)
                {
                    _queue.RemoveAt(i--);
                    Rejected?.Invoke(result.Error);
                    p.Synced?.Invoke(ServiceResult.Success());
                    continue;
                }
                _state = working;
                p.AtMs = at;
                p.ExpectedVersion = expected;
                Predict(p, result);
            }
            Version = _state.Version;
        }

        private void ResetQueue()
        {
            _queue.Clear();
            _sending = false;
            _retryIn = -1;
            _failures = 0;
        }
    }
}
