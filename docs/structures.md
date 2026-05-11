# Structures

All buildings in the city. Categorized by placement model and function.

## LV Tier and Placement

**LV is per cell, not per structure type.** Any structure (residential, commercial, industrial, civic, healthcare, education, utility, restoration) can be built in any LV tier (LV1–LV4) as long as zoning rules allow. The cell's LV determines:

- The structure's **value** (and therefore property tax owed monthly).
- For residential structures, **utilities cost** (% of cell LV).

Note: residential **rent** is set per residential structure type, not per cell LV — see "Residential structure types and rent" below. The per-cell LV affects property tax and utilities, but not rent.

## Placement Models

The simulation supports two placement modes for structures, used in combination:

- **Auto-spawn** — the system places structures automatically when matching demand exists. Used only for residential and commercial structures, both of which require their respective zone.
- **Manual placement** — the player can place any structure type directly, including residential and commercial.

Both modes coexist within zones. In a residential zone, residential structures can both auto-spawn and be manually placed. **Manual placement takes priority** over auto-spawn for any given slot.

### Zone-mismatch rule

A manually placed structure that requires a specific zone (residential or commercial) **will not build** if placed outside its matching zone. Construction is blocked, no goods are consumed, the player is notified.

Structures with no zone requirement (industrial, civic, healthcare, education, restoration, utility) are placed in the un-zoned remainder of the region — they cannot be placed inside residential or commercial zones, since doing so would interfere with auto-spawn within those zones.

### Auto-spawn rules per category

| Category | Examples | Auto-spawn? | Zone required? | Manual placement allowed? |
|---|---|---|---|---|
| Residential | House, apartment, townhouse, condo, affordable housing | Yes | Yes (residential zone) | Yes (must be inside residential zone) |
| Commercial | Shop, marketplace | Yes | Yes (commercial zone) | Yes (must be inside commercial zone) |
| Industrial — extractor | Forest extractor, mine, quarry, sand pit, farm, coal mine | No | No | Yes (outside zones) |
| Industrial — processor | Sawmill, smelter, mill, aggregate plant, silicate plant, fuel refinery | No | No | Yes (outside zones) |
| Industrial — manufacturer | Household factory, bldg supplies factory, metal goods factory, food packing, clothing factory, concrete plant, glass works | No | No | Yes (outside zones) |
| Industrial — storage | Storage (manufactured goods), fuel storage (fuel only) | No | No | Yes (outside zones) |
| Civic | Police station, fire station, town hall | No | No | Yes (outside zones) |
| Healthcare | Clinic, hospital | No | No | Yes (outside zones) |
| Education | Primary school, secondary school, college | No | No | Yes (outside zones) |
| Utility | Generator, well | No | No | Yes (outside zones) |
| Restoration | Park, reforestation site, wetland restoration | No | No | Yes (outside zones) |

**Restoration is manual-only.** The restoration demand pool was removed; the player monitors climate/nature directly and decides when to place restoration structures.

## Construction

Every structure takes time to build and consumes goods.

- **Build duration: 3 months (90 ticks)** for all structure types — see `goods-and-recipes.md` and `time-and-pacing.md`. A construction progress indicator is shown.
- **Construction recipe** is per structure type — `{resource: quantity}` for required goods. See `goods-and-recipes.md`.
- **Construction is blocking.** A structure does not become operational until its recipe is fulfilled and its build duration has elapsed.
- **Goods are pulled per the standard routing priority** (industrial storage → regional → import). See `demand-and-goods.md`.
- **Money:** treasury-funded structures (civic, healthcare, education, utility) draw construction goods cost from the treasury. Player-funded structures (residential, commercial, industrial) — see `economy.md`.

Imports for construction are currently free at base. Import upcharge of 25% over local price applies — see `economy.md`.

## Residential Structure Types and Rent

In the radically simplified model, residential rent is **set per structure type** (not per cell LV). Each residential type has a fixed monthly rent. The cell LV still affects property tax and utilities for that structure, but rent is fixed by type:

| Residential type | Monthly rent | Capacity | Targets wage tier |
|---|---|---|---|
| House | $800 | 4 agents | Uneducated ($2,000) |
| Apartment | $1,400 | 40 agents | Primary ($3,500) |
| Townhouse | $1,800 | 12 agents | Secondary ($4,500) |
| Condo | $2,800 | 25 agents | College ($7,000) |
| Affordable housing | $500 (treasury-subsidized) | ~30 agents | Sub-$2,000 / wageless |

Agents at immigration prefer their wage-tier-matched residential type. If their preferred type has no vacancy, they take any cheaper type that has one. They never refuse to immigrate due to housing-type mismatch.

Agents do not migrate between residential types in-city under the simplified model. The single emigration check (see `agents.md`) is run from their current residential each month.

### Affordable housing

A treasury-subsidized residential structure type with $500 rent (the treasury covers any gap between $500 and the structure's operating cost).

- **Eligibility:** the agent must have a job AND their **current wage strictly under $2,000/month**, OR be wageless because their employer went inactive.
- **Capacity:** 40 agents.
- **One-attempt-to-move:** an agent who fails the monthly emigration check tries to move into affordable housing **once**. If a seat is available, they move there and re-run the check next month from the cheaper rent. If no seat is available, they emigrate.
- No 6-month timer, no hire priority, no special re-hire pathway — those mechanics were removed in the radical simplification. Affordable housing is now just "cheaper rent that delays emigration by some months."
- **Negative land value impact** on surrounding cells (radius and magnitude TBD, requires spatial layer; mechanically inert until then).

### Industrial sub-types

Four industrial structure sub-categories implement the supply chain (see `supply-chain.md`):

- **Extractor** — pulls a raw material from a natural resource.
- **Processor** — converts raw material into a processed good.
- **Manufacturer** — converts processed good(s) into a manufactured good.
- **Storage** — stores manufactured goods for sale to commercial / regional. **Fuel storage** is a parallel structure for fuel only, feeding utility structures.

If no industrial storage exists for a manufactured good, the manufacturer stalls. Back-pressure cascades upstream as internal buffers fill. **Default internal storage capacity for extracting / processing / manufacturing structures: 1000 units, user-adjustable.** See `supply-chain.md`.

**Storage revenue model.** Storage earns a **20% pass-through fee** on goods moving through it. Manufacturers sell to storage at 80% of the manufactured price; storage sells to commercial / regional at the full manufactured price. The 20% spread is storage's revenue, used to cover its utilities, property tax, and wages.

**Storage workforce** is much smaller than producer structures (extractors / processors / manufacturers). Default: **10 workers per storage**, at the standard tier mix scaled down (15% college / 20% secondary / 40% primary / 25% uneducated → roughly 1 / 2 / 4 / 3 workers respectively, rounding favors the lower tiers for warehouse work). Fuel storage uses the same 10-worker default.

**Profitability check.** A storage moving 10k units/month of $40 household goods earns $80k revenue, pays ~$36k wages + $1k utilities + $400 property tax = ~$37k costs → ~$43k/mo profit. At low volumes (e.g., 5k units/mo), revenue drops to $40k and storage runs at near break-even. Storage's profitability is therefore tightly coupled to throughput — a poorly-utilized storage will go inactive within a couple months.

## Utility Structures

Utility structures (generator, well) provide infrastructure services (power, water) to the city. They are **treasury-funded for both construction and upkeep** (see `economy.md`) and **consume fuel** drawn from fuel storage. If fuel is unavailable, utility availability drops, which lowers efficiency across all structures via the supply-chain efficiency formula.

## Service Structure Capacities

Each civic / healthcare / education / utility structure satisfies some number of agents (or seats) per month. Demand-fulfillment math: `satisfaction = min(100%, capacity_serving / demand_count)`.

| Structure | Capacity | Serves |
|---|---|---|
| Primary school | 1,000 | seats (children of primary-school age) |
| Secondary school | 1,500 | seats (children of secondary-school age) |
| College | 2,500 | seats (agents of college age) |
| Clinic | 2,500 | agents (healthcare demand) |
| Hospital | 12,500 | agents (healthcare demand) |
| Police station | 5,000 | agents (civic demand) |
| Fire station | 5,000 | agents (civic demand) |
| Town hall | 25,000 | agents (civic demand) |
| Generator | 10,000 | agents + commercial/industrial structures combined (utility demand) |
| Well | 10,000 | agents + commercial/industrial structures combined (utility demand) |

Scaled so a 50k city needs only a handful of each major service: ~4 hospitals, ~2 town halls, ~5 generators+wells, etc. All values are user-adjustable via `levers.md`.

## Commercial Sub-types

For alpha-1, commercial structures share a single **commercial demand pool**. Visual sub-types (shopping, business, technology, entertainment, sports) are cosmetic variety in most respects, with one mechanically meaningful distinction:

### Service-only sub-types

**Entertainment** sub-types (restaurants, clubs, parks, tourism) and other service-style sub-types are flagged as **service-only**. Service-only commercial structures:

- Do **not** consume goods from industrial storage / regional reservoir / imports.
- Generate commercial revenue purely from agent spending (the agent's entertainment / service portion of COL).
- Pay **licensing fees** (ASCAP, franchise costs, marketing, etc.) to the **Region.Treasury** — a recurring monthly cost equivalent in magnitude to what a goods-backed structure would spend on input goods. This balances the absence of real input costs and routes the money out of the city economy into the regional treasury (see `economy.md`).
- Otherwise behave like normal commercial structures (jobs, taxes, utilities, etc.).

Goods-backed sub-types (shopping, retail) consume manufactured goods from the standard goods routing chain. The agent's food / clothing / household COL flows through goods-backed structures.

The flag is per sub-type, not per individual structure: every entertainment-flagged commercial structure is service-only; every shopping-flagged commercial structure is goods-backed.

## Jobs and Education Requirements

Each structure type has:

- A number of **job slots**.
- A **required education level** per slot (uneducated / primary / secondary / college).

If a job slot's education requirement cannot be matched by an available agent, the slot remains unfilled and adds pressure to the matching **job-education demand pool**, which drives matched-education immigration.

### Industrial job tiers

All industrial structure types — extractors, processors, manufacturers, and storage — include **uneducated** as a valid job-slot education requirement.

**Default mix per industrial structure: 100 workers** (15 college / 20 secondary / 40 primary / 25 uneducated). Storage uses a smaller 10-worker mix.

These are alpha-1 defaults; user-adjustable per structure type.

### Industrial workforce: a long-arc gameplay implication

The 100-worker requirement per industrial structure has a deliberate gameplay consequence:

- **Bootstrap provides only 50 settlers** (60% uneducated, 40% primary). Even one industrial structure can't be fully staffed at bootstrap — only ~45 of those settlers qualify for any industrial slots (no college/secondary settlers exist).
- **Industrial buildout requires city growth first.** The intended arc is:
  1. Bootstrap settlers create initial population.
  2. Player builds **commercial structures** (5–15 workers each) — these can be staffed from the bootstrap pool.
  3. Commercial demand creates job demand, which drives **immigration** (capped at `max(50, 1% × city_population)/month`).
  4. Population grows over months/years to hundreds, then thousands of working-age agents.
  5. Once population supports it (~250+ for one industrial chain), player builds extractors → processors → manufacturers → storage.
  6. Industrial scaling continues as population grows, becoming the late-game economy.
- **Real-time pacing:** at 1 tick = 1 second, 1 game-month = 30 seconds; reaching ~500 population takes ~10 game-months ≈ 5 real-time minutes. Industrial chains become viable after that.

This long-arc design is intentional. Industrial is the "late game." For unit tests of industrial mechanics, staged staffing (manually setting `FilledSlots`) bypasses the population-growth ramp.

### Production tied to jobs filled

An industrial structure's output scales **linearly with the percentage of its jobs filled**. A structure with 100 jobs and a normal output of 10,000 units/month produces 5,000 units when half its workforce is gone.

### Structure inactive on unprofitability

A structure's profitability is checked each end-of-month tick (day 30 — see `time-and-pacing.md`). The trigger is **2 consecutive unprofitable months**:

- **Month N:** profit < 0. Flag "warning"; notification fires ("Structure X is unprofitable; will go inactive if next month is also unprofitable").
- **Month N+1:** profit < 0 again. Structure goes **inactive** at start of month N+2; notification fires.
- **Auto-reactivate:** an inactive structure re-evaluates at end of each month. If conditions would now make it profitable (demand returned, supply chain restored, etc.), it auto-reactivates at the start of the next month; notification fires.

Inactive structures pay no wages, produce nothing, consume no inputs. They still occupy their slot. All employees become wageless and run the standard agent monthly emigration check (see `agents.md`) — most fail and emigrate within a month or two.

The 2-month buffer prevents transient one-month dips from killing structures. Auto-reactivate makes recovery seamless when conditions improve. The player can also intervene during the warning month or during inactivity (subsidize from treasury, demolish, fix upstream supply chain).

There are **no wage cuts, no wageless queues, no re-instatement, no fired-for-cause randomness** in the simplified model. A structure either operates fully or sits dormant — see `economy.md` for the rationale.

## Demolition

Players can demolish structures via the UI. Demolition behavior depends on category:

- **Residential demolition:** tenants run an **immediate** emigration check (do not wait for next month). They get their one attempt to move into affordable housing; otherwise they emigrate. Confirmation dialog warns the player of the displacement count.
- **Industrial / commercial demolition:** all employees become wageless immediately, then run their normal monthly emigration check at the next end-of-month. Confirmation dialog warns of the layoff count.
- **Treasury-funded demolition** (civic / healthcare / education / utility): no agents directly displaced. Service capacity for the relevant demand pool drops; agents may experience reduced satisfaction. Confirmation dialog warns of the capacity loss.
- **Industrial storage demolition:** manufacturers feeding that storage stall (no place for output) until alternative storage exists. Confirmation dialog warns.

Demolition is final — no goods refund, no money refund.

## Environmental Externalities (industrial only)

Each industrial structure type has a per-day base degradation rate at 100% operation, scaled linearly with current operation (jobs filled × utility availability).

| Structure | Climate | Nature |
|---|---|---|
| Forest extractor | 0 | 0.0001 |
| Mine | 0.00005 | 0.0001 |
| Coal mine | 0.0001 | 0.0001 |
| Quarry | 0 | 0.0001 |
| Sand pit | 0 | 0.00005 |
| Farm | 0.00002 | 0.00002 |
| Sawmill | 0 | 0.00005 |
| Smelter | 0.0002 | 0 |
| Mill | 0.00005 | 0 |
| Aggregate plant | 0.00005 | 0.00005 |
| Silicate plant | 0.0001 | 0.00002 |
| Fuel refinery | 0.0002 | 0 |
| All manufacturers | 0.00005 | 0.00002 |
| Storage / fuel storage | 0 | 0 |

**Floor: 0.05** for both climate and nature — values cannot drop below this. User-adjustable via `levers.md`.

Calibration check: ~10 active heavy-industry structures degrading climate at combined ~0.002/day → ~250 days = ~9 months to drop climate by 0.5. Roughly 1–2 game years of heavy industrialization noticeably degrades the environment.

## Restoration

Restoration structures (parks, reforestation sites, wetland restoration) recover climate and/or nature per tick. **They are manually placed by the player; they are not auto-spawned, and there is no restoration demand pool.**

The player monitors regional climate and nature values directly and decides when to place restoration structures.

**Recovery rates per restoration structure (per day):**

| Structure | Climate | Nature |
|---|---|---|
| Park | 0.00002 | 0.00005 |
| Reforestation site | 0 | 0.0001 |
| Wetland restoration | 0.00005 | 0.00005 |

Calibration check: 1 reforestation site (0.0001/day nature) exactly balances 1 sawmill (0.00005/day) plus 1 forest extractor (0.0001/day) — a 2:1 sustainable ratio. The player needs roughly equal restoration to industry to maintain environmental quality.

By design, restoration should make recovery from heavy industrial damage **possible but slow**. User-adjustable via `levers.md`.
