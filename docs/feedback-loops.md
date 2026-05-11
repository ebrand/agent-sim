# Feedback Loops

The interlocking systems that prevent runaway growth and runaway collapse. Each is described in isolation here; see `region.md`, `agents.md`, and `structures.md` for the underlying primitives.

## Population Limiters

Population is bounded by six mechanisms acting together:

1. **Regional reservoir cap (60k).** Total agent count across simulation. Emigrants return to the reservoir ("well of souls").
2. **Housing capacity.** Immigration is gated by available housing slots. A residential backlog spawns new residential structures within zones, but until they finish construction, immigration cannot fulfill.
3. **Service satisfaction → emigration** (worst-of mechanism, below).
4. **Environmental degradation** → climate/nature drop → reduced immigration and increased emigration.
5. **Treasury bankruptcy** → upkeep cuts → service satisfaction at 0% → emigration spikes via worst-of. Treasury negative for **6 consecutive months → game over**.
6. **Monthly emigration check** → at end of each month, every agent checks `wage + savings ≥ monthly expenses?`. If they fail, they get one chance to move to affordable housing; otherwise emigrate next tick. See `economy.md`.

A 50k city in a healthy region behaves very differently from a 50k city in a degraded region or a bankrupt one.

## Worst-of Service Emigration

For each agent, **satisfaction** is computed as the **worst** (lowest) percentage among four service pools:

- Civic
- Healthcare
- Education at the agent's child-relevant tier (or agent's own tier if still in school)
- Utility

Per-pool satisfaction = `min(100%, capacity_serving / demand_count)` — see `structures.md` for capacities.

### Emigration formula

```
per_agent_monthly_emigration_chance = max(0, (60 − worst_satisfaction%) / 100) × 0.02
```

- **Threshold: 60%.** Above this on the worst service, no emigration pressure.
- **Scale: 0.02.** At 0% worst satisfaction, agent has 0.6 × 0.02 = **1.2% chance per month** of emigrating from service dissatisfaction.
- At 30% worst satisfaction, chance = 0.3 × 0.02 = 0.6%/month.
- Both threshold and scale are user-adjustable via `levers.md`.

**Worst-of** (rather than sum or product) was chosen so the player can triage: improving the most-broken service yields the largest reduction in emigration pressure.

A demand pool that has been **saturated at its backlog cap for K ticks** reads as 0% satisfaction for this calculation regardless of its raw fill rate. This is how backlog overflow translates into emigration without a separate penalty system. See `demand-and-goods.md`.

## Environmental Feedback

Industrial structures degrade climate and/or nature each tick at a rate scaled by their current operation level (see `structures.md`).

Climate and nature in turn additively modify immigration and emigration rates (see `region.md`).

This forms the loop:

> more industry → environmental degradation → reduced immigration / increased emigration → labor shortage → industry slows → degradation slows.

A healthy city must either keep industrial intensity in balance with regional carrying capacity, or invest in restoration structures.

### Restoration

Restoration is **manual-only**. There is no restoration demand pool. The player monitors regional climate and nature directly and chooses when to place restoration structures (parks, reforestation sites, wetland restoration). Each restores climate and/or nature per tick at TBD rates.

A floor on climate/nature degradation (recommended **0.05**) prevents fully dead regions and preserves the possibility of recovery.

By design, restoration should make recovery from heavy industrial damage **possible but slow**. Whether a region can be reclaimed in playable time is a tuning question.

## Education–Job Mismatch Loop

A job slot with an unfilled education requirement adds to the matching **job-education demand pool**. This pool drives two effects:

1. **Matched-education immigration.** The next immigrant pulled is preferentially drawn from the matching regional reservoir.
2. **Player signal.** When the relevant reservoir is depleted, the demand pool stays elevated, signaling that the player must invest in the relevant education tier in-city.

In parallel, **education capacity demand** is driven independently by the school-age population in each tier relative to available seats (see `agents.md`).

These two pools — capacity demand and job-education demand — jointly shape long-run education investment. The first is a signal to build more schools; the second is a signal to attract or grow more educated workers.

## Treasury Bankruptcy Loop

Treasury inflows (income tax, rent, utilities, sales tax, property tax) must exceed treasury outflows (civic, healthcare, education, utility upkeep + new construction) on average for the city to remain solvent.

When the treasury hits zero, treasury-funded structures cannot pay upkeep. Affected services read 0% satisfaction, which feeds the worst-of emigration mechanism. Population drops → tax base shrinks → treasury further constrained → more service cuts.

**Game-over trigger:** the game ends when the treasury remains in a negative balance for **6 consecutive months**. The bankruptcy period (treasury < 0 for fewer than 6 months) gives the player a window to recover.

## Monthly Emigration (radically simplified)

Every agent runs one check at end of each month:

```
wage + savings >= monthly_expenses?
  yes -> stay
  no  -> try once to move to affordable housing
         if accepted -> re-check next month
         else -> emigrate next tick
```

There is no wageless queue, no homeless state, no COL cut sequence, no shelter, no LV migration, no hire priority, no fired-for-cause randomness, no wage variance. Job loss is one event: an agent's structure goes inactive and stops paying wages. The next monthly check fails for that agent (no wage, savings cover at most a few months) and they emigrate.

### Affordable housing as one-time second chance

Affordable housing is a $500-rent treasury-subsidized residential structure. An agent failing the monthly check tries to move there once. If a seat is available and the agent qualifies (employed at wage strictly under $2,000 OR wageless because their employer went inactive), they move in and re-run the check from cheaper rent next month.

Affordable housing has **no** 6-month timer, **no** hire priority — those mechanics were removed in the radical simplification. It's purely a one-time savings-extension tool that delays emigration by some months. See `structures.md`.

If affordable housing is at capacity, the agent doesn't get a seat and emigrates immediately on the failed check.

## The Steady-State Equation

A city in dynamic equilibrium has all of the following true:

- Housing capacity ≥ active population.
- Service satisfaction ≥ emigration threshold across all relevant services.
- Industrial degradation ≤ restoration recovery (or environmental quality is acceptably stable).
- Job-education demand met by a combination of immigration and in-city schooling.
- Goods production sufficient to feed commercial demand and ongoing construction.
- Treasury inflows ≥ treasury outflows over a rolling window.

Breaking any one of these starts a feedback that resolves either by player action or by population shrinkage. Resolving each requires a different player intervention — that asymmetry is what creates gameplay.
