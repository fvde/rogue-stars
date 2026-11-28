import html, sys, os
sys.path.insert(0, os.path.dirname(__file__))
from data import *

E = html.escape
OUT = sys.argv[1]

KIND = {"D": ('<span class="pill pd">D · fly-in</span>'), "R": '<span class="pill pr">R · sheet</span>', "N": '<span class="pill pn">toast</span>'}
EST = {"art": '<span class="pill pd">reuse</span>', "def": '<span class="pill pb">reuse · art not imported</span>', "new": '<span class="pill pc">NEW</span>'}
BST = {"art": '<span class="pill pd">reuse</span>', "artonly": '<span class="pill pb">art approved · NEW def</span>',
       "redo": '<span class="pill pb">reuse · art redo</span>', "new": '<span class="pill pc">NEW</span>'}
RES_ICON = {"Biomass":"res-bio","Ore":"res-ore","Grain":"res-grain","Fuel":"res-fuel","Data":"res-data","Gear Cores":"res-gear","Hero XP":"res-xp"}

def fam_chip(f):
    n = FAMILIES[f][0]
    extra = " (NEW family)" if FAMILIES[f][3] == "new" else ""
    return f'<span class="fam f-{f}">{E(n)}{extra}</span>'

def weather_chips(bid):
    bo, bp, we, wp, _, _ = WEATHER[bid]
    return f'<span class="pill pd">{E(bo)} +{bp}%</span> <span class="pill pc">{E(we)} −{wp}%</span>'

def art_ref(a, biome=None):
    kind, aid = a.split(":")
    if aid in EXISTING_ART:
        return f'{"NPC" if kind=="npc" else "Prop"} <code>{E(aid)}</code> <span class="pill pd">reuse</span>'
    return f'{"NPC" if kind=="npc" else "Prop"} <code>{E(aid)}</code> <span class="pill pc">NEW · WIP</span>'

# ---- counts ------------------------------------------------------------------------------------------------
reg_reuse, reg_new, el_reuse, el_new, b_reuse, b_new = set(), set(), set(), set(), set(), set()
for b in BIOMES:
    for n, s, _ in b["enemies"]: (reg_new if s == "new" else reg_reuse).add(n)
    for n, s, _ in b["elites"]: (el_new if s == "new" else el_reuse).add(n)
    for n, s, _ in b["bosses"]: (b_new if s == "new" else b_reuse).add(n)
n_events = sum(len(b["events"]) for b in BIOMES)
res_count = {}
for b in BIOMES: res_count.setdefault(b["reward"][0], []).append(b["name"])

new_npc_props = []
for b in BIOMES:
    if b["npc"][0] not in EXISTING_ART: new_npc_props.append(b["npc"][0])
    for p in b["props"]:
        if p[0] not in EXISTING_ART: new_npc_props.append(p[0])

o = []
w = o.append
w('<title>Biome Identities</title>')
w('<meta name="viewport" content="width=device-width,initial-scale=1">')
w('<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>')
w('<link href="https://fonts.googleapis.com/css2?family=Fredoka:wght@500;600&family=Inter:wght@400;500;600&family=JetBrains+Mono&display=swap" rel="stylesheet">')
w("""<style>
:root{
 --bg:#f7f5fb; --surface:#ffffff; --sunk:#f0edf7; --fg:#1d1a2e; --muted:#5f5a75; --line:#dedaea; --accent:#5a3fc0;
 --d:#1f7a4d; --d-bg:#e2f4ea; --r:#8a5a00; --r-bg:#fbf0d6; --c:#a3343c; --c-bg:#f9e1e3; --n:#4b5563; --n-bg:#eceef2; --b:#2a5f9e; --b-bg:#e1ecf9;
 --ember:#c2410c; --ember-bg:#fde8dc; --glacier:#1d6fa8; --glacier-bg:#dcecf8; --static:#8a6d00; --static-bg:#f8efc8;
 --void:#7a3fb0; --void-bg:#efe3fa; --chrono:#8a5a2b; --chrono-bg:#f3e8da;
 --display:"Fredoka",system-ui,sans-serif; --body:"Inter",system-ui,sans-serif; --mono:"JetBrains Mono",ui-monospace,monospace;
}
@media (prefers-color-scheme: dark){:root:not([data-theme="light"]){
 --bg:#14121d; --surface:#1d1a2a; --sunk:#181624; --fg:#ebe8f5; --muted:#a6a1bd; --line:#34304a; --accent:#a893ff;
 --d:#6fd6a0; --d-bg:#18382a; --r:#f0c060; --r-bg:#3a2f14; --c:#f08a92; --c-bg:#3d1c20; --n:#c3c8d2; --n-bg:#2a2d36; --b:#8ab8f0; --b-bg:#1b2a40;
 --ember:#ff9b6a; --ember-bg:#3a1f12; --glacier:#7cc4f5; --glacier-bg:#14283a; --static:#f2d24a; --static-bg:#33300f;
 --void:#c79bf2; --void-bg:#2a1a3a; --chrono:#e0b27a; --chrono-bg:#33261a; color-scheme:dark}}
:root[data-theme="dark"]{
 --bg:#14121d; --surface:#1d1a2a; --sunk:#181624; --fg:#ebe8f5; --muted:#a6a1bd; --line:#34304a; --accent:#a893ff;
 --d:#6fd6a0; --d-bg:#18382a; --r:#f0c060; --r-bg:#3a2f14; --c:#f08a92; --c-bg:#3d1c20; --n:#c3c8d2; --n-bg:#2a2d36; --b:#8ab8f0; --b-bg:#1b2a40;
 --ember:#ff9b6a; --ember-bg:#3a1f12; --glacier:#7cc4f5; --glacier-bg:#14283a; --static:#f2d24a; --static-bg:#33300f;
 --void:#c79bf2; --void-bg:#2a1a3a; --chrono:#e0b27a; --chrono-bg:#33261a; color-scheme:dark}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font-family:var(--body);font-size:15px;line-height:1.5;
 padding:24px max(16px,env(safe-area-inset-right,0px)) 64px max(16px,env(safe-area-inset-left,0px))}
main{max-width:1120px;margin:0 auto;display:flex;flex-direction:column;gap:36px}
h1,h2,h3,h4{font-family:var(--display);font-weight:600;margin:0;text-wrap:balance}
h1{font-size:2rem} h2{font-size:1.45rem;border-bottom:2px solid var(--line);padding-bottom:6px}
h3{font-size:1.15rem} h4{font-size:.95rem;color:var(--muted);text-transform:uppercase;letter-spacing:.05em;font-family:var(--body);font-weight:600}
p{margin:0;max-width:78ch} section{display:flex;flex-direction:column;gap:14px;min-width:0}
.lede{color:var(--muted);font-size:1.02rem}
nav.toc{display:flex;flex-wrap:wrap;gap:6px}
nav.toc a{font-size:.82rem;padding:3px 10px;border-radius:99px;background:var(--surface);border:1px solid var(--line);color:var(--fg);text-decoration:none}
nav.toc a:hover{border-color:var(--accent)}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}
.tile{background:var(--surface);border:1px solid var(--line);border-radius:12px;padding:12px 14px}
.tile .n{font-family:var(--display);font-size:1.9rem;line-height:1.1;font-variant-numeric:tabular-nums}
.tile .l{font-size:.82rem;color:var(--muted)}
.wrap{overflow-x:auto;border:1px solid var(--line);border-radius:12px;background:var(--surface)}
table{border-collapse:collapse;width:100%;font-size:.88rem}
.wrap table{min-width:720px}
th,td{text-align:left;vertical-align:top;padding:8px 10px;border-bottom:1px solid var(--line)}
th{font-weight:600;font-size:.74rem;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);background:var(--sunk)}
tr:last-child td{border-bottom:0}
td.num{font-variant-numeric:tabular-nums;text-align:right}
code{font-family:var(--mono);font-size:.8em;background:var(--n-bg);padding:0 4px;border-radius:4px;word-break:break-word}
.pill{display:inline-block;font-size:.7rem;font-weight:600;padding:1px 8px;border-radius:99px;white-space:nowrap;vertical-align:1px}
.pd{background:var(--d-bg);color:var(--d)} .pr{background:var(--r-bg);color:var(--r)} .pc{background:var(--c-bg);color:var(--c)}
.pn{background:var(--n-bg);color:var(--n)} .pb{background:var(--b-bg);color:var(--b)}
.fam{display:inline-block;font-size:.74rem;font-weight:600;padding:1px 9px;border-radius:6px;white-space:nowrap}
.f-ember{background:var(--ember-bg);color:var(--ember)} .f-glacier{background:var(--glacier-bg);color:var(--glacier)}
.f-static{background:var(--static-bg);color:var(--static)} .f-void{background:var(--void-bg);color:var(--void)} .f-chrono{background:var(--chrono-bg);color:var(--chrono)}
.status{font-size:.7rem;font-weight:600;padding:1px 8px;border-radius:99px}
.live{background:var(--d-bg);color:var(--d)} .planned{background:var(--n-bg);color:var(--n)}
.yield{display:inline-flex;align-items:center;gap:6px;font-family:var(--display);font-weight:600;font-size:.95rem;padding:2px 12px;border-radius:8px;background:var(--sunk);border:1px solid var(--line);white-space:nowrap}
.card{background:var(--surface);border:1px solid var(--line);border-radius:16px;padding:18px;display:flex;flex-direction:column;gap:14px;scroll-margin-top:12px;border-top:5px solid var(--line)}
.card.c-ember{border-top-color:var(--ember)} .card.c-glacier{border-top-color:var(--glacier)} .card.c-static{border-top-color:var(--static)}
.card.c-void{border-top-color:var(--void)} .card.c-chrono{border-top-color:var(--chrono)}
.card-head{display:flex;flex-wrap:wrap;align-items:center;gap:8px 12px}
.card-head h3{font-size:1.4rem;margin-right:auto}
.idtag{font-family:var(--mono);font-size:.7rem;color:var(--muted)}
.ident{display:grid;grid-template-columns:110px 1fr;gap:4px 12px;font-size:.9rem}
.ident dt{color:var(--muted);font-weight:500} .ident dd{margin:0}
.cols{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:14px}
.box{background:var(--sunk);border-radius:12px;padding:12px 14px;display:flex;flex-direction:column;gap:8px;min-width:0}
ul.ros{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:6px;font-size:.88rem}
ul.ros li b{font-weight:600}
ul.ros .why{color:var(--muted)}
.events{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:12px}
.ev{border:1px solid var(--line);border-radius:12px;padding:12px 14px;display:flex;flex-direction:column;gap:8px;background:var(--surface);scroll-margin-top:12px}
.ev:target,.card:target{outline:3px solid var(--accent);outline-offset:2px}
.ev-h{display:flex;justify-content:space-between;gap:8px;align-items:baseline}
.ev-h b{font-family:var(--display);font-size:1.02rem;font-weight:600}
.setup{font-style:italic;color:var(--muted);font-size:.9rem}
.opts{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:6px}
.opts li{display:grid;grid-template-columns:1fr auto;gap:2px 8px;font-size:.87rem;padding:6px 8px;border-radius:8px;background:var(--sunk)}
.opts .btn{font-weight:600}
.opts .res{grid-column:1/2;color:var(--fg)}
.opts .k{grid-row:1/3;grid-column:2;align-self:center}
.artline{font-size:.8rem;color:var(--muted)}
.note{background:var(--surface);border:1px solid var(--line);border-left:4px solid var(--accent);border-radius:10px;padding:12px 14px;display:flex;flex-direction:column;gap:6px}
ol,ul.plain{margin:0;padding-left:1.3em;display:flex;flex-direction:column;gap:6px;max-width:84ch}
.q{scroll-margin-top:12px}
small.m{color:var(--muted)}
@media (max-width:520px){ .ident{grid-template-columns:1fr} .ident dt{margin-top:4px} h1{font-size:1.6rem} .card{padding:14px} }
</style>""")
w('<main>')

# ---- header ------------------------------------------------------------------------------------------------
w('<header style="display:flex;flex-direction:column;gap:10px">')
w('<h1>Biome Identities</h1>')
w('<p class="lede">Design draft for review: every campaign biome gets its own enemies, boss, a doubled resource and its own events, on top of a larger shared event pool. '
  '8 live biomes and 9 planned ones (they join once their ground art exists). Nothing here is in the game yet; every item has a stable id for comments.</p>')
w('<nav class="toc">' + "".join(f'<a href="#{i}">{t}</a>' for i, t in [("summary","Summary"),("display","Event display"),("decisions","Decided"),("unlocks","Unlock path"),("weather","Synergy weather"),("rites","Rites"),("biomes","Biomes"),("shared","Shared pool"),("rules","Rules of play"),("art","Art needed"),("questions","Open questions")]) + '</nav>')
w('</header>')

w('<div class="tiles">')
for n, l in [(len(BIOMES), "biomes (8 live, 9 planned)"), 
             (f"{len(reg_reuse)} + {len(reg_new)}", "regular enemies: reused + NEW"), (f"{len(el_reuse)} + {len(el_new)}", "elites: reused + NEW"),
             (f"{len(b_reuse)} + {len(b_new)}", "bosses: reused + NEW"), (n_events, "biome events (4 per biome)"),
             (len(RITES), "Rites (1 per biome)"), (len(SHARED_NEW), "NEW shared events"), (len(new_npc_props) + len(SHARED_NEW_ART), "NEW event art pieces")]:
    w(f'<div class="tile"><div class="n">{n}</div><div class="l">{E(l)}</div></div>')
w('</div>')

w('<section id="decisions"><h2>Decided in review round 1 (2026-11-28)</h2><div class="note"><ul class="plain">' + "".join(f'<li><b>{E(k)}:</b> {E(v)}</li>' for k, v in DECISIONS) + '</ul></div></section>')

# ---- summary -----------------------------------------------------------------------------------------------
w('<section id="summary"><h2>Summary</h2>')
w('<div class="wrap"><table><thead><tr><th class="num">From level</th><th>Biome</th><th>Status</th><th>Boss</th><th>Reward</th><th>Synergy weather</th><th class="num">Events</th><th class="num">NEW enemies / elites</th></tr></thead><tbody>')
_UA = {**{x: 1 for x in START_BIOMES}, **{bid: lv for lv, _, bid, _ in UNLOCKS}}
for b in sorted(BIOMES, key=lambda b: _UA[b["id"]]):
    bosses = " / ".join(f'{E(n)}{" <span class=\"pill pc\">NEW</span>" if s=="new" else ""}' for n, s, _ in b["bosses"])
    ne = sum(1 for _, s, _ in b["enemies"] if s == "new"); nel = sum(1 for _, s, _ in b["elites"] if s == "new")
    w(f'<tr><td class="num">{_UA[b["id"]]}</td><td><a href="#biome-{b["id"]}">{E(b["name"])}</a></td><td><span class="status {"live" if b["live"] else "planned"}">{"live" if b["live"] else "planned"}</span></td>'
      f'<td>{bosses}</td><td><b>×2 {E(b["reward"][0])}</b></td><td>{weather_chips(b["id"])}</td><td class="num">{len(b["events"])}</td><td class="num">{ne} / {nel}</td></tr>')
w('</tbody></table></div>')
w('<h4>Reward spread</h4><div class="wrap"><table><thead><tr><th>Resource</th><th class="num">Biomes</th><th>Which</th></tr></thead><tbody>')
for r in ["Biomass","Ore","Grain","Fuel","Gear Cores","Data","Hero XP"]:
    lst = res_count.get(r, [])
    w(f'<tr><td><b>{r}</b></td><td class="num">{len(lst)}</td><td>{E(", ".join(lst))}</td></tr>')
w('</tbody></table></div>')
w('<p><small class="m">Not used as a biome reward: Diamonds, Crates, Signals, Schematics (pull resources stay rare drops), Metal (comes only from feeding parts) and run coins (in-run only).</small></p>')
w('</section>')

# ---- display rules -----------------------------------------------------------------------------------------
w('<section id="display"><h2>How every event option shows its result</h2>')
w('<div class="note"><p>Per the event-outcome rework, each option below is tagged:</p>'
  '<ul class="plain"><li>' + KIND["R"] + ' <b>random</b>: the outcome is news, so the result sheet opens (chance bar sweeps, SUCCESS / NO LUCK, one line per gain or loss).</li>'
  '<li>' + KIND["D"] + ' <b>deterministic</b>: the button already says what happens; no sheet, the gains fly into the HUD (coins, HP, stat chips, chest to the pending bag). A fight starts the battle directly.</li>'
  '<li>' + KIND["N"] + ' <b>nothing</b>: "You move on." as a short toast.</li></ul>'
  '<p>Texts follow the house rules: trigger → effect → number, round numbers, one gain and at most one price per option. '
  '"X chest (paid on clear, +Danger)" is account loot and counts toward the 3 account-loot offers per run (<code>RunTuning.MaxAccountLootOffersPerRun</code>).</p></div>')
w('<p><small class="m">Every option uses an existing <code>RunEffectKind</code> (Coins, HealPct, DamagePct, StatPercent, GainSkill, Battle, PendingReward, BossStatPct, Meter, AwakenRatePct, StrengthenEnemies, Shards, Xp). A biome chest is a <code>PendingReward</code> of one resource. The only new piece is the Rite Offering (pick an owned skill of one synergy to remove), which reuses the quest sacrifice.</small></p>')
w('</section>')

# ---- weather -------------------------------------------------------------------------------------------------
w('<section id="weather"><h2>Synergy weather</h2>')
w('<p>Every biome boosts one synergy and weakens another (owner addition). It tells the player at a glance which build shines here, and the boosted synergy is usually the one the biome\'s enemies use, so the boost cuts both ways.</p>')
w('<h3>Rule</h3><ul class="plain">'
  '<li><b>What it scales:</b> the <b>damage</b> of that synergy only. Heat / Supernova: Fire damage and the Heat burst. Chill / Freeze: Ice damage. Shock / Arc: Lightning damage. Radiation: Radiation damage (DoT). Bleed: Bleed damage. Combo: combo hits. Counter: counter hits. Alien: alien ability damage. Time: Time skill damage. General: damage of skills with no synergy tag. Chances, stack caps, durations and Freeze / Delay / Stun turns stay as they are, so the text can stay one number.</li>'
  '<li><b>Numbers:</b> boost +25% everywhere. Weakened −25%, or −50% for a direct opposite (Chill in Lava Fields and Desert Ruins, Heat on the Seabed, Bleed in Clockwork City).</li>'
  '<li><b>Who:</b> the weakened synergy hits <b>the player only</b>, so a biome never feels easier because its own enemy status got cut. The boost also hits the enemies that use that synergy (named on each card), e.g. Ember enemies\' Heat in Lava Fields. Bosses count as enemies.</li>'
  '<li><b>Stacking (recommended):</b> a separate <b>final multiplier</b> (×1.25 or ×0.75 / ×0.5) applied after all additive bonuses from skills, parts, aliens, heroes and the Laboratory. Example: +40% Fire damage from skills and parts → ×1.4, then Lava ×1.25 → ×1.75. It can never go below zero, so no floor is needed. Alternative: add it to the same pool (+40% + 25% = +65%) with a floor of 25% of base damage; this is harder to read when the weakened synergy is the player\'s main build.</li>'
  '<li><b>Shown:</b> two chips on the portal card and the level-select portal (“Heat +25%” green, “Chill −50%” red), a banner at level start, and a small “+25% here” / “−50% here” badge on signature cards, skill drafts and shop skills whose tags match. In battle the damage numbers include it; the battle HUD keeps the two chips in a corner.</li>'
  '<li><b>Signature pick:</b> the level-start banner shows <b>before</b> the signature choice (from level 3), and the two signature cards carry the badge, so the player can lean in or hedge.</li>'
  '<li><b>Modes:</b> main campaign (Normal and Overcharged) only. Dark Side (ash wastes, already +45–90% enemy stats) and Burrow floors get no weather; a rotating Dark Side weather is a possible later twist. Invasions and the Arena: none.</li></ul>')
w('<h3>Spread</h3><div class="wrap"><table><thead><tr><th>Synergy</th><th class="num">Boosted</th><th>Where</th><th class="num">Weakened</th><th>Where</th></tr></thead><tbody>')
for syn in SYNERGIES:
    up = [b["name"] for b in BIOMES if WEATHER[b["id"]][0] == syn]
    dn = [b["name"] + " (−" + str(WEATHER[b["id"]][3]) + "%)" for b in BIOMES if WEATHER[b["id"]][2] == syn]
    w(f'<tr><td><b>{E(syn)}</b></td><td class="num">{len(up)}</td><td>{E(", ".join(up))}</td><td class="num">{len(dn)}</td><td>{E(", ".join(dn))}</td></tr>')
w('</tbody></table></div>')
w('<p><small class="m">Every synergy is boosted in 1–2 biomes and weakened in 1–2. No biome boosts the synergy it weakens, and no biome weakens the status its own enemies use.</small></p>')
w('</section>')


# ---- unlock path ---------------------------------------------------------------------------------------------
BN = {b["id"]: b for b in BIOMES}
w('<section id="unlocks"><h2>Unlock path: one signature and one biome at a time</h2>')
w('<p>Signatures unlock by campaign level, no longer through the Laboratory. Each unlock adds one synergy to the signature pick and one biome to the rotation, and the new biome <b>boosts the new synergy</b>, so the player meets a home for it on the next levels.</p>')
w('<div class="wrap"><table><thead><tr><th class="num">From level</th><th>New signature</th><th>New biome</th><th class="num">Rotation</th><th>Note</th></tr></thead><tbody>')
w('<tr><td class="num">1</td><td>' + E(", ".join(START_SIGNATURES)) + ' <small class="m">(pick from level 3)</small></td><td>' + E(", ".join(BN[i]["name"] for i in START_BIOMES)) + '</td><td class="num">4</td><td>Each start biome boosts one start synergy: Lava Fields Heat, Toxic Swamp Radiation, Shipwreck Bleed, Gas Giant Combo.</td></tr>')
for i, (lv, sig, bid, note) in enumerate(UNLOCKS, 1):
    b = BN[bid]
    st = '' if b["live"] else ' <span class="status planned">planned</span>'
    w(f'<tr><td class="num">{lv}</td><td>{E(sig) if sig else "—"}</td><td><a href="#biome-{bid}">{E(b["name"])}</a>{st} {weather_chips(bid)}</td><td class="num">{4+i}</td><td>{E(note)}</td></tr>')
w('</tbody></table></div>')
w('<h3>Rules</h3><ul class="plain">'
  '<li><b>Thresholds:</b> 10, 20, 35, 60, 90 as given, then spread to 500: 125, 160, 200, 245, 295, 350, 420, 500 (gaps grow from 35 to 80). Today\'s ceiling with perfect gear is about level 300, so the last 3 biomes wait for future power; that is intended.</li>'
  '<li><b>First visit:</b> the level right after an unlock is always the new biome (a “New biome” banner on its portal), then it joins the shuffle.</li>'
  '<li><b>Shuffle:</b> the seeded order shuffles the unlocked biomes in blocks of the rotation size; never the same biome twice in a row.</li>'
  '<li><b>Planned biomes</b> join when their art is in. If a planned biome is not ready at its level, its slot waits and the next ready one fills it; the signature unlocks on time regardless.</li>'
  '<li><b>Laboratory:</b> the six signature packages (Combo … Time) go, no replacement. The tree is restructured around the gap: the synergy skill nodes (Chain Strikes, Frost Nova, Overload, Knockout, Bulwark, Pack Tactics) hang off the neighbouring stat nodes and need the synergy\'s level unlock instead of the package. Combo moves into the start set: 4 start synergies for 4 start biomes.</li>'
  '<li><b>Weather early on:</b> with 4 start biomes that each boost a start synergy, weather can be on from level 1; the first penalty that bites a start synergy is the Seabed at level 35 (Heat −50%).</li></ul>')
w('</section>')


# ---- rites ---------------------------------------------------------------------------------------------------
KIND["O"] = '<span class="pill pb">Offering</span>'
w('<section id="rites"><h2>Rites: events that know the biome</h2>')
w('<p>Every biome has one Rite: an event that reaches into <b>this level\'s</b> enemies and boss. It plays against the weather: the biome\'s <b>weakened</b> synergy is what the land asks for.</p>')
w('<h3>How a Rite works</h3><ul class="plain">'
  '<li><b>' + KIND["O"] + ' Offering:</b> give up one owned run skill of the biome\'s weakened synergy (you pick which). The land answers with one thing for the rest of the level: boss −20% ATK, boss −20% HP, the boss awakens 25% slower, or nearby enemies −25%. Shown only if you own such a skill; the skill is gone (its levels too, like the existing sacrifice). A signature can\'t be offered.</li>'
  '<li><b>Second option:</b> a biome-specific deal with one gain and one price (HP, coins, or a stronger boss).</li>'
  '<li><b>Leave</b> is always there.</li>'
  '<li><b>Placement (decided):</b> the Rite is the 5th biome event, with double weight, and only on event tiles in the first half of the board, so its effect still has a boss to matter for. About 1 visit in 3 meets it. Cost: the offered skill, nothing more.</li>'
  '<li><b>Shown:</b> like every boss modifier today (boss ATK / HP %, awakening speed); no new art.</li></ul>')
w('<div class="events">')
for b in sorted(BIOMES, key=lambda b: (not b["live"], b["name"])):
    bid = b["id"]; name, setup, art, opts = RITES[bid]
    eid = f"rite-{bid}"
    w(f'<div class="ev" id="{eid}"><div class="ev-h"><b>{E(b["name"])}: {E(name)}</b><span class="idtag">#{eid}</span></div>')
    w(f'<div class="setup">{E(setup)}</div><ul class="opts">')
    for btn, res, k in opts:
        w(f'<li><span class="btn">{E(btn)}</span><span class="k">{KIND[k]}</span><span class="res">{E(res)}</span></li>')
    w('</ul>')
    w(f'<div class="artline">Weather {weather_chips(bid)} · Art: {art_ref(art)}</div></div>')
w('</div>')
w('<h3>More directions for biome-aware events</h3><ul class="plain">' + "".join(f'<li><b>{E(k)}:</b> {E(v)}</li>' for k, v in RITE_IDEAS) + '</ul>')
w('</section>')

# ---- biome cards -------------------------------------------------------------------------------------------
w('<section id="biomes"><h2>Biomes</h2>')
w('<p class="lede">In unlock order. Stats of NEW enemies sit on the standard budget of their role (regular, elite, boss: HP × ATK within a few %), like the elites added in playtest 0.1.359.</p>')

def needs_new_effect(t):
    tags = []
    return tags

UNLOCK_AT = {**{i: 1 for i in START_BIOMES}, **{bid: lv for lv, _, bid, _ in UNLOCKS}}
order = sorted(BIOMES, key=lambda b: UNLOCK_AT[b["id"]])
for b in order:
    bid = b["id"]
    w(f'<article class="card" id="biome-{bid}">')
    w(f'<div class="card-head"><h3>{E(b["name"])}</h3><span class="status {"live" if b["live"] else "planned"}">{"live" if b["live"] else "planned"}</span>'
      f'<span class="yield">×2 {E(b["reward"][0])}</span><span class="pill pn">from level {UNLOCK_AT[bid]}</span><span class="idtag">#biome-{bid}</span></div>')
    w('<dl class="ident">'
      f'<dt>Fantasy</dt><dd>{E(b["fantasy"])}</dd><dt>Mood</dt><dd>{E(b["palette"])}</dd>'
      f'<dt>Gameplay</dt><dd>{E(b["flavour"])}</dd>'
      f'<dt>Reward</dt><dd><b>×2 {E(b["reward"][0])}</b>: {E(b["reward"][1])} Portal card chip “×2 {E(b["reward"][0])}”.</dd>'
      f'<dt>Synergy weather</dt><dd>{weather_chips(bid)} <small class="m">Boost also applies to: {E(WEATHER[bid][4])}. Weakened: you only. {E(WEATHER[bid][5])}</small></dd>'
      f'<dt>Hazard</dt><dd>{E(b["hazard"])} <small class="m">(optional)</small></dd></dl>')
    w('<div class="cols">')
    w('<div class="box"><h4>Enemies</h4><ul class="ros">')
    for n, s, why in b["enemies"]:
        w(f'<li><b>{E(n)}</b> {EST[s]} <span class="why">{E(why)}</span></li>')
    w('</ul><h4>Elites (2: the two lairs per run must differ)</h4><ul class="ros">')
    for n, s, why in b["elites"]:
        w(f'<li><b>{E(n)}</b> {EST[s]} <span class="why">{E(why)}</span></li>')
    w('</ul></div>')
    w('<div class="box"><h4>Boss' + ("es (2 variants, alternate per visit)" if len(b["bosses"]) > 1 else "") + '</h4><ul class="ros">')
    for n, s, hook in b["bosses"]:
        w(f'<li><b>{E(n)}</b> {BST[s]}<br><span class="why">{E(hook)}</span></li>')
    w('</ul><h4>Event art</h4><ul class="ros">')
    npc = b["npc"]
    w(f'<li><b>{E(npc[1])}</b> {"<span class=\"pill pd\">reuse</span>" if npc[0] in EXISTING_ART else "<span class=\"pill pc\">NEW · WIP</span>"} <span class="why">local character</span></li>')
    for pid, pn, look in b["props"]:
        w(f'<li><b>{E(pn)}</b> {"<span class=\"pill pd\">reuse</span>" if pid in EXISTING_ART else "<span class=\"pill pc\">NEW · WIP</span>"} <span class="why">prop</span></li>')
    w('</ul></div></div>')
    w('<h4>Biome events</h4><div class="events">')
    for i, (name, setup, art, opts) in enumerate(b["events"], 1):
        eid = f"ev-{bid}-{i}"
        newfx = sorted({t for _, r, _ in opts for t in needs_new_effect(r)})
        w(f'<div class="ev" id="{eid}"><div class="ev-h"><b>{E(name)}</b><span class="idtag">#{eid}</span></div>')
        w(f'<div class="setup">{E(setup)}</div><ul class="opts">')
        for btn, res, k in opts:
            w(f'<li><span class="btn">{E(btn)}</span><span class="k">{KIND[k]}</span><span class="res">{E(res)}</span></li>')
        w('</ul>')
        w(f'<div class="artline">Art: {art_ref(art)}' + (f' · new effect: {E(", ".join(newfx))}' if newfx else '') + '</div></div>')
    w(f'</div><p><small class="m">Rite: <a href="#rite-{bid}">{E(RITES[bid][0])}</a></small></p></article>')
w('</section>')

# ---- shared pool ---------------------------------------------------------------------------------------------
w('<section id="shared"><h2>Shared event pool</h2>')
w('<h3>Mix rule</h3>')
w('<div class="note"><ul class="plain">'
  '<li>Every <b>Strange Signal</b> event tile: <b>40%</b> a biome event of this level\'s biome, <b>60%</b> a shared event (by weight).</li>'
  '<li>A run has ~2 event tiles (event weight 8 of ~87 pool weight, ~23 rolls), so <b>≈ 1 biome event per run</b>; a player meets all 4 of a biome in ~4 visits.</li>'
  '<li>No event repeats within a run. The 4 biome events have equal weight. Once all 4 are used up the tile draws from the shared pool.</li>'
  '<li>Treasure Pod and Campfire stay tile-bound and are not part of the mix.</li></ul></div>')
w('<h3>Current shared events (live today)</h3>')
w('<div class="wrap"><table><thead><tr><th>Id</th><th>Name</th><th class="num">Weight</th><th>Art</th><th>Proposal</th></tr></thead><tbody>')
for i, nm, wt, art, note in CURRENT_EVENTS:
    w(f'<tr><td><code>{i}</code></td><td>{E(nm)}</td><td class="num">{wt}</td><td><code>{art}</code></td><td>{E(note)}</td></tr>')
w('</tbody></table></div>')
w(f'<h3>{len(SHARED_NEW)} NEW shared events</h3><p>Each pushes one synergy (or none) so every build finds something on any biome. Suggested weights in brackets (current events weigh 2–4).</p>')
w('<div class="events">')
for i, (name, setup, syn, art, opts, wt) in enumerate(SHARED_NEW, 1):
    eid = f"ev-shared-{i}"
    newfx = sorted({t for _, r, _ in opts for t in needs_new_effect(r)})
    w(f'<div class="ev" id="{eid}"><div class="ev-h"><b>{E(name)}</b><span class="idtag">#{eid}</span></div>')
    w(f'<div class="setup">{E(setup)}</div><ul class="opts">')
    for btn, res, k in opts:
        w(f'<li><span class="btn">{E(btn)}</span><span class="k">{KIND[k]}</span><span class="res">{E(res)}</span></li>')
    w('</ul>')
    w(f'<div class="artline">Synergy: {E(syn)} · weight {wt} · Art: {art_ref(art)}' + (f' · new effect: {E(", ".join(newfx))}' if newfx else '') + '</div></div>')
w('</div></section>')

# ---- rules of play -------------------------------------------------------------------------------------------
w('<section id="rules"><h2>Rules of play</h2>')
w('<h3 id="rule-yield">Biome reward (×2)</h3><ul class="plain">'
  '<li><b>What doubles:</b> every amount of the biome\'s resource that level pays: its completion line, its share of the 3 completion loot rolls and of every reward chest. Nothing else changes.</li>'
  '<li><b>Never a zero ×2:</b> Grain, Fuel, Hero XP and Gear Cores are only loot rolls today, and main levels pay no Data at all. A biome with such a resource adds <b>one guaranteed roll</b> of it to the completion, then doubles it. Data gets a new roll entry for its biomes (Data 10–20 × chapter scale).</li>'
  '<li><b>Biome chest</b> (events): one doubled roll of the resource, e.g. at chapter 1 Fuel 20–40, Ore 40–80; scales with the chapter like all loot, rounded (<code>RunSystem.NiceAmount</code>).</li>'
  '<li><b>Shown:</b> portal card chip “×2 Ore” next to the biome name; level select portal shows the same chip; result screen line “Ore 100 (×2 biome)”.</li>'
  '<li><b>Never doubled by the biome:</b> Diamonds, Crates, Signals, Schematics, milestone and bonus chests, the first-clear part, feat Diamonds.</li></ul>')
w('<div class="wrap"><table><thead><tr><th>Chapter 1, step 1 (scale 1)</th><th>Normal level</th><th>Biome level</th></tr></thead><tbody>'
  '<tr><td>Biomass (completion)</td><td class="num">150</td><td class="num">300</td></tr>'
  '<tr><td>Ore (completion)</td><td class="num">50</td><td class="num">100</td></tr>'
  '<tr><td>Grain (guaranteed roll)</td><td class="num">— (loot roll 20–40, 14% weight)</td><td class="num">40–80</td></tr>'
  '<tr><td>Fuel (guaranteed roll)</td><td class="num">— (loot roll 10–20, 8%)</td><td class="num">20–40</td></tr>'
  '<tr><td>Hero XP (guaranteed roll)</td><td class="num">— (loot roll 15–30, 8%)</td><td class="num">30–60</td></tr>'
  '<tr><td>Gear Cores (guaranteed roll)</td><td class="num">— (loot roll 1–2, 6%)</td><td class="num">2–4</td></tr>'
  '<tr><td>Data (NEW roll)</td><td class="num">—</td><td class="num">20–40</td></tr></tbody></table></div>')
w('<h3>With chapter rewards and the Biomass surge</h3><ul class="plain">'
  '<li>Chapters keep the rhythm: reward scale, Crate every 5th step, milestone chest every 10th level, stamina, dice. The biome only picks <b>which</b> resource is doubled.</li>'
  '<li><b>Biomass surge</b> (first 3 won runs a day): <b>×2 instead of ×4</b> (decided). It multiplies after the biome, so a Biomass biome on a surge run pays ×4. This halves today\'s surge Biomass on non-Biomass biomes: the simulator run below has to re-check Biomass pacing, and the lost surge income may need to move into base Biomass.</li>'
  '<li>Overcharged ×1.8, run-reward modifiers (planetary events) and the double-reward ad apply on top, as today.</li>'
  '<li><b>Economy impact:</b> a resource in k biomes gains ~k/17 of its per-level income (Biomass, Ore, Fuel +18%; the rest +12%), plus the guaranteed rolls. Before shipping: one simulator run (<code>tools/RogueStars.Sim</code>, runs-per-upgrade table in economy-model.md).</li></ul>')
w('<h3>Bosses</h3><ul class="plain">'
  '<li>One boss per biome. Biomes with 2 variants (Lava Fields, Ice Shelf, Shipwreck) <b>alternate per visit</b>: the biome\'s 1st, 3rd, 5th… appearance in the seeded order uses A, the 2nd, 4th… B. Deterministic, same for every player.</li>'
  '<li>From level 501 the existing endless titles (Elder, Hollow, Radiant, Ashen, Ascended, Feral) and boss-tile combos keep rotating every 5 chapters on top of the biome boss.</li>'
  '<li>NEW bosses sit on the standard boss budget (HP × ATK within ~5%), get the standard awakening actions (25/50/75/100) and 3 boss tiles named for them (feed / relief / tribute, the same numbers for every boss). From level 501 the endless boss variants borrow another boss\'s tiles instead of another family\'s.</li>'
  '<li>Rust Queen, Gloop Matriarch, Hollow Cantor and Prism Tyrant are invasion bosses today; as biome bosses they get a board kit (awakening, tiles, escort). Invasions keep using them (decided).</li></ul>')
w('<h3>Difficulty</h3><ul class="plain">'
  '<li>Stays keyed by the level number: threat curve, early-level ramp, chapter scale, level-unlocked run content (signature pick, minibosses, forks, campfires).</li>'
  '<li><b>No biome offsets:</b> difficulty comes only from the level scaling. The current family offsets (Void −25%, Glacier −20% from level 401) go; those enemies are retuned to the standard role budget instead.</li>'
  '<li>Level 1 stays Lava Fields (tutorial pool). Adding biomes reshuffles the order for everyone; pre-release, no migration.</li>'
  '<li>Biome hazards are reskins of the 3 existing hazards (same numbers): only their name and tile art change.</li></ul>')
w('</section>')

# ---- art table -----------------------------------------------------------------------------------------------
w('<section id="art"><h2>Art needed</h2>')
w('<p>All NEW subjects with a one-line look for the art session (enemies face LEFT, bold outline, flat colours; no text). Event art is WIP: busts for characters, props as standalone objects like <code>event_wreck</code>.</p>')
w('<div class="wrap"><table><thead><tr><th>Biome</th><th>Kind</th><th>Subject</th><th>Look</th></tr></thead><tbody>')
seen = set()
def row(bname, kind, subj, look):
    w(f'<tr><td>{E(bname)}</td><td>{kind}</td><td><b>{E(subj)}</b></td><td>{E(look)}</td></tr>')
for b in BIOMES:
    for n, s, why in b["enemies"]:
        if s == "new" and n not in seen: seen.add(n); row(b["name"], "Enemy", n, why)
    for n, s, why in b["elites"]:
        if s == "new" and n not in seen: seen.add(n); row(b["name"], "Elite", n, why)
    for n, s, hook in b["bosses"]:
        if s == "new" and n not in seen: seen.add(n); row(b["name"], "Boss", n, hook)
    if b["npc"][0] not in EXISTING_ART: row(b["name"], "Event NPC", b["npc"][1], b["npc"][2])
    for pid, pn, look in b["props"]:
        if pid not in EXISTING_ART: row(b["name"], "Event prop", pn, look)
for k, (kind, nm, look) in SHARED_NEW_ART.items():
    row("Shared", "Event NPC" if kind == "npc" else "Event prop", nm, look)
w('</tbody></table></div>')
w('<h3>Reused (no new art)</h3><ul class="plain">'
  '<li><b>Enemies in game:</b> Ember Imp, Magma Slug, Cinder Hound, Ash Wraith, Frost Mite, Ice Crawler, Glacier Golem, Spark Drone, Volt Beetle, Static Eel, Void Spawn, Null Shade, Tunnel Stalker, Burrow Grub.</li>'
  '<li><b>Elites in game:</b> Lava Brute, Cinder Alpha, Ash Matriarch, Frost Knight, Glacier Warden, Storm Caller, Void Reaver, Rift Echo, Burrow Horror.</li>'
  '<li><b>Defs without imported art</b> (candidates in <code>~/AI/assets</code>, need import): Snow Lurker, Thunder Moth, Lurker Alpha, Volt Monarch, Eel Tyrant (redo), Null Wraith, Grub Mother. Stalker Prime has art but no biome here (stays Burrow).</li>'
  '<li><b>Bosses in game:</b> Magma Tyrant, Frost Leviathan, Storm Hive Queen, Void Maw, Rust Queen, Gloop Matriarch, Prism Tyrant. <b>Approved, no def:</b> Elder Magma, Chrono Wyrm. <b>Needs redo:</b> Hollow Cantor (cut off at the edge).</li>'
  '<li><b>Event art in game:</b> props <code>event_wreck</code>, <code>abandoned_cart</code>, <code>event_shrine</code>; characters <code>black_marketeer</code> (Neon City dealer), <code>cursed_vendor</code> (Swamp Witch), <code>shopkeeper</code> (Wandering Trader).</li></ul>')
w('</section>')

# ---- open questions -------------------------------------------------------------------------------------------
QS = [
 ("q-weather-stack","Weather as a separate final multiplier (recommended) or added to the skill / part pool with a 25% floor?"),
 ("q-yield","Biome reward for Grain / Fuel / Hero XP / Gear Cores: “guaranteed roll ×2” (recommended) or a flat bonus line shown as “+80 Grain”?"),
 ("q-data","Should main levels pay Data at all (Space Station, Neon City)? Today Data is the Dark Side's job; the alternative is Hero XP or Gear Cores there."),
 ("q-scope","Art scope: ~{} NEW enemies/elites, {} NEW bosses, {} NEW event NPCs/props. Phasing follows the unlock path: the 8 live biomes cover levels 1–89, Clockwork City is needed by level 90.".format(len(reg_new)+len(el_new), len(b_new), len(new_npc_props)+len(SHARED_NEW_ART))),
 ("q-mix","Biome-event share 40% per event tile (≈1 biome event per run): more, less?"),
 ("q-spores","Move Strange Spores from the shared pool to Mushroom Forest (it is a biome event in spirit)?"),
 ("q-elites","Elites: 2 per biome minimum (two lairs per run must differ). Do you want a 3rd biome-only elite for the live biomes?"),
 ("q-texts","Several live shared events break the one-thing rule (Abandoned Cache lists 4 effects). Rewrite them in the same pass?"),
]
w('<section id="questions"><h2>Open questions</h2><ol>')
for qid, q in QS:
    w(f'<li class="q" id="{qid}">{E(q)} <span class="idtag">#{qid}</span></li>')
w('</ol></section>')
w('<p><small class="m">Sources: ContentDb.Run.cs, ContentDb.Campaign.cs, RunDefs.cs (CampaignDef), docs/design/GDD.md §3, economy-model.md, client Art/Enemies and Run/Props + Characters, RunScenes.Look, docs/art/art-session.md. Draft 2026-11-28.</small></p>')
w('</main>')
open(OUT, "w").write("\n".join(o))
print("ok", len("\n".join(o)), "bytes; regular", len(reg_reuse), len(reg_new), "elites", len(el_reuse), len(el_new), "bosses", len(b_reuse), len(b_new), "events", n_events, "shared new", len(SHARED_NEW), "new event art", len(new_npc_props), "+", len(SHARED_NEW_ART))
print({k: len(v) for k, v in res_count.items()})
