# Rogue Stars backlog

The owner's backlog. Source of truth for two things:

1. **Features** that need more specification, design and prompts before they are built.
2. **Known and deferred topics**: things that are open, parked or on hold on purpose.

**Playtest agents: read this file before reporting anything.** Do not report an issue that is listed under "Known and deferred" (mention it only if it got clearly worse or the screen changed so the entry is stale). Playtests only run when the owner asks.

Published as the "Rogue Stars Backlog" artifact; keep this file and the artifact in sync (the artifact is generated from this file).

## 1. Features that need specification

| ID | Feature | State | Notes |
|---|---|---|---|
| F1 | **Arena** | Needs spec + design + prompts | A first version exists in the rules (ArenaSystem, weekly league, tickets) but has no real design. Needs: matchmaking idea, rewards, the screen, art. |
| F2 | **Guilds** | Needs spec + design + prompts | A first version exists (GuildSystem, chat, marks, weekly shop). Needs: roles, guild goals/events, the screen, art. |
| F3 | **Permanent tile improvements** | Idea, needs spec | A special run event: pay a high price, or beat an extra-strong miniboss, to make a tile type slightly better forever (for example a bit more loot or a better chest chance on that tile). Needs: which tiles, how strong, how it stacks, how it shows on the board, a cap, how it relates to the Laboratory. |
| F4 | **Animation pipeline** | Proof of concept done | Shaders for props, code spine for the worm and limbless creatures, cut-out rigs for a few key aliens, AI-video loops for a few hero effects. Needs the owner's decision which to adopt (branch `anim-pilot`). |
| F5 | **Genes** | "Coming soon" | Breeding genes and the Gene Lab. Parked until the rest of breeding is settled. |
| F6 | **Weather and the worm visiting the plaza** | Idea (City doc section 7) | Not built. |

## 2. Known and deferred topics

| ID | Topic | Why it is open |
|---|---|---|
| D1 | **Part icons** (30 parts have no dedicated icon) | On hold until the general parts review. |
| D2 | **Parts review**: Gear Cores as a second currency, the "Legs" slot on a legless worm | Owner review pending. |
| D3 | **Building art**: all buildings use stand-in set pieces and markers | Real building sprites (1x1, 2x1, 2x2, 3x2, 3x3) and "lights on" overlays are an art task. |
| D4 | **12 aliens have no sprite** (coloured placeholders) | Art queue. |
| D5 | **Skill icons**: skills show letters or stars | Art queue (skills-v2, about 160 icons). |
| D6 | **Shard Reactor screen** (empty state, selected chip colour) | Fix with the building UI work. |
| D7 | **Rolling shop offer names** ("Special Offer · Step N") | Rework later. |
| D8 | **Auto roll** | Comes later. |
| D9 | **Toast replacements still simplified**: egg crack on hatch, star row filling on the card, rarity sweep on part merge, NEW badge on event icon, hammering alien on construction sites, dust on empty caches | Polish pass. |
| D10 | **Casual players get the Laboratory late** (about day 77 to 110) | Owner accepted the friction; revisit with a first-time guarantee if needed. |
| D11 | **Hero and mount summon events have no 80/20 rule** | Their Mythic tier is only the featured item. |
| D12 | **Enemy, boss, event, mount, mini-game, status, currency and shop-pack art** | Art queue (see the art page). |
| D13 | **Playtest 0.1.251 findings** | Owner is reviewing; do not re-report. |
| D14 | **The cut-out sprite pedestal on the dice button is code-drawn** | The imported pedestal art does not fit the button shape. |
| D16 | **Camp mode (levels 6 to 14) is sparse**: large empty land, pod uses a burrow-mound stand-in and sits behind the saucer | Real pod art and a tighter camp layout come with the building art. |
| D17 | **"Watch 1 ad" daily quest needs an "ads enabled" gate** | `d_ad` requires the Shop feature and hides while `ShopConfig.AdsDisabled` is set (the only ad notion the rules have). When ads ship, give it a real gate (ad gateway ready, ad placement available to the player) instead of the global flag. The early daily track (100 points) never depends on it. |

## 3. How to use this file

- New idea or large feature: add a row to section 1.
- Something parked on purpose: add a row to section 2.
- When something ships, delete its row (the git history keeps it).
