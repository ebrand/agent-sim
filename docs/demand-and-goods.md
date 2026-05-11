# Demand and Goods

## Demand Pool Model

All demand in the simulation is modeled as an **accumulating backlog** — a stock, not a per-tick flow. Unmet demand persists and grows tick over tick until either:

- It is met by structure construction, structure operation, or agent assignment, or
- The pool's backlog cap is reached.

### Demand formula (per `legend.png`)

For consumer-driven demand pools (commercial, residential, civic, healthcare, education, utility):

```
demand pool amount = source × demand factor
demand supply      = sum of structure-provided supply for that pool (negative)
demand balance     = source × demand factor + demand supply
```

- **Source** is the count of demand drivers (e.g., 100 agents → 100 commercial source).
- **Demand factor** is a per-resource (or per-pool) multiplier that scales source to demand units.
- **Demand supply** is the negative contribution from operating structures fulfilling that demand.
- **Demand balance** is the net outstanding demand — the value the player sees and the value that drives auto-spawn or signals manual placement.

### Per-capita demand factors (resolved)

For alpha-1, the per-capita demand factors are simple:

- **Goods-backed COL** (food, clothing, household): the agent spends X% of wage on each good (food 15%, clothing 5%, household 10% — see `economy.md`). The dollar amount divided by the good's per-unit price = units of good consumed per agent per month. Example: an uneducated agent spending $300/mo on food at $24/unit = 12.5 units/mo. The commercial structure earns $300; the food units flow from local industrial storage / regional reservoir / imports.

- **Entertainment** (service-only commercial): the agent spends 5% of wage on entertainment. Dollars flow to a service-only commercial structure (no goods consumed; the structure pays licensing fees instead — see `structures.md`).

- **Civic / healthcare / utility:** each agent generates **1 unit of demand per month** for each. A clinic with capacity 500 satisfies 500 agents' healthcare demand per month (see `structures.md` for service-structure capacities).

- **Education (per tier):** each child of matching age generates **1 unit of education demand per month** for their tier. A primary school with 200 seats satisfies 200 primary-aged children.

The "1 unit per agent per month" model means service-structure capacities translate directly to "agents satisfied per month."

For demand pools driven by structures (e.g., industrial demand for raw materials), the source is the structure's input requirement rather than a consumer count. The same balance accounting applies.

### Backlog cap and saturation effect

Each pool has a **backlog cap**. When the cap is reached, the user is notified.

If a pool remains saturated at its cap for **K** consecutive ticks (K: **TBD**), that pool's satisfaction reads **0%**. This 0% reading feeds directly into the existing **worst-of service emigration** mechanism (see `feedback-loops.md`).

There is no separate penalty system for saturated demand — saturated demand becomes a satisfaction crisis through the existing plumbing.

## Demand Pool Inventory

| Pool | Source of demand | Fulfilled by |
|---|---|---|
| Residential (per type) | Active agents needing housing matched to their wage tier | Residential structures of matching type (auto-spawn in zones). Immigration of a tier prefers matched-type housing but accepts any cheaper type if matched is full. |
| Commercial — goods-backed (per resource) | Per-capita agent COL spending on food / clothing / household | Commercial structures (auto-spawn in zones), drawing goods through routing priority |
| Commercial — service-only | Per-capita agent entertainment spending | Service-only commercial structures (entertainment sub-types) — no goods consumed |
| Civic | Per-capita | Manually placed civic structures |
| Healthcare | Per-capita | Manually placed healthcare structures |
| Education capacity (×3 tiers) | Population in matching age band minus available seats | Manually placed education structures |
| Job-education demand (×4 by education level) | Unfilled job slots, broken out by required education tier | Immigration of agents with matching education; in-city education progression |
| Utility demand (power, water) | Per-capita + per-structure (commercial, industrial) | Treasury-funded utility structures, drawing fuel from fuel storage |
| Industrial extraction demand (per resource) | Available capacity in extractor's internal storage ÷ efficiency | Extractor operating output |
| Industrial processing demand (per resource) | Available capacity in processor's internal storage ÷ efficiency | Processor operating output |
| Industrial manufacturing demand (per resource) | Available capacity in manufacturer's internal storage ÷ efficiency | Manufacturer operating output |
| Industrial storage demand (per resource) | Available capacity in industrial storage | Output of manufacturers |
| Fuel storage demand | Available fuel storage capacity | Fuel refinery output |

Per-capita coefficients for civic, healthcare, and commercial demand: **TBD.**

The two education-related pools (capacity and job-education) are independent and serve different signals. Capacity demand says "we need more schools." Job-education demand says "we need more workers educated to this level."

## Goods System

### Resource typing

Every good carries a resource type. The following all track inventory, demand, or both **per resource**:

- Regional goods reservoir
- Regional goods demand pool
- Industrial storage (in-city, per `structures.md`)
- Commercial goods demand pool
- Industrial structures (extractor / processor inputs and outputs)
- Regional import goods reservoir

**Note:** there is no separate "local goods reservoir" object. The role of the in-city goods buffer is played by player-placed **industrial storage structures.** Goods that exit the industrial chain land in storage, where they are available to commercial structures and to the regional goods reservoir.

### Industrial chain output

Goods flow through the industrial chain in stages, with each stage selling to the next:

```
extractor → processor → storage → consumer (commercial / regional)
```

Storage is the chain's export point. Without at least one storage structure for a given resource, that resource has nowhere to land — whether processors back-pressure or simply stop producing is **TBD** (see `open-questions.md`).

### Routing priority

Per resource:

- **Local commercial demand** is fulfilled in this order:
  1. Local industrial storage (in-city, per resource)
  2. Regional goods reservoir
  3. Regional import goods reservoir
- **Regional goods demand** is fulfilled by:
  1. Local industrial storage (overflow from local production)
- **Construction** uses the same priority as commercial demand (storage → regional → import).

When local industrial storage is empty, local commercial demand cascades to the regional goods reservoir. Regional goods demand cannot cascade to imports — it represents demand that the city's industry could export to satisfy, not demand that imports should fill.

**Import upcharge:** imports cost 25% more per unit than the equivalent local manufactured-goods price. The upcharge rate is user-adjustable. See `economy.md`.

No friction, delay, or cost on any of these flows in the alpha. Friction (transportation networks, range constraints, shipping nodes/edges) is deferred — see `open-questions.md`.

### Money flow on goods transactions

Each industrial transaction along the chain transfers money: extractor → processor pays for raw materials, processor → manufacturer pays for processed goods, storage → commercial pays for finished manufactured goods. See `economy.md` and `supply-chain.md` for the full price grid.

When local industrial storage overflows into the regional goods reservoir, the **regional treasury** purchases that overflow at the manufactured-goods sale price. When local commercial later draws from the regional goods reservoir, the commercial structure pays the regional treasury at the same price. regional treasury is **functionally infinite** — it never runs out of money to buy overflow, and the cash inflow from commercial purchases is not bookkept. See `economy.md` and `region.md`.

### Construction draws on goods

Structure construction consumes goods per a per-type recipe (recipes: **TBD**) and follows the same routing priority. This closes the loop between the industrial chain and city growth: industry produces materials → materials build structures → structures employ agents → agents demand commercial goods → commercial goods consumption pulls back through industry.

Construction is **blocking**: the structure does not begin operating until its full recipe has been fulfilled and its build duration has elapsed. See `structures.md`.

### Known hole: free imports

Construction goods can be drawn from the regional import reservoir at no cost. With no economic layer, this means the player has limited mechanical reason to produce construction goods locally. The economic layer is the resolution. See `open-questions.md`.
