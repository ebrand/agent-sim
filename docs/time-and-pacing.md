# Time and Pacing

How simulation time relates to real time.

## Tick = One Day

The fundamental simulation tick represents **one game-day**. Agents age one day per tick; industrial production accumulates one day's worth of output per tick; demand pools update once per tick; environmental degradation applies daily.

Many design timelines in other docs are written in **months** or **years** for readability. The implementation translates these to ticks at 30 ticks per month (e.g., "3-month wageless queue" = 90 ticks; "6-month construction" = 180 ticks; "1-year primary school" = 360 ticks). The design language stays in months; only the runtime works in ticks.

## Periodic Events

Settlement events are spread across **five days of the month** so no single day carries all the weight. Each event has its own dedicated day:

| Day | Event | Direction |
|---|---|---|
| 1 | Treasury upkeep paid out | Treasury → civic / healthcare / education / utility / affordable-housing subsidy |
| 1 | Agent rent | Agent → city treasury |
| 1 | Wage installment 1 (with income tax withheld) | Employer → agent (income tax → city treasury) |
| 8 | Licensing fees | Service-only commercial → regional treasury |
| 15 | Utilities | Agent / commercial / industrial → city treasury |
| 15 | Wage installment 2 (with income tax withheld) | Employer → agent (income tax → city treasury) |
| 22 | Sales tax | Commercial → city treasury |
| 30 | Property tax | Commercial / industrial → city treasury |
| 30 | End-of-month profitability check | Per industrial / commercial structure |

### Sub-ordering: payouts before incomes (per actor)

On settlement days (1, 15) where an actor has both an outflow and an inflow, **the outflow fires first**. This is a per-actor rule, not a global ordering — each actor independently processes their outflows before their inflows on a given day.

- **Treasury** on day 1: upkeep paid out (treasury → civic / healthcare / education / utility / affordable-housing subsidy) before rent comes in (agent → treasury). Treasury must cover upkeep from its existing balance; this month's rent doesn't bail it out same-day. Bankruptcy timer counts only end-of-day state, so transient mid-day-1 dips don't trigger.
- **Agents** on day 1: rent paid (agent → treasury) before wage installment 1 (employer → agent). Agents must hold savings to cover rent upfront. New immigrants arrive with starting savings sized to one month of expenses at their education tier (see `agents.md`).
- **Agents** on day 15: utilities paid before wage installment 2.

This rule does not require global ordering across actors — treasury can be paying out at the same conceptual moment that an agent is paying rent. Each actor's internal sub-order is what's specified.

### End-of-month profitability check (day 30)

The profitability check fires **after** all day-30 settlements have completed. The structure evaluates the month with this month's property tax already paid — an honest measure. If the structure was unprofitable over the month, it queues wage cuts to take effect on the next scheduled pay cycle (day 1 or day 15, whichever comes first).

**Wage cuts are re-evaluated against current profitability on the payday they would take effect**, not committed at the moment of the day-30 check. So a structure that was unprofitable on day 30 but recovers between day 30 and day 1 (e.g., a buyer placed a large order) will skip the wage cut. This makes the system responsive to short-term recovery and avoids penalizing structures whose situation has already improved by paycheck day.

### Lever changes take effect next applicable settlement

When the player adjusts a tax rate, wage table, utility rate, or other lever via `levers.md` mid-month, the new value applies starting from the next applicable settlement. A property-tax-rate change on day 5 doesn't affect the day-30 settlement... wait, that's wrong: day 5 is before day 30, so the day-30 settlement uses the new rate. Restated: levers take effect immediately for any settlement that fires after the change. A property-tax change on day 25 affects the day-30 settlement; one made on day 31 does not.

### Daily (every tick)

- Agent aging (1 day per tick).
- Industrial production (per `supply-chain.md`).
- Demand pool recalculation.
- Environmental degradation per industrial structure.
- Restoration recovery per restoration structure.
- Construction progress (a 90-tick building advances 1/90 per tick).
- Birth / death probability checks.
- Goods sales transactions (industrial → industrial, industrial → commercial, commercial → agent) settle continuously as buyers pull. Structures' cash balances grow continuously from sales. **Continuous transactions all settle before periodic settlement events fire on a given day** — i.e., all of day N's goods sales settle into structures' cash, then any day-N settlement events (wages, taxes, etc.) fire against that updated cash balance.

### Daily (every tick)

- Agent aging (1 day per tick).
- Industrial production (per `supply-chain.md`).
- Demand pool recalculation.
- Environmental degradation per industrial structure.
- Restoration recovery per restoration structure.
- Construction progress (a 90-tick building advances 1/90 per tick).
- Birth / death probability checks.


## Game Speed

Variable game speed with pause:

- **Pause** — sim halts; player can examine state, place structures, etc.
- **1× / 2× / 5× / etc.** — multipliers on real-time tick rate.

**Default real-time pace: 1 tick / second.** At default, 1 month of game time = 30 seconds; 1 year = 6 minutes; a full 60-year lifespan = 6 hours real-time. User-adjustable.

## Save / Load

Long sessions are expected (a 60-year city evolution at 1× = ~6 hours real-time). The simulation supports:

- **Pause at any time** — full state preserved.
- **Save game in progress** — serialize the current simulation state to disk.
- **Load saved game** — resume from a saved state.
- **Autosave** — every 10 game-years (3,600 ticks) by default. Configurable.

### Save file format

**Binary format from alpha-1** (msgpack or protobuf — specific choice TBD; both support versioning and compact serialization). JSON was considered but discarded because save state for 60k agents + structures + pools easily exceeds 100MB in JSON, which is slow to parse and bulky to ship. Versioned via a `format_version` field so saves can be migrated.

**Captured state:**

- Current tick (day count from sim start) and current date
- RNG seed and current PRNG state
- Region: climate, nature, natural resources distribution, regional agent reservoir per education tier, regional goods reservoir per resource
- City: zones (geometry), structures (id, type, position, construction progress, internal storage levels, employee roster, cash balance, profitability state and warning flag)
- Agents: id, age in days, education tier, employer (structure id or null), residence (structure id), current wage, savings, in-affordable-housing flag, attempt-used flag, wageless flag
- Treasury: city treasury balance (regional treasury is functionally infinite; no balance to save)
- Demand pools: per-pool current values, backlog counters, saturation timers
- Active and dismissed notifications

## Tick Processing Order

Within a single tick, events fire in a defined order. This is critical for save/load determinism and avoids subtle ordering bugs.

1. **Increment tick counter; advance current date.**
2. **Daily events** (every tick, every agent / structure / pool):
   - Agent aging (1 day)
   - Construction progress on building structures
   - Industrial production (per chain stage)
   - Environmental degradation per industrial structure
   - Restoration recovery per restoration structure
   - Demand pool recalculation (from current population / structure capacity)
   - Birth / death probability rolls per agent
3. **Continuous transactions settle:**
   - All goods sales for the day (extractor → processor → manufacturer → storage → commercial → agent)
   - Updates structure cash balances
4. **Monthly settlement** (day 30 only — all money flows happen here in a fixed sequence). Days 1, 8, 15, 22 are economic no-ops. The sequence:
   1. **Treasury upkeep**: civic / healthcare / education / utility / affordable housing structures funded. Sets `UpkeepFundingFraction`. Partial-pay if treasury < total upkeep (pay treasury / 6 this month).
   2. **Agent outflows**: rent + utilities → treasury.
   3. **Structure outflows**: commercial / industrial utilities + property tax → treasury.
   4. **COL spending**: agent → commercial, then commercial pays storage / region / imports for goods.
   5. **Sales tax**: commercial → treasury (computed from this month's COL revenue).
   6. **Wages**: employer → agent net of income tax (single full payment, no installment splitting). Income tax → treasury.
   7. **Profitability check + monthly accumulator reset.**
   8. **Insolvency emigration**: agents whose Savings went negative after all the above.
   9. **Worst-of service emigration**: surviving agents roll against `(60 - worst_sat) / 100 × 0.02` (see `feedback-loops.md`).
   10. **Births.**
   11. **Bankruptcy clock + game-over check.**
5. **Notification firing and auto-pause checks.**
6. **Autosave check.**

The old "outflows before inflows" sub-ordering rule is no longer load-bearing — the single-day settlement applies all flows in the fixed sequence above, which already deducts agent costs before crediting wages.

## RNG Seeding

A single seeded PRNG instance handles all random operations: immigration tier selection, settler distribution, birth/death rolls, vacancy contention, natural-resource distribution at game start, and any other stochastic events.

- Seed is stored in the save state.
- Loading a save re-creates the PRNG with the saved seed and state, ensuring deterministic replay.
- New game: seed defaults to current Unix time; player can override at game-start config for reproducibility.

## Execution Model

Alpha-1 is **single-threaded.** All tick processing runs in one thread, simplifying RNG ordering, save state consistency, and event sequencing. Future versions may parallelize daily-event processing across agents, but that requires a different RNG strategy (per-agent seeds or thread-local PRNGs) and isn't a concern for alpha-1.

## Notifications and Auto-Pause

Critical events should fire notifications so the player doesn't have to watch the screen continuously. Candidates:

- Treasury below threshold (e.g., < $50,000).
- Treasury negative (bankruptcy timer started).
- Industrial structure unprofitable / cutting wages.
- Population dropping rapidly.
- Demand pool saturated for K consecutive ticks.
- Environmental quality below threshold.

**TBD:** which events warrant auto-pause (vs. just a notification) and which can be silenced by the player.

## Construction Durations

**All structures take 3 months (90 ticks) to build**, regardless of category. At 1× game speed (1 tick/sec), every build takes 90 seconds real-time. A construction progress indicator is shown during the build period so the player can see status without watching the clock.

Players who don't want to wait can speed up game-time.
