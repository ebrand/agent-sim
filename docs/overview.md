# Agent Simulation — Overview

This is an alpha design document. Numbers, names, and behaviors are subject to change. Items marked **TBD** are explicitly unresolved.

## Concept

A regional simulation in which a player establishes and grows a city by zoning land, placing infrastructure, and managing a goods supply chain. The city grows by attracting agents from a finite regional reservoir; agents create demand; demand drives structure spawning and player decisions; industrial activity creates goods, jobs, and environmental externalities.

The simulation is bounded in scale (60k regional agent cap, no reservoir refill) and shaped by feedback loops between population, services, environment, and the goods chain. The economy, transportation, and spatial layers are deferred — see `open-questions.md`.

## The Core Loop

1. **Bootstrap.** Player creates a residential zone. N settlers immigrate from the regional reservoir. The regional goods reservoir's bootstrap stock is consumed to build initial homes.
2. **Commercial layer.** Player creates a commercial zone. Commercial structures auto-spawn to meet agent commercial demand. Agents fill commercial jobs. Without industry, growth plateaus here.
3. **Industrial supply chain.** Player manually places industrial extractors, processors, and storage. These produce goods, create jobs (with education requirements), and degrade climate and/or nature.
4. **Civic, healthcare, and education infrastructure.** Player manually places these. Unmet service demand drives emigration; education progresses agents through primary → secondary → college tiers.
5. **Steady-state.** Population grows toward the regional reservoir cap, gated by housing, service satisfaction, environmental quality, and education-matched labor.

## Top-Level Objects

- **Region** — top-level container. Holds climate, nature, agent reservoirs, goods reservoirs, import reservoirs, and the city.
- **City** — the player-managed entity inside the region. Holds zones, structures, and active agents.
- **Agent** — an individual with age, education level, employment, and residence.
- **Structure** — buildings categorized as residential, commercial, industrial (extractor / processor / storage), civic, healthcare, education, utility, or restoration.
- **Demand Pool** — accumulating backlog of unmet need for a specific good, service, or job-education level.

## Scope of the Alpha

**In scope:**
- Region, climate, and nature with skewed-band characterization.
- Per-education-level agent reservoirs with no refill.
- Per-resource regional goods reservoirs, local goods reservoirs, regional demand pools, and import reservoirs.
- Goods routing priority: local → regional → import.
- Demand-driven auto-spawn (residential, commercial, restoration) and manual placement (industrial, civic, healthcare, education).
- Construction time and goods consumption (recipes TBD).
- Aging, education progression (primary → secondary → college), birth, death.
- Worst-of service emigration.
- Per-structure environmental degradation, scaled by operation level.

**Partially in scope:**
- **Economic layer** — alpha-1 sketches money flows (wages, taxes, treasury, goods pricing) but leaves rates and goods prices TBD. See `economy.md`. Free imports are a known hole.

**Deferred (see `open-questions.md`):**
- Transportation networks (shipping nodes, edges, range, friction).
- Spatial layer (tile/footprint system, land value, adjacency).
- Multi-region trade and world structure.

**Explicitly out of scope:**
- **Multi-city per region.** Alpha-1 is one city per region. The "regional" entities (regional reservoir, regional treasury, regional goods reservoir) abstract the off-region world rather than representing other player-managed cities.

## Starting a New Game

At game start, the player configures:

- **Region name** (player-chosen).
- **Initial climate** (slider 0.000–0.999; or randomized).
- **Initial nature** (slider 0.000–0.999; or randomized).
- **Starting treasury** (default $500,000; player can override).
- **Difficulty preset** (easy / medium / hard) — affects starting climate/nature, starting treasury, and base immigration rate.
- **Random seed** for natural-resource distribution and other stochastic events. Defaults to current Unix time; player can override for reproducibility.

After config, the simulation starts dormant: no settlers immigrate until the player creates the first residential zone. See `agents.md` for the bootstrap sequence.

## Diagram Reference

`agent-sim.png` in this folder is the working diagram. It shows the region object, demand pools, structure categories, and the demand-system, education-system, and land-value-system subsystems in the legend.

## Document Map

- `region.md` — region object, climate/nature, characterization, all reservoirs.
- `agents.md` — lifecycle, aging, education, immigration/emigration, settlers.
- `structures.md` — types, placement model (auto-spawn + manual), construction, jobs, externalities.
- `demand-and-goods.md` — demand pool model, backlog mechanics, goods routing.
- `goods-and-recipes.md` — goods set, processing chain, per-structure construction recipes, bootstrap stock.
- `supply-chain.md` — four-tier industrial chain (natural resource → raw → processed → manufactured), fuel chain, demand cascade formula, sales prices.
- `economy.md` — money flows, treasury, taxes, wages, goods pricing.
- `time-and-pacing.md` — tick definition (1 tick = 1 day), monthly cadence, game speed, save/load, tick processing order, RNG seeding.
- `ui-and-player.md` — player UI: speed controls, notifications, levers UI, zone drawing, dashboard, save/load, demolition confirmations.
- `levers.md` — list of user-adjustable parameters (tax rates, prices, etc.).
- `diagrams-out-of-date.md` — punch list for diagram updates that haven't kept up with the simplification pass. Text docs are the source of truth; diagrams are illustrative.
- `feedback-loops.md` — population limiters, environmental feedback, emigration mechanics.
- `open-questions.md` — deferred subsystems, tuning parameters, edge cases.
