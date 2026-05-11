# Open Questions and TBDs

Decisions deferred from the alpha design, parameters that need tuning, and entire subsystems not yet spec'd.

## Diagrams — outdated, see `diagrams-out-of-date.md`

The illustrative diagrams have lagged the simplification pass. Full punch list in `diagrams-out-of-date.md`. Quick summary:

- `resource-pricing.png` — **current**, no changes
- `education.png` — minor: rename `% chance` to seats-driven
- `legend.png` — medium: natural resources list, agent property tax, education label
- `supply-chain.png` — medium: outdated right-side pricing table (use `resource-pricing.png` as source of truth)
- `agent-income-expense.png` — medium: 5%/10% income tax / disposable swap
- `economy.png` — medium: same income/expense swap + remove agent property tax row
- `agent-sim.png` — **major**: 4-tier industrial chain, add fuel storage, add utility category, residential demand per-type. This is the master diagram.

Diagram updates do not block coding the simulation — text docs are the source of truth. Prioritize `agent-sim.png` as the master overview when convenient.

## Tier 3 — RESOLVED

All Tier 3 items resolved and documented:

### Edge cases
- **Demolition** — residential displaces tenants (immediate emigration check), industrial/commercial lays off employees (next monthly check), treasury-funded reduces service capacity, storage stalls upstream chain. Confirmation dialogs warn before demolition. Documented in `structures.md`.
- **Mass-immigration throttling** — capped at 200 immigrants/month city-wide, spread across the 30 days. Documented in `agents.md`.
- **Empty regional reservoir** — immigration halts when reservoir is at 0; city continues to function via births/deaths/emigration. Notification fires. Documented in `agents.md`.
- **No commercial structures** — agent COL spending fails silently (money stays in disposable / savings). No money lost; commercial revenue just doesn't happen. Notification fires. Documented in `economy.md`.
- **Multi-city** — explicitly out of scope; one city per region. Documented in `overview.md`.
- **New-game configuration** — region name, initial climate/nature, starting treasury override, difficulty preset, RNG seed. Documented in `overview.md`.

### UI / Player interaction
- All UI items consolidated into a new `ui-and-player.md` doc covering: game speed controls, notifications (with auto-pause defaults), levers UI, zone drawing, dashboard / HUD, save/load UI, demolition confirmation dialogs.

### Implementation specifics
- **Save file format** — binary (msgpack or protobuf TBD), versioned. JSON discarded due to size for 60k agents.
- **Execution model** — single-threaded alpha-1; multi-threaded later.
- **Notification criticality tiers** — critical (auto-pause+modal), important (toast+log), informational (log-only). 3 critical events, 7 important, 4 informational.
- **Mass-immigration cap** — `max(50, 1% × city_population)`/month, scales with city growth.
- **Tick processing order** — full per-tick sequence documented in `time-and-pacing.md`.
- **RNG seeding** — single seeded PRNG, seed in save state, deterministic replay. Documented in `time-and-pacing.md`.

## Tier 2 — RESOLVED

All Tier 2 default values landed:

- **Income tax:** 5% flat (lowered from 10%)
- **Property tax:** 0.5% of structure value per month (lowered from 2%). Placeholder structure values landed in `economy.md` (commercial $200k–$400k, industrial $80k–$500k by category, residential & treasury-funded exempt).
- **Sales tax:** 3% of commercial revenue per month (lowered from 5%)
- **Treasury upkeep raised ~3×** to better balance against revenue (police/fire $30k, town hall $90k, clinic $60k, hospital $250k, schools $60k–$180k, generator $80k, well $50k, affordable housing $20k)
- **Storage revenue:** 20% pass-through fee (manufacturer sells to storage at 80% of price; storage sells to commercial / regional at full price). Storage workforce reduced to 10 workers (vs. standard 100). Tightly coupled to throughput — under-utilized storage runs unprofitable.
- **Income tax:** 5% (lowered from 10%); the freed 5% goes entirely to disposable income (now 10% of wage from 5%). Other expense percentages unchanged.
- **Utility cost (residential):** fixed per type ($200 / $350 / $450 / $700 / $200)
- **Utility cost (commercial / industrial):** placeholder per-structure-type values landed in `economy.md`. Shops $2k, marketplaces $4k, extractors $1–1.5k, processors $3–5k, manufacturers $4–6k, storage $1k. Calibrated to ~5% of expected revenue.
- **Climate / nature → immigration & emigration:** `1 ± (climate − 0.5) × 0.3 ± (nature − 0.5) × 0.3`. Range ~0.7× to ~1.3×.
- **Climate / nature degradation per industrial structure:** spec'd in `structures.md`.
- **Climate / nature floor:** 0.05.
- **Restoration recovery rates:** spec'd in `structures.md`.
- **Region.Treasury overflow cap:** 50,000 units per resource per month.
- **Default game speed:** 1 tick / second (1×).
- **Affordable housing capacity:** 40 agents per structure.
- **Construction money cost** (treasury-funded structures): $10k civic, $20k–$50k healthcare/education/utility/affordable housing.
- **Treasury upkeep:** spec'd per structure type in `economy.md`.

## Tier 1 — RESOLVED

All Tier 1 items resolved and documented:

1. **Agent lifecycle** — 60-year lifespan (21,600 days), no retirement band; age bands documented in `agents.md`; education durations 6/5/5 game-years for primary/secondary/college; **seats-driven** education progression; birth rate 0.5%/mo of working-age population, gated by housing waitlist; settler distribution 60% uneducated / 40% primary.
2. **Job assignment** — FIFO queue per education tier. Higher-tier agents try matching tier first, drop to lower tiers if no openings. Lower-tier agents cannot take higher-tier slots.
3. **Service structure capacities** — alpha-1 placeholder values documented in `structures.md` (primary school 200 seats, hospital 2,500 agents, etc.). User-adjustable via `levers.md`.
4. **"Non-profitable" definition** — 2 consecutive unprofitable months → inactive. Auto-reactivate when conditions improve. Notification on warning (month 1) and on transition.
5. **Worst-of service emigration** — threshold 60%, scale 0.05 (max 3% emigration/mo at 0% satisfaction). Worst-of {civic, healthcare, education-at-tier, utility}. Documented in `feedback-loops.md`.
6. **Per-capita demand factors** — goods-backed COL converts $-amount to units via price; entertainment is service-only ($-amount only); civic/healthcare/utility/education = 1 unit per agent (or per child, for education tier) per month. Documented in `demand-and-goods.md`.

## Recently Resolved (kept here for traceability)

- **Natural-resources reconciliation** — resolved by `supply-chain.png`. Eight resources: timber, ore, arable land, stone, sand, coal, water, wind.
- **Restoration demand pool** — removed. Restoration is manual-only; player monitors climate/nature directly.
- **Industrial chain back-pressure** — extraction, processing, and manufacturing proceed by default; they stall only when there is nowhere to store output (storage missing or full).
- **Manufactured-goods sales prices** — provided in `supply-chain.png`. Raw materials sell at 25%, processed goods at 50%, manufactured at 100% of unit price.
- **Wage model** — per (structure type × education level). Specific values TBD via calibration.
- **Commercial sub-types** — alpha-1 has a single commercial demand pool. Sub-types are cosmetic.
- **Utility cost flow** — agents, commercial, and industrial all pay utilities to the treasury. Treasury funds utility-structure construction and upkeep.
- **Education progression model** — `% chance` per agent at each tier transition. **TBD** confirm seats gating.
- **Internal industrial storage capacity** — default 1000 units, user-adjustable.
- **Water role** — utility consumption only (not a separately tracked good consumed by agents).
- **Food role** — purchased from commercial structures as part of cost-of-living (arable-land chain provides it).
- **Import upcharge** — 25% over local price, user-adjustable.
- **Bankruptcy game-over trigger** — treasury negative for 6 consecutive months ends the game.
- **Agent spending priority** — rent → utilities → COL. Shortfalls cut COL first, then utilities, then rent.
- **Sustained-rent emigration** — agent whose rent has been shorted for 4 consecutive months begins emigrating.

## Major Subsystems Not Yet Specified

### Spatial Layer

The alpha references zones, structure placement, land value, and adjacency without defining a spatial system. To resolve:

- Tile-based grid vs. polygonal zones vs. abstract slots.
- Structure footprints, zone sizing.
- Cell adjacency rules and the **resource adjustment** + **externality adjustment** functions referenced in `region.md` (the land-value formula needs concrete distance/proximity definitions).
- How auto-spawn chooses placement within a zone.
- Whether industry near residential should impose a local externality penalty.

Until the spatial layer lands, **land value uses a placeholder fixed value per structure type.** Rent and property tax read off a fixed table rather than from cell calculations.

### Transportation Networks

All goods routing in the alpha is instantaneous and frictionless. To resolve:

- Define **shipping nodes** and **edges** between them.
- Define **range constraints** — sources and targets must be within range of a node.
- Decide on friction: time delay, throughput capacity, loss in transit, cost.
- Decide whether transportation is player-built infrastructure (with construction recipes) or implicit.

### Multi-Region World

The "regional import reservoir" implies an off-region elsewhere. Whether multiple player-managed regions exist, or imports come from an abstract off-screen world: undecided.

### Agriculture / Food Loop

Resolved as a cost-of-living component: agents purchase food from commercial structures, food is supplied by the arable-land chain (food packing). Food shortages flow through the standard COL-shortage path (COL is shorted first when wages are insufficient). **TBD** whether food specifically — vs. other COL goods like clothing or toys — has additional standing as a "necessity" with stronger consequences when shorted.

## Recently Resolved (continued)

- **Branched-chain pricing** — resolved by explicit per-good pricing (single price per intermediate good regardless of downstream destination).
- **Manufactured-goods price and recipe table** — captured from `resource-pricing.png` into `supply-chain.md`. See sanity flag on metal goods below.
- **Wage table defaults** — primary $2,500 / secondary $4,500 / college $7,000 per month; all employer categories share these defaults but each is independently user-adjustable.
- **Income tax** — 10% flat (default), user-adjustable.
- **Rent** — fixed per residential land-value tier ($750 / $1,350 / $2,100 for low / medium / high LV).
- **Utility cost** — % of land value (rate user-adjustable). Same model for residential, commercial, industrial.
- **COL components and percentages** — food 15%, clothing 10%, household 10%, sporting goods 5%, toys 5% (in the balanced case).
- **"Furniture" renamed to "household."**

## Recently Resolved (continued)

- **Metal goods 12× markup anomaly** — resolved. Rebalanced to clean 2× markup ($48 sale / 3-unit recipe).
- **Sporting goods and toys** — removed from the manufactured goods set. Alpha-1 has 7 manufactured goods (household, bldg supplies, metal goods, food, clothing, concrete, glass goods).
- **Within-COL shortfall ordering** — resolved. Order is food (highest) → clothing → household → entertainment (shorted first).
- **Uneducated wages** — added: $2,000/month default.
- **Residential property tax** — removed entirely. No structure-level or agent-level residential property tax; what was that line is now folded into the rent agents pay. Single residential housing-cost line.
- **Entertainment** — added as a new COL category (5% of wage in tier-matched case). Implicitly fulfilled by commercial structures via the entertainment commercial sub-type.
- **Manufactured-goods 2× markup norm** — all 7 manufactured goods now sit at exactly 2× markup over input cost.

## Recently Resolved (continued)

- **Uneducated jobs** — uneducated is a valid job-slot education requirement for all industrial structures (extractor, processor, manufacturer, storage). Mix per structure: TBD.
- **4 LV tiers** — confirmed; `region.md` updated. Spatial layer (when it lands) needs to support 4 LV bands.
- **Disposable income** — saved by the agent as a buffer to cover future shortfalls. Savings parameters (cap, draw order, interest, fate on emigration) TBD.
- **Levers.md** — cleaned by the user.

## Recently Resolved (continued)

- **Entertainment as a service** — resolved. Entertainment commercial sub-types are flagged service-only: they generate commercial revenue from agent spending but do not consume manufactured goods. Goods-backed sub-types (shopping) handle food / clothing / household via the standard goods chain.
- **Savings draw vs. cut order** — savings-first.
- **Job-loss downstream flow** — disposable → 0 → savings drained → COL cut bottom-up (entertainment → household → clothing → food) → **homeless** (no longer immediate emigration).
- **Industrial education mix** — 15% college / 20% secondary / 40% primary / 25% uneducated, per industrial structure.
- **Service-only revenue balance** — service-only commercial structures pay licensing fees (ASCAP, franchise, marketing) equivalent in magnitude to manufactured goods cost, balancing the absence of real input costs.
- **Job loss trigger** — two mechanisms: (1) non-profitable structure cuts wages bottom-up; agent works one cut-wage month then quits; (2) random fired-for-cause at 0.5–1.5%/month per agent.
- **Emigrants retain savings** — savings travel with the agent.
- **Homelessness replaces immediate emigration** — homeless agents stay in the city, look for work, and degrade attractiveness. Eventual emigration occurs after sustained homelessness (timing TBD).

## Recently Resolved (continued)

- **Homelessness → emigration timing** — ~2 months out of work, then emigrate. Same window applies whether sheltered or unsheltered.
- **Wage-cut mechanism** — one worker at a time, lowest-paid tier first (uneducated → primary → secondary → college). Higher-tier workers can do lower-tier work.
- **Wageless duration before quitting** — 3 months at zero wage, then the worker quits.
- **Fired-for-cause rate** — 0.05% per month per agent.
- **Licensing fee destination** — Region.Treasury (new entity), not city treasury.
- **Region.Treasury** — new top-level object alongside city treasury. Receives licensing fees from service-only commercial and revenue from regional goods sold to local commercial.
- **Affordable housing** — new residential sub-type for homelessness mitigation. Treasury-subsidized low rent, negative LV impact on neighbors.
- **Homeless shelter** — new civic sub-type. Treasury upkeep. Holds homeless in seats, reduces attractiveness penalty, does not extend the 2-month emigration timer.

## Recently Resolved (continued)

- **Tick = 1 day.** All monthly events fire together on the end-of-month tick (every 30th tick). Daily events: production, aging, demand, environmental degradation, construction progress. See `time-and-pacing.md`.
- **All construction is 3 months (90 ticks)** with a progress indicator.
- **Age bands stored as day-counts**, not months/years. Months are calendrical shorthand in docs only.
- **Affordable housing benefits** — $500 rent, **6-month stay timer** (independent of the structure's 3-month wageless queue, which is unchanged), and **FIFO hire priority** over fresh immigrants from the regional reservoir.
- **Hire priority order: FIFO** (longest-resident-in-affordable-housing first).
- **Affordable housing at capacity** — agents who can't get a seat emigrate at end of their wageless window.
- **Save / load and pause** — alpha-1 supports pause-anywhere and save/load to disk; long sessions are expected.
- **Settlement cadence (final)** — events spread across 5 days: Day 1 (treasury upkeep, rent, wage 1), Day 8 (licensing fees), Day 15 (utilities, wage 2), Day 22 (sales tax), Day 30 (property tax, profitability check). All "pay-out before pay-in" sub-ordering on settlement days.
- **"% of labor pool from affordable housing" stat** — surface this in the player HUD.
- **Job-loss flow simplified** — homelessness state, homeless shelter civic structure, and COL cut sequence are all REMOVED. Flow is now: wage cut → 3 months wageless (savings drain, optional move to affordable housing) → re-instate or emigrate.
- **Affordable housing eligibility** — employed AND current wage strictly under $2,000. Wageless-queue agents (current wage $0) qualify. Jobless agents do not (and don't need to, since the homeless state is gone — they emigrate at end of 3-month wageless window).
- **Affordable housing rent** — $500/month, treasury-subsidized.
- **Wage cut order** — highest-paid first (profit-maximizing, since production drops linearly per cut regardless of tier).
- **Production tied to jobs filled** — output scales linearly. Daily accumulation per tick.
- **Region.Treasury is functionally infinite for accounting** — no balance tracked. Per-resource cap on overflow purchases (TBD value) prevents runaway local production.
- **Wageless queue + re-hire** — within the 3-month window, automatic re-instatement on profitability recovery. After 3 months, agent quits and emigrates back to regional reservoir.
- **Emigrants retain savings.** Agent records return to the regional reservoir (well of souls).

## Recently Resolved — RADICAL SIMPLIFICATION (current state)

The full simplification pass collapsed many intermediate mechanics into one monthly check. The following are now removed:

- **Wage variance (±5%)** — REMOVED. Single fixed wage per tier.
- **Wage cuts in unprofitable months** — REMOVED. Structures go fully inactive when unprofitable; no per-worker wage reductions.
- **Wageless 3-month queue + re-instatement** — REMOVED. Inactive structure → all employees wageless → run standard emigration check.
- **Fired-for-cause randomness** — REMOVED. Job loss only happens via structure-inactive.
- **Homeless state and homeless shelter civic structure** — REMOVED.
- **COL cut sequence (entertainment → household → clothing → food)** — REMOVED. Shortfall is a single binary: pay full or fail check.
- **LV migration in-city** (agents moving between LV tiers based on wage) — REMOVED.
- **6-month affordable housing stay timer** — REMOVED.
- **Hire priority (FIFO) for affordable housing residents** — REMOVED.
- **Per-cell LV affecting residential rent** — REMOVED. Rent is now per residential structure type.

The following are still in:

- **Single emigration rule** — `if wage + savings < monthly expenses → one attempt to move to affordable housing → otherwise emigrate`. See `economy.md`.
- **Affordable housing as one-time second chance** — $500 rent, treasury-subsidized, eligibility = employed under $2,000 OR wageless. One-attempt-to-move per agent.
- **Residential structure types with fixed rent** — house ($800) / apartment ($1,400) / townhouse ($1,800) / condo ($2,800) / affordable housing ($500). Capacity per type. Replaces the LV-tier rent scheme.
- **Structure-inactive on unprofitability** — single state change at day-30 profitability check; no graceful degradation.
- **Cell LV still affects** structure value (for property tax) and utility costs (for any structure). Just not rent.

## Still Open

- **Affordable housing capacity per structure.** Rent and eligibility spec'd; capacity TBD (currently noted as ~30 agents in the recipe table, comparable to apartments).
- **Affordable housing LV penalty** — magnitude / radius. Mechanically inert until spatial layer.
- **Region.Treasury overflow purchase cap (per resource).** TBD value.
- **"Non-profitable" definition** — single-month, sustained K months, margin threshold? TBD.
- **Default game speed** — likely 1 tick/sec, but TBD.
- **Notification triggers and auto-pause defaults** — which events get notifications, which auto-pause, and which can be disabled by player. Should at minimum include "structure went inactive" event.
- **Savings buffer cap and interest** — currently uncapped, no interest. Worth deciding before alpha-2.
- **Should commercial structures also go inactive on unprofitability?** The simplification says structures generically — implying yes. Commercial revenue depends on agent COL spending, which is more variable than industrial. May want a tolerance window.
- **Should structures auto-recover when profitability returns?** A structure goes inactive in month N. In month N+1 conditions improve. Auto-reactivate or stay-inactive-until-player-intervenes? Auto-reactivate is more responsive; stay-inactive forces player attention.
- **Immigrant starting savings** — sized to one month of expenses at the agent's education tier: $1,800 / $3,000 / $4,000 / $6,000 (uneducated / primary / secondary / college). Listed in `levers.md` for tuning. Coordinated with COL percentages — if those change, starting cash needs recalculation.
- **Immigration gates on matched housing.** A tier's immigration only fires when residential housing of the matched LV tier has a vacancy. Player must build housing for each tier they want to attract.
- **Continuous transactions settle before periodic events** on a given day — daily goods sales hit structure cash balances first, then settlement events fire against the updated balances.
- **Lever changes** — take effect at the next applicable settlement that fires after the change. A property-tax change on day 25 affects the day-30 settlement.
- **Sub-ordering is per actor**, not global — each actor's outflows fire before their inflows on a given day, but cross-actor ordering on the same day is not specified.
- **End-of-month profitability check timing** — resolved: fires **after** all day-30 settlements (honest measure including paid taxes).
- **Bankruptcy timing** — treasury bankruptcy timer counts only end-of-day state, not transient mid-day-1 dips when treasury upkeep has paid out but rent hasn't come in yet.

## Active Reconciliations

### Internal storage capacity behavior

`supply-chain.md` notes that each industrial structure has internal storage capacity that drives its upstream demand. The exact rules (size, fill rate, instant-stop vs. buffer behavior) are TBD beyond the high-level rule that no storage = downstream stalls.

### Water and wind utilization

Water and wind appear as natural resources but only feed utility structures. **TBD** whether water is also a consumed good (agents drink water, industry uses water in processing) or only an input to utility structures.

### Non-zoned structures inside zones

Current docs say industrial / civic / healthcare / education / utility / restoration cannot be placed inside residential or commercial zones. **TBD** confirm.

## Construction Recipes

Alpha-1 recipes are now in `goods-and-recipes.md` against the manufactured-goods tier. They have not been simulation-tested and will need adjustment once the economic layer is calibrated. Once raw and processed prices are set, recipes may need balancing against intra-chain costs.

## Tuning Parameters

Listed with proposed defaults where possible.

### Region

| Parameter | Proposed default | Notes |
|---|---|---|
| Climate/nature band cuts | low: 0–0.4, mid: 0.4–0.75, high: 0.75–0.999 | Skewed |
| Climate/nature additive coefficients on immigration/emigration | TBD | Both axes contribute additively |
| Climate/nature degradation floor | 0.05 | Prevents fully dead regions |
| Base land value (intrinsic) | $100 | Per `legend.png` |
| Resource adjustment range | TBD | Multiplier from proximity to natural resources |
| Externality adjustment range | TBD | Multiplier from proximity to industrial / amenity structures |

### Population and Bootstrap

| Parameter | Proposed default | Notes |
|---|---|---|
| Regional reservoir cap | 60,000 | Across four reservoirs |
| Settler count N | 50 | Calibrated against bootstrap goods stock |
| Bootstrap regional goods stock | 200 bldg supplies, 100 concrete, 40 glass goods, 0 metal goods | Sufficient to build N settler homes |

### Demand and Backlog

| Parameter | Proposed default | Notes |
|---|---|---|
| Backlog cap per pool | TBD per pool | |
| Saturation duration K (ticks before satisfaction reads 0%) | TBD | |
| Per-capita commercial demand factor (per resource) | TBD | |
| Per-capita civic demand factor | TBD | |
| Per-capita healthcare demand factor | TBD | |
| Per-capita utility demand factor | TBD | |

### Emigration

| Parameter | Proposed default | Notes |
|---|---|---|
| Service satisfaction emigration threshold | 60% | Below this, emigration begins |
| Emigration rate function | proportional `(threshold − satisfaction) × scale` | |

### Education and Aging

| Parameter | Proposed default | Notes |
|---|---|---|
| Education durations P / S / C (months) | TBD | Should be substantially shorter than working life |
| Education progression % per tier | TBD | Per-agent `% chance` of continuing, likely gated by available seats |
| Birth rate / trigger | TBD | Per-population rate, possibly housing/service-modified |
| Death age | TBD | |
| Retirement (separate from death) | TBD | Currently undefined whether agents stop working before death |
| Job-education match: behavior on partial fill | TBD | Slot stays unfilled? Reduced output? Demoted? |
| Settler education distribution | TBD | Likely weighted toward uneducated and primary |

### Industrial / Environmental

| Parameter | Proposed default | Notes |
|---|---|---|
| Internal storage capacity per industrial structure type | TBD | Drives the upstream demand cascade |
| Per-structure environmental rates | TBD per structure type | Base rate, axis (climate/nature/both), operation scalar |
| Restoration structure recovery rates | TBD per type and axis | |
| Efficiency formula | `jobs filled × utility availability` | Per `supply-chain.png` |

### Economy

| Parameter | Proposed default | Notes |
|---|---|---|
| Treasury starting amount | $500,000 | Per `economy.png` |
| Income tax rate | TBD | % of agent wages |
| Property tax rate | TBD | % of structure land value |
| Sales tax rate | TBD | % of commercial revenue |
| Utility cost rates | TBD | Per-capita + per-structure (commercial, industrial) |
| Wage table | TBD | (structure type × education level) → wage |
| Manufactured goods prices | Set per `supply-chain.png` | Furniture $30, Bldg supplies $15, etc. |
| Raw material prices | TBD | Inside chain (extractor → processor) |
| Processed good prices | TBD | Inside chain (processor → manufacturer) |
| Rent function | TBD | Function of residential land value |
| Agent shortfall behavior | TBD | When wages < cost of living |
| Bankruptcy → game over trigger | TBD | Immediate at zero, sustained for K ticks, threshold combined with population? |
| Free imports | Currently free | Distorts both construction and commercial supply |

## Behavioral Edge Cases

- **Agent retirement.** Whether agents leave the workforce at an age before death is undefined.
- **Reservoir depletion endgame.** When all four agent reservoirs hit zero, the simulation transitions from a growth game to a retention game. UI/messaging undefined.
- **Region recovery from full collapse.** If climate/nature hit floor and population has fallen below sustainable thresholds, can a region recover? Tuning question.
- **What happens to a structure when its zone is removed?** Demolition rules, residual demand, in-progress construction: undefined.
- **Birth without housing.** If birth fires when housing is fully utilized, the new agent has no residence. Behavior: undefined.
- **Multiple cities per region.** Implicitly out of scope; should be made explicit.
- **Industrial chain stall.** What happens if a processor needs a raw material that no extractor produces? Demand pool grows unbounded until backlog cap.
- **Storage overfill.** If industrial storage is at capacity and manufactured output keeps arriving, what happens? Implicit answer: manufacturer stalls.

## Diagram-Driven Items

`agent-sim.png`, `economy.png`, `legend.png`, `supply-chain.png`, and `education.png` are the working diagrams.

After any further diagram revisions, a docs pass should re-read all five to catch new mismatches.
