# UI and Player Interaction

How the player interacts with the simulation: speed controls, notifications, zone drawing, lever adjustments, dashboard, save/load.

## Game Speed Controls

- **Pause / 1× / 2× / 5× / 10×** speed buttons. Default speed: 1× (1 tick/sec).
- **Keyboard:** spacebar = pause toggle; `+` / `−` = speed up / slow down by one notch.
- Current speed displayed prominently in the HUD.

## Notifications

Events fire notifications grouped by **criticality**. Each level has different default UI treatment:

- **Critical** — auto-pause + modal dialog. Player must dismiss before continuing.
- **Important** — non-blocking notification (toast / banner) + entry in notifications log + audible cue. No auto-pause.
- **Informational** — log entry only; quietly visible in the notifications panel but no toast or sound.

All thresholds, criticality assignments, and auto-pause behaviors user-adjustable via `levers.md`. Player can override per-event criticality in settings.

### Critical (auto-pause)

| Event |
|---|
| Treasury bankruptcy game over (negative for 6 months) |
| First treasury-negative event of the session (bankruptcy timer started) |
| First structure-inactive event of the session |

### Important (toast + log)

| Event |
|---|
| Treasury balance below threshold (e.g., < $50,000) |
| Subsequent structure-inactive events |
| Demand pool saturated for K consecutive ticks |
| Population dropping > X% in a month |
| Climate or nature below threshold (e.g., < 0.2) |
| Regional reservoir empty |
| High-tier jobs unfilled (tier without matched housing) |

### Informational (log only)

| Event |
|---|
| Structure unprofitable warning (month 1 of 2) |
| Structure auto-reactivated |
| Storage running near-unprofitable |
| Milestones (first settler arrival, first commercial structure built, 10k population, etc.) |

The "first X of session" events surface only once per game session even if the underlying condition recurs — to avoid spamming the player after they've acknowledged the mechanic exists.

## Levers UI

Sidebar / menu listing all `levers.md` parameters, grouped by category:

- **Taxes** (income, property, sales, import upcharge)
- **Capacities** (service-structure capacities, internal industrial storage, regional treasury overflow cap)
- **Wages** (per-employer-category × education tier)
- **Rents** (per residential structure type)
- **Utility costs** (per residential / commercial / industrial structure type)
- **Environment** (degradation rates, restoration rates, climate/nature coefficients)
- **Game** (game speed default, autosave interval, notification thresholds)

Each lever has a slider (for percentages) or numeric input (for dollar amounts). "Reset to defaults" button per category and globally.

Lever changes take effect at the next applicable settlement (per `time-and-pacing.md`).

## Zone Drawing

- **Click-and-drag rectangle** to draw a residential or commercial zone.
- **Visual color overlay**: residential zones green, commercial zones blue.
- Click an existing zone to **resize** (drag corners) or **delete** (with confirmation if structures exist inside).
- Hover to see zone capacity (number of structures that fit).

## Dashboard / HUD

Always-visible top bar:

- **Population** — city / reservoir / total (e.g., "12,432 / 47,568 / 60,000").
- **Treasury balance** with delta arrow (this-month inflow vs. outflow).
- **Climate / Nature** values with characterization label (e.g., "Eden / pristine").
- **Time / date** (current game date, current speed).

Collapsible panels:

- **Demand pools overview** — list of pools with fill bars; saturated pools flagged red.
- **Labor pool composition** — breakdown by employed / unemployed / in-affordable-housing / wageless.
- **Notifications log** — chronological list of fired notifications, dismissable.
- **Structure inventory** — count of each structure type, status (active / inactive / under construction).

## Save / Load UI

- **File picker / save slot system** — 10 slots with thumbnail previews.
- **Autosave** — every 10 game-years (3,600 ticks) by default; configurable.
- **Save file naming**: `[city_name]_[date]_[tick].save` for manual saves; `autosave_[N].save` for autosaves.
- Quicksave / quickload keyboard shortcuts (F5 / F9 by convention).

## Demolition Confirmation Dialogs

When the player attempts to demolish a structure with consequences, show a confirmation:

- **Residential:** "Demolish [Structure]? This will displace N tenants who will run an immediate emigration check."
- **Industrial / commercial:** "Demolish [Structure]? This will lay off M workers; they will become wageless and most will emigrate within 1–2 months."
- **Treasury-funded (civic / healthcare / education / utility):** "Demolish [Structure]? You will lose [X] capacity for [demand pool]; affected agents may experience reduced satisfaction."
- **Industrial storage:** "Demolish [Structure]? Manufacturing of [resources] will stall until alternative storage exists."
