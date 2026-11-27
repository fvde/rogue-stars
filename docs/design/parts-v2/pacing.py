# Mythic ladder pacing sim (design only). Pull-level: published Parts rates, Mythic pity 100, Legendary pity 40,
# 10x rule, featured 80/20 on a weekly banner rotation. Writes pacing.json for build.py.
import random, statistics, sys, json
sys.path.insert(0, '.')
from data import M

RATES = [('Mythic', 1.2), ('Legendary', 3.0), ('Epic', 9.5), ('Rare', 28.0), ('Common', 58.3)]
METAL = {'Common': 1, 'Rare': 2, 'Epic': 6, 'Legendary': 20}
# rung -> (copies of the same Mythic, other Mythics of the same slot, Metal)
LADDER_REQ = {1: (1, 0, 100), 2: (1, 0, 150), 3: (1, 0, 200), 4: (2, 2, 300), 5: (2, 2, 450), 6: (5, 5, 600)}
RUNG_NAME = {0: 'Mythic', 1: 'Mythic+1', 2: 'Mythic+2', 3: 'Mythic S', 4: 'Mythic S+1', 5: 'Mythic S+2', 6: 'Mythic SS'}
TOP = 6
REPL = 3
# (name, year-1 Parts pulls, steady pulls/day after the first ~4 months) from the game simulator
PROFILES = [('Casual F2P', 2450, 7), ('Active F2P', 5900, 17), ('Active Dolphin', 7900, 22), ('Hardcore F2P', 8800, 25), ('Active Whale', 8900, 25)]
TARGET_SLOTS = ['Head', 'Body', 'Tail', 'Legs', 'Arm', 'Arm']
DEFS = [(m['id'], m['slot']) for m in M]

def day_rate(prof, day):
    _, y1, steady = prof
    return (y1 - steady * 245) / 120.0 if day < 120 else steady

# owner (rev 3f): all 30 Mythics at launch, in 10 Part banners of 3 (one per synergy, 3 different slots), one banner a week
BANNERS = [('Inferno Arsenal', 'Fire'), ('Glacier Vault', 'Ice'), ('Storm Works', 'Lightning'), ('Fallout Foundry', 'Radiation'),
           ('Shadow Forge', 'Bleed'), ('Whirlwind Workshop', 'Combo'), ('Bulwark Armory', 'Counter'), ('Hive Hangar', 'Alien'),
           ('Hourglass Depot', 'Time'), ('Proving Grounds', 'General')]
def rotation():
    return [[m['id'] for m in M if m['syn'] == syn] for _, syn in BANNERS]

def run(prof, strategy, seed, years=12, repl=REPL, req=LADDER_REQ, spark=0):
    rng = random.Random(seed)
    rot = rotation()
    slot_of = dict(DEFS)
    targets, pool = [], {s: {} for s in set(slot_of.values())}
    metal = 0.0
    mp = lp = in10 = sp = 0; got_epic = False; crates = 0.0; pulls = 0
    res = {}
    rem_copies = lambda t: sum(req[r][0] for r in req if r > t['rung'])
    nxt_others = lambda t: req[t['rung'] + 1][1] if t['rung'] < TOP else 0
    others_avail = lambda t: sum(n for d, n in pool[t['slot']].items() if d != t['def'])
    def surplus(slot):
        # spare Mythics of a slot beyond what its targets need for their next rung
        return sum(pool[slot].values()) - sum(nxt_others(t) for t in targets if t['slot'] == slot)
    def take_others(t, n):
        tdefs = {x['def'] for x in targets}
        for d in sorted(pool[t['slot']], key=lambda d: d in tdefs):
            if d == t['def'] or n == 0: continue
            k = min(n, pool[t['slot']][d]); pool[t['slot']][d] -= k; n -= k
    def take_any(n):
        for s in pool:
            room = surplus(s)
            for d in list(pool[s]):
                if n == 0 or room <= 0: break
                k = min(n, pool[s][d], room); pool[s][d] -= k; n -= k; room -= k
    def add_mythic(d):
        s = slot_of[d]
        for t in targets:
            if t['def'] == d:
                if t['copies'] < rem_copies(t): t['copies'] += 1; return
                break
        have = [t for t in targets if t['slot'] == s]
        if d not in {t['def'] for t in have} and len(have) < TARGET_SLOTS.count(s):
            targets.append({'def': d, 'slot': s, 'rung': 0, 'copies': 0}); return
        pool[s][d] = pool[s].get(d, 0) + 1
    def advance():
        nonlocal metal
        moved = True
        while moved:
            moved = False
            for t in sorted(targets, key=lambda t: -t['rung']):
                if t['rung'] >= TOP: continue
                c, o, m = req[t['rung'] + 1]
                if others_avail(t) < o or metal < m: continue
                short = c - t['copies']
                if short > 0:
                    if repl and sum(max(0, surplus(s)) for s in pool) >= short * repl:
                        take_any(short * repl); t['copies'] += short
                    else: continue
                t['copies'] -= c; take_others(t, o); metal -= m; t['rung'] += 1; moved = True
    for day in range(int(years * 365)):
        feat = rot[(day // 7) % len(rot)]
        if strategy == 'slotpick' and len(targets) == 6:
            live = [t for t in targets if t['rung'] < TOP]
            if live:
                sl = min(live, key=lambda t: (t['rung'], t['copies']))['slot']
                feat = [d for d, s2 in DEFS if s2 == sl]
        crates += day_rate(prof, day)
        n = int(crates); crates -= n
        for _ in range(n):
            pulls += 1; mp += 1; lp += 1; in10 += 1; sp += 1
            x = rng.random() * 100; acc = 0
            for rar, p in RATES:
                acc += p
                if x < acc: break
            if mp >= 100: rar = 'Mythic'
            elif rar != 'Mythic' and lp >= 40: rar = 'Legendary'
            if in10 == 10 and not got_epic and rar in ('Common', 'Rare'): rar = 'Epic'
            if rar in ('Epic', 'Legendary', 'Mythic'): got_epic = True
            if in10 == 10: in10 = 0; got_epic = False
            if rar == 'Mythic':
                mp = lp = 0
                add_mythic(rng.choice(feat) if rng.random() < 0.8 else rng.choice([d for d, _ in DEFS if d not in feat]))
            else:
                if rar == 'Legendary': lp = 0
                metal += METAL[rar]
            if spark and sp >= spark:
                sp = 0
                live = [t for t in targets if t['rung'] < TOP]
                add_mythic(min(live, key=lambda t: (t['rung'], t['copies']))['def'] if live else rng.choice(feat))
        advance()
        rungs = sorted((t['rung'] for t in targets), reverse=True)
        for name, r in (('S', 3), ('SS', 6)):
            if name not in res and rungs and rungs[0] >= r: res[name] = day
        if 'all6' not in res and len(rungs) == 6 and rungs[-1] >= TOP:
            res['all6'] = day; res['pulls_all6'] = pulls; res['metal_left'] = metal; break
    res.setdefault('metal_left', metal)
    return res

def summary(prof, strategy='steady', n=24, **kw):
    rs = [run(prof, strategy, s, **kw) for s in range(n)]
    out = {k: statistics.median(r.get(k, 10**6) for r in rs) for k in ('S', 'SS', 'all6', 'pulls_all6', 'metal_left')}
    out['unreached'] = sum(1 for r in rs if 'all6' not in r)
    return out

if __name__ == '__main__':
    out = {'profiles': [list(p) for p in PROFILES], 'runs': {}}
    variants = [('steady', 'steady', {}), ('no_metal', 'steady', {'req': {k: (c, o, 0) for k, (c, o, m) in LADDER_REQ.items()}}),
                ('repl2', 'steady', {'repl': 2}), ('norepl', 'steady', {'repl': 0}), ('slotpick', 'slotpick', {}), ('spark300', 'steady', {'spark': 300})]
    for key, strat, kw in variants:
        out['runs'][key] = {}
        for p in PROFILES:
            sm = summary(p, strat, n=24 if key == 'steady' else 12, **kw)
            out['runs'][key][p[0]] = sm
            print(key, p[0], {k: (round(v / 30.4, 1) if k in ('S', 'SS', 'all6') else v) for k, v in sm.items()}, flush=True)
    json.dump(out, open('pacing.json', 'w'), indent=1)
