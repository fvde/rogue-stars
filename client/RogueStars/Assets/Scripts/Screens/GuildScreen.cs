using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Guild create/join, members (weekly invasion contribution), chat and guild shop. Server only.</summary>
    public sealed class GuildScreen : UiScreen
    {
        private MyGuild _mine;
        private List<GuildInfo> _search;
        private readonly List<ChatMessage> _chat = new List<ChatMessage>();
        private long _chatSince;
        private string _error;
        private float _pollTimer;
        private int _tab;

        public override string Title => "Guild";
        public override bool AutoRebuild => false; // keeps the chat/search inputs; rebuilt on data arrival

        private IGuildApi Api => Session.Service?.Guilds;

        public override void OnShow() => Load();

        private void Load()
        {
            if (Api == null) return;
            Api.Mine(r =>
            {
                _error = r.Ok ? null : r.Error;
                _mine = r.Ok ? r.Value : null;
                if (_mine?.Guild == null) Api.Search("", s => { _search = s.Ok ? s.Value : null; Rebuild(); });
                else PollChat();
                Rebuild();
            });
        }

        private void PollChat()
        {
            if (Api == null || _mine?.Guild == null) return;
            Api.Chat(_chatSince, r =>
            {
                if (!r.Ok || r.Value.Count == 0) return;
                foreach (var m in r.Value) { _chat.Add(m); if (m.AtMs > _chatSince) _chatSince = m.AtMs; }
                if (_chat.Count > 200) _chat.RemoveRange(0, _chat.Count - 200);
                if (_tab == 1) Rebuild();
            });
        }

        public override void Tick()
        {
            base.Tick();
            _pollTimer += 0.5f;
            if (_pollTimer >= 5f) { _pollTimer = 0; PollChat(); }
        }

        protected override void Build(RectTransform body)
        {
            if (Api == null)
            {
                var card = Card(body, Theme.PanelAlt);
                Ui.Label(card, "Guilds require online mode.", Theme.FontLarge, Theme.Warn, TextAnchor.MiddleCenter);
                Note(card, "Guild roster, chat and invasion totals live on the server. Switch to online in Settings.");
                Ui.Button(card, "Open Settings", () => App.Open("settings"), Theme.Accent);
                return;
            }
            if (_error != null) Ui.Label(body, Fmt.Humanize(_error), Theme.FontSmall, Theme.Bad);
            if (_mine == null) { Note(body, "Loading…"); return; }
            if (_mine.Guild == null) NoGuild(body);
            else InGuild(body);
        }

        private void NoGuild(RectTransform body)
        {
            var create = Card(body, Theme.Header);
            Section(create, "Create a guild");
            var name = Ui.Input(create, "", "Guild name");
            var desc = Ui.Input(create, "", "Description (optional)");
            Ui.Button(create, "Create", () => Api.Create(name.text.Trim(), desc.text.Trim(), r =>
            {
                if (!r.Ok) Session.Error(Fmt.Humanize(r.Error)); else Session.Info("Guild created");
                Load();
            }), Theme.Accent);

            Section(body, "Join a guild");
            var searchRow = Ui.Row(body);
            var query = Ui.Input(searchRow, "", "Search");
            Ui.Button(searchRow, "Search", () => Api.Search(query.text.Trim(), r => { _search = r.Ok ? r.Value : null; Rebuild(); }), Theme.Button, 220);
            if (_search == null) { Note(body, "Loading…"); return; }
            if (_search.Count == 0) Note(body, "No open guilds found.");
            foreach (var g in _search)
            {
                var guild = g;
                var row = Ui.Row(body, 96, 10, Theme.Panel, 12);
                Ui.Label(row, guild.Name + "  Lv " + guild.Level + "\n<size=22>" + guild.Members + "/" + guild.MaxMembers + " · " + guild.Description + "</size>", Theme.FontSmall);
                Ui.Button(row, "Join", () => Api.Join(guild.Id, r =>
                {
                    if (!r.Ok) Session.Error(Fmt.Humanize(r.Error)); else Session.Info("Joined " + guild.Name);
                    Load();
                }), Theme.Good, 180, 80);
            }
        }

        private void InGuild(RectTransform body)
        {
            var g = _mine.Guild;
            var head = Card(body, Theme.Header);
            Ui.Label(head, g.Name + "  (Lv " + _mine.Level + ")", Theme.FontLarge);
            if (!string.IsNullOrEmpty(g.Description)) Note(head, g.Description);
            KeyValue(head, "Your role", State.Guild.Role ?? "member");
            KeyValue(head, "Guild Marks", State.Wallet.Get(Currencies.GuildMarks).ToString());
            Ui.TabBar(body, new[] { "Members", "Chat", "Shop" }, _tab, i => { _tab = i; Rebuild(); });
            if (_tab == 0) Members(body);
            else if (_tab == 1) Chat(body);
            else Shop(body);
            Ui.Button(body, "Leave guild", () => Modal("Leave " + g.Name + "?", null,
                new ModalButton("Stay", null),
                new ModalButton("Leave", () => Api.Leave(r => { _mine = null; _chat.Clear(); _chatSince = 0; Load(); }), Theme.Bad)), Theme.Bad);
        }

        private void Members(RectTransform body)
        {
            Note(body, "Weekly invasion damage");
            if (_mine.Members.Count == 0) Note(body, "No contributions yet this week.");
            foreach (var m in _mine.Members)
            {
                var row = Ui.Row(body, 64, 10, Theme.Panel, 12);
                Ui.Label(row, Fmt.PlayerName(m.DisplayName), Theme.FontSmall);
                Ui.Label(row, Fmt.Num(m.Damage), Theme.FontSmall, Theme.Text, TextAnchor.MiddleRight);
            }
        }

        private void Chat(RectTransform body)
        {
            var list = Ui.List(body, 900);
            if (_chat.Count == 0) Note(list, "No messages yet.");
            foreach (var m in _chat)
                Ui.Label(list, "<color=#8FB3FF>" + Fmt.PlayerName(m.DisplayName) + "</color>: " + m.Text, Theme.FontSmall);
            var row = Ui.Row(body);
            var input = Ui.Input(row, "", "Say something (max 300)");
            input.characterLimit = 300;
            Ui.Button(row, "Send", () =>
            {
                string text = input.text.Trim();
                if (text.Length == 0) return;
                Api.Post(text, r => { if (!r.Ok) Session.Error(Fmt.Humanize(r.Error)); PollChat(); });
                input.text = "";
            }, Theme.Accent, 180);
        }

        private void Shop(RectTransform body)
        {
            foreach (var it in Content.ShopItems.Values)
            {
                if (it.Section != GuildSystem.ShopSection) continue;
                if (it.Contents.Count > 0 && !RewardGate.AnyUsable(State, Content, it.Contents, Now)) continue; // rewards follow use
                var item = it;
                var row = Ui.Row(body, 90, 10, Theme.Panel, 12);
                Ui.Label(row, item.Name + "\n<size=22>" + Fmt.Rewards(item.Contents, Content, State, Now) + "</size>", Theme.FontSmall);
                Ui.Button(row, Fmt.Costs(item.Price, Content), () => Run(new GuildShopBuy { ItemId = item.Id }, _ => Rebuild()), Theme.Premium, 300, 76, State.Wallet.CanAfford(item.Price), Theme.FontSmall);
            }
        }
    }
}
