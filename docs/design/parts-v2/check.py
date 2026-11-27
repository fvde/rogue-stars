import re, sys, itertools, json
sys.path.insert(0, sys.argv[1] if len(sys.argv) > 1 else '.')
from data import *

REPO = '/Users/ferdinandvondeneichen/Projects/rogue-stars/docs/design/'

def all_skills():
    out = []
    for (slot, role), sk in E.items():
        for i, (t, eng) in enumerate(sk):
            out.append(dict(rar='Epic', slot=slot, item=f'{TIER["Epic"]} {role}', role=role, idx=i, tier='Epic', text=t, eng=eng))
    for (slot, role), sk in L.items():
        for i, (t, eng) in enumerate(sk):
            out.append(dict(rar='Legendary', slot=slot, item=f'{TIER["Legendary"]} {role}', role=role, idx=i, tier='Legendary', text=t, eng=eng))
    for m in M:
        for i, (t, eng) in enumerate(m['skills']):
            out.append(dict(rar='Mythic', slot=m['slot'], item=m['name'], role=m['syn'], idx=i, tier=LADDER[i], text=t, eng=eng, myth=m, aw=False))
    # awakened versions last, so their pairs are judged after the base pairs they mirror
    for m in M:
        for i, (t, eng) in enumerate(m['awk']):
            out.append(dict(rar='Mythic', slot=m['slot'], item=m['aw'], role=m['syn'], idx=i, tier=LADDER[i], text=t, eng=eng, myth=m, aw=True,
                            base=m['skills'][i][0]))
    return out

def pid(s):
    # the base skill an entry stands for (an awakened skill stands for its base skill)
    if s['rar'] == 'Mythic': return ('M', s['myth']['id'], s['idx'])
    return (s['rar'], s['slot'], s['role'], s['idx'])

VAGUE = r'\b(may|might|some|several|sharpen\w*|rally|empower\w*|boost\w*|enhance\w*|improve\w*|bolster\w*|greatly|slightly|various|stronger|weaker|bit|lots?)\b'
CC = r'\b(stun|stuns|freeze|freezes|Delay|loses? its next turn|Charm|charms|frozen \d+ more turn)\b'
SYN = r'\b(Heat|Chill\w*|Shock\w*|Arc\w*|Radiation|irradiat\w*|Bleed\w*|combo\w*|counter\w*|sidekick\w*|Echo\w*|Drone\w*|Supernova\w*|Freez\w*|frozen)\b'

# standard SKILLS stay simple: no statuses, Arcs, Echoes, sidekicks / Drones or Supernovas (combo / counter words are fine, owner rev 3f)
STD_SYN = r'\b(Heat|Chill\w*|Shock\w*|Arc\w*|Radiation|irradiat\w*|Bleed\w*|sidekick\w*|Echo\w*|Drone\w*|Supernova\w*|Freez\w*|frozen)\b'

def norm(t):
    t = t.lower()
    t = re.sub(r'\d+', 'n', t)
    t = re.sub(r'[^a-z+% ]', ' ', t)
    return t.split()

STOP = set('the a an you your of for to on each every and is at it its by with in that this'.split())
def toks(t):
    return set(w for w in norm(t) if w not in STOP)

def jac(a, b):
    A, B = toks(a), toks(b)
    return len(A & B) / max(1, len(A | B))

TRIG = [
    ('battle_start', r'^battle start'), ('first_turn', r'first turn'), ('first_hit', r'first (hit|\d+ attacks|attack on you|rage attack)'),
    ('dodge', r'dodg'), ('when_hit', r'^(when hit|hits you take|each hit you take|when a hit|the first hit you take|when a rage attack hits you|each \d+% max hp you lose|when an enemy at|when an irradiated enemy hits|at full rage, a hit)'),
    ('low_hp', r'below \d+% hp'), ('every_nth_turn', r'every \d+(st|nd|rd|th) turn'), ('every_nth_round', r'every \d+(st|nd|rd|th) round'),
    ('each_round', r'^each round'), ('each_turn', r'^each turn'), ('kill', r'kill'), ('crit', r'\bcrit'),
    ('rage_attack', r'rage attack'), ('counter', r'counter'), ('combo', r'combo'), ('shield_event', r'shield (breaks|absorbs)|on your shield'),
    ('arc', r'\barc'), ('sidekick', r'sidekick|drone'), ('echo', r'echo'), ('supernova', r'supernova'), ('status_event', r'apply chill|apply a status|consum'),
]
EFF = [
    ('apply', r'gains? \d+ (heat|chill|shock|radiation|bleed)|appl(y|ies) \d|adds? \d+ (radiation|bleed)|gives? every (other )?enemy \d'),
    ('consume', r'consum'), ('tick', r'tick'), ('spread', r'cop(y|ies)|gives \d+ radiation to|leaves half|move|spread|next enemy (gains|with)'),
    ('force', r'go(es)? supernova'), ('damage', r'(hit|strike|zap)s? .*\d+% atk|\d+% atk|double damage|deals? \+|full damage|hits? every|arc to|as damage'),
    ('shield', r'shield(?! power)'), ('heal', r'heal|regenerat'), ('rage', r'gains? \d+ rage|gives? (you )?\d+ rage|gives? double rage|refund|fill your rage|lose \d+ rage'),
    ('free_rage', r'free rage attack|fires your rage attack|another rage attack'), ('extra_turn', r'extra turn|taken twice'), ('sure_crit', r'always crit|sure crit'),
    ('buff', r'\+\d+%? [a-z ]+(for the battle|until|on your next|for \d|that turn)|for the battle'), ('cc', CC.lower()),
    ('rule', r'instead of|no longer|never|counts? as|becomes|doubled|lasts|always miss|at once\b'), ('act', r'\bacts?\b|charge|hatch'),
    ('undo', r'undo|undone'), ('miss', r'miss'), ('reduce', r'less damage|half damage|no damage|lowers'), ('cleanse', r'remove'),
]
STAT = r'\b(heat|chill|shock|radiation|bleed|exposed|combo|counter|echo|drone|sidekick|crit|dodge|speed|atk|def|rage|shield|regeneration)\b'
def mech(t):
    tl = t.lower()
    trig = next((k for k, r in TRIG if re.search(r, tl)), 'passive')
    effs = tuple(sorted(k for k, r in EFF if re.search(r, tl)))
    stats = tuple(sorted(set(re.findall(STAT, tl.split(':')[-1]))))
    return trig, effs, stats

def ref_texts():
    refs = []
    for line in open(REPO + 'run-skill-levels.md'):
        m = re.match(r'\| ([^|]+) \| `([^`]+)` \| (\w+) \| [^|]+ \| 1 \| (.+) \|$', line)
        if m: refs.append((f'run {m.group(1).strip()}', m.group(4)))
        m = re.match(r'\| (\w[^|]+) \| `(sig_\w+)` \| [^|]+ \| [^|]+ \| ([^|]+) \|', line)
        if m: refs.append((f'signature {m.group(1).strip()}', m.group(3)))
    for line in open(REPO + 'heroes-mounts.md'):
        m = re.match(r'\| \d★ \| \w+ \| ([^|]+) \| ([^|]+) \| `', line)
        if m: refs.append((f'hero {m.group(1).strip()}', m.group(2)))
        m = re.match(r'\| (1★|5★|A5) \| \*\*([^*]+):\*\* ([^|]+) \| \*\*([^*]+):\*\* ([^|]+) \|', line)
        if m:
            refs.append((f'mount {m.group(2)}', m.group(3))); refs.append((f'mount {m.group(4)}', m.group(5)))
    return refs


def corpus():
    """Every sentence of every run / lab skill level, signature (and its awakened levels), hero kit star and mount kit row."""
    out = []
    def addtxt(src, txt):
        txt = re.sub(r'\d+(\.\d+)?%? → ', '', txt)          # keep the top level of "a → b" upgrades
        for sen in re.split(r'(?<=[.!])\s+', txt.strip()):
            sen = sen.strip()
            if len(sen.split()) >= 3: out.append((src, sen))
    for line in open(REPO + 'run-skill-levels.md'):
        m = re.match(r'\| ([^|]+) \| `([^`]+)` \| (\w+) \| ([^|]+) \| \d+ \| (.+) \|$', line)
        if m: addtxt(f'run skill {m.group(1).strip()}', m.group(5)); continue
        m = re.match(r'\| (\w[^|]+) \| `(sig_\w+)` \| [^|]+ \| [^|]+ \| ([^|]+) \| ([^|]+) \|', line)
        if m: addtxt(f'signature {m.group(1).strip()}', m.group(3) + ' ' + m.group(4))
    for line in open(REPO + 'heroes-mounts.md'):
        m = re.match(r'\| \d+★ \| \w+ \| ([^|]+) \| ([^|]+) \| `', line)
        if m: addtxt(f'hero kit {m.group(1).strip()}', m.group(2)); continue
        m = re.match(r'\| (\d★|A5) \| \*\*([^*]+):\*\* ([^|]+) \| \*\*([^*]+):\*\* ([^|]+) \|', line)
        if m: addtxt(f'mount kit {m.group(2)}', m.group(3)); addtxt(f'mount kit {m.group(4)}', m.group(5))
    seen, uniq = set(), []
    for src, sen in out:
        if (src, sen) not in seen: seen.add((src, sen)); uniq.append((src, sen))
    return uniq

# rule values written as absolute numbers (a skill, signature or kit may change the same value): write them relative
ABSOLUTE = [
    (r'instead of \d+', 'absolute override ("instead of N")'),
    (r'\bat \d+ (rage|charge|Heat|Chill|Shock|Bleed|Radiation)\b(?!\+)', 'absolute threshold ("at N ...")'),
    (r'\b\d+\+ rage\b', 'absolute rage threshold (RageMax can change)'),
    (r'\bneeds? \d+ (?!less|more)', 'absolute requirement ("needs N")'),
    (r'\bfires? at \d+', 'absolute trigger value ("fires at N")'),
    (r'\bat \d+ (stacks|Heat|Chill)', 'absolute stack threshold'),
    (r'always miss(?!.*(each battle|once per))', 'guaranteed miss with no limit'),
]

def interplay(sk):
    """Cross-source scan: part texts vs the game's skill, signature and kit sentences."""
    refs = corpus()
    errs, kept = [], []
    allow = globals().get('INTERPLAY_ALLOW', {})
    for s in sk:
        t = s['text']
        for rx, why in ABSOLUTE:
            if re.search(rx, t if 'each battle' not in t else '', re.I): errs.append(f'[{s["item"]} #{s["idx"]+1}{" aw" if s.get("aw") else ""}] {why}: "{t}"')
        ms = mech(t)
        for src, sen in refs:
            j = jac(t, sen)
            mr = mech(sen)
            # a duplicate = same trigger and same payload (effects), or near-verbatim text
            same = mr[0] == ms[0] and ms[0] != 'passive' and mr[1] == ms[1]
            if j >= 0.75 or (same and j >= 0.35):
                line = f'{s["rar"]} {s["slot"]} {s["item"]} #{s["idx"]+1}{" aw" if s.get("aw") else ""} ~ {src} (j={j:.2f}{", same key" if same else ""}): "{t}" | "{sen}"'
                key = next((k for k in allow if k in t), None)
                (kept if key else errs).append(line + (f'  [kept: {allow[key]}]' if key else ''))
    return refs, errs, kept

def run():
    sk = all_skills()
    errs, notes = [], []
    def err(s, msg): errs.append(f'[{s["rar"]} {s["slot"]} {s["item"]} #{s["idx"]+1} {s["tier"]}] {msg}: "{s["text"]}"')
    for s in sk:
        t = s['text']
        w = len(t.split())
        if w > 15: err(s, f'{w} words')
        if not t.endswith('.') or re.search(r'[.!?]\s+\S', t[:-1]): err(s, 'not one sentence')
        if re.search(r'\d+\.\d+', t): err(s, 'decimal')
        for n in re.findall(r'(?<![\w.])(\d+)(?![\w])', t):
            if int(n) >= 20 and int(n) % 5: err(s, f'number {n} not a multiple of 5')
        if re.search(VAGUE, t, re.I): err(s, 'vague word')
        if re.search(CC, t, re.I):
            if s['tier'] not in ('Mythic S', 'Mythic SS'): err(s, 'crowd control below Mythic S')
            if 'once per enemy' not in t: err(s, 'crowd control without "once per enemy"')
            if s.get('aw') and not re.search(CC, s['base'], re.I): err(s, 'awakened skill adds crowd control its base skill does not have')
        if s.get('aw'):
            if t.strip().lower() == s['base'].strip().lower(): err(s, 'awakened skill identical to its base')
            if jac(t, s['base']) < 0.3: err(s, f'awakened skill drifts from its base (overlap {jac(t, s["base"]):.2f}): "{s["base"]}"')
        if s['rar'] in ('Legendary', 'Mythic') and re.match(r'^\+\d+%', t): err(s, 'plain +X% at Legendary/Mythic')
        if s['rar'] in ('Epic', 'Legendary') and re.search(STD_SYN, t, re.I): err(s, 'standard part skill uses a synergy mechanic')
        if s['rar'] == 'Mythic':
            if not any(k.lower() in t.lower() for k in s['myth']['kw']): err(s, f'identity keyword missing {s["myth"]["kw"]}')
        if re.search(r'\b(sixth|6th skill|awakened skill)\b', t, re.I): err(s, 'mentions a sixth / extra skill')
    # counts
    for slot in SLOTS:
        for rar, D in (('Epic', E), ('Legendary', L)):
            n = sum(1 for (sl, r) in D if sl == slot)
            if not 5 <= n <= 6: errs.append(f'{rar} {slot}: {n} items')
    for m in M:
        if len(m['skills']) != 5: errs.append(f'{m["name"]}: {len(m["skills"])} skills')
        if len(m['awk']) != 5: errs.append(f'{m["name"]}: {len(m["awk"])} awakened versions')
    names = [m['name'] for m in M] + [m['aw'] for m in M]
    for n in set(names):
        if names.count(n) > 1: errs.append(f'name used twice: {n}')
    ids = [m['id'] for m in M]
    if len(set(ids)) != len(ids): errs.append('duplicate Mythic id')
    # exact / near duplicates
    dup_err, dup_note = [], []
    seen = set()  # base-equivalent pairs already judged
    mirrored = 0
    for a, b in itertools.combinations(sk, 2):
        pa, pb = pid(a), pid(b)
        if pa == pb: continue  # a skill and its own awakened version
        key = frozenset((pa, pb))
        if a.get('aw') or b.get('aw'):
            if key in seen: mirrored += 1; continue
        j = jac(a['text'], b['text'])
        same_group = a['rar'] == b['rar'] and a['slot'] == b['slot']
        same_item = (a['item'] == b['item'] and a['rar'] == b['rar'] and a['slot'] == b['slot']) or (a['rar'] == b['rar'] == 'Mythic' and a['myth'] is b['myth'])
        ma, mb = mech(a['text']), mech(b['text'])
        tag = f'{a["rar"]} {a["slot"]} {a["item"]} #{a["idx"]+1}{" aw" if a.get("aw") else ""} ~ {b["rar"]} {b["slot"]} {b["item"]} #{b["idx"]+1}{" aw" if b.get("aw") else ""} (j={j:.2f}, {ma[0]}/{"+".join(ma[1])}/{"+".join(ma[2])})'
        line = f'{tag}: "{a["text"]}" | "{b["text"]}"'
        n_before = len(dup_err) + len(dup_note)
        if norm(a['text']) == norm(b['text']):
            dup_err.append('EXACT ' + line)
        elif j >= 0.6 and (same_group or same_item):
            dup_err.append('NEAR(same slot+rarity) ' + line)
        elif ma == mb and ma[0] != 'passive' and same_item:
            dup_err.append('SAME MECHANIC in one item ' + line)
        elif ma == mb and ma[0] != 'passive' and same_group:
            dup_err.append('SAME MECHANIC in slot+rarity ' + line)
        elif j >= 0.6:
            dup_note.append(line)
        elif a['rar'] == 'Mythic' and b['rar'] == 'Mythic' and a['role'] == b['role'] and ma == mb and ma[0] != 'passive':
            dup_note.append('same synergy, same mechanic key ' + line)
        if len(dup_err) + len(dup_note) > n_before and not (a.get('aw') or b.get('aw')): seen.add(key)
    # against run skills / kits
    ref_hits = []
    refs = ref_texts()
    for s in sk:
        for name, rt in refs:
            j = jac(s['text'], rt)
            if j >= 0.5:
                ref_hits.append(f'{s["rar"]} {s["slot"]} {s["item"]} #{s["idx"]+1} ~ {name} (j={j:.2f}): "{s["text"]}" | "{rt.strip()}"')
    run.mirrored = mirrored
    run.corpus, run.inter_err, run.inter_kept = interplay(sk)
    errs += run.inter_err
    return sk, errs, dup_err, dup_note, ref_hits, refs

if __name__ == '__main__':
    sk, errs, dup_err, dup_note, ref_hits, refs = run()
    print(f'skills: {len(sk)}  refs: {len(refs)}')
    print('== RULE ERRORS', len(errs)); print('\n'.join(errs))
    print('== DUP ERRORS', len(dup_err)); print('\n'.join(dup_err))
    print('== DUP NOTES', len(dup_note)); print('\n'.join(dup_note))
    print('== VS RUN SKILLS / KITS', len(ref_hits)); print('\n'.join(ref_hits))
    print('== mirrored pairs skipped (awakened twin of an already-judged base pair):', run.mirrored)
    print('== interplay: corpus', len(run.corpus), 'sentences; errors', len(run.inter_err), '; kept overlaps', len(run.inter_kept)); print('\n'.join(run.inter_kept))
    print('== exact duplicates:', sum(1 for x in dup_err if x.startswith('EXACT')))
