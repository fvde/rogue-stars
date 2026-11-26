# Mothwick, the Lost Courier: Run Quest review

Generated from `shared/RogueStars.Rules/Content/Quests/QuestMothwick.cs` by `RunQuestDocsGenerator`; how quests are built: [recipe.md](recipe.md).
Comment on any node by its id (for example `m05a`).

## The character

- **Name:** Mothwick, the Lost Courier
- **Look:** A round, fuzzy lilac moth the size of a teacup: big soft pale purple wings, huge shiny black eyes, feathery antennae, an oversized blue courier cap that keeps slipping over one eye, a little mail satchel, and a glass lantern with a tiny star inside.
- **Voice:** Polite, flustered and very proper: delivery jargon (“Signature here, please!”, “Delivery rule forty-two”), “Dear me!”, apologises a lot, bravest when he is most scared. Light, warm humour; never mean.
- **The story:** Mothwick of the Interplanetary Post crash-lands with a parcel addressed to “The Worm Who Fell”: a lantern with Pim, a baby star who fell from the sky. Pim must go home to the Top of the Sky. On the way: the Hollow Postmaster, who lost Pim long ago and wants her back, and a small mystery about where the worm itself came from. Pim's thanks is Starglint: extra coins on every coin you find.
- **Portrait:** `quest_mothwick` (art package `tools/art/packages/quest-mothwick.json`; the client draws a placeholder until the art exists).
- **Account bonus:** Starglint. Each boon adds +1 run coin to every run-coin gain, for the whole account; after the chain: +3 on every coin gain.

## How it plays

- Unlocks after main level 20 (feature `run_quests`, "Strangers on the Road"). Not in the Burrow.
- About one run in five meets Mothwick: chance by runs since the last meeting 0% / 5% / 15% / 25% / 35% / 55% / 80% / 100% (mean 5 runs, never two runs in a row, never more than 8 runs apart). The very first meeting comes within two runs of the unlock.
- At most one meeting per run: a visitor tile on the main path of the second board segment (every path passes it, it stops the worm).
- 30 chapters per playthrough. Story choices always move on; battles and prices can be declined or failed, then the story waits on the same node until the next meeting.
- Battles do not end the run: a lost quest battle leaves the worm with at least 10% HP.
- After the last chapter Mothwick keeps visiting at the same rate with a friendly epilogue scene and a small reward (no battles, no prices).

## Ratios (every playthrough)

| Type | Per playthrough | Share | Target |
|---|---|---|---|
| Story | 15 | 50% | ~50% |
| Battle | 6 | 20% | ~20% |
| Sacrifice | 6 | 20% | ~20% |
| Boon | 3 | 10% | ~10% |
| **Total** | **30** | | |

42 nodes in all; 128 different playthroughs (branches re-merge, every one is 30 chapters with the same ratios and the same Starglint at the end).

## Story map

| Ch | Type | Node(s) |
|---|---|---|
| 1 | Story | `m01` A Very Lost Moth |
| 2 | Story | `m02a` What’s in the Box · `m02b` No Return Address |
| 3 | Battle | `m03` The Mail Thief |
| 4 | Story | `m04` The Address |
| 5 | Sacrifice | `m05a` Star Toll · `m05b` Directions for Sale |
| 6 | Story | `m06a` Over the Bridge · `m06b` Left, Right, Up |
| 7 | Battle | `m07` Hounds of the Hollow |
| 8 | Boon | `m08` Starglint |
| 9 | Story | `m09` A Letter for Mothwick |
| 10 | Sacrifice | `m10a` The Biting Mailbox · `m10b` Ignored Mail Fine |
| 11 | Story | `m11a` A Patch for Bravery · `m11b` The Receipt |
| 12 | Battle | `m12` The Cloud Stairs |
| 13 | Story | `m13` Growth Spurt |
| 14 | Sacrifice | `m14` A Gift for the Wind |
| 15 | Story | `m15` The Door in the Cloud |
| 16 | Story | `m16a` Hall of Lost Letters · `m16b` Something Breathing |
| 17 | Battle | `m17a` The Shelf Keeper · `m17b` The Pet Wakes Up |
| 18 | Sacrifice | `m18a` Postage Due · `m18b` A Bandage of Light |
| 19 | Boon | `m19` Starglint, Brighter |
| 20 | Story | `m20` Wet Ink |
| 21 | Story | `m21a` A Hole Where a Heart Was · `m21b` Footprints of Light |
| 22 | Sacrifice | `m22` The Star Gate |
| 23 | Battle | `m23` The Last Guard |
| 24 | Story | `m24` The Hollow Postmaster |
| 25 | Story | `m25a` Old Friends · `m25b` A Worm’s Promise |
| 26 | Story | `m26` The Truth About Worms |
| 27 | Battle | `m27` The Light-Eater |
| 28 | Story | `m28` The Top of the Sky |
| 29 | Sacrifice | `m29a` Every Trick I Know · `m29b` Everything, Together |
| 30 | Boon | `m30a` Pim Goes Home · `m30b` Delivery Complete |

## Every node

### Ch 1 · `m01` · Story: A Very Lost Moth

> A teacup-sized moth tumbles out of the sky, cap first. “Dear me! Mothwick, Interplanetary Post. I have a parcel for… oh. ‘The Worm Who Fell.’ That’s you, isn’t it? You look like you fell.”

Story choice: always moves on.

- **Open the parcel** → `m02a` What’s in the Box  
  Journal: *I opened Mothwick’s parcel: a lantern with a sleeping baby star inside.*
- **Ask who sent it** → `m02b` No Return Address  
  Journal: *I asked who sent the parcel. Mothwick didn’t know, which worried him a great deal.*

### Ch 2 · `m02a` · Story: What’s in the Box

> In the lantern a star the size of a pea snores softly. “Oh no. We do not deliver stars! Stars deliver themselves!” Mothwick flaps in circles. “Her tag says Pim. What do we do?”

Story choice: always moves on.

- **Let her sleep** → `m03` The Mail Thief  
  Journal: *We let Pim sleep. She snored tiny sparks.*
- **Wake her gently** → `m03` The Mail Thief  
  Journal: *I woke Pim gently. She sneezed a comet and giggled.*

### Ch 2 · `m02b` · Story: No Return Address

> Mothwick turns the parcel over and over. “No stamp. No sender. Just a smudge that looks like… a hole?” Inside, something small and bright knocks to be let out.

Story choice: always moves on.

- **Peek inside** → `m03` The Mail Thief  
  Journal: *I peeked inside: a baby star named Pim, knocking politely.*
- **Knock back** → `m03` The Mail Thief  
  Journal: *I knocked back. The parcel knocked twice. We became friends.*

### Ch 3 · `m03` · Battle: The Mail Thief

> A big grumpy brute stomps out of the bushes, sniffing at the lantern. “It wants Pim!” squeaks Mothwick from behind your tail. “Please, er, return to sender!”

Hard battle: the level's miniboss at +40% HP/ATK with 1 elite. Lost or declined: the story waits.

- **Fight it** (fight; won →) → `m04` The Address  
  Journal: *I chased off a brute that tried to swallow Pim’s lantern.*
- **Run for it** → the story waits here

### Ch 4 · `m04` · Story: The Address

> Mothwick unfolds a map bigger than himself. “Pim lives at the Top of the Sky. That’s not on any map. That’s not even a street!” He looks at you hopefully. “You don’t know the way up, do you?”

Story choice: always moves on.

- **Follow the brightest star** → `m05a` Star Toll  
  Journal: *We set off after the brightest star in the sky.*
- **Ask the locals** → `m05b` Directions for Sale  
  Journal: *We asked the locals the way to the sky.*

### Ch 5 · `m05a` · Sacrifice: Star Toll

> The brightest star is a lamp on a toll bridge. A sleepy alien taps a sign: 100 COINS. STARS FLY FREE. WORMS DO NOT. Mothwick checks his pockets. They hold one button and a smaller moth.

Price: 100 run coins. Refused or not affordable: the story waits.

- **Pay the toll** (pay →) → `m06a` Over the Bridge  
  Journal: *I paid 100 coins at the star bridge. Pim waved at the toll alien.*
- **Not now** → the story waits here

### Ch 5 · `m05b` · Sacrifice: Directions for Sale

> The only local who knows the way up is a snail selling directions, 100 coins each. “Left, right, up,” it whispers. “Satisfaction guaranteed. No refunds.”

Price: 100 run coins. Refused or not affordable: the story waits.

- **Buy directions** (pay →) → `m06b` Left, Right, Up  
  Journal: *I bought directions from a snail for 100 coins: left, right, up.*
- **Not now** → the story waits here

### Ch 6 · `m06a` · Story: Over the Bridge

> Beyond the bridge the road climbs into fog and Pim’s light flickers. “She’s homesick,” Mothwick whispers. “Stars go dim when they’re sad. Can we cheer her up?”

Story choice: always moves on.

- **Sing her a song** → `m07` Hounds of the Hollow  
  Journal: *I sang Pim a worm song. It was mostly humming. She glowed brighter.*
- **Tell her a joke** → `m07` Hounds of the Hollow  
  Journal: *I told Pim a joke about a black hole. She didn’t get it. Then she got it.*

### Ch 6 · `m06b` · Story: Left, Right, Up

> Left, right… and “up” is a cliff. Mothwick flutters to the top and lowers a strip of parcel tape. “Delivery rule forty-two: when in doubt, climb.” Pim hums encouragement.

Story choice: always moves on.

- **Climb the tape** → `m07` Hounds of the Hollow  
  Journal: *I climbed Mothwick’s parcel tape up the cliff. It held. Mostly.*
- **Look for stairs** → `m07` Hounds of the Hollow  
  Journal: *I found stairs behind the cliff. Mothwick pretended he knew.*

### Ch 7 · `m07` · Battle: Hounds of the Hollow

> Something big with too many teeth guards the path, wearing a collar with a postage stamp. “That’s the Hollow Postmaster’s stamp,” Mothwick gulps. “He collects fallen stars. And he sent his guard dog.”

Hard battle: the level's miniboss at +45% HP/ATK with 1 elite. Lost or declined: the story waits.

- **Fight the guard** (fight; won →) → `m08` Starglint  
  Journal: *I beat the Hollow Postmaster’s guard. Mothwick stamped its collar RETURNED.*
- **Not now** → the story waits here

### Ch 8 · `m08` · Boon: Starglint

> Pim wakes fully for the first time and blinks at you. Then she sneezes, and glitter settles on your coin pouch. “Starglint!” Mothwick gasps. “She likes you. Every coin you find will bring a friend now. Forever, I think.”

Boon: +1 run coin on every run-coin gain, account-wide, forever.

- **Thank you, Pim!** → `m09` A Letter for Mothwick  
  Journal: *Pim gave me Starglint: one extra coin on every coin I find, forever.*

### Ch 9 · `m09` · Story: A Letter for Mothwick

> A letter is pinned to a tree, addressed to Mothwick: HAND OVER THE STAR, LITTLE COURIER, OR LOSE YOUR CAP. He clutches his cap. “He wouldn’t. It’s regulation!”

Story choice: always moves on.

- **Write a rude reply** → `m10a` The Biting Mailbox  
  Journal: *We wrote the Hollow Postmaster a rude reply. Mothwick added “Kind regards”.*
- **Ignore it** → `m10b` Ignored Mail Fine  
  Journal: *We ignored the Postmaster’s letter. Mothwick filed it under “Nope”.*

### Ch 10 · `m10a` · Sacrifice: The Biting Mailbox

> The reply has to go in the Postmaster’s mailbox, and the mailbox bites. Someone has to reach in. You don’t have hands. You have a whole worm.

Price: 50% of current HP. Refused or not affordable: the story waits.

- **Feed the mailbox** (pay →) → `m11a` A Patch for Bravery  
  Journal: *I fed our reply to the biting mailbox. It took half of me with it.*
- **Not now** → the story waits here

### Ch 10 · `m10b` · Sacrifice: Ignored Mail Fine

> A second letter arrives at once: IGNORING MAIL IS FINED 150 COINS. Mothwick is horrified. “It’s real! It has a seal and everything!”

Price: 150 run coins. Refused or not affordable: the story waits.

- **Pay the fine** (pay →) → `m11b` The Receipt  
  Journal: *I paid the Postmaster’s silly 150-coin fine. Mothwick insisted on a receipt.*
- **Not now** → the story waits here

### Ch 11 · `m11a` · Story: A Patch for Bravery

> While you rest, Mothwick sews a star patch onto his cap. “For bravery,” he says, and starts on a second one for you. “Where do worms keep their medals?”

Story choice: always moves on.

- **On my head** → `m12` The Cloud Stairs  
  Journal: *Mothwick sewed a bravery patch on my head. I wear it proudly.*
- **On my tail** → `m12` The Cloud Stairs  
  Journal: *Mothwick sewed a bravery patch on my tail. It wiggles proudly.*

### Ch 11 · `m11b` · Story: The Receipt

> The receipt has a map printed on the back, by accident: a staircase of clouds and a round door. “The Postmaster is a terrible bureaucrat,” Mothwick beams. “Bureaucrats always leave clues.”

Story choice: always moves on.

- **Follow the map** → `m12` The Cloud Stairs  
  Journal: *The receipt had a map to the cloud stairs on the back. We followed it.*
- **Frame the receipt** → `m12` The Cloud Stairs  
  Journal: *I framed the receipt. Then we followed its map anyway.*

### Ch 12 · `m12` · Battle: The Cloud Stairs

> The cloud stairs are guarded by something as wide as the steps and twice as rude, and it brought a friend. “Tickets, please,” it rumbles. You have no tickets.

Hard battle: the level's miniboss at +50% HP/ATK with 1 elite. Lost or declined: the story waits.

- **Fight your way up** (fight; won →) → `m13` Growth Spurt  
  Journal: *I fought past the guardians of the cloud stairs.*
- **Not now** → the story waits here

### Ch 13 · `m13` · Story: Growth Spurt

> Pim is the size of a cherry now and keeps bumping against her lantern. “Stars grow fast near the sky,” says Mothwick. “Soon she’ll be too bright to carry.”

Story choice: always moves on.

- **Let her fly a little** → `m14` A Gift for the Wind  
  Journal: *Pim flew loops around me for a while. I got dizzy.*
- **Keep her safe inside** → `m14` A Gift for the Wind  
  Journal: *I kept Pim safe in her lantern. She sulked, then glowed.*

### Ch 14 · `m14` · Sacrifice: A Gift for the Wind

> The wind on the stairs is far too strong for a little moth. “The Windkeeper will calm it,” Mothwick shouts, “for something legendary. One of your best tricks, I’m afraid.”

Price: one Legendary skill (player's choice). Refused or not affordable: the story waits.

- **Give a Legendary skill** (pay →) → `m15` The Door in the Cloud  
  Journal: *I gave the Windkeeper one of my Legendary skills. The wind went quiet.*
- **Not now** → the story waits here

### Ch 15 · `m15` · Story: The Door in the Cloud

> At the top of the stairs: a round door with a slot. MAIL ONLY. Mothwick straightens his cap. “Technically, Pim is mail.” Behind the door, two corridors: one sparkling, one very dark.

Story choice: always moves on.

- **The sparkling one** → `m16a` Hall of Lost Letters  
  Journal: *We took the sparkling corridor.*
- **The dark one** → `m16b` Something Breathing  
  Journal: *We took the dark corridor. Pim lit the way.*

### Ch 16 · `m16a` · Story: Hall of Lost Letters

> Shelves of letters that never arrived reach all the way up. Mothwick reads one and sniffles. “From a comet to her mother. Twelve thousand years late.”

Story choice: always moves on.

- **We’ll deliver it** → `m17a` The Shelf Keeper  
  Journal: *We took a comet’s letter along, twelve thousand years late, to deliver it.*
- **Read another** → `m17a` The Shelf Keeper  
  Journal: *We read lost letters until Pim fell asleep.*

### Ch 16 · `m16b` · Story: Something Breathing

> In the dark something enormous breathes slowly. Pim’s light shows a sleeping beast wearing a postmaster’s cap. “That’s not HIM,” Mothwick whispers. “That’s his pet.”

Story choice: always moves on.

- **Tiptoe past** → `m17b` The Pet Wakes Up  
  Journal: *We tried to tiptoe past the Postmaster’s pet. Worms are bad at tiptoeing.*
- **Say hello** → `m17b` The Pet Wakes Up  
  Journal: *I said hello to the Postmaster’s pet. It woke up.*

### Ch 17 · `m17a` · Battle: The Shelf Keeper

> A guardian of the lost letters blocks the exit, eyeing the comet’s letter. “No letter leaves the Hall,” it booms, and its helpers crack their knuckles.

Hard battle: the level's miniboss at +55% HP/ATK with 1 elite. Lost or declined: the story waits.

- **Fight the Keeper** (fight; won →) → `m18a` Postage Due  
  Journal: *I beat the Shelf Keeper. The comet’s letter is ours to deliver.*
- **Not now** → the story waits here

### Ch 17 · `m17b` · Battle: The Pet Wakes Up

> The pet is awake, and it brought friends. Mothwick puts on his sternest face, which is not very stern. “Sit! Stay! Please?”

Hard battle: the level's miniboss at +55% HP/ATK with 1 elite. Lost or declined: the story waits.

- **Fight the pet** (fight; won →) → `m18b` A Bandage of Light  
  Journal: *The Postmaster’s pet and its friends lost. Mothwick gave it a biscuit anyway.*
- **Not now** → the story waits here

### Ch 18 · `m18a` · Sacrifice: Postage Due

> To finally send the comet’s letter it needs stamps from a very old stamp machine: 300 coins. “It’s the right thing to do,” Mothwick says, and looks away politely while you count.

Price: 300 run coins. Refused or not affordable: the story waits.

- **Pay the postage** (pay →) → `m19` Starglint, Brighter  
  Journal: *I paid 300 coins to post a comet’s letter twelve thousand years late.*
- **Not now** → the story waits here

### Ch 18 · `m18b` · Sacrifice: A Bandage of Light

> Mothwick’s wing got nipped in the scuffle. Pim can mend it, but she needs warmth to borrow: yours, and a lot of it.

Price: 80% of current HP. Refused or not affordable: the story waits.

- **Lend her your warmth** (pay →) → `m19` Starglint, Brighter  
  Journal: *I lent Pim most of my warmth to mend Mothwick’s wing.*
- **Not now** → the story waits here

### Ch 19 · `m19` · Boon: Starglint, Brighter

> Pim, now the size of a plum, hums a note that makes your coin pouch jingle by itself. “Your Starglint!” cries Mothwick. “Two extra coins for every coin you find now. Do worms say thank you?”

Boon: +1 run coin on every run-coin gain, account-wide, forever.

- **Thank you, Pim!** → `m20` Wet Ink  
  Journal: *Pim made my Starglint brighter: two extra coins on every coin I find.*

### Ch 20 · `m20` · Story: Wet Ink

> Another letter, the ink still wet: I ONLY WANT HER BACK. SHE WAS MY STAR FIRST. Mothwick goes very quiet. “Villains aren’t supposed to be sad.”

Story choice: always moves on.

- **Maybe he isn’t a villain** → `m21a` A Hole Where a Heart Was  
  Journal: *We wondered whether the Hollow Postmaster was really a villain.*
- **Keep going** → `m21b` Footprints of Light  
  Journal: *We kept going. Pim looked back once.*

### Ch 21 · `m21a` · Story: A Hole Where a Heart Was

> Mothwick explains: the Postmaster once carried the Night’s mail, until he dropped a star and a hole was left in his chest. “Hollow isn’t a nickname,” he sighs. “It’s a diagnosis.”

Story choice: always moves on.

- **We could help him** → `m22` The Star Gate  
  Journal: *Mothwick told me the Postmaster’s story. We decided to help him too.*
- **Pim comes first** → `m22` The Star Gate  
  Journal: *Mothwick told me the Postmaster’s story. Pim still comes first.*

### Ch 21 · `m21b` · Story: Footprints of Light

> Tiny glowing footprints lead up the sky road. Pim gasps: they are hers, from long ago, when she first fell. “She’s retracing her steps,” says Mothwick. “Clever star.”

Story choice: always moves on.

- **Follow her footprints** → `m22` The Star Gate  
  Journal: *We followed Pim’s old glowing footprints toward the sky.*
- **Make new ones** → `m22` The Star Gate  
  Journal: *I made new footprints next to Pim’s. Mine were wigglier.*

### Ch 22 · `m22` · Sacrifice: The Star Gate

> The sky road ends at a gate with a keyhole shaped like a legend. Only something mythic fits. Mothwick fidgets. “I’d give my own, but all I have is a cap.”

Price: one Mythic skill (player's choice). Refused or not affordable: the story waits.

- **Give a Mythic skill** (pay →) → `m23` The Last Guard  
  Journal: *I gave a Mythic skill to open the Star Gate.*
- **Not now** → the story waits here

### Ch 23 · `m23` · Battle: The Last Guard

> Beyond the gate stands the Postmaster’s last guard, polished and proud, with a helper at its side. “The Postmaster is not seeing visitors,” it says. “Especially worms.”

Hard battle: the level's miniboss at +60% HP/ATK with 2 elites. Lost or declined: the story waits.

- **Fight the guard** (fight; won →) → `m24` The Hollow Postmaster  
  Journal: *I beat the Postmaster’s last guard behind the Star Gate.*
- **Not now** → the story waits here

### Ch 24 · `m24` · Story: The Hollow Postmaster

> There he is: a tall cloak with a lantern-shaped hole in its chest, sorting letters for ever. He sees Pim and stops. “You found her,” he says, very softly. “I dropped her. I never stopped looking.”

Story choice: always moves on.

- **Let them talk** → `m25a` Old Friends  
  Journal: *We met the Hollow Postmaster. He had dropped Pim long ago.*
- **Stand between them** → `m25b` A Worm’s Promise  
  Journal: *I stood between the Postmaster and Pim, just in case.*

### Ch 25 · `m25a` · Story: Old Friends

> Pim floats to the Postmaster and lights the hole in his chest from inside. For a moment he isn’t hollow at all. “She can’t stay with me,” he says. “She belongs to the whole sky. But thank you.”

Story choice: always moves on.

- **Hug the Postmaster** → `m26` The Truth About Worms  
  Journal: *I hugged the Hollow Postmaster. It was like hugging a draught.*
- **Shake his hand** → `m26` The Truth About Worms  
  Journal: *I shook the Postmaster’s hand with my tail. He understood.*

### Ch 25 · `m25b` · Story: A Worm’s Promise

> The Postmaster bows. “Good. Someone should guard her better than I did.” He hands you a stamp: TOP OF THE SKY. “The last stretch is the hardest. Something there eats light.”

Story choice: always moves on.

- **I’ll protect her** → `m26` The Truth About Worms  
  Journal: *I promised the Postmaster I would protect Pim to the end.*
- **What eats light?** → `m26` The Truth About Worms  
  Journal: *The Postmaster warned us about the thing that eats light.*

### Ch 26 · `m26` · Story: The Truth About Worms

> On the last stretch Pim whispers to Mothwick, and he drops his satchel. “She says you fell too. Long ago. A tiny bright thing that landed on an empty planet and forgot it was a star.” You look at your tail. It is, faintly, glowing.

Story choice: always moves on.

- **That explains a lot** → `m27` The Light-Eater  
  Journal: *Pim told us I fell from the sky too, long ago. That explains a lot.*
- **I’m just a worm** → `m27` The Light-Eater  
  Journal: *Pim says I fell from the sky. I’m just a worm. Probably.*

### Ch 27 · `m27` · Battle: The Light-Eater

> At the Top of the Sky waits a shadow that eats starlight, and it has brought its whole family. Pim hides behind your glowing tail. Mothwick rolls up his sleeves. He has no sleeves. He rolls up his wings.

Hard battle: the level's miniboss at +70% HP/ATK with 2 elites. Lost or declined: the story waits.

- **Fight the Light-Eater** (fight; won →) → `m28` The Top of the Sky  
  Journal: *We beat the Light-Eater at the Top of the Sky.*
- **Not now** → the story waits here

### Ch 28 · `m28` · Story: The Top of the Sky

> A quiet place full of humming stars, with a gap exactly Pim-shaped. But the way up is dark, and Pim is too small to light it alone. “Something must burn very bright,” says Mothwick. “Something full of memories.”

Story choice: always moves on.

- **Let me light the way** → `m29a` Every Trick I Know  
  Journal: *I offered to light Pim’s way home myself.*
- **We’ll do it together** → `m29b` Everything, Together  
  Journal: *Mothwick and I decided to light Pim’s way together.*

### Ch 29 · `m29a` · Sacrifice: Every Trick I Know

> To burn bright enough you would have to let go of everything you learned on this run. Every skill, in one glorious flash. Pim’s light wobbles. “You don’t have to,” Mothwick whispers. “But oh, it would be bright.”

Price: ALL run skills (signatures stay). Refused or not affordable: the story waits.

- **Forget every run skill** (pay →) → `m30a` Pim Goes Home  
  Journal: *I burned every skill I had into one flash and lit Pim’s way to the sky.*
- **Not now** → the story waits here

### Ch 29 · `m29b` · Sacrifice: Everything, Together

> Mothwick empties his satchel of every undelivered letter into the light. “Your turn,” he says. To make it bright enough you must give everything you learned this run: every skill, gone in one flash.

Price: ALL run skills (signatures stay). Refused or not affordable: the story waits.

- **Forget every run skill** (pay →) → `m30b` Delivery Complete  
  Journal: *Mothwick gave his letters and I gave every skill I had. It was very bright.*
- **Not now** → the story waits here

### Ch 30 · `m30a` · Boon: Pim Goes Home

> Pim zips up the bright path and clicks into her place. The whole sky hums one note louder. A last sprinkle of glitter falls on you. “Starglint at full strength!” sniffs Mothwick. “Signature here, please. Delivery complete.”

Boon: +1 run coin on every run-coin gain, account-wide, forever.

- **Sign for it** → **the end**  
  Journal: *Pim went home to the sky. My Starglint is at full strength: three extra coins on every coin I find.*

### Ch 30 · `m30b` · Boon: Delivery Complete

> Pim rises, then turns back and kisses the tip of your nose before she goes. The sky over your planet glows a little brighter now. “Three extra coins on every coin, for ever,” says Mothwick, stamping his book. “Best delivery of my career.”

Boon: +1 run coin on every run-coin gain, account-wide, forever.

- **Wave goodbye** → **the end**  
  Journal: *Pim kissed my nose and went home. Three extra coins on every coin I find, for ever.*

## Epilogue scenes (after the chain, in rotation)

| Scene | Text | Reward |
|---|---|---|
| Tea Break | Mothwick lands on your head with two thimbles of tea. “Just passing through! Pim says hello. She sent you pocket money.” | +80 run coins (+Starglint) |
| A Warm Glow | Pim’s light falls on you from far above, warm as a blanket. Mothwick waves from a cloud. “She’s keeping an eye on you!” | heal 30% HP |
| Special Delivery | “Parcel for the Worm Who Fell!” Mothwick hands you a box. Inside: a choice of new tricks. “No signature needed. You’re family.” | a free skill pick (1 of 3) |
| Star Biscuit | Mothwick shares a biscuit baked on Pim’s warmth. You feel sturdier already. “Don’t tell the Postmaster. He wanted the last one.” | +10% max HP for the run |
