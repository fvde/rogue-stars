namespace RogueStars.Rules.Content.Quests
{
    /// <summary>
    /// Run Quest 1: Mothwick, the Lost Courier (review doc: docs/design/run-quests/mothwick.md). A teacup-sized moth of the
    /// Interplanetary Post carries a parcel addressed to "The Worm Who Fell": a lantern with a sleeping baby star, Pim, who must
    /// go home to the Top of the Sky. 30 chapters per playthrough, 42 nodes (branches re-merge), 3 boons = Starglint +3 coins
    /// on every run-coin gain. Recipe: docs/design/run-quests/recipe.md.
    /// </summary>
    public static class QuestMothwick
    {
        public const string Id = "mothwick";

        public static RunQuestDef Build()
        {
            var q = new QuestBuilder(Id, "Mothwick", "the Lost Courier", "quest_mothwick", "Starglint", 1, 30, "m01");
            const string Wait = "Not now";

            // ---- Act 1: the parcel (chapters 1–8) -------------------------------------------------------------
            q.Story("m01", 1, "A Very Lost Moth",
                "A teacup-sized moth tumbles out of the sky, cap first. “Dear me! Mothwick, Interplanetary Post. I have a parcel for… oh. “The Worm Who Fell.” That’s you, isn’t it? You look like you fell.”",
                QuestBuilder.Go("Open the parcel", "m02a", "I opened Mothwick’s parcel: a lantern with a sleeping baby star inside."),
                QuestBuilder.Go("Ask who sent it", "m02b", "I asked who sent the parcel. Mothwick didn’t know, which worried him a great deal."));
            q.Story("m02a", 2, "What’s in the Box",
                "In the lantern a star the size of a pea snores softly. “Oh no. We do not deliver stars! Stars deliver themselves!” Mothwick flaps in circles. “Her tag says Pim. What do we do?”",
                QuestBuilder.Go("Let her sleep", "m03", "We let Pim sleep. She snored tiny sparks."),
                QuestBuilder.Go("Wake her gently", "m03", "I woke Pim gently. She sneezed a comet and giggled."));
            q.Story("m02b", 2, "No Return Address",
                "Mothwick turns the parcel over and over. “No stamp. No sender. Just a smudge that looks like… a hole?” Inside, something small and bright knocks to be let out.",
                QuestBuilder.Go("Peek inside", "m03", "I peeked inside: a baby star named Pim, knocking politely."),
                QuestBuilder.Go("Knock back", "m03", "I knocked back. The parcel knocked twice. We became friends."));
            q.Battle("m03", 3, "The Mail Thief",
                "A big grumpy brute stomps out of the bushes, sniffing at the lantern. “It wants Pim!” squeaks Mothwick from behind your tail. “Please, er, return to sender!”",
                0, 1, "Fight it", "m04", "I chased off a brute that tried to swallow Pim’s lantern.", "Run for it");
            q.Story("m04", 4, "The Address",
                "Mothwick unfolds a map bigger than himself. “Pim lives at the Top of the Sky. That’s not on any map. That’s not even a street!” He looks at you hopefully. “You don’t know the way up, do you?”",
                QuestBuilder.Go("Follow the brightest star", "m05a", "We set off after the brightest star in the sky."),
                QuestBuilder.Go("Ask the locals", "m05b", "We asked the locals the way to the sky."));
            q.Sacrifice("m05a", 5, "Star Toll",
                "The brightest star is a lamp on a toll bridge. A sleepy alien taps a sign: 100 COINS. STARS FLY FREE. WORMS DO NOT. Mothwick checks his pockets. They hold one button and a smaller moth.",
                QuestBuilder.Coins(100), "Pay the toll", "m06a", "I paid 100 coins at the star bridge. Pim waved at the toll alien.", Wait);
            q.Sacrifice("m05b", 5, "Directions for Sale",
                "The only local who knows the way up is a snail selling directions, 100 coins each. “Left, right, up,” it whispers. “Satisfaction guaranteed. No refunds.”",
                QuestBuilder.Coins(100), "Buy directions", "m06b", "I bought directions from a snail for 100 coins: left, right, up.", Wait);
            q.Story("m06a", 6, "Over the Bridge",
                "Beyond the bridge the road climbs into fog and Pim’s light flickers. “She’s homesick,” Mothwick whispers. “Stars go dim when they’re sad. Can we cheer her up?”",
                QuestBuilder.Go("Sing her a song", "m07", "I sang Pim a worm song. It was mostly humming. She glowed brighter."),
                QuestBuilder.Go("Tell her a joke", "m07", "I told Pim a joke about a black hole. She didn’t get it. Then she got it."));
            q.Story("m06b", 6, "Left, Right, Up",
                "Left, right… and “up” is a cliff. Mothwick flutters to the top and lowers a strip of parcel tape. “Delivery rule forty-two: when in doubt, climb.” Pim hums encouragement.",
                QuestBuilder.Go("Climb the tape", "m07", "I climbed Mothwick’s parcel tape up the cliff. It held. Mostly."),
                QuestBuilder.Go("Look for stairs", "m07", "I found stairs behind the cliff. Mothwick pretended he knew."));
            q.Battle("m07", 7, "Hounds of the Hollow",
                "Something big with too many teeth guards the path, wearing a collar with a postage stamp. “That’s the Hollow Postmaster’s stamp,” Mothwick gulps. “He collects fallen stars. And he sent his guard dog.”",
                5, 1, "Fight the guard", "m08", "I beat the Hollow Postmaster’s guard. Mothwick stamped its collar RETURNED.", Wait);
            q.Boon("m08", 8, "Starglint",
                "Pim wakes fully for the first time and blinks at you. Then she sneezes, and glitter settles on your coin pouch. “Starglint!” Mothwick gasps. “She likes you. Every coin you find will bring a friend now. Forever, I think.”",
                1, "Thank you, Pim!", "m09", "Pim gave me Starglint: one extra coin on every coin I find, forever.");

            // ---- Act 2: the Hollow Postmaster (chapters 9–19) -------------------------------------------------------
            q.Story("m09", 9, "A Letter for Mothwick",
                "A letter is pinned to a tree, addressed to Mothwick: HAND OVER THE STAR, LITTLE COURIER, OR LOSE YOUR CAP. He clutches his cap. “He wouldn’t. It’s regulation!”",
                QuestBuilder.Go("Write a rude reply", "m10a", "We wrote the Hollow Postmaster a rude reply. Mothwick added “Kind regards”."),
                QuestBuilder.Go("Ignore it", "m10b", "We ignored the Postmaster’s letter. Mothwick filed it under “Nope”."));
            q.Sacrifice("m10a", 10, "The Biting Mailbox",
                "The reply has to go in the Postmaster’s mailbox, and the mailbox bites. Someone has to reach in. You don’t have hands. You have a whole worm.",
                QuestBuilder.HpPct(50), "Feed the mailbox", "m11a", "I fed our reply to the biting mailbox. It took half of me with it.", Wait);
            q.Sacrifice("m10b", 10, "Ignored Mail Fine",
                "A second letter arrives at once: IGNORING MAIL IS FINED 150 COINS. Mothwick is horrified. “It’s real! It has a seal and everything!”",
                QuestBuilder.Coins(150), "Pay the fine", "m11b", "I paid the Postmaster’s silly 150-coin fine. Mothwick insisted on a receipt.", Wait);
            q.Story("m11a", 11, "A Patch for Bravery",
                "While you rest, Mothwick sews a star patch onto his cap. “For bravery,” he says, and starts on a second one for you. “Where do worms keep their medals?”",
                QuestBuilder.Go("On my head", "m12", "Mothwick sewed a bravery patch on my head. I wear it proudly."),
                QuestBuilder.Go("On my tail", "m12", "Mothwick sewed a bravery patch on my tail. It wiggles proudly."));
            q.Story("m11b", 11, "The Receipt",
                "The receipt has a map printed on the back, by accident: a staircase of clouds and a round door. “The Postmaster is a terrible bureaucrat,” Mothwick beams. “Bureaucrats always leave clues.”",
                QuestBuilder.Go("Follow the map", "m12", "The receipt had a map to the cloud stairs on the back. We followed it."),
                QuestBuilder.Go("Frame the receipt", "m12", "I framed the receipt. Then we followed its map anyway."));
            q.Battle("m12", 12, "The Cloud Stairs",
                "The cloud stairs are guarded by something as wide as the steps and twice as rude, and it brought a friend. “Tickets, please,” it rumbles. You have no tickets.",
                10, 1, "Fight your way up", "m13", "I fought past the guardians of the cloud stairs.", Wait);
            q.Story("m13", 13, "Growth Spurt",
                "Pim is the size of a cherry now and keeps bumping against her lantern. “Stars grow fast near the sky,” says Mothwick. “Soon she’ll be too bright to carry.”",
                QuestBuilder.Go("Let her fly a little", "m14", "Pim flew loops around me for a while. I got dizzy."),
                QuestBuilder.Go("Keep her safe inside", "m14", "I kept Pim safe in her lantern. She sulked, then glowed."));
            q.Sacrifice("m14", 14, "A Gift for the Wind",
                "The wind on the stairs is far too strong for a little moth. “The Windkeeper will calm it,” Mothwick shouts, “for something legendary. One of your best tricks, I’m afraid.”",
                QuestBuilder.Legendary(), "Give a Legendary skill", "m15", "I gave the Windkeeper one of my Legendary skills. The wind went quiet.", Wait);
            q.Story("m15", 15, "The Door in the Cloud",
                "At the top of the stairs: a round door with a slot. MAIL ONLY. Mothwick straightens his cap. “Technically, Pim is mail.” Behind the door, two corridors: one sparkling, one very dark.",
                QuestBuilder.Go("The sparkling one", "m16a", "We took the sparkling corridor."),
                QuestBuilder.Go("The dark one", "m16b", "We took the dark corridor. Pim lit the way."));
            q.Story("m16a", 16, "Hall of Lost Letters",
                "Shelves of letters that never arrived reach all the way up. Mothwick reads one and sniffles. “From a comet to her mother. Twelve thousand years late.”",
                QuestBuilder.Go("We’ll deliver it", "m17a", "We took a comet’s letter along, twelve thousand years late, to deliver it."),
                QuestBuilder.Go("Read another", "m17a", "We read lost letters until Pim fell asleep."));
            q.Story("m16b", 16, "Something Breathing",
                "In the dark something enormous breathes slowly. Pim’s light shows a sleeping beast wearing a postmaster’s cap. “That’s not HIM,” Mothwick whispers. “That’s his pet.”",
                QuestBuilder.Go("Tiptoe past", "m17b", "We tried to tiptoe past the Postmaster’s pet. Worms are bad at tiptoeing."),
                QuestBuilder.Go("Say hello", "m17b", "I said hello to the Postmaster’s pet. It woke up."));
            q.Battle("m17a", 17, "The Shelf Keeper",
                "A guardian of the lost letters blocks the exit, eyeing the comet’s letter. “No letter leaves the Hall,” it booms, and its helpers crack their knuckles.",
                15, 1, "Fight the Keeper", "m18a", "I beat the Shelf Keeper. The comet’s letter is ours to deliver.", Wait);
            q.Battle("m17b", 17, "The Pet Wakes Up",
                "The pet is awake, and it brought friends. Mothwick puts on his sternest face, which is not very stern. “Sit! Stay! Please?”",
                15, 1, "Fight the pet", "m18b", "The Postmaster’s pet and its friends lost. Mothwick gave it a biscuit anyway.", Wait);
            q.Sacrifice("m18a", 18, "Postage Due",
                "To finally send the comet’s letter it needs stamps from a very old stamp machine: 300 coins. “It’s the right thing to do,” Mothwick says, and looks away politely while you count.",
                QuestBuilder.Coins(300), "Pay the postage", "m19", "I paid 300 coins to post a comet’s letter twelve thousand years late.", Wait);
            q.Sacrifice("m18b", 18, "A Bandage of Light",
                "Mothwick’s wing got nipped in the scuffle. Pim can mend it, but she needs warmth to borrow: yours, and a lot of it.",
                QuestBuilder.HpPct(80), "Lend her your warmth", "m19", "I lent Pim most of my warmth to mend Mothwick’s wing.", Wait);
            q.Boon("m19", 19, "Starglint, Brighter",
                "Pim, now the size of a plum, hums a note that makes your coin pouch jingle by itself. “Your Starglint!” cries Mothwick. “Two extra coins for every coin you find now. Do worms say thank you?”",
                1, "Thank you, Pim!", "m20", "Pim made my Starglint brighter: two extra coins on every coin I find.");

            // ---- Act 3: the Top of the Sky (chapters 20–30) ---------------------------------------------------------
            q.Story("m20", 20, "Wet Ink",
                "Another letter, the ink still wet: I ONLY WANT HER BACK. SHE WAS MY STAR FIRST. Mothwick goes very quiet. “Villains aren’t supposed to be sad.”",
                QuestBuilder.Go("Maybe he isn’t a villain", "m21a", "We wondered whether the Hollow Postmaster was really a villain."),
                QuestBuilder.Go("Keep going", "m21b", "We kept going. Pim looked back once."));
            q.Story("m21a", 21, "A Hole Where a Heart Was",
                "Mothwick explains: the Postmaster once carried the Night’s mail, until he dropped a star and a hole was left in his chest. “Hollow isn’t a nickname,” he sighs. “It’s a diagnosis.”",
                QuestBuilder.Go("We could help him", "m22", "Mothwick told me the Postmaster’s story. We decided to help him too."),
                QuestBuilder.Go("Pim comes first", "m22", "Mothwick told me the Postmaster’s story. Pim still comes first."));
            q.Story("m21b", 21, "Footprints of Light",
                "Tiny glowing footprints lead up the sky road. Pim gasps: they are hers, from long ago, when she first fell. “She’s retracing her steps,” says Mothwick. “Clever star.”",
                QuestBuilder.Go("Follow her footprints", "m22", "We followed Pim’s old glowing footprints toward the sky."),
                QuestBuilder.Go("Make new ones", "m22", "I made new footprints next to Pim’s. Mine were wigglier."));
            q.Sacrifice("m22", 22, "The Star Gate",
                "The sky road ends at a gate with a keyhole shaped like a legend. Only something mythic fits. Mothwick fidgets. “I’d give my own, but all I have is a cap.”",
                QuestBuilder.Mythic(), "Give a Mythic skill", "m23", "I gave a Mythic skill to open the Star Gate.", Wait);
            q.Battle("m23", 23, "The Last Guard",
                "Beyond the gate stands the Postmaster’s last guard, polished and proud, with a helper at its side. “The Postmaster is not seeing visitors,” it says. “Especially worms.”",
                20, 2, "Fight the guard", "m24", "I beat the Postmaster’s last guard behind the Star Gate.", Wait);
            q.Story("m24", 24, "The Hollow Postmaster",
                "There he is: a tall cloak with a lantern-shaped hole in its chest, sorting letters for ever. He sees Pim and stops. “You found her,” he says, very softly. “I dropped her. I never stopped looking.”",
                QuestBuilder.Go("Let them talk", "m25a", "We met the Hollow Postmaster. He had dropped Pim long ago."),
                QuestBuilder.Go("Stand between them", "m25b", "I stood between the Postmaster and Pim, just in case."));
            q.Story("m25a", 25, "Old Friends",
                "Pim floats to the Postmaster and lights the hole in his chest from inside. For a moment he isn’t hollow at all. “She can’t stay with me,” he says. “She belongs to the whole sky. But thank you.”",
                QuestBuilder.Go("Hug the Postmaster", "m26", "I hugged the Hollow Postmaster. It was like hugging a draught."),
                QuestBuilder.Go("Shake his hand", "m26", "I shook the Postmaster’s hand with my tail. He understood."));
            q.Story("m25b", 25, "A Worm’s Promise",
                "The Postmaster bows. “Good. Someone should guard her better than I did.” He hands you a stamp: TOP OF THE SKY. “The last stretch is the hardest. Something there eats light.”",
                QuestBuilder.Go("I’ll protect her", "m26", "I promised the Postmaster I would protect Pim to the end."),
                QuestBuilder.Go("What eats light?", "m26", "The Postmaster warned us about the thing that eats light."));
            q.Story("m26", 26, "The Truth About Worms",
                "On the last stretch Pim whispers to Mothwick, and he drops his satchel. “She says you fell too. Long ago. A tiny bright thing that landed on an empty planet and forgot it was a star.” You look at your tail. It is, faintly, glowing.",
                QuestBuilder.Go("That explains a lot", "m27", "Pim told us I fell from the sky too, long ago. That explains a lot."),
                QuestBuilder.Go("I’m just a worm", "m27", "Pim says I fell from the sky. I’m just a worm. Probably."));
            q.Battle("m27", 27, "The Light-Eater",
                "At the Top of the Sky waits a shadow that eats starlight, and it has brought its whole family. Pim hides behind your glowing tail. Mothwick rolls up his sleeves. He has no sleeves. He rolls up his wings.",
                30, 2, "Fight the Light-Eater", "m28", "We beat the Light-Eater at the Top of the Sky.", Wait);
            q.Story("m28", 28, "The Top of the Sky",
                "A quiet place full of humming stars, with a gap exactly Pim-shaped. But the way up is dark, and Pim is too small to light it alone. “Something must burn very bright,” says Mothwick. “Something full of memories.”",
                QuestBuilder.Go("Let me light the way", "m29a", "I offered to light Pim’s way home myself."),
                QuestBuilder.Go("We’ll do it together", "m29b", "Mothwick and I decided to light Pim’s way together."));
            q.Sacrifice("m29a", 29, "Every Trick I Know",
                "To burn bright enough you would have to let go of everything you learned on this run. Every skill, in one glorious flash. Pim’s light wobbles. “You don’t have to,” Mothwick whispers. “But oh, it would be bright.”",
                QuestBuilder.AllSkills(), "Forget every run skill", "m30a", "I burned every skill I had into one flash and lit Pim’s way to the sky.", Wait);
            q.Sacrifice("m29b", 29, "Everything, Together",
                "Mothwick empties his satchel of every undelivered letter into the light. “Your turn,” he says. To make it bright enough you must give everything you learned this run: every skill, gone in one flash.",
                QuestBuilder.AllSkills(), "Forget every run skill", "m30b", "Mothwick gave his letters and I gave every skill I had. It was very bright.", Wait);
            q.Boon("m30a", 30, "Pim Goes Home",
                "Pim zips up the bright path and clicks into her place. The whole sky hums one note louder. A last sprinkle of glitter falls on you. “Starglint at full strength!” sniffs Mothwick. “Signature here, please. Delivery complete.”",
                1, "Sign for it", null, "Pim went home to the sky. My Starglint is at full strength: three extra coins on every coin I find.");
            q.Boon("m30b", 30, "Delivery Complete",
                "Pim rises, then turns back and kisses the tip of your nose before she goes. The sky over your planet glows a little brighter now. “Three extra coins on every coin, for ever,” says Mothwick, stamping his book. “Best delivery of my career.”",
                1, "Wave goodbye", null, "Pim kissed my nose and went home. Three extra coins on every coin I find, for ever.");

            // ---- Epilogue: after the chain, Mothwick keeps dropping by (rotating, purely positive) ---------------------
            q.Epilogue("tea", "Tea Break",
                "Mothwick lands on your head with two thimbles of tea. “Just passing through! Pim says hello. She sent you pocket money.”",
                "Thank you!", QuestEpilogueKind.Coins, 80);
            q.Epilogue("glow", "A Warm Glow",
                "Pim’s light falls on you from far above, warm as a blanket. Mothwick waves from a cloud. “She’s keeping an eye on you!”",
                "Thank you!", QuestEpilogueKind.HealPct, 30);
            q.Epilogue("parcel", "Special Delivery",
                "“Parcel for the Worm Who Fell!” Mothwick hands you a box. Inside: a choice of new tricks. “No signature needed. You’re family.”",
                "Open it", QuestEpilogueKind.SkillPick, 1);
            q.Epilogue("biscuit", "Star Biscuit",
                "Mothwick shares a biscuit baked on Pim’s warmth. You feel sturdier already. “Don’t tell the Postmaster. He wanted the last one.”",
                "Thank you!", QuestEpilogueKind.MaxHpPct, 10);
            return q.Quest;
        }
    }
}
