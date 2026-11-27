# Parts redesign rev 3: data (design only). Source for check.py and build.py.

SLOTS = ["Head", "Body", "Tail", "Legs", "Arm"]
SLOT_NOUN = {"Head": "Helm", "Body": "Shell", "Tail": "Tail", "Legs": "Legs"}
SLOT_LANG = {
    "Head": "opener and precision: battle start, first hits, crits",
    "Body": "defence: when hit, shields, low HP",
    "Tail": "rhythm: every Nth turn, each round, ramps",
    "Legs": "evasion: dodges and speed",
    "Arm": "attacks: rage attacks, basic attacks, kills (arms are interchangeable: weapon names are cosmetic, no attack-style multipliers)",
}
ROLES = ["Guard", "Strike", "Swift", "Keen", "Mend", "Fury"]
ROLE_DESC = {
    "Guard": "tank: DEF, shields, less damage taken",
    "Strike": "offense: ATK, extra damage, area hits",
    "Swift": "speed: speed, dodge, extra turns",
    "Keen": "crit: crit chance, sure crits, crit payoffs",
    "Mend": "sustain: life steal, heals, regeneration",
    "Fury": "rage: rage gain, rage attacks",
}
# arm weapon noun per role: cosmetic only (owner rev 3g: no attack styles, arms are interchangeable)
ARM_NOUN = {"Guard": "Hammer", "Strike": "Cannon", "Swift": "Whip", "Keen": "Claw", "Mend": "Drill", "Fury": "Blaster"}
TIER = {"Common": "Scrap", "Rare": "Alloy", "Epic": "Plasma", "Legendary": "Quantum"}
MULT = {"Common": 1.0, "Rare": 1.7, "Epic": 2.4, "Legendary": 3.6, "Mythic": 5.6}
CAP = {"Common": 10, "Rare": 20, "Epic": 30, "Legendary": 50, "Mythic": 70}

# slot core stats at Common level 1 and per level (Common); every item of the slot carries them x rarity multiplier
CORE = {
    "Head": [("HP", 50)],
    "Body": [("HP", 100), ("DEF", 6)],
    "Tail": [("HP", 40), ("ATK", 5)],
    "Legs": [("DEF", 8), ("Speed", 3)],
    "Arm": [("ATK", 15)],
}
PER_LEVEL = {
    "Head": [("HP", 8)],
    "Body": [("HP", 14), ("DEF", 1)],
    "Tail": [("HP", 6), ("ATK", 1)],
    "Legs": [("DEF", 1)],
    "Arm": [("ATK", 2)],
}
ROLE_STAT = {  # Common level 1
    "Guard": ("DEF", 6),
    "Strike": ("ATK", 5),
    "Swift": ("Speed", 4),
    "Keen": ("crit chance %", 3),
    "Mend": ("life steal %", 2),
    "Fury": ("rage gain %", 6),
}

# ---- Epic (1 skill) and Legendary (2 skills): skill text, engine need (None = existing primitives) ----
E = {}  # (slot, role) -> [(text, engine)]
L = {}
def _e(slot, role, t, eng=None): E[(slot, role)] = [(t, eng)]
def _l(slot, role, t1, e1, t2, e2): L[(slot, role)] = [(t1, e1), (t2, e2)]

SURE_CRIT = "Sure crit on a basic / rage attack (today only skill damage has the crit token)"
EXTRA_TURN = "ExtraTurn as an effect with a per-battle count (today extra_turn is a passive rule)"
RAGE_CHARGE = "Charge for the next rage attack (today Charge is for counterattacks only)"
SELF_GUARD = "Guard (next hit -X%) cast by the worm on itself, for N hits"

# HEAD
_e("Head", "Guard", "Battle start: gain a shield of 10% max HP.")
_e("Head", "Strike", "Your first hit each battle deals double damage.")
_e("Head", "Swift", "Battle start: +20 speed for 2 rounds.")
_e("Head", "Keen", "Your first hit on each enemy always crits.", SURE_CRIT)
_e("Head", "Mend", "Each kill heals you 5% max HP.")
_e("Head", "Fury", "Battle start: gain 30 rage.")
_l("Head", "Guard", "Battle start: every enemy deals 10% less damage for 3 rounds.", None,
   "Each kill gives you a shield of 8% max HP.", None)
_l("Head", "Strike", "Battle start: hit every enemy for 50% ATK.", None,
   "Each kill: your next hit deals double damage.", None)
_l("Head", "Swift", "Your first turn each battle is taken twice.", EXTRA_TURN,
   "Enemies slower than you deal 15% less damage to you.", "Condition: attacker slower than the owner")
_l("Head", "Keen", "Your first 3 attacks each battle always crit.", SURE_CRIT,
   "Crits ignore 50% of the target's DEF.", "DEF ignore on crits (a DefIgnore stat or a Damage token)")
_l("Head", "Mend", "Healing past full HP becomes a shield, max 15% max HP.", None,
   "Each crit heals you 2% max HP.", None)
_l("Head", "Fury", "Your first rage attack each battle deals double damage.", None,
   "Each kill gives 25 rage.", None)

# BODY
_e("Body", "Guard", "The first hit you take each round deals 20% less damage.")
_e("Body", "Strike", "When hit: +4% ATK for the battle, max +20%.")
_e("Body", "Swift", "When hit below 50% HP: +30% dodge until your next turn.")
_e("Body", "Keen", "When hit: your next attack gets +15% crit chance.")
_e("Body", "Mend", "When hit: heal 2% max HP, once per round.")
_e("Body", "Fury", "Hits you take give +10 rage.")
_l("Body", "Guard", "No single hit can take more than 20% of your max HP.", "Rule: damage cap per hit (% of max HP)",
   "Once per battle below 30% HP: gain a shield of 30% max HP.", None)
_l("Body", "Strike", "When hit: your next attack also hits every enemy at 30% damage.", "BuffStat next_attack with an area echo of the attack",
   "Once per battle below 30% HP: hit every enemy for 150% ATK.", None)
_l("Body", "Swift", "Once per battle below 30% HP: take an extra turn.", EXTRA_TURN,
   "When hit: +3 speed for the battle, max +15.", None)
_l("Body", "Keen", "Your crits shield you for 5% of their damage.", None,
   "When hit by a crit, your next attack always crits.", SURE_CRIT)
_l("Body", "Mend", "When a hit takes over 15% of your max HP, heal half of it back.", "Condition on the trigger amount (hit > N% max HP)",
   "When hit: remove 1 status stack from yourself.", None)
_l("Body", "Fury", "Below 50% HP, your rage attacks need 20 less rage.", "Rule: RageMax −N (relative, floor 50) gated by self_hp_below",
   "When hit by a crit: gain 25 rage.", "Condition: the incoming hit was a crit")

# TAIL
_e("Tail", "Guard", "Every 3rd turn: gain a shield of 8% max HP.")
_e("Tail", "Strike", "Every 3rd turn: hit the lowest-HP enemy for 80% ATK.")
_e("Tail", "Swift", "Each round: +2 speed for the battle, max +20.")
_e("Tail", "Keen", "Every 3rd turn: your attacks that turn always crit.", SURE_CRIT)
_e("Tail", "Mend", "End of each turn below 50% HP: heal 4% max HP.")
_e("Tail", "Fury", "Every 4th turn: gain 25 rage.")
_l("Tail", "Guard", "Each round you end shielded, your shield grows by 3% max HP.", None,
   "Every 4th round: the next 2 hits on you deal half damage.", SELF_GUARD)
_l("Tail", "Strike", "Each turn: +3% ATK for the battle, max +30%.", None,
   "Every 4th turn: your attacks that turn hit every enemy.", "Rule: basic_aoe for one turn")
_l("Tail", "Swift", "Every 5th turn: take an extra turn.", EXTRA_TURN,
   "From round 3, you act before every enemy.", "Rule: act first from round N")
_l("Tail", "Keen", "Each turn without a crit: +10% crit chance until you crit.", "BuffStat that ends on the owner's crit",
   "Every 4th turn: hit the front enemy with a sure crit for 100% ATK.", None)
_l("Tail", "Mend", "Each round: heal 1% max HP per round passed, max 6%.", "Heal scaled by the round number",
   "Every 4th turn: remove all statuses from yourself.", None)
_l("Tail", "Fury", "Each round: gain 5 rage per living enemy.", "GainRage per living enemy",
   "Your rage attacks target the lowest-HP enemy.", "Rule: rage attack targeting")

# LEGS
_e("Legs", "Guard", "On dodge: gain a shield of 4% max HP.")
_e("Legs", "Strike", "On dodge: hit the attacker for 40% ATK.")
_e("Legs", "Swift", "Round 1: +20% dodge.")
_e("Legs", "Keen", "On dodge: your next attack always crits.", SURE_CRIT)
_e("Legs", "Mend", "On dodge: heal 3% max HP.")
_e("Legs", "Fury", "On dodge: gain 10 rage.")
_l("Legs", "Guard", "After a dodge, the next hit on you deals half damage.", SELF_GUARD,
   "Each hit you take: +3% dodge until you dodge, max +15%.", "BuffStat that ends on the owner's dodge")
_l("Legs", "Strike", "On dodge: your next attack deals double damage.", None,
   "On dodge: hit every enemy for 20% ATK.", None)
_l("Legs", "Swift", "Each dodge: +5 speed for the battle, max +25.", None,
   "The first attack on you each battle always misses.", "Guaranteed dodge for the next N attacks")
_l("Legs", "Keen", "Each dodge: +10% crit damage for the battle, max +50%.", None,
   "On dodge: hit the attacker with a sure crit for 50% ATK.", None)
_l("Legs", "Mend", "Each dodge: +1% regeneration for the battle, max +5%.", None,
   "After a dodge, healing you receive is doubled until your next turn.", "BuffStat HealReceived until_turn")
_l("Legs", "Fury", "Rage attacks give +20% dodge until your next turn.", None,
   "A dodge within 25 rage of full fires your rage attack at once.", "Condition: rage within N of the owner's RageMax")

# ARM
_e("Arm", "Guard", "Rage attacks shield you for 15% max HP.")
_e("Arm", "Strike", "Every 3rd attack deals +50% damage.")
_e("Arm", "Swift", "Each attack: +3 speed for the battle, max +30.")
_e("Arm", "Keen", "Crits also hit another enemy for 30% ATK.")
_e("Arm", "Mend", "Your rage attacks heal you 15% of their damage.")
_e("Arm", "Fury", "Rage attacks refund 20 rage.")
_l("Arm", "Guard", "Rage attacks: take 30% less damage until your next turn.", None,
   "Your hits shield you for 5% of their damage.", None)
_l("Arm", "Strike", "Your attacks also hit the enemy behind for 30% ATK.", None,
   "Every 4th attack ignores the target's DEF.", "DEF ignore on an attack")
_l("Arm", "Swift", "A kill gives you an extra turn, once per battle.", EXTRA_TURN,
   "Every 4th attack is made twice.", None)
_l("Arm", "Keen", "Crits apply 3 Exposed: +1% damage taken per stack.", None,
   "Crit kills give a free attack on the next enemy.", None)
_l("Arm", "Mend", "Hits on enemies below 30% HP heal you 20% of the damage.", None,
   "Overkill damage heals you 50% of the excess.", "Heal from overkill amount")
_l("Arm", "Fury", "Rage attack kills refund 50 rage.", None,
   "Each crit gives 5 rage.", None)

# ---- Mythics: 30 build enablers. skills = M1, M2, M3 (Mythic), S (Mythic S), SS (Mythic SS).
# awk = the awakened version of each of the five skills (same mechanic and identity, a bigger number,
# a lower threshold or a twist on the same mechanic; crowd control only where the base skill has it).
# An awakened skill's engine note lists only what it needs beyond its base skill.
CC_ONCE = "Crowd control once per enemy per battle on a trigger (MaxPerTarget); bosses lose 20 rage instead"
SHIELD_BREAK = "Trigger OnShieldBreak (an OnShieldHit that empties the shield)"
IF_RULE = "If gate on a passive rule (today If gates triggers and cond: bonuses only)"
SPREAD_ONE = "SpreadStatus restricted to one named status, with a cap"
GUAR_DODGE = "Guaranteed dodge for the next N attacks"

M = []
def _m(id, name, aw_name, slot, syn, identity, kw, skills, awk, style=None, stat=None):
    M.append(dict(id=id, name=name, aw=aw_name, slot=slot, syn=syn, identity=identity, kw=kw, skills=skills, awk=awk, style=style, stat=stat))

# FIRE
_m("myth_cinder_crown", "Cinder Crown", "Wildfire Crown", "Head", "Fire",
   "Supernova chain: Heat carries over from enemy to enemy, and Supernovas come sooner.",
   ["Heat", "Supernova"], [
    ("Battle start: the front enemy gains 6 Heat.", None),
    ("When a heated enemy dies, the next enemy gains all its Heat.", "TransferStatus for Heat, onto the next enemy (built for Radiation: Fission)"),
    ("Each Supernova you cause shields you for 10% max HP.", None),
    ("Supernova triggers 2 Heat sooner on every enemy.", "Rule: Supernova threshold −N (burst_delta, additive with burst_at, floor 6)"),
    ("A Supernova kill makes the next enemy with 3+ Heat go Supernova, once per turn.", "Condition: the kill was a Supernova"),
   ], [
    ("Battle start: the first 2 enemies gain 5 Heat.", None),
    ("When a heated enemy dies, every other enemy gains all its Heat.", "TransferStatus onto every other enemy (a copy, not a move)"),
    ("Each Supernova you cause shields you for 20% max HP.", None),
    ("Supernova triggers 4 Heat sooner on every enemy.", None),
    ("A Supernova kill makes the next enemy with 1+ Heat go Supernova, twice per turn.", None),
   ], stat="+10% fire damage")
_m("myth_ashwalkers", "Ashwalkers", "Caldera Walkers", "Legs", "Fire",
   "Dodge ignition: every dodge heats the attacker until it blows up.",
   ["dodge", "Heat", "Supernova"], [
    ("On dodge: the attacker gains 2 Heat.", None),
    ("On dodge: an attacker within 2 Heat of Supernova goes Supernova at once.", "Condition: Heat within N of the target's Supernova threshold"),
    ("Each Supernova you cause: +5% dodge for the battle, max +20%.", None),
    ("Supernovas you set off with a dodge deal you no recoil.", "Rule: no_recoil for Supernovas forced by a dodge trigger"),
    ("Every 4th attack on you misses and gives the attacker 3 Heat.", GUAR_DODGE),
   ], [
    ("On dodge: the attacker gains 4 Heat.", None),
    ("On dodge: an attacker within 5 Heat of Supernova goes Supernova at once.", None),
    ("Each Supernova you cause: +10% dodge for the battle, max +30%.", None),
    ("Supernovas your dodges set off deal double damage and no recoil.", None),
    ("Every 3rd attack on you misses and gives the attacker 5 Heat.", None),
   ], stat="+10% fire damage")
_m("myth_firebrand_blaster", "Firebrand Blaster", "Starburst Blaster", "Arm", "Fire",
   "Rage detonator: rage attacks stack Heat, and Supernovas refill your rage.",
   ["rage", "Heat", "Supernova"], [
    ("Rage attacks apply 4 Heat.", None),
    ("Each Supernova you cause gives you 40 rage.", None),
    ("Rage attacks copy the target's Heat to the enemy behind it.", SPREAD_ONE),
    ("Supernovas set off by your rage attack stun survivors for 1 turn, once per enemy.", CC_ONCE),
    ("Each Supernova your rage attack sets off fires another rage attack, once per turn.", "Condition: Supernova caused by a rage attack"),
   ], [
    ("Rage attacks apply 8 Heat.", None),
    ("Each Supernova you cause gives you 60 rage.", None),
    ("Rage attacks copy the target's Heat to every other enemy.", None),
    ("Any Supernova you cause stuns survivors for 1 turn, once per enemy.", None),
    ("Each Supernova your rage attack sets off fires another rage attack, twice per turn.", None),
   ], stat="+10% fire damage")

# ICE
_m("myth_rimeguard_plate", "Rimeguard Plate", "Glacier Aegis", "Body", "Ice",
   "Frozen fortress: Chill feeds your shield, and a broken shield freezes the attacker.",
   ["shield", "Chill", "freeze"], [
    ("Each round: gain a shield of 2% max HP per chilled enemy.", "Shield per enemy carrying a status"),
    ("When your shield breaks, every enemy gains 3 Chill.", SHIELD_BREAK),
    ("Each time you apply Chill, gain a shield of 2% max HP, once per turn.", None),
    ("When your shield breaks, freeze the attacker for 1 turn, once per enemy.", SHIELD_BREAK + "; " + CC_ONCE),
    ("While you have a shield, Chill on enemies never wears off.", IF_RULE + " (no_expire gated by self_shielded)"),
   ], [
    ("Each round: gain a shield of 4% max HP per chilled enemy.", None),
    ("When your shield breaks, every enemy gains 6 Chill.", None),
    ("Each time you apply Chill, gain a shield of 5% max HP, once per turn.", None),
    ("When your shield breaks, freeze every chilled enemy for 1 turn, once per enemy.", None),
    ("Shielded: Chill on enemies never wears off and grows by 1 each round.", None),
   ], stat="+10% ice damage")
_m("myth_frostglide_skates", "Frostglide Skates", "Whiteout Skates", "Legs", "Ice",
   "Cold dodge: your dodge chance chills the enemy line, and chilled attackers miss more.",
   ["dodge", "Chill", "freeze"], [
    ("Each round: the front enemy gains 1 Chill per 10% dodge you have.", "ApplyStatus stacks scaled by an owner stat"),
    ("You dodge 2% more per Chill stack on the attacker.", "Defensive conditional per stack on the attacker"),
    ("On dodge: hit an attacker with 5+ Chill for 80% ATK.", None),
    ("Every 3rd dodge: freeze the most chilled enemy for 1 turn, once per enemy.", CC_ONCE + "; target: enemy with the most stacks of a status"),
    ("Against chilled attackers, your dodge cap is 5% higher.", "Rule: MaxDodge +N vs attackers with a status (ceiling 85)"),
   ], [
    ("Each round: every enemy gains 1 Chill per 10% dodge you have.", None),
    ("You dodge 3% more per Chill stack on the attacker.", None),
    ("On dodge: hit an attacker with 3+ Chill for 120% ATK.", None),
    ("Every 2nd dodge: freeze the most chilled enemy for 1 turn, once per enemy.", None),
    ("Against chilled attackers, your dodge cap is 10% higher.", None),
   ], stat="+10% ice damage")
_m("myth_shatter_cannon", "Shatter Cannon", "Zero Point Cannon", "Arm", "Ice",
   "Deep-freeze hunter: rage attacks stack Chill, cash it in, and shatter frozen enemies.",
   ["rage", "Chill", "frozen", "freeze"], [
    ("Rage attacks give every enemy 3 Chill.", None),
    ("Rage attacks consume the target's Chill: 15% ATK per stack.", None),
    ("Each round: every enemy with 5+ Chill gains 1 Chill.", None),
    ("Rage attacks freeze a chilled enemy for 1 turn, once per enemy.", CC_ONCE),
    ("Rage attacks on a frozen enemy also hit every chilled enemy.", "Rule: rage attack area gated by the target's status (If has:Freeze / has:Chill)"),
   ], [
    ("Rage attacks give every enemy 5 Chill.", None),
    ("Rage attacks consume the target's Chill: 25% ATK per stack.", None),
    ("Each round: every enemy with 3+ Chill gains 2 Chill.", None),
    ("Rage attacks freeze every chilled enemy for 1 turn, once per enemy.", None),
    ("Rage attacks on a chilled enemy also hit every chilled enemy.", None),
   ], stat="+10% ice damage")

# LIGHTNING
_m("myth_dynamo_hull", "Dynamo Hull", "Stormheart Hull", "Body", "Lightning",
   "Shock armor: hits on you shock the attacker and Arc back at the enemy line.",
   ["hit", "Shock", "Arc"], [
    ("When hit: the attacker gains 1 Shock.", None),
    ("When hit: Arc to 1 enemy for 40% ATK.", None),
    ("Each Arc jump gives you a shield of 2% max HP, once per turn.", None),
    ("Stun an enemy with 4+ Shock that hits you for 1 turn, once per enemy.", CC_ONCE),
    ("Every 6th Arc jump: every enemy's Shock discharges for 30% ATK per stack.", "ConsumeStatus on all enemies for Shock (verify)"),
   ], [
    ("When hit: the attacker gains 2 Shock.", None),
    ("When hit: Arc to 2 enemies for 60% ATK.", None),
    ("Each Arc jump gives you a shield of 2% max HP, three times per turn.", None),
    ("Stun an enemy at 3+ Shock that hits you for 1 turn, once per enemy.", None),
    ("Every 4th Arc jump: every enemy's Shock discharges for 30% ATK per stack.", None),
   ], stat="+10% Arc damage")
_m("myth_storm_coil", "Storm Coil", "Tempest Coil", "Tail", "Lightning",
   "Arc storm: a timed Arc volley that grows as Shock builds up.",
   ["Arc", "Shock", "volley"], [
    ("Every 3rd turn: Arc to every enemy for 40% ATK.", None),
    ("Each Arc jump: +2% Arc damage for the battle, max +40%.", None),
    ("Each round: the enemy with the least Shock gains 1 Shock.", "Target: enemy with the fewest stacks of a status"),
    ("Your Arc volley stuns enemies with 4+ Shock for 1 turn, once per enemy.", CC_ONCE),
    ("Your Arc volley fires every 2nd turn instead of every 3rd.", None),
   ], [
    ("Every 3rd turn: Arc to every enemy for 60% ATK.", None),
    ("Each Arc jump: +3% Arc damage for the battle, max +60%.", None),
    ("Each round: the 2 enemies with the least Shock gain 1 Shock.", None),
    ("Your Arc volley stuns enemies at 3+ Shock for 1 turn, once per enemy.", None),
    ("Your Arc volley fires every 2nd turn and on your first turn.", None),
   ], stat="+10% Arc damage")
_m("myth_arcwright_drill", "Arcwright Drill", "Stormbore Drill", "Arm", "Lightning",
   "Conductor: your basic attacks Arc, and your Arcs can crit.",
   ["Arc", "basic attack"], [
    ("Basic attacks Arc to 1 more enemy than they otherwise would.", "Stat: +N Arc jumps on basic attacks (relative; adds to the Arc signature)"),
    ("Your Arc jumps can crit.", "Rule: Arc jumps roll the owner's crit"),
    ("Arc jumps onto enemies with 3+ Shock heal you 2% max HP, once per turn.", None),
    ("Every 5th Arc jump stuns its target for 1 turn, once per enemy.", CC_ONCE),
    ("Arc jumps that crit apply 1 Shock.", None),
   ], [
    ("Basic attacks Arc to 2 more enemies than they otherwise would.", None),
    ("Your Arc jumps can crit and get +20% crit chance.", None),
    ("Arc jumps onto shocked enemies heal you 2% max HP, once per turn.", None),
    ("Every 3rd Arc jump stuns its target for 1 turn, once per enemy.", None),
    ("Arc jumps that crit apply 2 Shock.", None),
   ], stat="+10% Arc damage")

# RADIATION
_m("myth_isotope_visor", "Isotope Visor", "Half-Life Visor", "Head", "Radiation",
   "Fallout spreader: Radiation spreads from enemy to enemy.",
   ["Radiation", "spread"], [
    ("Your first hit on each enemy applies 6 Radiation.", None),
    ("Crits copy 20% of the target's Radiation to every other enemy, max 10.", SPREAD_ONE),
    ("Your hits also give 1 Radiation to the enemy behind the target.", None),
    ("Each round: the most irradiated enemy gives 5 Radiation to every other enemy.", SPREAD_ONE),
    ("Radiation you spread to other enemies is doubled.", "Rule: double every spread / copy of a status"),
   ], [
    ("Your first hit on each enemy applies 10 Radiation.", None),
    ("Crits copy 40% of the target's Radiation to every other enemy, max 20.", None),
    ("Your hits also give 2 Radiation to the 2 enemies behind the target.", None),
    ("Each round: the most irradiated enemy gives 10 Radiation to every other enemy.", None),
    ("Radiation you spread to other enemies is tripled.", None),
   ], stat="+10% radiation damage")
_m("myth_hotcell_tank", "Hotcell Tank", "Meltdown Tank", "Body", "Radiation",
   "Fallout tank: hits on you irradiate the enemy line, and Radiation keeps you standing.",
   ["Radiation", "irradiat", "hit"], [
    ("When hit: every enemy gains 1 Radiation.", None),
    ("Each round: heal 1% max HP per 20 Radiation on all enemies, max 5%.", "Heal per total stacks of a status on all enemies"),
    ("Enemies with 20+ Radiation deal 15% less damage to you.", "Defensive conditional vs the attacker's status"),
    ("Once per battle below 30% HP: double every enemy's Radiation, max +40.", None),
    ("When an irradiated enemy hits you, its Radiation ticks at once.", None),
   ], [
    ("When hit: every enemy gains 2 Radiation.", None),
    ("Each round: heal 1% max HP per 10 Radiation on all enemies, max 10%.", None),
    ("Enemies with 10+ Radiation deal 25% less damage to you.", None),
    ("Once per battle below 50% HP: double every enemy's Radiation, max +60.", None),
    ("When an irradiated enemy hits you, its Radiation ticks twice at once.", None),
   ], stat="+10% radiation damage")
_m("myth_decay_stinger", "Decay Stinger", "Widowmaker Stinger", "Tail", "Radiation",
   "Fast decay: Radiation ticks extra times on your rhythm and compounds.",
   ["Radiation", "tick"], [
    ("Every 2nd turn: the front enemy gains 5 Radiation.", None),
    ("Every 4th turn: every enemy's Radiation ticks now.", None),
    ("Rage attacks make the target's Radiation tick twice.", None),
    ("Non-boss enemies with 40+ Radiation die below 20% HP.", None),
    ("Each Radiation tick adds 1 Radiation to that enemy.", "OnStatusDamage per tick with ApplyStatus on that enemy"),
   ], [
    ("Every 2nd turn: the front enemy gains 10 Radiation.", None),
    ("Every 3rd turn: every enemy's Radiation ticks now.", None),
    ("Rage attacks make the target's Radiation tick twice, at +50% damage.", None),
    ("Non-boss enemies with 30+ Radiation die below 30% HP.", None),
    ("Each Radiation tick adds 2 Radiation to that enemy.", None),
   ], stat="+10% radiation damage")

# BLEED
_m("myth_bloodtide_fin", "Bloodtide Fin", "Crimson Tide Fin", "Tail", "Bleed",
   "Blood tide: Bleed on every enemy on a rhythm, cashed in at the end of your turn.",
   ["Bleed"], [
    ("Every 3rd turn: every enemy gains 2 Bleed.", None),
    ("End of your turn: consume 8+ Bleed on the front enemy, 25% ATK per stack.", None),
    ("Consuming Bleed shields you for 20% of the damage.", "Shield of_amount on OnStatusConsumed"),
    ("Consuming Bleed leaves half the stacks on another enemy.", "OnStatusConsumed amount → ApplyStatus per_unit on another enemy"),
    ("Bleed on enemies at max Bleed ticks twice.", IF_RULE + " (dot rule gated by max:Bleed)"),
   ], [
    ("Every 3rd turn: every enemy gains 4 Bleed.", None),
    ("End of your turn: consume 5+ Bleed on the front enemy, 30% ATK per stack.", None),
    ("Consuming Bleed shields you for 40% of the damage.", None),
    ("Consuming Bleed leaves half the stacks on every other enemy.", None),
    ("Bleed on enemies with 5+ Bleed ticks twice.", None),
   ], stat="+10% bleed damage")
_m("myth_thornstep_legs", "Thornstep Legs", "Briarstorm Legs", "Legs", "Bleed",
   "Bleeding dodge: each dodge sets up Bleed and tears the attacker's wounds open.",
   ["dodge", "Bleed"], [
    ("Your first hit after a dodge applies 3 Bleed.", "BuffStat-style next_hit rider with ApplyStatus (CarrySignatures next_hit exists; verify)"),
    ("On dodge: a bleeding attacker's Bleed ticks now.", None),
    ("Bleeding attackers that miss you lose 10 rage.", "Negative GainRage on an enemy (verify)"),
    ("On dodge: every other bleeding enemy gains 1 Bleed.", None),
    ("On dodge: consume the attacker's Bleed, 30% ATK per stack.", None),
   ], [
    ("Your first hit after a dodge applies 6 Bleed.", None),
    ("On dodge: every bleeding enemy's Bleed ticks now.", None),
    ("Bleeding attackers that miss you lose 20 rage.", None),
    ("On dodge: every other enemy gains 2 Bleed.", None),
    ("On dodge: consume the attacker's Bleed, 45% ATK per stack.", None),
   ], stat="+10% bleed damage")
_m("myth_bloodletter_claw", "Bloodletter Claw", "Reaper Claw", "Arm", "Bleed",
   "Blood harvest: crits stack Bleed, and every Bleed you finish heals you.",
   ["crit", "Bleed"], [
    ("Crits apply 3 Bleed.", None),
    ("Crits on an enemy at max Bleed consume it, 20% ATK per stack.", None),
    ("Consuming Bleed heals you 2% max HP per stack.", None),
    ("When a bleeding enemy dies, heal 2% max HP per Bleed stack.", "Heal per stack of a dying enemy's status"),
    ("Crits make Bleed tick on every enemy.", None),
   ], [
    ("Crits apply 5 Bleed.", None),
    ("Crits on an enemy with 7+ Bleed consume it, 30% ATK per stack.", None),
    ("Consuming Bleed heals you 3% max HP per stack.", None),
    ("When a bleeding enemy dies, heal 4% max HP per Bleed stack.", None),
    ("Crits make Bleed tick twice on every enemy.", None),
   ], stat="+10% bleed damage")

# COMBO
_m("myth_afterburner_tail", "Afterburner Tail", "Hyperburn Tail", "Tail", "Combo",
   "Burst turns: every 3rd turn is a long combo chain that pays out big.",
   ["combo", "3rd turn"], [
    ("Every 3rd turn: your first attack strikes 3 times in a row.", "Forced combo strikes (a set strike count)"),
    ("A turn with 3+ combo strikes gives 20 rage.", None),
    ("On every 3rd turn, each combo strike shields you for 2% max HP.", None),
    ("A turn with 4+ combo strikes gives an extra turn, once per battle.", EXTRA_TURN),
    ("On every 3rd turn, your combo strikes deal full attack damage.", "Rule: combo multiplier override, gated by turn count"),
   ], [
    ("Every 3rd turn: your first attack strikes 5 times in a row.", None),
    ("A turn with 2+ combo strikes gives 30 rage.", None),
    ("On every 3rd turn, each combo strike shields you for 4% max HP.", None),
    ("A turn with 4+ combo strikes gives an extra turn, twice per battle.", None),
    ("On every 2nd turn, your combo strikes deal full attack damage.", None),
   ], stat="+5% combo chance")
_m("myth_flicker_treads", "Flicker Treads", "Phantom Treads", "Legs", "Combo",
   "Evasive flurry: dodges feed your combos, and combos feed your dodge.",
   ["dodge", "combo"], [
    ("On dodge: +20% combo chance on your next turn.", None),
    ("Each combo strike: +2% dodge until your next turn, max +10%.", None),
    ("On dodge: 50% chance to strike the attacker, and that strike can combo.", "Strike with the Damage combo token (exists for sidekicks; verify)"),
    ("After a turn with 3+ combo strikes, the next hit on you misses.", GUAR_DODGE),
    ("Half your dodge chance also counts as combo chance.", "Rule: stat counts as another stat (Perfect Form does combo → counter), with a share"),
   ], [
    ("On dodge: +35% combo chance on your next turn.", None),
    ("Each combo strike: +4% dodge until your next turn, max +20%.", None),
    ("On dodge: strike the attacker, and that strike can combo.", None),
    ("After a turn with 3+ combo strikes, the next 2 hits on you miss.", None),
    ("Your full dodge chance also counts as combo chance.", None),
   ], stat="+5% combo chance")
_m("myth_chainlash_whip", "Chainlash Whip", "Maelstrom Whip", "Arm", "Combo",
   "Chain cleaver: combo strikes spill over onto other enemies.",
   ["combo"], [
    ("Each combo strike also hits another enemy for 30% ATK.", None),
    ("Each combo strike deals 10% more than the one before.", "Negative combo_falloff (strikes ramp up)"),
    ("A combo strike that kills carries the chain on to the next enemy.", "Rule: combo chain continues after a kill"),
    ("Your combo strikes after the first hit the lowest-HP enemy.", "Rule: combo strike targeting"),
    ("Every 10th combo strike in a battle is a free rage attack.", None),
   ], [
    ("Each combo strike also hits another enemy for 50% ATK.", None),
    ("Each combo strike deals 20% more than the one before.", None),
    ("A combo strike that kills carries the chain on and adds 1 strike.", None),
    ("Your combo strikes after the first hit the lowest-HP enemy for double damage.", None),
    ("Every 6th combo strike in a battle is a free rage attack.", None),
   ], stat="+5% combo chance")

# COUNTER
_m("myth_feint_mask", "Feint Mask", "Grandmaster Mask", "Head", "Counter",
   "Read and punish: counters cripple the attacker.",
   ["counter"], [
    ("The first hit you take each round: +40% counter chance against it.", None),
    ("After you counter, the attacker can't crit until its next turn.", "BuffStat until_turn on an enemy (verify)"),
    ("Your counterattacks make the attacker lose 10 rage.", "Negative GainRage on an enemy (verify)"),
    ("Countering a rage attack reflects 50% of its damage.", "Condition: the countered attack was a rage attack"),
    ("Your 3rd counter on one enemy makes it lose its next turn, once per enemy.", "Count per target; " + CC_ONCE),
   ], [
    ("The first hit you take each round: +70% counter chance against it.", None),
    ("After you counter, the attacker can't crit for its next 2 turns.", None),
    ("Your counterattacks make the attacker lose 20 rage.", None),
    ("Countering a rage attack reflects 100% of its damage.", None),
    ("Your 2nd counter on one enemy makes it lose its next turn, once per enemy.", None),
   ], stat="+5% counter chance")
_m("myth_rampart_carapace", "Rampart Carapace", "Citadel Carapace", "Body", "Counter",
   "Shield counters: counterattacks raise shields, and shields trigger counters.",
   ["counter", "shield"], [
    ("Counterattacks double your current shield, max 15% max HP.", "Shield multiply with a cap (% max HP)"),
    ("Once per round, a hit on your shield is always countered.", None),
    ("Counterattacks add 5% of your max HP as damage.", "Damage scaled on the owner's max HP"),
    ("When your shield breaks, counter every hit until your next turn.", SHIELD_BREAK),
    ("While you have a shield, your counterattacks hit every enemy.", IF_RULE + " (counter_all-style area counters)"),
   ], [
    ("Counterattacks double your current shield, max 25% max HP.", None),
    ("Twice per round, a hit on your shield is always countered.", None),
    ("Counterattacks add 10% of your max HP as damage.", None),
    ("When your shield breaks, counter every hit for the next 2 rounds.", None),
    ("Your counterattacks hit every enemy, shielded or not.", None),
   ], stat="+5% counter chance")
_m("myth_backlash_hammer", "Backlash Hammer", "Worldbreaker Hammer", "Arm", "Counter",
   "Counter-rage: counters charge your rage attacks.",
   ["counter", "rage"], [
    ("Counterattacks give 15 rage.", None),
    ("Each counter: your next rage attack deals +10%, max +50%.", RAGE_CHARGE),
    ("Rage attacks give +25% counter chance until your next turn.", None),
    ("Every 3rd counterattack is a free rage attack.", None),
    ("At full rage, a hit on you fires your rage attack as a counter.", "Rule: rage attack fired as a counter"),
   ], [
    ("Counterattacks give 25 rage.", None),
    ("Each counter: your next rage attack deals +20%, max +100%.", None),
    ("Rage attacks give +50% counter chance until your next turn.", None),
    ("Every 2nd counterattack is a free rage attack.", None),
    ("At full rage, a hit on you fires your rage attack as a +50% counter.", None),
   ], stat="+5% counter chance")

# ALIEN
_m("myth_swarmcaller_antenna", "Swarmcaller Antenna", "Swarmlord Antenna", "Head", "Alien",
   "Commander: your sidekicks act far more often, driven by your crits and statuses.",
   ["sidekick"], [
    ("Battle start: every sidekick acts.", None),
    ("Your crits make a random sidekick act, once per turn.", None),
    ("When you apply a status, every sidekick gains 10 charge, once per turn.", None),
    ("Your first sidekick action each round happens twice.", "AllyAction repeat of the first action per round"),
    ("Sidekicks need 25 less charge to act.", "Rule: SidekickActAt −N (relative, floor 50)"),
   ], [
    ("Battle start: every sidekick acts twice.", None),
    ("Your crits make a random sidekick act, twice per turn.", None),
    ("When you apply a status, every sidekick gains 20 charge, once per turn.", None),
    ("Your first 2 sidekick actions each round happen twice.", None),
    ("Sidekicks need 50 less charge to act.", None),
   ], stat="+10% alien power")
_m("myth_broodguard_legs", "Broodguard Legs", "Broodmother Legs", "Legs", "Alien",
   "Guardian swarm: dodges call sidekick guards, and every guarded hit strikes back.",
   ["dodge", "sidekick", "guard"], [
    ("On dodge: a sidekick guards you, the next hit deals 30% less.", SELF_GUARD),
    ("When a sidekick guards or blocks a hit, strike the attacker for 50% ATK.", None),
    ("Each guarded or blocked hit makes a sidekick act.", None),
    ("On dodge: every sidekick gains 20 charge.", None),
    ("Sidekick guards stop the whole hit instead of part of it.", "Rule: Guard value 100"),
   ], [
    ("On dodge: a sidekick guards you, the next hit deals 50% less.", None),
    ("When a sidekick guards or blocks a hit, strike the attacker for 80% ATK.", None),
    ("Each guarded or blocked hit makes 2 sidekicks act.", None),
    ("On dodge: every sidekick gains 35 charge.", None),
    ("Sidekick guards stop the whole hit and reflect 50% of it.", "Guard value 100 with a reflect of the guarded amount"),
   ], stat="+10% alien power")
_m("myth_hatchery_tail", "Hatchery Tail", "Swarmqueen Tail", "Tail", "Alien",
   "Drone hatchery: hatch Drones on your rhythm, and they guard and carry your statuses.",
   ["Drone"], [
    ("Every 3rd turn: hatch a Drone that zaps for 25% ATK, max 3.", None),
    ("Rage attacks make every Drone act.", "AllyAction filtered to Drones"),
    ("Each Drone guards one hit for 30%, then breaks.", "Summon guard:N (exists for the Hive Carrier)"),
    ("Drone zaps apply 1 stack of a random status you own.", None),
    ("Battle start: hatch Drones up to the max.", None),
   ], [
    ("Every 2nd turn: hatch a Drone that zaps for 25% ATK, max 4.", None),
    ("Rage attacks make every Drone act twice.", None),
    ("Each Drone guards 2 hits for 50%, then breaks.", None),
    ("Drone zaps apply 2 stacks of a random status you own.", None),
    ("Battle start and every 5th round: hatch Drones up to the max.", None),
   ], stat="+10% alien power")

# TIME
_m("myth_foresight_visor", "Foresight Visor", "Oracle Visor", "Head", "Time",
   "Echo opener: Echoes from your first turn on, after every rage attack, and in chains.",
   ["Echo"], [
    ("Your first turn each battle ends with an Echo.", None),
    ("Your rage attacks are followed by an Echo.", None),
    ("Echoes on an enemy above 70% HP deal double damage.", None),
    ("Each Echo: 25% chance to echo again.", "Trigger OnEcho (depth-capped)"),
    ("Echoes also strike every other enemy for 50% damage.", "Echo area token"),
   ], [
    ("Your first turn each battle ends with 2 Echoes.", None),
    ("Your rage attacks are followed by an Echo at double damage.", None),
    ("Echoes on an enemy above 50% HP deal double damage.", None),
    ("Each Echo: 50% chance to echo again.", None),
    ("Echoes also strike every other enemy for 80% damage.", None),
   ], stat="+10% Echo damage")
_m("myth_hindsight_shell", "Hindsight Shell", "Eternity Shell", "Body", "Time",
   "Rewind tank: undo the hits that hurt most, and turn them against the attacker.",
   ["undo", "undone"], [
    ("The first hit you take each battle is undone.", None),
    ("Each undone hit gives you 20 rage.", "Trigger: a hit was undone (Rewind)"),
    ("Each undone hit shields you for 5% max HP.", "Trigger: a hit was undone (Rewind)"),
    ("The first rage attack each enemy lands on you is undone.", "Condition: incoming rage attack; Rewind per enemy"),
    ("Hits you undo are dealt to the attacker instead.", "Rewind reflect token (Rewind reflect:N exists; verify full amount)"),
   ], [
    ("The first 2 hits you take each battle are undone.", None),
    ("Each undone hit gives you 40 rage.", None),
    ("Each undone hit shields you for 10% max HP.", None),
    ("The first 2 rage attacks each enemy lands on you are undone.", None),
    ("Hits you undo are dealt to the attacker instead, at double damage.", None),
   ], stat="+10% Echo damage")
_m("myth_timeslip_boots", "Timeslip Boots", "Event Horizon Boots", "Legs", "Time",
   "Tempo thief: every dodge steals time from the attacker.",
   ["dodge", "turn", "Echo"], [
    ("On dodge: the attacker loses 10 speed for the battle, max 30.", None),
    ("Every 3rd dodge: take an extra turn.", EXTRA_TURN),
    ("On dodge: repeat your last attack at 40% (an Echo).", None),
    ("On dodge: 25% chance the attacker loses its next turn, once per enemy.", CC_ONCE),
    ("Your extra turns end with an Echo.", None),
   ], [
    ("On dodge: the attacker loses 15 speed for the battle, max 45.", None),
    ("Every 2nd dodge: take an extra turn.", None),
    ("On dodge: repeat your last attack at 70% (an Echo).", None),
    ("On dodge: 50% chance the attacker loses its next turn, once per enemy.", None),
    ("Your extra turns end with 2 Echoes.", None),
   ], stat="+10% Echo damage")

# GENERAL
_m("myth_deadeye_visor", "Deadeye Visor", "Omniscope Visor", "Head", "General",
   "Crit executioner: sure crits that snowball and finish enemies.",
   ["crit"], [
    ("Every 4th hit is a sure crit.", SURE_CRIT),
    ("Each crit: +2% crit chance for the battle, max +30%.", None),
    ("Crit kills give 25 rage.", None),
    ("Crits on non-boss enemies below 30% HP kill them.", None),
    ("Crit chance above 100% becomes crit damage, 2 per point.", "Rule: overflow crit chance → crit damage"),
   ], [
    ("Every 3rd hit is a sure crit.", None),
    ("Each crit: +3% crit chance for the battle, max +45%.", None),
    ("Crit kills give 40 rage.", None),
    ("Crits on non-boss enemies below 40% HP kill them.", None),
    ("Crit chance above 100% becomes crit damage, 3 per point.", None),
   ], stat="+5% crit chance")
_m("myth_last_stand_plate", "Last Stand Plate", "Undying Plate", "Body", "General",
   "Last stand: the lower your HP, the harder you hit.",
   ["HP"], [
    ("Your hits deal +1% damage per 2% HP you are missing.", "Conditional bonus per % of the owner's missing HP"),
    ("Once per battle below 30% HP: gain 100 rage.", None),
    ("Below 50% HP: kills heal you 15% max HP.", None),
    ("Once per battle below 30% HP: take 2 extra turns.", EXTRA_TURN),
    ("Below 30% HP: your attacks hit every enemy.", IF_RULE + " (basic_aoe gated by self_hp_below:30)"),
   ], [
    ("Your hits deal +1% damage per 1% HP you are missing.", None),
    ("Once per battle below 50% HP: gain 100 rage.", None),
    ("Below 50% HP: kills heal you 25% max HP.", None),
    ("Once per battle below 30% HP: take 3 extra turns.", None),
    ("Below 50% HP: your attacks hit every enemy.", None),
   ], stat="+10% HP")
_m("myth_rage_turbine", "Rage Turbine", "Overdrive Turbine", "Tail", "General",
   "Rage engine: your rage attack comes far more often.",
   ["rage"], [
    ("Each turn: gain 10 extra rage.", None),
    ("Every 3rd rage attack is followed by a free rage attack.", None),
    ("Rage attacks heal you 5% max HP.", None),
    ("Rage attacks hit every enemy at 60% damage.", None),
    ("Rage attacks need 25 less rage.", "Rule: RageMax −N for the owner (relative, floor 50)"),
   ], [
    ("Each turn: gain 20 extra rage.", None),
    ("Every 2nd rage attack is followed by a free rage attack.", None),
    ("Rage attacks heal you 10% max HP.", None),
    ("Rage attacks hit every enemy at full damage.", None),
    ("Rage attacks need 40 less rage.", None),
   ], stat="+10% rage gain")

LADDER = ["Mythic", "Mythic", "Mythic", "Mythic S", "Mythic SS"]

# ---- Interplay with run skills, signatures and kits (rev 3d). check.py flags a part text that shares trigger and payload with a
# game text; these overlaps are kept on purpose (key = a phrase of the part text, value = why it complements).
INTERPLAY_ALLOW = {
    "Battle start: gain a shield": "stacks by rule: battle-start shields from all sources add up",
    "Healing past full HP becomes a shield": "stacks by rule: overheal-shield caps from all sources add up",
    "Rage attacks refund": "stacks by rule: rage refunds from all sources add up",
    "Hits you undo are dealt": "stacks by rule: with Hour of Need both strike back (the full undone hit and 50% ATK)",
    "Drone guards": "stacks by rule: Drone guards from parts and the Hive Carrier each guard their own hit",
    "times in a row": "stacks by rule: on the Echo signature's turn both fire, the burst turn is the big turn",
    "combo strikes gives": "stacks by rule: rage from Momentum and this part adds up",
    "combo strike deals": "opposite of Endless Chain's falloff; both are relative and add up per strike (+10% and -15% = -5%)",
    "counter chance against it": "same trigger as Mirror Carapace (first hit each round), different payload: a counter chance, not a reflect",
    "Supernova triggers": "a relative threshold modifier; Chain Reaction refills Heat after a Supernova (different payload)",
    "Chill gains": "a Chill ramp; Fallout Aura adds Radiation per status (different status and condition)",
    "least Shock gains": "a Shock spreader; Fallout Aura adds Radiation per status (different status)",
    "Radiation die below": "executes never add up: the highest threshold of all sources applies (Cull 15-24% any hit, this one needs 30+/40+ Radiation)",
    "Crits on non-boss enemies below": "executes never add up: the highest threshold applies; this one needs a crit (Cull: any hit, lower threshold)",
    "first turn each battle ends with": "Opening Flurry buffs combo chance on the same turn; this adds an Echo",
    "Rage attacks hit every enemy at": "Bloodletting adds a single-target hit; this turns the rage attack itself into an area attack",
    "double your current shield": "grows an existing shield (capped) instead of granting one; Bulwark Protocol and Rebound Plating grant shields on counter",
    "Arc jumps can crit": "a crit rule for Arc jumps; flat crit-chance skills raise every hit",
}

# (anchor, item, old text, new text, why) — the rev 3d interplay pass
INTERPLAY_CHANGES = [
 ("e-head-guard", "Plasma Guard Helm", "Battle start: gain a shield of 10% max HP.", "Battle start: +30% DEF for 2 rounds.", "Copy of Frost Armor, Starguard (Star Knight) and Bulwark Protocol: all battle-start shields."),
 ("e-body-fury", "Plasma Fury Shell", "Hits you take give 20 rage instead of 10.", "Hits you take give +10 rage.", "Absolute override of rage per hit; now adds to the base and to Sparkplug's Ignition."),
 ("e-arm-fury", "Plasma Fury Blaster", "Rage attacks refund 20 rage.", "Every 3rd attack gives you 15 rage.", "Copy of the Magma Barge's Pressure Valve refund."),
 ("l-head-mend", "Quantum Mend Helm", "Healing past full HP becomes a shield, max 15% max HP.", "Your first 3 hits each battle heal you 30% of their damage.", "Copy of Siphon Line (Siphon Hauler 5★) and Second Wind: overheal into shield."),
 ("l-body-fury", "Quantum Fury Shell", "Below 50% HP, your rage attack fires at 80 rage.", "Below 50% HP, your rage attacks need 20 less rage.", "Absolute rage cost; now relative and stacks with Rage Turbine (floor 50)."),
 ("l-legs-fury", "Quantum Fury Legs", "A dodge at 75+ rage fires your rage attack at once.", "A dodge within 25 rage of full fires your rage attack at once.", "\"75+\" broke once the rage cost is lowered (at 75 the attack has already fired)."),
 ("m-cinder-crown", "Cinder Crown (S, awakened S)", "Enemies go Supernova at 8 Heat instead of 10. / … at 6 Heat …", "Supernova triggers 2 Heat sooner on every enemy. / … 4 Heat sooner …", "Absolute threshold clashed with Controlled Burn (\"needs 12 Heat\"): relative now, floor 6."),
 ("m-cinder-crown", "Cinder Crown (SS, awakened SS)", "A Supernova kill makes the next enemy with 3+ / 1+ Heat go Supernova.", "… , once per turn. / … , twice per turn.", "Chain cap: with Chain Reaction and the Ignite signature a kill could cascade through a whole wave."),
 ("m-cinder-crown", "Wildfire Crown (awakened 1)", "Battle start: every enemy gains 6 Heat.", "Battle start: the first 2 enemies gain 5 Heat.", "At the awakened threshold (6) it detonated every enemy at battle start."),
 ("m-ashwalkers", "Ashwalkers (2, awakened 2)", "On dodge: an attacker with 8+ / 5+ Heat goes Supernova at once.", "On dodge: an attacker within 2 / 5 Heat of Supernova goes Supernova at once.", "Absolute Heat gate under a moving Supernova threshold; now relative to it."),
 ("m-rimeguard-plate", "Glacier Aegis (awakened SS)", "Chill on enemies never wears off, shielded or not.", "Shielded: Chill on enemies never wears off and grows by 1 each round.", "The awakened version was Permafrost without its 5-stack gate."),
 ("m-frostglide-skates", "Frostglide Skates (S, awakened S)", "On dodge: an attacker at 10 / 7+ Chill freezes for 1 turn, once per enemy.", "Every 3rd / 2nd dodge: freeze the most chilled enemy for 1 turn, once per enemy.", "Dead with Deep Freeze (it freezes at 5 Chill and resets it) and close to Timeslip Boots' on-dodge skip."),
 ("m-frostglide-skates", "Frostglide Skates (SS, awakened SS)", "Attacks from enemies at 10 / 7+ Chill always miss you.", "Against chilled attackers, your dodge cap is 5% / 10% higher.", "100% miss with the Chill signature was permanent immunity; dead with Deep Freeze. Now a relative cap, ceiling 85%."),
 ("m-shatter-cannon", "Shatter Cannon (S, awakened S)", "Rage attacks freeze an enemy / every enemy at 10 Chill …", "Rage attacks freeze a chilled enemy / every chilled enemy …", "Dead with Deep Freeze (Chill resets at 5)."),
 ("m-dynamo-hull", "Dynamo Hull (S) and Storm Coil (S)", "… at max Shock …", "… with 4+ Shock …", "Capacitor discharges and resets Shock at 5 (= max), so \"at max\" was never seen; Deep Wells doubles the max."),
 ("m-arcwright-drill", "Arcwright Drill (3)", "Arc jumps onto max-Shock enemies heal you …", "Arc jumps onto enemies with 3+ Shock heal you …", "Same Capacitor problem."),
 ("m-arcwright-drill", "Arcwright Drill (1, awakened 1)", "Basic attacks Arc to 2 other enemies for 30% / 50% ATK.", "Basic attacks Arc to 1 / 2 more enemies than they otherwise would.", "Copy of the Arc signature; now adds jumps to it (and works without it)."),
 ("m-arcwright-drill", "Arcwright Drill (SS, awakened SS)", "Basic attacks Arc to every other enemy (twice).", "Arc jumps that crit apply 1 / 2 Shock.", "Made skill 1 redundant at SS."),
 ("m-decay-stinger", "Widowmaker Stinger (awakened 3)", "Rage attacks make the target's Radiation tick three times.", "Rage attacks make the target's Radiation tick twice, at +50% damage.", "Tick cap (3 per enemy per round) would clip it."),
 ("m-hindsight-shell", "Hindsight Shell (SS, awakened SS)", "Hits you undo are dealt to the attacker instead (at double damage).", "After an undone hit, your next turn ends with an Echo (2 Echoes).", "Copy of Hour of Need (Hourglass Hermit 10★: undone hits strike back)."),
 ("m-hatchery-tail", "Hatchery Tail (3, awakened 3)", "Each Drone guards one / 2 hits for 30% / 50%, then breaks.", "Each Drone zap heals you 1% / 2% max HP.", "Copy of Launch Bay (Hive Carrier 5★: a Drone guards one hit, then breaks)."),
 ("m-swarmcaller-antenna", "Swarmcaller Antenna (SS, awakened SS)", "Sidekicks act at 75 / 50 charge instead of 100.", "Sidekicks need 25 / 50 less charge to act.", "Absolute override; relative now, floor 50."),
 ("m-rage-turbine", "Rage Turbine (SS, awakened SS)", "Rage attacks need 75 / 60 rage instead of 100.", "Rage attacks need 25 / 40 less rage.", "Absolute override that ignored the Quantum Fury Shell; relative now, floor 50."),
 ("m-backlash-hammer", "Worldbreaker Hammer (awakened SS)", "At 50+ rage, a hit on you fires your rage attack as a counter.", "At full rage, a hit on you fires your rage attack as a +50% counter.", "\"50+ rage\" was a hidden rage-cost cut past every floor."),
 ("m-flicker-treads", "Flicker Treads (SS, awakened SS)", "Your dodge chance also counts as combo chance (awakened: counts double).", "Half your dodge chance also counts as combo chance (awakened: your full dodge chance).", "With Beast or Quick Hands it pinned combo chance at the 95% cap."),
 ("m-feint-mask", "Feint Mask (1, awakened 1)", "The first hit you take each round: 40% / 70% chance to counter it.", "… : +40% / +70% counter chance against it.", "A separate roll ignored the Riposte signature and counter chance; now it adds to them."),
 ("m-rampart-carapace", "Rampart Carapace (1, awakened 1)", "Counterattacks give you a shield of 4% / 8% max HP.", "Counterattacks double your current shield, max 15% / 25% max HP.", "Copy of Bulwark Protocol and Rebound Plating (shield on counter)."),
 ("m-afterburner-tail", "Afterburner Tail (rhythm, 2)", "Every 3rd turn …; a turn with 3+ combo strikes gives 20 rage.", "Every 4th turn …; a turn with 3+ combo strikes heals you 5% max HP.", "Same turn as the Echo signature (every 3rd turn) and a copy of Momentum (combo → rage)."),
]

# rev 3e (owner: "boring adjustments"): these overlaps are kept and handled by the stacking rules instead
INTERPLAY_REVERTED = {
    1: "Reverted: battle-start shields from all sources stack (add up).",
    3: "Reverted: rage refunds from all sources stack (add up).",
    4: "Reverted: overheal is fun; overheal-shield caps from all sources stack (the caps add up).",
    20: "Reverted: strike-backs on undone hits stack (with Hour of Need both fire).",
    21: "Reverted: Drone guards from parts and the Hive Carrier stack, each guards its own hit (a 100% guard counts toward the 1 avoided hit per round).",
    28: "Reverted: on the Echo signature's turn both fire (the burst turn is the big turn); rage from Momentum and this part adds up.",
}
for _i, _why in INTERPLAY_REVERTED.items():
    _c = INTERPLAY_CHANGES[_i - 1]
    INTERPLAY_CHANGES[_i - 1] = (_c[0], _c[1], _c[2], 'kept as before', _why)

# rev 3f (owner): standard parts may carry synergy STATS where they fit the role; their skills stay simple.
# Arms add a second role stat (Guard arms counter chance, Swift arms combo chance); Keen parts already have crit chance.
ARM_ROLE_STAT = {"Guard": ("counter chance %", 3), "Swift": ("combo chance %", 3)}

# rev 3f (owner): Symbiote rework (run Mythic skill mythic_symbiote) so it no longer silences charge / sidekick-act effects
SYMBIOTE = dict(
    id="mythic_symbiote", name="Symbiote",
    before="Your aliens stop acting alone. Your rage attack also fires every alien's ability.",
    after="Your rage attack also fires every alien's ability. Each alien that joins loses 50 charge.",
    engine="Drop the symbiote rule (aliens keep acting at their own charge); keep AllyAction all on OnRageAttack; add: each ally that joins loses 50 charge (ChargeAllies −50 on the joining allies, floor 0).",
    why="Same fantasy, the worm and its aliens fight as one, but the aliens keep their own turns. The cost moved from 'never act alone' to charge, so every charge source (Swarmcaller Antenna, Broodguard Legs, Wingman, the Command signature) refuels them instead of being silenced.",
)
