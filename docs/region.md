# Region

The top-level simulation entity. A region contains a single city, environmental quality, and the reservoirs that supply both agents and goods to the city.

## Climate and Nature

Two independent decimal values, each in `[0.000, 0.999]`:

- **Climate** — quality of regional climate. Higher is better.
- **Nature** — amount of nature (forests, lakes, biodiversity). Higher is more.

These values are **never aggregated into a single regional score**. Both feed directly and additively into immigration and emigration rates. They are also displayed using the characterization matrix below.

### Direction of effect

Climate and nature additively modify both immigration and emigration rates:

```
immigration_modifier = 1 + (climate − 0.5) × 0.3 + (nature − 0.5) × 0.3
emigration_modifier  = 1 − (climate − 0.5) × 0.3 − (nature − 0.5) × 0.3
```

- At max climate (0.999) and max nature (0.999): immigration ≈ 1.30×, emigration ≈ 0.70×.
- At zero on both: immigration ≈ 0.70×, emigration ≈ 1.30×.
- Coefficients (0.3 each) user-adjustable via `levers.md`.

### Characterization Matrix (display only)

The two values are bucketed into three bands per axis and combined into a 3×3 label grid for UI/flavor. The simulation reads raw values; characterization labels are for the player-facing display only and never feed back into mechanics.

Proposed band cuts (skewed to make extremes feel earned): `low: 0–0.4`, `mid: 0.4–0.75`, `high: 0.75–0.999`. **TBD pending tuning.**

|              | Low nature       | Mid nature       | High nature      |
|--------------|------------------|------------------|------------------|
| **High climate** | Cleared temperate | Temperate plains | Eden (pristine)  |
| **Mid climate**  | Open scrubland    | Mixed terrain    | Lush wilderness  |
| **Low climate**  | Wasteland         | Tundra           | Boreal forest    |

## Reservoirs

A region holds three classes of reservoir.

### Regional Agent Reservoirs

Four reservoirs by working-age education level, with a combined cap of **60,000 agents** across all four.

- Uneducated working-age
- Primary-educated working-age
- Secondary-educated working-age
- College-educated working-age

**No babies or children in the reservoir.** Only working-age adults immigrate. Babies are born in-city.

**No refill.** The 60k is the lifetime population budget for the region. Once drained, no further immigration is possible.

**Emigrants return ("well of souls").** Agents who emigrate return to the reservoir matching their **current** education level — which may differ from the level at which they immigrated, since in-city schooling can advance an agent's education tier. The reservoir is a queue, not a one-way drain. A region recovering from bad management can re-attract its former residents.

The 60k cap is the **total agent count across the entire simulation** — agents are always either in the city or in the reservoir waiting to emigrate. Quitting, emigrating, dying, etc., return the agent to the reservoir (or remove them from the simulation in the case of death; new births in-city refill the agent count). The simulation never instantiates more than 60k distinct agent records.

**Soft cap → notify.** When the active city population approaches a level where simulation performance degrades, the user is notified. The simulation continues to function past that point until performance becomes unacceptable.

### Regional Goods Reservoir (per resource)

Limited capacity per resource type. Holds overflow from in-city industrial storage and is drained by regional-level demand and the local commercial demand fallback chain. Note: there is no separate "local goods reservoir" — the in-city goods buffer is played by player-placed industrial storage structures (see `structures.md`).

The reservoir starts with a non-zero **bootstrap stock** sufficient to construct initial settler homes:

| Good     | Stock |
|----------|-------|
| Lumber   | 200   |
| Concrete | 100   |
| Glass    | 40    |
| Metal    | 0     |

Calibrated against N = 50 settlers and a house recipe of 10 lumber + 5 concrete + 2 glass per house. Metal is intentionally excluded from bootstrap — see `goods-and-recipes.md`.

### Region.Treasury

A separate, regional-level treasury distinct from the city treasury. **Functionally infinite** — no balance is tracked. See `economy.md` for details.

- Inflows: licensing fees from service-only commercial; payments from local commercial buying from the regional goods reservoir. (Sink — not tracked.)
- Outflows: purchases of overflow from local industrial storage at full manufactured-goods price; other regional functions TBD. (Source — not tracked.)
- Money does not flow between Region.Treasury and the city treasury.

The infinite-balance simplification means local industry can always sell its overflow to the regional layer at full price, and local commercial can always buy from the regional reservoir, without the regional layer becoming a constraint or finite resource the player tracks.

### Regional Import Goods Reservoir (per resource)

Covers goods the city cannot produce locally. Per-resource availability. Supplies construction and consumption when local and regional sources are insufficient.

**Pricing is not yet implemented.** Imports are currently free, which trivializes local production for construction. This is a known hole that resolves with the economic layer. See `open-questions.md`.

## Goods Routing Priority

Per resource type:

- **Buying** (commercial / construction / consumption):
  1. Local industrial storage (per `structures.md`)
  2. Regional goods reservoir
  3. Regional import goods reservoir
- **Selling** (production overflow from local structures):
  1. Local industrial storage (where local commercial / construction draws first)
  2. Regional goods reservoir (excess capacity)

There is no separate "local goods reservoir" — that role is played by player-placed industrial storage. See `demand-and-goods.md`.

No transportation friction, delay, or cost in the alpha. Friction is deferred to the transportation layer.

## Natural Resources

Per `supply-chain.png`, the regional natural-resource set for alpha-1 is:

- Timber
- Ore
- Arable land
- Stone
- Sand
- Coal
- Water
- Wind

The presence and quantity of each at region creation is randomized. Extraction structures (forest extractors, mines, farms, quarries, sand pits, coal mines) are tied to specific natural resources. Water and wind feed utility structures directly (wells, generators) without an extraction → manufacturing chain.

See `supply-chain.md` for the per-resource progression through raw → processed → manufactured tiers.

## Land Value

Per the legend, every cell has a land value computed as:

```
land value = intrinsic value × resource adjustment × externality adjustment
```

- **Intrinsic value:** base $100 per cell.
- **Resource adjustment:** multiplier from proximity to natural resources (e.g., 1.25× near a forest).
- **Externality adjustment:** multiplier from proximity to industrial structures and other neighbors. May increase or decrease land value.

Example from the legend: `100 × 1.25 × 1.5 = $187.50`.

Land value drives:
- Residential rent paid by agents to treasury (per LV tier — see below).
- Property tax paid by commercial / industrial structures to treasury (% of land value).
- Utility cost paid by agents (residential), commercial, and industrial structures (% of land value).

### LV tiers (simplified)

For alpha-1 (until the spatial layer lands), land value is bucketed into **4 tiers (LV1–LV4)**. **LV is per cell, not per structure type** — any structure (residential, commercial, industrial, civic, etc.) can be built in any LV tier.

In the radically simplified model, the cell's LV affects:

- The structure's **value** (and therefore property tax owed monthly by commercial / industrial structures).
- **Utilities cost** for any structure (% of cell LV).

It does **not** affect residential rent. Residential rent is set per residential structure type (house = $800, apartment = $1,400, etc. — see `structures.md`), independent of the cell LV.

The full land value system (continuous LV per cell, resource adjustment, externality adjustment) is part of the **deferred spatial layer**.
