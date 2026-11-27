import sys, re, html, collections
sys.path.insert(0, sys.argv[1])
from data import *
import check

OUT = sys.argv[2]
esc = html.escape

def rnd(x):
    if x < 20: return int(x + 0.5)
    return int(round(x / 5.0)) * 5

def stats_for(slot, role, rar):
    d = collections.OrderedDict()
    for k, v in CORE[slot]: d[k] = d.get(k, 0) + v
    if role:
        k, v = ROLE_STAT[role]; d[k] = d.get(k, 0) + v
        if slot == 'Arm' and role in ARM_ROLE_STAT:
            k, v = ARM_ROLE_STAT[role]; d[k] = d.get(k, 0) + v
    return d

def fmt_stats(d, mult, extra=None):
    out = []
    for k, v in d.items():
        val = rnd(v * mult)
        if k.endswith('%'): out.append(f'+{val}% {k[:-2]}')
        else: out.append(f'+{val} {k}')
    if extra: out.append(extra)
    return ', '.join(out)

GROWTH = 0.15  # flat stats (HP, ATK, DEF, speed): +15% of the level-1 value per level
PCT_MAX = 3     # percentage stats grow linearly from their level-1 value to 3x at the part's max level (owner rev 3h)
MYTHIC_CAP = {0: 70, 1: 75, 2: 80, 3: 85, 4: 90, 5: 95, 6: 100}   # level cap per Mythic ladder step; 100 at SS (final)
def lvl_stats(d, mult, extra=None):
    out = []
    for k, v in d.items():
        out.append((k, rnd(v * mult)))
    if extra:
        m = re.match(r'\+(\d+)(%?) (.+)', extra)
        out.append((m.group(3) + (' %' if m.group(2) else ''), int(m.group(1))))
    return out
def lab(k): return f'% {k[:-2]}' if k.endswith('%') else f' {k}'
def flat_step(base): return rnd(base * GROWTH)
def at_cap(k, base, cap):
    return base * PCT_MAX if k.endswith('%') else base + (cap - 1) * flat_step(base)
def fmt_lvl(slot, mult, role=None, extra=None, cap=None):
    """Per level: flat stats as +N each level; percentage stats as level-1 → 3x at the cap."""
    out = []
    for k, b in lvl_stats(stats_for(slot, role, None), mult, extra):
        if k.endswith('%'): out.append(f'+{b}{lab(k)} → +{at_cap(k, b, cap)}% by level {cap}')
        else: out.append(f'+{flat_step(b)}{lab(k)}')
    return ', '.join(out)
def fmt_max(slot, mult, role=None, extra=None, cap=None):
    return ', '.join(f'+{grp_n(at_cap(k, b, cap))}{lab(k)}' for k, b in lvl_stats(stats_for(slot, role, None), mult, extra))
def grp_n(n): return f'{n:,}'.replace(',', ' ')

def item_name(rar, slot, role):
    noun = ARM_NOUN[role] if slot == 'Arm' else SLOT_NOUN[slot]
    return f'{TIER[rar]} {role} {noun}'

def iid(prefix, slot, role): return f'{prefix}-{slot.lower()}-{role.lower()}'
def mid(m): return 'm-' + m['id'][5:].replace('_', '-')

CC_RE = re.compile(check.CC, re.I)
SYN_CLASS = {'Fire': 'fire', 'Ice': 'ice', 'Lightning': 'light', 'Radiation': 'rad', 'Bleed': 'bleed', 'Combo': 'combo',
             'Counter': 'counter', 'Alien': 'alien', 'Time': 'time', 'General': 'gen'}
SYN_ORDER = ['Fire', 'Ice', 'Lightning', 'Radiation', 'Bleed', 'Combo', 'Counter', 'Alien', 'Time', 'General']
SYN_PACK = {'Fire': 'start', 'Radiation': 'start', 'Bleed': 'start', 'Combo': 'Lab package', 'Ice': 'Lab package', 'Lightning': 'Lab package',
            'Counter': 'Lab package', 'Alien': 'Lab package', 'Time': 'Lab package', 'General': 'any build'}

# engine needs: requirement -> [(label, anchor)]
ENG = collections.OrderedDict()
FAMILIES = [
 ('Crowd control once per enemy per battle on a trigger (a per-trigger MaxPerTarget; bosses lose 20 rage instead). Today only ApplyStatus cd:N exists and it blocks the status from every source.', r'Crowd control'),
 ('Extra turn as an effect with a per-battle count (today extra_turn is a passive rule).', r'ExtraTurn'),
 ('Sure crit on an attack (basic, rage, counter, Echo, every Nth hit); today only skill damage has the crit token.', r'Sure crit'),
 ('Guaranteed dodge / guard on the worm: next N attacks miss, the worm guards itself, a guard that stops the whole hit, Drone guards.', r'Guaranteed dodge|Guard \(next hit|Guard value|Summon guard'),
 ('Trigger OnShieldBreak (an OnShieldHit that empties the shield).', r'OnShieldBreak'),
 ('Named-status moves: SpreadStatus for one status with a cap, TransferStatus for Heat / Bleed, consumed stacks re-applied elsewhere, a rule that doubles spreads.', r'SpreadStatus|TransferStatus|re-applied|ApplyStatus per_unit|double every spread'),
 ('If gate on passive rules (no_expire, dot, counter_all-style area counters, basic_aoe, RageMax); today If gates triggers and cond: bonuses only.', r'If gate|gated by self_hp_below'),
 ('Owner rule overrides: damage cap per hit, RageMax, SidekickActAt, combo multiplier, stat counts as another stat, crit overflow → crit damage, act first from round N, round-1 stat multiplier, one-turn basic_aoe.', r'damage cap|RageMax|SidekickActAt|combo multiplier|counts as another|overflow crit|act first|stat multiplier in round|basic_aoe for one'),
 ('Targeting and attack-shape rules: rage attack / combo strike targeting, combo chain continues after a kill, rage attack fired as a counter, rage attack area gated by the target\'s status, Echo area token.', r'targeting|chain continues|fired as a counter|area gated|Echo area'),
 ('New conditions: attacker slower than you, hit above N% max HP, incoming crit, incoming / countered rage attack, Supernova kill, Supernova caused by a rage attack, distinct enemies hit, count per target.', r'Condition|Count per target'),
 ('New triggers: OnEcho, a hit was undone (Rewind), own Drone broke, per-tick OnStatusDamage → ApplyStatus.', r'OnEcho|hit was undone|Drone broke|OnStatusDamage per tick'),
 ('Scaled amounts: heal / shield / rage / stacks per round number, living enemy, stack on the target, enemy with a status, total stacks, dying enemy\'s stacks, owner stat, max HP, missing HP, overkill; defensive conditionals vs the attacker\'s status.', r'per round number|scaled by the round|per living|per status stack|per enemy carrying|per total stacks|dying enemy|scaled by an owner stat|scaled on the owner|missing HP|overkill|Defensive conditional|Conditional damage taken|Shield of_amount on OnStatusConsumed'),
 ('Buffs and debuffs: BuffStat that ends on the owner\'s crit / dodge, HealReceived until_turn, until_turn / stacking debuffs and negative rage on an enemy, next-attack riders (area echo, ApplyStatus), Charge for the next rage attack.', r'BuffStat|Negative GainRage|combo_no_decay|Charge for the next rage|Charge \(exists'),
 ('DEF ignore on an attack or on crits.', r'DEF ignore'),
 ('Supernova recoil rules: no_recoil for Supernovas a dodge forced.', r'no_recoil'),
 ('Combo mechanics: forced strike count, strikes that ramp up (negative combo_falloff), a Strike that can combo.', r'Forced combo|combo_falloff|combo token|Turn counter offset'),
 ('Relative rule modifiers (rev 3d): Supernova threshold −N, RageMax −N, SidekickActAt −N, MaxDodge +N vs a status, each with a floor / ceiling; "within N of" conditions; shield multiply with a cap; +N Arc jumps; target the enemy with the most stacks.', r'−N|\+N|within N|Shield multiply|most stacks'),
 ('Small extensions (verify): Arc jumps roll crit, ConsumeStatus on all enemies for Shock, AllyAction repeat / filtered to Drones, Rewind per enemy and reflect of the full amount, target with the fewest stacks.', r'Arc jumps roll|ConsumeStatus on all|AllyAction|Rewind|fewest stacks'),
]
ENG_FAM = collections.OrderedDict((f, []) for f, _ in FAMILIES)
UNMATCHED = []
def eng_add(e, label, anchor):
    hit = False
    for f, rx in FAMILIES:
        if re.search(rx, e):
            ENG_FAM[f].append((label, anchor, e)); hit = True
    if not hit: UNMATCHED.append(e)
    ENG.setdefault(e, []).append((label, anchor))

def skill_flags(t, eng, label, anchor):
    flags = ''
    if CC_RE.search(t): flags += '<span class="flag cc" title="crowd control, once per enemy per battle">CC</span>'
    if eng:
        flags += f'<span class="flag eng" title="{esc(eng)}">engine</span>'
        eng_add(eng, label, anchor)
    note = f'<div class="engnote">engine: {esc(eng)}</div>' if eng else ''
    return flags, note

def skill_li(t, eng, tier=None, label='', anchor='', awk=None, aw_label=''):
    tags = []
    if tier: tags.append(f'<span class="lad lad-{tier.replace(" ", "").lower()}">{esc(tier)}</span>')
    flags, note = skill_flags(t, eng, label, anchor)
    aw = ''
    if awk:
        at, ae = awk
        af, an = skill_flags(at, ae, aw_label, anchor)
        aw = f'<div class="awk"><span class="lad lad-awakened">Awakened</span><span class="sk">{esc(at)}</span>{af}{an}</div>'
    return f'<li>{"".join(tags)}<span class="sk">{esc(t)}</span>{flags}{note}{aw}</li>'

parts = []
P = parts.append

CSS = r'''
:root{--bg:#f6f5f1;--panel:#ffffff;--panel2:#f0eee8;--ink:#1d1f24;--muted:#5d6270;--line:#dedad0;--accent:#3b5bdb;--accent-ink:#ffffff;
--cc:#c2255c;--eng:#9c6500;--eng-bg:#fff3d6;--cc-bg:#ffe3ec;--ok:#2b8a3e;
--c-common:#6b7280;--c-rare:#1c7ed6;--c-epic:#8e44c9;--c-leg:#d9480f;--c-myth:#c2255c;
--fire:#e8590c;--ice:#1c9ad6;--light:#c99400;--rad:#5c940d;--bleed:#c92a2a;--combo:#7048e8;--counter:#495057;--alien:#0c8599;--time:#a61e4d;--gen:#343a40;}
@media (prefers-color-scheme: dark){:root:not([data-theme="light"]){--bg:#14161b;--panel:#1c1f26;--panel2:#232731;--ink:#e7e8ec;--muted:#a0a6b4;--line:#323744;--accent:#7b93ff;--accent-ink:#0d0f14;
--cc:#ff7aa2;--eng:#ffc552;--eng-bg:#3a2e12;--cc-bg:#3d1a27;--ok:#69db7c;
--c-common:#adb5bd;--c-rare:#4dabf7;--c-epic:#c58af0;--c-leg:#ff8a4c;--c-myth:#ff7aa2;
--fire:#ff8c42;--ice:#4fc3f7;--light:#ffd43b;--rad:#94d82d;--bleed:#ff6b6b;--combo:#9775fa;--counter:#ced4da;--alien:#3bc9db;--time:#f06595;--gen:#dee2e6;}}
:root[data-theme="dark"]{--bg:#14161b;--panel:#1c1f26;--panel2:#232731;--ink:#e7e8ec;--muted:#a0a6b4;--line:#323744;--accent:#7b93ff;--accent-ink:#0d0f14;
--cc:#ff7aa2;--eng:#ffc552;--eng-bg:#3a2e12;--cc-bg:#3d1a27;--ok:#69db7c;
--c-common:#adb5bd;--c-rare:#4dabf7;--c-epic:#c58af0;--c-leg:#ff8a4c;--c-myth:#ff7aa2;
--fire:#ff8c42;--ice:#4fc3f7;--light:#ffd43b;--rad:#94d82d;--bleed:#ff6b6b;--combo:#9775fa;--counter:#ced4da;--alien:#3bc9db;--time:#f06595;--gen:#dee2e6;}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
main{max-width:1180px;margin:0 auto;padding:20px 16px 80px}
h1{font-size:28px;margin:8px 0 4px}h2{font-size:22px;margin:36px 0 10px;padding-top:8px;border-top:2px solid var(--line)}
h3{font-size:17px;margin:22px 0 8px}h4{margin:0;font-size:15px}
p{margin:6px 0}.muted{color:var(--muted)}small{color:var(--muted)}
a{color:var(--accent)}
.box{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:14px 16px;margin:12px 0}
.box ul{margin:6px 0;padding-left:20px}.box li{margin:3px 0}
.model{border-left:5px solid var(--accent)}
nav.jump{position:sticky;top:0;z-index:5;background:var(--bg);padding:8px 0;display:flex;flex-wrap:wrap;gap:6px;border-bottom:1px solid var(--line)}
nav.jump a{text-decoration:none;font-size:13px;padding:3px 9px;border-radius:999px;border:1px solid var(--line);background:var(--panel);color:var(--ink)}
.tw{overflow-x:auto;-webkit-overflow-scrolling:touch;margin:8px 0;border:1px solid var(--line);border-radius:8px;background:var(--panel)}
table{border-collapse:collapse;width:100%;font-size:14px}
th,td{overflow-wrap:anywhere;padding:6px 9px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}
th{background:var(--panel2);font-weight:600;white-space:nowrap}
tr:target,.card:target{outline:3px solid var(--accent);outline-offset:2px}
.rar{font-weight:600;font-size:12px;text-transform:uppercase;letter-spacing:.04em}
.r-common{color:var(--c-common)}.r-rare{color:var(--c-rare)}.r-epic{color:var(--c-epic)}.r-leg{color:var(--c-leg)}.r-myth{color:var(--c-myth)}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(270px,1fr));gap:10px}
.mgrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:12px}
.card{overflow-wrap:anywhere;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:10px 12px;min-width:0}
.card.epic{border-top:4px solid var(--c-epic)}.card.leg{border-top:4px solid var(--c-leg)}.card.myth{border-top:4px solid var(--c-myth)}
.card header{display:flex;justify-content:space-between;gap:8px;align-items:baseline;flex-wrap:wrap}
.card .id{font:12px ui-monospace,Menlo,monospace;color:var(--muted);text-decoration:none}
.card .st{font-size:13px;color:var(--muted);margin:3px 0}
.card ul{list-style:none;margin:6px 0 0;padding:0}.card li{margin:5px 0;padding-left:0}
.sk{font-weight:500}
.ident{font-size:14px;margin:4px 0 6px;padding:6px 8px;border-radius:6px;background:var(--panel2)}
.chip{display:inline-block;font-size:12px;padding:1px 8px;border-radius:999px;border:1px solid currentColor;font-weight:600;margin-right:4px;white-space:nowrap}
.fire{color:var(--fire)}.ice{color:var(--ice)}.light{color:var(--light)}.rad{color:var(--rad)}.bleed{color:var(--bleed)}.combo{color:var(--combo)}.counter{color:var(--counter)}.alien{color:var(--alien)}.time{color:var(--time)}.gen{color:var(--gen)}
.lad{display:inline-block;min-width:74px;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.03em;color:var(--muted);margin-right:6px}
.lad-mythics,.lad-mythicss{color:var(--c-myth)}.lad-awakened{color:var(--accent)}
.awk{margin:2px 0 0;padding:3px 0 3px 8px;border-left:3px solid var(--accent)}
.awname{font-size:13px;color:var(--accent);font-weight:600}
.fuel td:nth-child(n+2){white-space:nowrap}
.flag{display:inline-block;font-size:11px;font-weight:700;padding:0 6px;border-radius:4px;margin-left:6px;vertical-align:1px}
.flag.cc{color:var(--cc);background:var(--cc-bg)}.flag.eng{color:var(--eng);background:var(--eng-bg)}
.engnote{font-size:12px;color:var(--eng);margin:1px 0 0 0}
.role{font-size:12px;color:var(--muted)}
.mx td{min-width:120px}.mx td a{display:block;text-decoration:none;font-size:13px}
.kpi{display:flex;flex-wrap:wrap;gap:10px;margin:10px 0}.kpi div{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:8px 12px;min-width:120px}
.kpi b{display:block;font-size:22px}
.slotlang{font-size:14px;color:var(--muted);margin:2px 0 8px}
ol.q li{margin:8px 0}
.engnote-hide .engnote{display:none}
@media (max-width:520px){.mgrid{grid-template-columns:1fr}.grid{grid-template-columns:1fr}h1{font-size:23px}}
'''

P('<title>Parts Redesign</title>')
P('<meta name="viewport" content="width=device-width, initial-scale=1">')
P(f'<style>{CSS}</style>')
P('<main>')
P('<h1>Parts Redesign</h1>')
P('<p class="muted">Rev 3 of the parts review, rebuilt on the owner\'s new model: four separate standard sets (Common, Rare, Epic, Legendary) that fill plain roles, and 30 Mythics that each enable one synergy build. Design only; no game code changed. Every row and card has a stable id (shown as <code>#id</code>) for per-item comments.</p>')
P('<div class="box" id="rev3d"><b>Rev 3d: interplay with skills.</b> Every part text was checked against all run and laboratory skills (every level), the signatures and their awakened levels, the run Mythics and every hero and mount kit line, and against the engine\'s real thresholds. Absolute rule values became relative modifiers with floors, copies of skill or kit effects were changed to complement them, and blow-up combinations got caps: see <a href="#interplay">Interplay with skills</a>.</div>')
P('<div class="box" id="rev3h"><b>Rev 3h: owner decisions.</b> Mythic max level 100 at SS, the cap per ladder step is in the <a href="#fuel">ladder table</a> (70 / 75 / 80 / 85 / 90 / 95 / 100, no plate raises). Flat stats keep +15% of level 1 per level; every percentage stat now grows linearly to 3× at max level. Tables and cards show level 1 and max.</div>')
P('<div class="box" id="rev3g"><b>Rev 3g: owner answers.</b> Arms are interchangeable: no attack styles, weapon names are cosmetic, every arm gives ATK plus its role stats (<a href="#q-7">q-7</a>). The Mythic stat line stays, as a percentage that grows per level (<a href="#q-8">q-8</a>). All questions are decided.</div>')
P('<div class="box" id="rev3f"><b>Rev 3f: owner answers.</b><ul>'
  '<li>All 30 Mythics at launch, in <a href="#pace-assumptions">10 Part banners of 3</a> (one per synergy), one a week; pacing recomputed.</li>'
  '<li>No copy pick; the Replicator stays 3 : 1; no second Metal sink (the surplus is known and accepted).</li>'
  '<li>Standard parts may carry synergy stats where the role fits (Slam arms counter chance, Whip arms combo chance); their skills stay simple.</li>'
  '<li><a href="#interplay-symbiote">Symbiote reworked</a>: aliens keep acting; joining your rage attack costs them charge, so charge parts refuel them instead of being silenced.</li>'
  '<li>Arm styles and the Mythic stat line: decided in rev 3g.</li>'
  '</ul></div>')
P('<div class="box" id="rev3e"><b>Rev 3e: stack, don\'t blandify.</b> Owner: the overlap fixes that only made a part blander are reverted, and the overlap is handled by stacking: battle-start shields, overheal into shield (caps add up), rage refunds, strike-backs on undone hits, Drone guards and the every-3rd-turn burst now simply stack with the skills and kits that do the same (6 rows in the change table). The real fixes stay. All floors and caps are <b>decided</b> (owner).</div>')
P('<div class="box" id="rev3c"><b>Rev 3c: owner feedback on 3b.</b><ul>'
  '<li>The spare-part currency is <b>Metal</b>.</li>'
  '<li><b>Pulls drive the Mythic ladder, Metal does not.</b> The ladder ends at <b>Mythic SS</b> (no SS+1 / SS+2). Mythic → S takes 1 copy of the same Mythic per step (3 in all); S → SS takes 2 + 2 at S+1, 2 + 2 at S+2 and 5 + 5 at SS (copies + other Mythics of the same slot; 9 + 9 in all, spacing decided by the owner). Total to SS: 12 copies + 9 other Mythics. Metal is small and secondary on every step.</li>'
  '<li>Direct drops are <b>decided</b>: campaign boss first clears give one Rare or Epic part; Dark Side clears have a 5% chance of a Legendary.</li>'
  '<li><b>Levelling grows every stat</b> of every part (slot stats, role stat, a Mythic\'s synergy stat): +15% of the level-1 value per level, whole numbers; the per-level columns list every stat.</li>'
  '<li>New <a href="#pacing">Pacing</a> section: time to the first S, the first SS and all six slots at SS for each player type, from a pull simulation.</li>'
  '</ul></div>')
P('<div class="box" id="rev3b"><b>Rev 3b: the owner\'s answers folded in.</b><ul>'
  '<li>Spare Common to Legendary parts feed the Mythic ladder (now as <b>Metal</b>); nothing merges into a higher rarity (<a href="#fuel">Metal and the ladder</a>).</li>'
  '<li>Uncommon stays retired. Parts come mainly from <b>Parts pulls</b> (the Shop\'s Parts section, paid with Crates); the assumed rates and the minor other sources are in <a href="#sources">Sources</a>.</li>'
  '<li>30 Mythics, as proposed.</li>'
  '<li><b>Awakening no longer adds a sixth skill.</b> It replaces each of the five skills with an awakened version of the same mechanic (bigger number, lower threshold or a twist), shown under each skill. The former sixth skills are gone; a few of them live on as twists.</li>'
  '<li><b>New names</b> for all 30 Mythics and their awakened forms (ids follow the new names); the standard parts keep their plain <i>tier role slot</i> names.</li>'
  '</ul></div>')

# counts
n_std = len(SLOTS) * len(ROLES) * 4
P('<div class="kpi">'
  f'<div><b>{len(SLOTS)*len(ROLES)}</b>Common</div><div><b>{len(SLOTS)*len(ROLES)}</b>Rare</div>'
  f'<div><b>{len(E)}</b>Epic (1 skill)</div><div><b>{len(L)}</b>Legendary (2 skills)</div><div><b>{len(M)}</b>Mythic (5 skills, each with an awakened version)</div>'
  f'<div><b>{len(E) + 2*len(L) + 10*len(M)}</b>skill texts</div></div>')

nav = ['<nav class="jump"><a href="#model">Model</a><a href="#fuel">Ladder and Metal</a><a href="#pacing">Pacing</a><a href="#sources">Sources</a><a href="#answers">Review answers</a>']
for s in SLOTS: nav.append(f'<a href="#slot-{s.lower()}">{s}</a>')
nav.append('<a href="#coverage">Coverage</a><a href="#engine">Engine</a><a href="#interplay">Interplay</a><a href="#selfcheck">Self-check</a><a href="#questions">Questions</a></nav>')
P(''.join(nav))

# 1 model
P('<h2 id="model">1. The model</h2>')
P('<div class="box model"><ul>'
  '<li><b>Rarities.</b> Common, Rare, Epic and Legendary are four separate item sets: per slot (head, body, tail, legs, arm; an arm part fits either arm) six items each, one per standard role: <b>Guard</b> (tank), <b>Strike</b> (offense), <b>Swift</b> (speed), <b>Keen</b> (crit), <b>Mend</b> (sustain), <b>Fury</b> (rage). Uncommon is retired. <b>Mythic</b> is a fifth set of 30 build enablers.</li>'
  '<li><b>Obtain.</b> Every rarity comes mainly from <b>Parts pulls</b> (the Shop\'s Parts section, paid with Crates) at the published rates: Common 58.3%, Rare 28%, Epic 9.5%, Legendary 3%, Mythic 1.2% (pity 100 / 40, every 10× has an Epic). Minor direct sources (decided): campaign boss first clears give one Rare or Epic part, Dark Side clears a 5% Legendary (see <a href="#sources">Sources</a>).</li>'
  '<li><b>Skills.</b> Common and Rare: stats only. Epic: exactly 1 skill. Legendary: exactly 2. Mythic: 5 skills, 3 active at Mythic, the 4th at Mythic S, the 5th at Mythic SS. Awakening upgrades these five; it never adds one.</li>'
  '<li><b>Stats.</b> Slot core (head HP; body HP + DEF; tail HP + ATK; legs DEF + speed; arm ATK) × rarity (1.0 / 1.7 / 2.4 / 3.6 / 5.6) plus one role stat (Guard arms also add counter chance, Swift arms combo chance; synergy stats are fine on standard parts where the role fits, their skills stay simple); a Mythic carries its synergy stat instead, as a percentage (+10% fire / ice / Arc / radiation / bleed / Echo damage, +10% alien power, +5% combo / counter / crit chance, +10% HP, +10% rage gain), growing per level like every stat. <b>Arms are interchangeable</b>: any arm part fits either arm, weapon names (Hammer, Cannon, Whip …) are cosmetic, and there are no attack-style multipliers. <b>Level caps:</b> Common 10, Rare 20, Epic 30, Legendary 50; a Mythic\'s cap rises with its ladder step: Mythic 70, +1 75, +2 80, S 85, S+1 90, S+2 95, <b>SS 100</b> (final; no plate raises). <b>Levelling grows every stat</b>: flat stats (HP, ATK, DEF, speed) add 15% of their level-1 value per level, whole numbers (Firebrand Blaster +85 ATK, +13 per level → +1 372 ATK at 100); every <b>percentage stat</b> (a Mythic\'s synergy %, HP %, rage gain; combo / counter / crit chance; life steal and the other role %) grows linearly from its level-1 value to <b>3×</b> at the part\'s max level (Firebrand Blaster +10% fire damage → +30% at 100, awakened ×1.4: +14% → +42%; +5% crit chance → +15%; a Common +3% → +9% at its cap 10). The tables show level 1, the growth and the value at max level. Same rule for every rarity and role; awakening multiplies level-1 values ×1.4.</li>'
  '<li><b>Merge.</b> Nothing merges into a higher rarity: a Legendary never becomes a Mythic, and standard parts have no merge of their own. Only Mythics climb: Mythic → +1 → +2 → <b>S</b> → S+1 → S+2 → <b>SS</b> (final). <b>Pulls drive it</b>: each step up to S takes 1 copy of the same Mythic; the steps from S to SS take copies + other Mythics of the same slot: 2 + 2 (S+1), 2 + 2 (S+2), 5 + 5 (SS). Spare standard parts become <b>Metal</b> (Common 1, Rare 2, Epic 6, Legendary 20), a small cost on every step. Full rule in <a href="#fuel">Ladder and Metal</a>.</li>'
  '<li><b>Mythic ladder.</b> Skills 1–3 at Mythic, skill 4 at <b>S</b>, skill 5 at <b>SS</b>; the other steps give stats and level cap. Steps never grow skill numbers; bigger numbers come only from awakening.</li>'
  '<li><b>Awakening</b> (once, Mythic or higher, 50 Awaken Cores): a new name, ×1.4 base stats, and each of the five skills becomes its <b>awakened version</b>: the same trigger and mechanic with a bigger number, a lower threshold or a twist on the same effect. The rung still decides how many are active (3, the 4th at S, the 5th at SS). No sixth skill, no twin families, and an awakened skill is crowd control only where its base skill already is (same rung, still once per enemy).</li>'
  '<li><b>Interplay.</b> A part never sets a game value absolutely: it shifts it ("2 Heat sooner", "25 less rage"), all sources add up, and every shifted value has a floor or ceiling (Supernova ≥ 6 Heat, rage attack ≥ 50 rage, sidekick action ≥ 50 charge, dodge ≤ 85%). Blow-ups are capped across all sources (full avoidance 1 per round, extra turns 2 per round, free rage attacks 2 per turn, status ticks 3 per enemy per round). Details in <a href="#interplay">Interplay with skills</a>.</li>'
  '<li><b>Crowd control</b> exists only on Mythic S / SS skills, at most once per enemy per battle; bosses lose 20 rage instead (as the Chrono Skiff).</li>'
  '<li><b>Text rule.</b> One sentence, at most 15 words, trigger → effect → number, whole numbers (multiples of 5 from 20 up), no vague verbs, one thing per skill. Legendary and Mythic skills are mechanics, never a plain "+X%".</li>'
  '</ul></div>')


# ladder, Metal, pacing and sources
import json, os
PJ = json.load(open(os.path.join(sys.argv[1], 'pacing.json')))
import pacing as PC
METAL_V = [('Common', 'r-common', 1), ('Rare', 'r-rare', 2), ('Epic', 'r-epic', 6), ('Legendary', 'r-leg', 20)]
EFF_RATE = {'Common': 56.2, 'Rare': 27.0, 'Epic': 11.3, 'Legendary': 3.8, 'Mythic': 1.7}   # per 100 pulls incl. pity and the 10x rule (simulated, 2M pulls)
PUB_RATE = {'Common': 58.3, 'Rare': 28, 'Epic': 9.5, 'Legendary': 3, 'Mythic': 1.2}
MP_PER_PULL = sum(v * EFF_RATE[r] for r, _, v in METAL_V) / 100
ORE = {1: 2200, 2: 3400, 3: 5100, 4: 7600, 5: 11400, 6: 17100}
def grp(n): return f'{n:,}'.replace(',', ' ')
METAL_TOTAL = sum(m for c, o, m in PC.LADDER_REQ.values())
P('<h2 id="fuel">Mythic ladder and Metal</h2>')
P('<div class="box model" id="fuel-rule"><ul>'
  '<li><b>Only Mythics climb, and pulls drive the climb.</b> Common, Rare, Epic and Legendary parts never merge into a higher rarity and have no steps of their own. A Mythic climbs Mythic → +1 → +2 → S → S+1 → S+2 → SS; <b>SS is the final state</b>.</li>'
  '<li><b>Mythic → S:</b> 1 copy of the same Mythic per step (+1, +2, S): 3 copies, no other Mythics. <b>S → SS:</b> copies + other Mythics of the same slot: 2 + 2 at S+1, 2 + 2 at S+2, 5 + 5 at SS (decided): 9 + 9. <b>To SS: 12 copies + 9 other Mythics</b> (13 of that Mythic counting the one you climb).</li>'
  '<li><b>A copy</b> is the same Mythic (same item) at its base rung: not climbed and not awakened. Its level Ore and runes come back.</li>'
  '<li><b>An other Mythic of the same slot</b> is any different Mythic item of that slot at its base rung, not awakened (the two arm slots share one pool: every arm Mythic is an arm). Climbed or awakened Mythics are never material. Each Mythic fills one seat only: an extra copy of the item you climb counts as a copy, never also as one of its other Mythics; for a <i>different</i> Mythic of that slot (e.g. your second arm) it is an other Mythic.</li>'
  '<li><b>Replicator:</b> 3 spare Mythics (any slot, base rung) + 5 000 Ore → 1 copy of a Mythic you own. With 12 copies per item this is the main copy source after the item itself (see <a href="#pacing">Pacing</a>: without it the endgame takes over twice as long).</li>'
  '<li><b>Metal</b> is the value of a spare standard part: Common <b>1</b>, Rare <b>2</b>, Epic <b>6</b>, Legendary <b>20</b> (any slot, any role). Feeding a part turns it into Metal at once; its level Ore and runes come back. Equipped, locked and Forged parts are never fed. Mythics are never Metal: spare Mythics are ladder material or Replicator input.</li>'
  f'<li><b>Metal is secondary.</b> Every step costs a little Metal ({grp(METAL_TOTAL)} per Mythic to SS, {grp(6 * METAL_TOTAL)} for six Mythics). At the effective pull rates the standard rarities bring about the same Metal per 100 pulls (' +
  ', '.join(f'{r} {EFF_RATE[r] * v:.0f}' for r, _, v in METAL_V) + f'), {MP_PER_PULL:.1f} per pull. Six Mythics at SS use about a third of the Metal a player earns on the way there, and in the simulation Metal never delays a step by more than the run-to-run noise. The rest piles up: a <b>known surplus</b>, accepted for now (owner: no second sink).</li>'
  '</ul></div>')
rows = ['<div class="tw"><table class="fuel"><thead><tr><th>Step</th><th>Level cap</th><th>Copies (same Mythic)</th><th>Other Mythics (same slot)</th><th>Metal</th><th>Ore</th><th>Unlocks</th></tr></thead><tbody>']
cc = co = cm = 0
for r in range(1, PC.TOP + 1):
    c, o, m = PC.LADDER_REQ[r]; cc += c; co += o; cm += m
    name = PC.RUNG_NAME[r]
    unl = 'skill 4' if r == 3 else 'skill 5 (final)' if r == 6 else 'stats, level cap'
    rid = 'fuel-' + name.lower().replace(' ', '-').replace('+', 'p')
    rows.append(f'<tr id="{rid}"><td><b>{name}</b></td><td>{MYTHIC_CAP[r]}</td><td>{c}</td><td>{o or "–"}</td><td>{m}</td><td>{grp(ORE[r])}</td><td class="role">{unl}</td></tr>')
rows.append(f'<tr><th>Total</th><th>max 100</th><th>{cc}</th><th>{co}</th><th>{grp(cm)}</th><th>{grp(sum(ORE.values()))}</th><th></th></tr></tbody></table></div>')
P(''.join(rows))
P('<p class="muted">A fresh Mythic caps at level 70; each step raises the cap by 5, to <b>100 at SS</b> (final, no plate raises). Ore per step is today\'s Mythic-ladder Ore. The S → SS spacing (2 + 2, 2 + 2, 5 + 5) is the owner\'s decision; the Replicator stays 3 : 1 (owner).</p>')

# pacing
R = PJ['runs']
def mo(d):
    if d >= 10**5: return 'not in 12 years'
    m = d / 30.4
    if m < 2: return f'{d / 7:.0f} weeks'
    return f'{m:.0f} months' if m < 24 else f'{m / 12:.1f} years'
P('<h2 id="pacing">Pacing</h2>')
P('<p>Simulated per player type (median of 24 runs, every Parts pull rolled): the player climbs one Mythic per slot (head, body, tail, legs and two arms), the first Mythic of each slot it pulls, focusing on whichever is furthest along, and uses the Replicator when spares allow. Times count from the Parts unlock.</p>')
rows = ['<div class="tw"><table class="fuel"><thead><tr><th>Player type</th><th>Parts pulls year 1 / steady per day</th><th>First Mythic at S</th><th>First Mythic at SS</th><th>All 6 slots at SS</th><th>Parts pulls to all 6</th><th>Metal used</th></tr></thead><tbody>']
for name, y1, pd in PJ['profiles']:
    x = R['steady'][name]
    used = 6 * METAL_TOTAL / (x['pulls_all6'] * MP_PER_PULL) if x['pulls_all6'] < 10**5 else 0
    pid_ = 'pace-' + name.lower().replace(' ', '-')
    rows.append(f'<tr id="{pid_}"><td><b>{name}</b></td><td>{grp(y1)} / ~{pd}</td><td>{mo(x["S"])}</td><td>{mo(x["SS"])}</td><td><b>{mo(x["all6"])}</b></td><td>{grp(int(round(x["pulls_all6"], -2)))}</td><td>{used:.0%}</td></tr>')
rows.append('</tbody></table></div>')
P(''.join(rows))
def cmp_row(key, label):
    return f'<tr id="pace-{key}"><td>{label}</td>' + ''.join(f'<td>{mo(R[key][n]["all6"])}</td>' for n, _, _ in PJ['profiles']) + '</tr>'
rows = ['<div class="tw"><table class="fuel"><thead><tr><th>All 6 slots at SS, if …</th>' + ''.join(f'<th>{n}</th>' for n, _, _ in PJ['profiles']) + '</tr></thead><tbody>']
for key, label in (('steady', 'as proposed'), ('no_metal', 'steps cost no Metal'), ('norepl', 'no Replicator'), ('repl2', 'Replicator at 2 : 1 (not adopted)'),
                   ('slotpick', 'the player picks the featured slot each week (not adopted)'), ('spark300', 'a chosen copy every 300 Parts pulls (not adopted)')):
    rows.append(cmp_row(key, label))
rows.append('</tbody></table></div>')
P(''.join(rows))
P('<div class="box" id="pace-assumptions"><ul>'
  '<li><b>Pulls:</b> the game simulator\'s Parts pulls per player type; the first 4 months get the rest of the year-1 total (Casual ~6, Active F2P ~14, Dolphin ~21, Hardcore ~22, Whale ~23 per day), then the steady pace.</li>'
  '<li><b>Rates:</b> the published Parts pull rates, Mythic pity 100 (a Mythic every ~58 pulls), Legendary pity 40, the 10× rule.</li>'
  '<li><b>Banner rotation (decided):</b> all 30 Mythics at launch in <b>10 Part banners of 3</b>, one per synergy (its 3 Mythics sit in 3 different slots), one banner a week, 10-week cycle (each Mythic featured ~5 weeks a year): ' + ', '.join(f'{n} ({syn})' for n, syn in PC.BANNERS) + '. 80% of Mythic results split between the 3 featured, 20% over the other 27. Inferno Arsenal, Glacier Vault, Storm Works and Shadow Forge are today\'s banner names; the other six are proposals. A player who pulls every day gets each Mythic at the same average rate whatever the rotation; it matters when players save Crates for their synergy\'s week.</li>'
  '<li><b>Metal</b> comes only from Parts pulls here (the boss and Dark Side drops add a little).</li>'
  '</ul></div>')
a, c_ = R['steady']['Active F2P'], R['steady']['Casual F2P']
P('<div class="box" id="pace-verdict"><b>Reading.</b><ul>'
  f'<li><b>Reachable, not too fast.</b> Every type reaches the ultra endgame; the paying and hardcore types take about {mo(R["steady"]["Active Whale"]["all6"])}, an Active F2P {mo(a["all6"])}. Nobody gets there in year 1.</li>'
  f'<li><b>The 12 copies are the bottleneck, not the other Mythics.</b> One item gets only 1 in 30 Mythic pulls, so the first SS comes late ({mo(a["SS"])} for an Active F2P, {mo(c_["SS"])} for a Casual) while S comes early ({mo(a["S"])}).</li>'
  f'<li><b>Casual F2P</b> needs {mo(c_["all6"])} for all six and {mo(c_["SS"])} for a first SS. That is effectively out of reach, which may be acceptable for an ultra endgame, but skill 5 is then a thing casual players rarely see.</li>'
  '<li><b>Decided:</b> no copy pick, Replicator stays 3 : 1 (owner). The rows "not adopted" above show what the alternatives would have changed.</li>'
  '</ul></div>')

P('<h2 id="sources">Sources and pull rates</h2>')
rows = ['<div class="tw"><table class="fuel"><thead><tr><th>Rarity</th><th>Parts pull rate (published)</th><th>Effective per 100 pulls</th><th>Item within the rarity</th><th>Metal</th><th>Direct drops (decided)</th></tr></thead><tbody>']
OTHER = {'Common': 'none', 'Rare': 'campaign boss first clears: 1 random Rare or Epic', 'Epic': 'campaign boss first clears (as Rare)',
         'Legendary': 'Dark Side clears: 5% for 1 random Legendary', 'Mythic': 'none (pulls and the Replicator only)'}
for r, cls in (('Common', 'r-common'), ('Rare', 'r-rare'), ('Epic', 'r-epic'), ('Legendary', 'r-leg'), ('Mythic', 'r-myth')):
    within = f'uniform over the {len(SLOTS) * len(ROLES)} items' if r != 'Mythic' else '80% featured, 20% the other Mythics'
    fv = {'Common': '1', 'Rare': '2', 'Epic': '6', 'Legendary': '20', 'Mythic': '– (ladder material)'}[r]
    rows.append(f'<tr id="src-{r.lower()}"><td class="rar {cls}">{r}</td><td>{PUB_RATE[r]}%</td><td>{EFF_RATE[r]}</td><td>{within}</td><td>{fv}</td><td class="role">{OTHER[r]}</td></tr>')
rows.append('</tbody></table></div>')
P(''.join(rows))
P('<div class="box"><ul>'
  '<li><b>Assumed rates</b> are today\'s published Parts pull rates, unchanged; "effective" includes the pity and the 10× rule (simulated over 2 million pulls): a Mythic every ~58 pulls, a Legendary every ~26.</li>'
  '<li><b>Crates</b> (the pull currency) carry the other income: daily / weekly quest chests, pull milestones (10 Crates per 100 Parts pulls), the arena shop, passes and offers. Runs, run chests and campaign stars pay Ore and Gear Cores, not parts.</li>'
  '<li><b>Direct drops (decided):</b> campaign boss first clears give one random Rare or Epic part; a Dark Side clear has a 5% chance of a random Legendary part. Nothing else drops parts.</li>'
  '</ul></div>')


# interplay with skills
P('<h2 id="interplay">Interplay with skills</h2>')
P('<p>Every part text was compared with the game\'s own skills and kits: all run and laboratory skills at every level, the nine signatures and their awakened levels, the run Mythics, and every hero and mount kit line (' + str(len(check.corpus())) + ' sentences, read from <code>run-skill-levels.md</code> and <code>heroes-mounts.md</code>, generated from <code>ContentDb</code>), and with the engine\'s values (<code>RunDefs</code> combat tuning, <code>ContentDb.Run</code> statuses, <code>BattleSimulator</code> rules). check.py now repeats the scan on every build.</p>')
P('<h3 id="interplay-values">Engine values the parts build on</h3><div class="box"><ul>'
  '<li>Heat: max 10, Supernova at 10 (Controlled Burn sets 12; Flashpoint forces it at 6+). Chill: max 10, 3 turns, cools Heat; Deep Freeze freezes at 5 Chill and resets it. Shock: max 5, 2 turns; Capacitor discharges and resets at 5. Bleed: max 10 (Hemorrhage +5 / +8). Deep Wells (Prism Alchemist 10★) doubles the Bleed, Chill and Shock max.</li>'
  '<li>Freeze and Stun: 1 turn, then 3 rounds before the same enemy can get them again. Rage: 100 for a rage attack, overfill to 125, +25 per action, +10 per hit taken. Sidekicks act at 100 charge (start at 50). Dodge at most 75% (soft knee at 10), crit at most 100%, combo chance at most 95% (80% with Endless Chain).</li>'
  '</ul></div>')
P('<h3 id="interplay-stacking">How parts stack with skills</h3><div class="box model"><ul>'
  '<li><b>Relative, never absolute.</b> A part shifts a value; it never sets it. A skill that sets a value (Controlled Burn: "Supernova needs 12 Heat") sets the base, then every shift from parts, skills and kits adds up.</li>'
  '<li><b>Floors and ceilings</b> (all sources together; <b>decided</b> by the owner): Supernova never below <b>6 Heat</b> (10 − 4 at most from parts; 12 − 4 = 8 with Controlled Burn); a rage attack never below <b>50 rage</b> (Rage Turbine −25 / −40, Quantum Fury Shell −20); a sidekick action never below <b>50 charge</b>; dodge never above <b>85%</b> (Frostglide Skates +5 / +10 against chilled attackers is the only part that lifts the 75% cap); counter and crit chance at most 100%.</li>'
  '<li><b>Conversions</b> (Flicker Treads dodge → combo, Perfect Form combo → counter, Deadeye Visor crit → crit damage) read the stat before conversions and are never chained: a converted value is not converted again.</li>'
  '<li><b>Rules that never add:</b> executes (Cull, Exsanguinate, Deadeye Visor, Decay Stinger) use the highest threshold, never the sum; area basic attacks (Thunderhead 60%, Quantum Strike Tail, Last Stand Plate) use the highest %; "never wears off" (Permafrost at 5+, Rimeguard Plate) uses the lowest stack gate.</li>'
  '<li><b>Rules that do add:</b> combo ramps and falloff per strike (Chainlash Whip +10%, Endless Chain −15% → −5%); Afterburner Tail\'s "full damage" sets the base combo multiplier and falloff applies on top; the Quantum Guard Shell damage cap (20% max HP) applies last, after every damage-taken modifier (Mirror Carapace +20%).</li>'
  '<li><b>Same effects stack</b> (rev 3e): battle-start shields from all sources add up (Plasma Guard Helm + Frost Armor + Starguard + Bulwark Protocol); overheal-into-shield caps add up (Quantum Mend Helm 15% + Siphon Line 10% = up to 25% max HP); rage refunds add up (Plasma Fury Blaster + Pressure Valve); strike-backs on undone hits both fire (Hindsight Shell + Hour of Need); Drone guards from parts and the Hive Carrier each guard their own hit; on the Echo signature\'s 3rd turn Afterburner Tail\'s burst fires too, and rage from Momentum and the part adds up.</li>'
  '<li><b>Blow-up caps</b> (all sources together; <b>decided</b> by the owner): fully avoided hits (forced misses from Ashwalkers, Flicker Treads, Quantum Swift Legs and full sidekick guards) at most <b>1 per round</b>, further ones fall back to normal dodge or the guard\'s own %; extra turns at most <b>2 per round</b> (more move to the next round; an extra turn never grants another); free rage attacks at most <b>2 per turn</b>; a status ticks at most <b>3 times per enemy per round</b> (its own tick, Time Dilation, "ticks twice" and every "tick now"); Echoes chain at most <b>2 deep</b>; Drones from all sources share one cap of <b>5</b>; Supernova chains from Cinder Crown once (awakened twice) per turn.</li>'
  '<li><b>Crowd control from parts</b> stays once per enemy per battle, respects the status\'s own 3-round cooldown and never extends a running stun / freeze / skip; bosses lose 20 rage instead.</li>'
  '<li><b>Rhythms are timers.</b> A part\'s "every Nth turn / round" counts as a timer for kits that advance timers (Patience, Slipstream A5).</li>'
  '</ul></div>')
rows = ['<div class="tw"><table><thead><tr><th>Item</th><th>Before</th><th>Now</th><th>Why</th></tr></thead><tbody>']
for k, (anc, item, old, new, why) in enumerate(INTERPLAY_CHANGES, 1):
    rows.append(f'<tr id="ix-{k}"><td><a href="#{anc}">{esc(item)}</a></td><td class="role">{esc(old)}</td><td>{esc(new)}</td><td class="role">{esc(why)}</td></tr>')
rows.append('</tbody></table></div>')
P(f'<h3 id="interplay-changes">What changed and why ({len(INTERPLAY_CHANGES) - len(INTERPLAY_REVERTED)} changes, {len(INTERPLAY_REVERTED)} reverted to stacking)</h3>')
P(''.join(rows))
P('<h3 id="interplay-kept">Overlaps kept on purpose</h3><div class="box"><ul>' +
  ''.join(f'<li><b>"{esc(k)}…"</b>: {esc(v)}.</li>' for k, v in INTERPLAY_ALLOW.items()) +
  '<li><b>Thornstep Legs</b> "first hit after a dodge applies 3 Bleed" and <b>Serrated Edge</b> "first hit each battle": same payload, different trigger (every dodge vs once per battle).</li>'
  '<li><b>Dynamo Hull</b> "when hit: Arc" and the <b>Arc signature</b> "on hit: Arc": the part answers hits you take, the signature hits you land.</li>'
  '</ul></div>')
P('<h3 id="interplay-contradictions">Contradictions</h3><div class="box"><ul>'
  '<li><b>Resolved:</b> Deep Freeze resets Chill at 5, so "at 10 Chill" part skills could never fire: they are gated on "chilled" now. Capacitor resets Shock at its max of 5, so "at max Shock" never showed: 4+ / 3+ Shock now. Controlled Burn\'s 12-Heat Supernova and Cinder Crown\'s "8 instead of 10" could not both hold: relative now. Rage Turbine and the Quantum Fury Shell each set a rage cost: both relative, with one floor.</li>'
  '<li><b>Kept as trade-offs of a run Mythic:</b> Thermal Shock ("can\'t gain Heat for 3 turns") also blocks Heat from parts; Bastion (no attacks on your turn) silences "your attacks / basic attacks" part skills; Ice parts on a Fire build cool Heat unless the run has Cold Fusion. The player chose the run Mythic; the part still works in every other run.</li>'
  '<li><b>Decided (owner): Symbiote is reworked</b> so it no longer silences charge and sidekick-act effects; see the run-skill change below and <a href="#q-9">question 9</a>.</li>'
  '</ul></div>')
P('<h3 id="interplay-symbiote">Run-skill change: Symbiote</h3>')
P('<div class="tw"><table><thead><tr><th>Run Mythic</th><th>Before</th><th>After</th><th>Engine</th><th>Why</th></tr></thead><tbody>'
  f'<tr id="ix-symbiote"><td><b>{esc(SYMBIOTE["name"])}</b> <code>{SYMBIOTE["id"]}</code></td><td class="role">{esc(SYMBIOTE["before"])}</td><td>{esc(SYMBIOTE["after"])}</td><td class="role">{esc(SYMBIOTE["engine"])}</td><td class="role">{esc(SYMBIOTE["why"])}</td></tr>'
  '</tbody></table></div>')

P('<h2 id="answers">How rev 3 answers the review</h2>')
P('<div class="box"><ul>'
  '<li><b>"The base design of the parts is wrong."</b> Parts are no longer one def climbing 14 rarities with five generated skills. Each rarity is its own set, standard sets fill plain roles, and only Mythics carry builds.</li>'
  '<li><b>Inconsistent.</b> One grid for all 120 standard items: role × slot × rarity, with fixed names (<i>tier role slot</i>: Scrap / Alloy / Plasma / Quantum + Guard … Fury + Helm / Shell / Tail / Legs / weapon). Every slot speaks one trigger language (head = openers and crits, body = when hit and low HP, tail = rhythm, legs = dodge and speed, arm = attacks: rage attacks, basic attacks, kills). Every Mythic has the same shape: five skills on the same ladder, each with its awakened version.</li>'
  f'<li><b>Duplication.</b> A script compares every pair of the {len(E) + 2*len(L) + 10*len(M)} skill texts (word overlap and a trigger + verb + status key) and every text against all run skills, signatures, hero and mount kits. Within a slot and rarity, no two skills share a mechanic. Standard part skills never use a synergy mechanic (no statuses, Arcs, Echoes, sidekicks or Supernovas), so they cannot duplicate a Mythic; their stats may carry combo, counter or crit chance where the role fits (owner, rev 3f). What is left is listed in <a href="#selfcheck">section 5</a>.</li>'
  f'<li><b>Identity not used.</b> Every Mythic states one build identity and a keyword set; the script checks that each of its five skills and five awakened versions uses a keyword (all {10*len(M)} do). Example: Ashwalkers is "dodge ignition", and every skill triggers on dodges or pays back into dodge.</li>'
  '</ul></div>')

# 2 slots
for slot in SLOTS:
    P(f'<h2 id="slot-{slot.lower()}">2. {slot}{" (either arm)" if slot == "Arm" else ""}</h2>')
    P(f'<p class="slotlang"><b>Trigger language:</b> {esc(SLOT_LANG[slot])}.</p>')
    # Common / Rare table
    P(f'<h3>Common and Rare <small>stats only</small></h3>')
    rows = ['<div class="tw"><table><thead><tr><th>Item</th><th>Rarity</th><th>Role</th><th>Stats at level 1</th><th>Per level</th><th>Cap</th><th>At max level</th><th>id</th></tr></thead><tbody>']
    for role in ROLES:
        for rar, cls, pre in (('Common', 'r-common', 'c'), ('Rare', 'r-rare', 'r')):
            i = iid(pre, slot, role)
            st = fmt_stats(stats_for(slot, role, rar), MULT[rar])
            style = ''
            rows.append(f'<tr id="{i}"><td><b>{esc(item_name(rar, slot, role))}</b>{style}</td><td class="rar {cls}">{rar}</td><td class="role">{role}</td>'
                        f'<td>{esc(st)}</td><td>{esc(fmt_lvl(slot, MULT[rar], role, None, CAP[rar]))}</td><td>{CAP[rar]}</td><td>{esc(fmt_max(slot, MULT[rar], role, None, CAP[rar]))}</td><td><a class="id" href="#{i}">#{i}</a></td></tr>')
    rows.append('</tbody></table></div>')
    P(''.join(rows))
    # Epic and Legendary
    for rar, D, cls, pre in (('Epic', E, 'epic', 'e'), ('Legendary', L, 'leg', 'l')):
        P(f'<h3><span class="rar r-{cls}">{rar}</span> <small>{"1 skill" if rar == "Epic" else "2 skills"}</small></h3><div class="grid">')
        for role in ROLES:
            i = iid(pre, slot, role)
            nm = item_name(rar, slot, role)
            st = fmt_stats(stats_for(slot, role, rar), MULT[rar])
            style = ''
            lis = ''.join(skill_li(t, e, label=f'{nm} ({rar} {slot})', anchor=i) for t, e in D[(slot, role)])
            P(f'<article class="card {cls}" id="{i}"><header><h4>{esc(nm)}</h4><a class="id" href="#{i}">#{i}</a></header>'
              f'<div class="role">{role}: {esc(ROLE_DESC[role])}{style}</div><div class="st">{esc(st)}; per level {esc(fmt_lvl(slot, MULT[rar], role, None, CAP[rar]))}; cap {CAP[rar]}: {esc(fmt_max(slot, MULT[rar], role, None, CAP[rar]))}</div><ul>{lis}</ul></article>')
        P('</div>')
    # Mythics
    ms = [m for m in M if m['slot'] == slot]
    P(f'<h3><span class="rar r-myth">Mythic</span> <small>{len(ms)} build enablers</small></h3><div class="mgrid">')
    for m in ms:
        i = mid(m)
        st = fmt_stats(stats_for(slot, None, 'Mythic'), MULT['Mythic'], m['stat'])
        style = ''
        lis = ''.join(skill_li(t, e, LADDER[k], label=f'{m["name"]} ({LADDER[k]})', anchor=i, awk=m['awk'][k],
                               aw_label=f'{m["aw"]} (awakened {LADDER[k]})') for k, (t, e) in enumerate(m['skills']))
        P(f'<article class="card myth" id="{i}"><header><h4>{esc(m["name"])}</h4><a class="id" href="#{i}">#{i}</a></header>'
          f'<div><span class="chip {SYN_CLASS[m["syn"]]}">{m["syn"]}</span><span class="awname">awakened: {esc(m["aw"])}</span><span class="role">{style}</span></div>'
          f'<div class="ident"><b>Build:</b> {esc(m["identity"])}</div>'
          f'<div class="st">{esc(st)} (Mythic, level 1); per level {esc(fmt_lvl(slot, MULT["Mythic"], None, m["stat"], 100))}; level 100 (SS): {esc(fmt_max(slot, MULT["Mythic"], None, m["stat"], 100))}</div><ul>{lis}</ul></article>')
    P('</div>')

# 3 coverage
P('<h2 id="coverage">3. Synergy coverage</h2>')
P('<p>Three Mythics per synergy in three different slots, plus three General ones; six per slot. Within a synergy each Mythic has its own build identity.</p>')
rows = ['<div class="tw"><table class="mx"><thead><tr><th>Synergy</th><th>Unlock</th>' + ''.join(f'<th>{s}</th>' for s in SLOTS) + '<th>Identities</th></tr></thead><tbody>']
for syn in SYN_ORDER:
    cells = []
    for s in SLOTS:
        ms = [m for m in M if m['syn'] == syn and m['slot'] == s]
        cells.append('<td>' + ''.join(f'<a href="#{mid(m)}">{esc(m["name"])}</a>' for m in ms) + '</td>')
    ids = '; '.join(esc(m['identity'].split(':')[0]) for m in M if m['syn'] == syn)
    rows.append(f'<tr id="cov-{syn.lower()}"><td><span class="chip {SYN_CLASS[syn]}">{syn}</span></td><td class="role">{SYN_PACK[syn]}</td>{"".join(cells)}<td class="role">{ids}</td></tr>')
cnt = collections.Counter(m['slot'] for m in M)
rows.append('<tr><th>Total</th><th></th>' + ''.join(f'<th>{cnt[s]}</th>' for s in SLOTS) + '<th></th></tr></tbody></table></div>')
P(''.join(rows))
cc = [(m, k, t) for m in M for k, (t, e) in enumerate(m['skills']) if CC_RE.search(t)]
P(f'<p><b>Crowd control</b> ({len(cc)} skills, all Mythic S / SS, all once per enemy; their awakened versions stay crowd control at the same rung, no other awakened skill is): ' + '; '.join(f'<a href="#{mid(m)}">{esc(m["name"])}</a> ({LADDER[k]})' for m, k, t in cc) + '.</p>')

# 4 engine
P('<h2 id="engine">4. Engine needs</h2>')
P('<p>Skills without a flag use primitives that exist today (If, EveryN, MaxPerBattle, OnSupernova, OnArcJump, OnShieldHit, OnBlock, OnSidekickAct, OnStackThreshold, OnTurnEnd, ConsumeStatus take:/heal:, ForceSupernova, MultiplyStatus cap:, TickStatus, Execute, Rewind, Echo, Charge, FreeRageAttack, AllyAction, ChargeAllies, Guard, BuffStat until_turn / next_turn / stack:N, Summon, rules burst_at / max / no_expire). "Verify" = probably exists, needs a test. Sorted by how many skills need it.</p>')
rows = ['<div class="tw"><table><thead><tr><th>#</th><th>Engine family</th><th>Skills</th><th>Used by</th></tr></thead><tbody>']
for n, (req, users) in enumerate(sorted(ENG_FAM.items(), key=lambda kv: -len(kv[1])), 1):
    if not users: continue
    links = '; '.join(f'<a href="#{a}" title="{esc(e)}">{esc(lab)}</a>' for lab, a, e in users)
    rows.append(f'<tr id="eng-{n}"><td>{n}</td><td>{esc(req)}</td><td>{len(users)}</td><td>{links}</td></tr>')
rows.append('</tbody></table></div>')
P(''.join(rows))
if UNMATCHED: print('UNMATCHED', UNMATCHED)
n_eng = sum(len(v) for v in ENG.values())
P(f'<p class="muted">{n_eng} of {len(E) + 2*len(L) + 10*len(M)} skill texts carry an engine flag (an awakened version is flagged only when it needs more than its base skill). Content work for all: hand-authored part skills with <code>SkillDef.Text</code> templates replace <code>BuildPartSkills</code>; <code>SkillPowerScale</code> goes (numbers are final values); the PartDef model changes to one def per item and rarity, with five skills and five awakened skills per Mythic (awakening swaps the set; <code>PartDef.AwakenedName</code> stays); <code>DeadEffectsTests</code> must cover every new part skill and the crowd-control allowlist must hold the CC skills above.</p>')

# 5 self-check
sk, errs, dup_err, dup_note, ref_hits, refs = check.run()
P('<h2 id="selfcheck">5. Self-check</h2>')
P(f'<p>Script over all {len(sk)} skill texts (and {len(refs)} run-skill, signature, hero and mount texts for comparison). Checks: ≤ 15 words; one sentence; no decimals; five skills and five awakened versions per Mythic, each awakened version shares its base skill\'s words (overlap ≥ 0.3) but is not identical, adds no crowd control, no sixth skill; unique names; numbers from 20 up on multiples of 5; vague-word list; crowd control only at Mythic S / SS and with "once per enemy"; no plain "+X%" at Legendary / Mythic; no status / Arc / Echo / sidekick mechanics in standard part skills; every Mythic skill uses its identity keyword; pairwise word overlap (Jaccard ≥ 0.6) and a trigger + verb + status key for duplicates; overlap ≥ 0.5 with any existing run skill or kit.</p>')
P(f'<div class="kpi"><div><b>{len(errs)}</b>rule violations</div><div><b>{len(check.run.inter_err)}</b>interplay errors (absolute values, skill / kit copies)</div><div><b>{len(check.run.inter_kept)}</b>skill / kit overlaps kept on purpose</div><div><b>{sum(1 for x in dup_err if x.startswith("EXACT"))}</b>exact duplicates</div><div><b>{len(dup_err)}</b>same-key pairs left (reviewed)</div><div><b>{len(dup_note)}</b>cross-slot / cross-rarity look-alikes</div></div>')
P('<h3>Fixed during the pass</h3><div class="box"><ul>'
  '<li>Rev 3b: the sixth (awakened) skill is gone from every Mythic; the 150 awakened versions were written per skill and checked against their base and every other text. Four new same-key clashes from the first draft were rewritten (Stormbore Drill and Worldbreaker Hammer rage-attack lines, Tempest Coil and Hyperburn Tail turn rhythms, Oracle Visor Echo lines).</li>'
  '<li>Two Legendaries were word-for-word Mythic skills ("Rage attack kills refund 50 rage", "Crits apply Exposed"): the Mythic versions were replaced.</li>'
  '<li>Three Legs Mythics opened with the same sentence ("On dodge: the attacker gains 2 Heat / Chill / Bleed"); only Ashwalkers keeps it, Frostglide Skates scales Chill on your dodge chance, Thornstep Legs loads Bleed on the first hit after a dodge.</li>'
  '<li>Four area rage attacks (Firebrand Blaster, Shatter Cannon, Backlash Hammer, Rage Turbine) cut to one (Rage Turbine, the General rage engine); Shatter Cannon carried two crowd-control skills, now one.</li>'
  '<li>Too close to existing kits or run skills: Afterburner Tail "every 3rd turn +combo chance" (Noodle Knot), Feint Mask "attacker deals less after a counter" (Brace), Last Stand Plate "fatal hit leaves 1 HP" (Last Word / Borrowed Sand), Hindsight Shell "undone hits strike back" (Hour of Need), Hatchery Tail Drone reload (Launch Bay): all replaced.</li>'
  '<li>Crit-damage ramps on four items (Deadeye Visor, two Keen Legendaries, Foresight Visor Echo ramp) cut to one ramp per stat.</li>'
  '</ul></div>')
P('<h3>Remaining intentional overlaps</h3><div class="box"><ul>')
P('<li><b>Deliberate families in the standard sets.</b> The role sets the payload, the slot sets the trigger, so e.g. the Guard Epics all give shields (battle start, every 3rd turn, on dodge, on rage attack) and the Swift items all ramp speed or tempo. Distinct within each slot and rarity.</li>')
P('<li><b>Identity loops inside one Mythic</b> (flagged by the key, kept on purpose): Rampart Carapace counters → shield and shield hit → counter.</li>')
P('<li><b>Two dodge Mythics with crowd control</b> (flagged by the key, kept): Frostglide Skates freezes the most chilled enemy on every 3rd dodge, Timeslip Boots makes the attacker lose a turn with a 25% chance; the script\'s key also pairs Ashwalkers\' no-recoil rule with Frostglide\'s dodge cap (different effects).</li>')
P('<li><b>Same payload, different build</b>: Ashwalkers (Supernova → dodge) and Flicker Treads (combo strike → dodge); Firebrand Blaster (Supernova → another rage attack) and Backlash Hammer (every 3rd counter → free rage attack); sure crits on Deadeye Visor (every 4th hit) and Epic / Legendary Keen items (first hits, every 3rd turn).</li>')
P('<li><b>Shared vocabulary with run skills</b> (same idea, different trigger or scope, never the same text): Legendary overheal → shield (Siphon Line, Second Wind), Quantum Swift Tail extra turn every 5th turn (Borrowed Time is every round with a cost), act first from round 3 (Slipstream is round 1), Deadeye Visor crit execute below 30% (Cull is any hit below 15%), Dynamo Hull when-hit Arc (Arc signature is on hit).</li>')
P('</ul></div>')
P('<details class="box"><summary>All look-alike pairs the script listed (' + str(len(dup_note) + len(dup_err)) + ')</summary><ul>' +
  ''.join(f'<li><small>{esc(x)}</small></li>' for x in dup_err + dup_note) + '</ul></details>')

# 6 questions
P('<h2 id="questions">6. Open questions for the owner</h2><ol class="q">')
Q = [
 ('Mythic release and banners (decided).', 'All 30 Mythics at launch, in 10 Part banners of 3, one per synergy, one banner a week (owner). Pacing uses this rotation.'),
 ('Copy pick (decided).', 'No copy pick (owner).'),
 ('Replicator ratio (decided).', '3 spare Mythics → 1 copy (+ 5 000 Ore) stays (owner).'),
 ('S → SS spacing (decided).', 'Decided by the owner: 2 copies + 2 other Mythics at S+1, 2 + 2 at S+2, 5 + 5 at SS (9 + 9 in all).'),
 ('Surplus Metal (decided).', 'No second sink for now; the late-game Metal surplus is known and accepted (owner).'),
 ('Standard parts and synergies (decided).', 'Standard parts may carry synergy stats where the role fits (Slam arms counter chance, Whip arms combo chance, Keen parts crit chance); their skills stay simple, with no status, Arc, Echo or sidekick mechanics (owner).'),
 ('Arm styles (decided).', 'Arms are interchangeable (owner): no fixed attack style per arm role or arm Mythic and no style multipliers; weapon names are cosmetic; every arm gives ATK plus its role stats (Guard: DEF and counter chance, Swift: speed and combo chance, …).'),
 ('Mythic stat line (decided).', 'Kept as a percentage (owner): +10% fire / ice / Arc / radiation / bleed / Echo damage, +10% alien power, +5% combo / counter / crit chance, +10% HP, +10% rage gain; it grows per level like every stat.'),
 ('Symbiote (decided).', 'Reworked (owner): aliens keep acting on their own; your rage attack also fires every alien\'s ability and each alien that joins loses 50 charge. It stacks with charge-based parts instead of silencing them (see Interplay).'),
]
for i, (h, t) in enumerate(Q, 1):
    P(f'<li id="q-{i}"><b>{esc(h)}</b> {esc(t)}</li>')
P('</ol>')
P('<p class="muted">Sources: shared/RogueStars.Rules/Content (ContentDb.Parts.cs, ContentDb.Run.cs statuses, run skills, signatures), docs/design/parts.md, skill-conventions.md, heroes-mounts.md, run-skill-levels.md, GDD §4–6, and Parts Review 2.</p>')
P('</main>')

open(OUT, 'w').write('\n'.join(parts))
print('written', OUT, sum(len(x) for x in parts), 'chars;', 'engine fams', {k[:40]: len(v) for k, v in ENG_FAM.items()})
