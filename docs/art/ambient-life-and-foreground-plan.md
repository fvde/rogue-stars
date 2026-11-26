# Ambient life and foreground scenery: plan

Two new art categories that make scenes feel alive and deep. Status: plan, waiting for the owner's OK to start the pilot.

## A. Ambient life (living things)

**What:** small creatures that live in the scene but are never interactive.
- **Flyers:** birds, space bats, glowflies, seed puffs drifting across the screen (one crosses every 10–30 s).
- **Grazers:** small harmless aliens grazing, sniffing, hopping next to the path or on the hub planet (1–3 on screen).
- **Swimmers / floaters** where the biome calls for it (coral reef fish, gas giant jellies).

**Where (priority order):** run board, hub planet, City, battle backdrops (very subtle, behind the fighters).

**How it moves (no skeletal animation, animation stays deferred):** simple code movement of flat sprites: a flyer slides along a gentle curve with a sine bob and a 2-frame wing flap (two sprites); a grazer bobs, flips left/right and does an occasional small hop. One setting toggles it with the other motion. Never over the path tiles, never over UI.

**Art per biome:** 1 flyer (2 frames: wings up / wings down) + 2 grazers (one pose each) = 4 sprites. Small cartoon side view, same style as the aliens (bold outline, flat colours, cel shading), no ground, no shadow (code adds a soft contact shadow for grazers), facing left (code flips).

## B. Foreground scenery (looking through)

**What:** plants, branches, leaves, vines, crystals or rocks that reach into the field of view from the screen edges, as if the camera looks through them. Gives depth and frames the scene.

**Where:** run board (edges only, never over the path or HUD), hub, City, battle backdrop frames.

**How it's shown:** drawn above the scene at the screen edges, slightly darker and softer (depth), moving a little faster than the board when the camera pans (parallax), a slow sway for plants. They crop at the screen edge on purpose.

**Art per biome:** 4 edge pieces: top-left corner, bottom-right corner, one side branch, one bottom strip of grass/plants. Each is designed to be cut off at one or two edges.

## Pipeline notes

- New categories in `tools/art/categories.json`: `ambient` (creature-like, 512 canvas, small fill) and `foreground` (edge pieces: the "touches the image edge" check is reversed: they MUST touch their anchor edge(s) and nowhere else).
- Prompts state the crop explicitly ("a leafy branch entering from the left edge, cut off at the left edge"), with the usual negatives (no ground, no shadow, no text, no frame).

## Scope and rollout

- About 20 biomes × 8 sprites = ~160 images in total; too many to start blind.
- **Pilot:** 2 biomes (meadow / alien jungle, and lava): 8 ambient + 8 foreground sprites → owner review on the art page → a small code pass (ambient movement + foreground layer) on the board and hub with screenshots → owner OK → the remaining biomes in queue order.

## Open questions for the owner

1. Animation is deferred; is simple code movement (slide, bob, 2-frame flap, sway) OK for these, or should they stay still for now?
2. Pilot biomes: meadow + lava, or others?
3. Should foreground pieces also appear in battles, or only on the board / hub / City?
