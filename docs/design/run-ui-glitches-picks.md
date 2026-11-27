# Run UI glitches: owner picks (2026-11-27)

Owner decisions per finding in `run-ui-glitches.md`. Findings not listed here are NOT to be fixed now.

#2 fix: modal HUD strip same as board strip (SliceHud call + anchors, keeps XP fill); ALSO gold count text sized to match the other pill contents, coin icon to board size
#9 fix differently: the Skills book button is only shown on skill selection screens (incl. shop), hidden elsewhere in the run
#10 fix differently: two floating, animated (bobbing) arrows pointing down at the two fork branch tiles; "Go left" / "Go right" buttons at the bottom of the UI, each tinted like its arrow
#12 fix as suggested
#16 fix differently: campfire Train offers only THREE skill cards (was 4, the 4th got clipped); pick 3 the same way as now, layout fits at 1080 and 720
#17 fix, consolidated: one shared choice-event sheet/layout for all choice events (event, chest, campfire, quest, outcome), not per-screen patches
#18 fix as suggested, on ALL chest types (mythic confirmed by owner)
#19 fix, with a new design using a tombstone icon
#20 fix differently: just drop the hint
#22 fix as suggested
#23 fix, but the cards themselves must NOT grow (fit the content inside the current card size)
#24 fix differently: the signature pick happens at the start of the run on the actual run map (board visible behind the pick sheet), not on a separate screen. Open sub-question asked in thread: before first ROLL vs node on start tile
#27 fix as suggested
#30 fix: 3+ enemies in battle: keep HP bars/labels at a readable minimum size; do NOT use two columns: stack the enemies in one column using the free vertical space below, so they need not shrink to 0.6
#31 fix as suggested
#33 fix differently: place the aliens better in battle (owner: "Place the aliens better"); agent proposes a placement with screenshots
#34 fix as suggested
#37 fix as suggested, and make it somewhat larger
#39 fix differently: run-result reward row becomes a horizontal (left-right) scroll strip, rewards ordered by rarity, highest first
#40 fix differently: remove the "surge xN applied" line; instead animate the doubling/multiplying on the reward tiles (count-up / pop when the multiplier applies)
#42 fix as suggested
#43 fix with the "consistent" option (same treatment everywhere)
