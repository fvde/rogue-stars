# Quest / achievement counters

Append-only list of `state.Count(...)` keys: `key` — meaning — owner.


## Run workstream (RunSystem, LaboratorySystem)
- `runs_started` — runs started (any mode) — Run
- `runs_started_<mode>` — runs started per mode (`normal`, `overcharged`, `burrow`, `darkside`) — Run
- `runs_completed` — runs won and paid out — Run
- `runs_completed_<mode>` — runs won per mode — Run
- `runs_failed` — runs lost or abandoned (pending loot lost) — Run
- `levels_cleared` — first clears of a level (normal or overcharged) — Run
- `run_rolls` — dice rolls in runs — Run
- `run_battles_won` — battles won in runs (incl. events and bosses) — Run
- `run_elites_killed` — elite and miniboss battles won — Run
- `run_bosses_killed` — run bosses (incl. burrow floor bosses) defeated — Run
- `run_boss_awakenings` — boss awakening thresholds reached — Run
- `run_skills_drafted` — draft picks — Run
- `run_events_resolved` — event / campfire / treasure choices resolved — Run
- `run_revives` — revives used — Run
- `run_feats_earned` — level feats earned (first time each) — Run
- `burrow_floors_cleared` — Burrow floors cleared — Run
- `lab_research_started` — laboratory research started — Run
- `lab_research_completed` — laboratory research collected — Run
