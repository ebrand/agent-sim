# Agents

Agents are individuals tracked by age, education level, employment, and residence. They are the primary movers of demand in the simulation.

## Time and Aging

**One simulation tick equals one game-day** (see `time-and-pacing.md`). Agents age one day per tick. **All age bands are stored as day-counts** (1 month = 30 days; 1 year = 360 days). Age thresholds use raw day values to avoid unit-conversion drift at runtime.

**Lifespan: 60 game-years (21,600 days). Agents work until death — no separate retirement band.**

Wages, rent, and taxes settle on the cadence in `time-and-pacing.md`.

### Age bands

| Band | Day range | Years | Duration |
|---|---|---|---|
| Baby | 0 – 1,800 | 0–5 | 5y |
| Primary-school age | 1,800 – 3,960 | 5–11 | 6y |
| Secondary-school age | 3,960 – 5,760 | 11–16 | 5y |
| College age | 5,760 – 7,560 | 16–21 | 5y |
| Working age | 7,560 – 21,600 | 21–60 | 39y |
| Death | 21,600 | 60 | — |

An agent enters working age when they exit their last completed education tier, or when they age past school-age without enrolling in further tiers.

## Education

Four states, ordered by tier:

1. **Uneducated** — has not completed any education tier. Default for newborns and uneducated immigrants.
2. **Primary-educated** — completed primary school.
3. **Secondary-educated** — completed primary and secondary.
4. **College-educated** — completed all three.

### Schooling progression

A school-aged agent attending the matching tier remains for:

- Primary school: **6 game-years (2,160 days)**
- Secondary school: **5 game-years (1,800 days)**
- College: **5 game-years (1,800 days)**

Total educational path (if completed): 16 game-years, leaving a 39-year working life out of a 60-year lifespan.

**Progression is seats-driven.** When an agent completes a tier:
- If seats exist in the next tier and the agent is age-eligible, they advance.
- Otherwise, they enter the workforce at their current education level.

This couples player infrastructure investment directly to educational outcomes — a city without colleges produces no college-educated workers from in-city schooling.

### Education capacity drives education demand

Education demand is computed per tier as the gap between eligible agents and available seats. Example: if 100 agents are of primary-school age and only 75 primary-school seats exist across all primary education structures in the city, the **primary education demand pool** reads `25`.

This pool is independent of the **job-education demand pool** (see below).

## Birth

Babies are born in-city only. There are no babies in the regional reservoir.

**Birth rate: 0.5% of working-age population per month**, evaluated each end-of-month tick. Rate is user-adjustable via `levers.md`.

**Birth gate: housing availability.** Births are halted when there is a residential housing waitlist (i.e., agents that need but don't have housing). The reasoning: no point creating babies who will reach school age in a city that can't house them now. Births resume automatically when the waitlist clears.

## Immigration

Working-age agents only. Each immigration event:

1. Reads the city's per-education-level **job-education demand pool** values.
2. Selects the matching reservoir (uneducated / primary / secondary / college) weighted by demand.
3. Pulls one agent from that reservoir.
4. Assigns the agent to a residence (preferred residential structure type matched to wage tier — house / apartment / townhouse / condo — falling back to any cheaper type with a vacancy).
5. Assigns the agent to a job (FIFO queue; see Job Assignment below).

Climate and nature additively modify the immigration rate (see `region.md`).

### Mass-immigration throttling

Immigration is capped per month, scaled with city population to allow growing cities to attract more immigrants:

```
monthly_immigration_cap = max(50, 0.01 × current_city_population)
```

- 50 settlers (alpha-1 bootstrap): cap = 50/month — city can ~double in early months.
- 1,000 agents: cap = 50/month (floor binds).
- 5,000 agents: cap = 50/month (floor still binds at 1%).
- 10,000 agents: cap = 100/month.
- 50,000 agents: cap = 500/month.

The cap is spread across the 30 days of the month (≈ floor / 30 per day). If vacancies exceed the cap, they backlog to subsequent months. Both the floor (50) and the percentage (1%) are user-adjustable via `levers.md`.

### Empty regional reservoir

If all 60,000 agents are in the city (regional reservoir at 0 across all four education tiers), immigration is impossible. The city continues to function — births, deaths, emigrations all still happen — but population growth halts. Notification fires: "Regional reservoir empty — immigration paused."

## Job Assignment

When an agent becomes unemployed (immigration, in-city graduation, employer went inactive), they enter their education-tier's **looking-for-work FIFO queue**. When a structure has an open slot at a matching education tier, the head-of-queue agent is hired.

**Multi-tier eligibility.** A higher-tier agent can take lower-tier work — a college-educated agent qualifies for college, secondary, primary, or uneducated slots. The agent always **tries the highest tier first** and drops to the next-lower tier if no openings exist there. So a college agent who can't find college work eventually takes a secondary job (and earns secondary wage) rather than sitting unemployed.

A lower-tier agent **cannot** take a higher-tier slot — an uneducated agent only qualifies for uneducated slots.

When multiple structures simultaneously open slots that the same agent qualifies for, FIFO across all opening events resolves it (oldest open slot wins).

## Emigration

Triggered by the **worst-of service satisfaction** mechanism (see `feedback-loops.md`).

When an agent emigrates, they return to the regional reservoir matching their **current** education level — which may differ from the level at which they immigrated, since in-city schooling can advance them. This means in-city education improves the regional pool composition over time.

## Settler Bootstrap

The simulation is dormant until the player creates the first **residential zone**. At that moment:

1. **N = 50 settlers** immigrate from the regional reservoir as a one-time burst.
2. Each settler arrives with a **founders' bonus** of **$5,000 starting savings** (flat, independent of education tier). This is substantially more than the regular immigrant savings (see Immigrant starting savings below) — sized to give settlers ~5 months of pre-commercial cushion to survive while the player builds the first commercial structure (which takes ~90 days from order to operational). Settlers are founders bringing extra resources from off-region; the bonus narratively distinct from regular immigration.
3. The regional goods reservoir's bootstrap stock (200 lumber, 100 concrete, 40 glass — see `goods-and-recipes.md`) is consumed to construct settlers' homes via residential auto-spawn. With houses at 4 capacity each, ~13 houses cover all settlers.
4. Settlers live without jobs until commercial zones are created.
5. Once commercial zones exist and commercial structures auto-spawn within them, agents take commercial jobs and the immigration loop activates.
6. Real growth requires the player to begin manually placing the industrial supply chain.

**Settler education distribution: 60% uneducated, 40% primary** (no secondary or college in the founding wave). Weighted toward uneducated because settlers are founders without prior school infrastructure.

### Immigrant starting savings

Every agent who immigrates from the regional reservoir into the city arrives with **enough starting savings to cover one month of expenses at their education tier**:

| Education tier | Monthly expenses (≈ 85% of wage) | Starting savings |
|---|---|---|
| Uneducated | $1,700 | **$1,800** |
| Primary | $2,975 | **$3,000** |
| Secondary | $3,825 | **$4,000** |
| College | $5,950 | **$6,000** |

This covers the day-1 rent payment (which fires before wage installment 1 same-day per the outflows-before-inflows rule) plus the rest of the first month's costs in the absence of an immediate job. By month 2 the agent is expected to either be employed or to have moved into affordable housing. Starting cash is conceptual — agents bring it with them from off-region; it does not deplete any in-sim resource. The values are user-adjustable via `levers.md`.

**Coordination with COL percentages:** the starting-savings values above are **derived** from the per-tier expense breakdown in `economy.md` (rent + utilities + COL = 85% of stated wage; income tax 5% and disposable 10% account for the remainder). If COL percentages, rent values, or tax rates are tuned, the immigrant starting cash should be recalculated to keep "1 month of expenses" honest. `levers.md` flags these as related tunables.

## What Agents Do

Each tick, an agent:

- Ages by one month.
- Consumes against per-capita demand pools (commercial, civic, healthcare, education-by-tier, utility).
- If of working age and assigned a job, contributes labor to that structure and earns wages from it.
- If of school age and enrolled, progresses toward completion of their current education tier.
- If unsatisfied (worst-of service satisfaction below threshold for their relevant pools), contributes to emigration pressure.

## Agent Money

Agents earn wages from their employer. Wage rates are defined per **(structure-category × education level)** — single fixed values, no per-agent variance. Default wage table:

| Education tier | Wage |
|---|---|
| Uneducated | $2,000 |
| Primary | $3,500 |
| Secondary | $4,500 |
| College | $7,000 |

Each employer category's wages are independently user-adjustable via `levers.md`.

Each month, an agent's accounting (per `time-and-pacing.md`):

1. **Income tax** withheld (5% flat).
2. **Rent** paid to treasury (fixed per residential structure type — see `structures.md`).
3. **Utilities** paid to treasury (% of cell land value).
4. **COL** paid to commercial structures (food, clothing, household, entertainment).

Disposable income = wage − all of the above. Saved as a personal buffer.

### Savings buffer

Disposable income is **saved** as a personal buffer. Savings are uncapped in alpha-1 (cap and interest TBD).

### The single emigration rule

At end of each month, every agent runs **one check**:

- **If wage + savings ≥ monthly expenses:** stay. Disposable income is added to savings.
- **If wage + savings < monthly expenses:** the agent fails the check. They get **one attempt** to move to affordable housing:
  - If they qualify (current wage strictly under $2,000, OR wageless because their employer went inactive) AND a seat is available: move to affordable housing, re-run the check next month from there.
  - Otherwise (or if they're already in affordable housing): **emigrate next tick.** Agent record returns to the regional reservoir at their current education tier, retaining any remaining savings.

That's the entire shortfall / emigration flow. No 3-month wageless queue, no re-instatement, no homeless state, no shelter, no LV migration, no COL cut sequence, no hire priority, no fired-for-cause randomness, no wage variance.

### Job loss

Agents lose their job in one way: their **employer goes inactive** when its monthly revenue can't cover its costs (see `structures.md`). At that moment, every employee of the inactive structure becomes wageless. They run the standard monthly emigration check at end of month — most fail (no wage, savings cover at most a few months) and emigrate. If the structure re-activates (player intervention, supply chain repair) before they emigrate, they resume their wage.

There are no slow-motion wage cuts, no re-instatement queue, no graceful per-worker reductions. A structure goes from fully operating to fully inactive in one tick at end-of-month. The player's job is to keep their structures profitable.

### Immigration / housing match

At immigration, an agent prefers a residential structure type matched to their wage tier (house for uneducated, apartment for primary, etc. — see `structures.md`). If their preferred type has no vacancy, they take any cheaper residential structure type they can afford. They never refuse to immigrate due to housing-type mismatch.

In-city, agents do not migrate between housing types. The single emigration check operates at their current housing — if expenses there are unaffordable, the agent tries affordable housing and otherwise emigrates.
