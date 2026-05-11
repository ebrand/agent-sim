# Supply Chain

The industrial supply chain produces consumable, sellable goods through a four-tier progression. Diagram authority: `supply-chain.png`.

## The Four Tiers

| Tier | Produced by | Examples |
|---|---|---|
| Natural resource | Region (random at creation) | Timber, ore, arable land, stone, sand, coal, water, wind |
| Raw material | Industrial extractor | Wood, iron-ore, crops, rock, sand, coal |
| Processed good | Industrial processor | Lumber, steel, grain, textiles, aggregate, silicate, fuel |
| Manufactured good | Industrial manufacturer | Furniture, bldg supplies, sporting goods, toys, metal goods, food, clothing, concrete, glass goods |

## Per-Resource Progression

| Natural resource | Raw material | Processed good | Manufactured goods (sales price) |
|---|---|---|---|
| Timber | Wood | Lumber | Household, Bldg supplies |
| Ore | Iron-ore | Steel | Metal goods (price TBD) |
| Arable land | Crops | Grain, Textiles | Food, Clothing (prices TBD) |
| Stone | Rock | Aggregate | Concrete (price TBD) |
| Sand | Sand | Silicate | Glass goods (price TBD) |
| Coal | Coal | Fuel | (none — fuel exits through fuel storage to utility structures) |
| Water | (utility-direct) | — | — |
| Wind | (utility-direct) | — | — |

### Coal exception

Coal bypasses the manufacturing tier entirely. Its processed output is **fuel**, which is held in a dedicated **fuel storage** structure and consumed by utility structures (generators, etc.).

### Water and wind

Water and wind are natural resources used directly by utility structures (wells, wind generators) without an extraction → manufacturing chain. **TBD** whether water is also a consumable good — agents drink water, industry uses water in processing — or strictly an input to utility structures.

## Industrial Structure Types

Four manually-placed structure types, each with **internal storage capacity**:

- **Industrial extractor** — produces a raw material from a natural resource. Examples: forest extractor, mine, farm, quarry, sand pit, coal mine.
- **Industrial processor** — produces a processed good from raw material(s). Examples: sawmill, smelter, mill, textile mill, aggregate plant, silicate plant, fuel refinery.
- **Industrial manufacturer** — produces a manufactured good from processed good(s). Examples: furniture factory, building supplies factory, metal goods factory, food packing plant, clothing factory, concrete plant, glass works.
- **Industrial storage** — final-stage store. Sells manufactured goods to commercial structures in-city and to the regional goods reservoir as overflow.
- **Fuel storage** — separate storage for fuel; feeds utility structures.

## Demand Cascade

Each industrial structure generates demand for its inputs as a function of its internal storage capacity and current operating efficiency. Per `supply-chain.png`:

```
processing demand = processing storage capacity ÷ efficiency
efficiency        = jobs filled × utility availability
```

A structure with all jobs staffed and full utility supply runs at 100% efficiency. Missing labor or missing utilities reduces efficiency proportionally — and **output scales linearly with efficiency**. A structure with 100 jobs producing 10,000 units/month at full staffing produces 5,000 units when half its workforce is laid off. Every cut worker reduces output by their share (1% per worker in a 100-job structure).

Production accumulates **per tick (per day)** rather than once per month. A structure with 10,000 units/month nominal output produces ~333 units/day. See `time-and-pacing.md`.

This makes wage cuts a real revenue tradeoff: cutting a worker saves their wage but costs the revenue from their share of production. Cutting lower-tier workers saves less wage; cutting higher-tier workers saves more wage. Either way, the same proportional production loss applies.

The cascade: extraction demand → processing demand → manufacturing demand → storage demand → commercial / regional demand. Each tier's demand is driven by available room in the next tier downstream.

## Storage and Back-Pressure

User decision: **extraction, processing, and manufacturing all proceed by default. They stop only when there is nowhere to store their output.**

- If no industrial storage structure exists for a manufactured good, the manufacturer cannot deposit output and stalls. Internal buffers fill, then processing stalls, then extraction stalls — back-pressure cascades upstream.
- If no fuel storage structure exists for coal, the fuel processor stalls. Utility structures starve, utility availability drops region-wide, efficiency drops chain-wide.

### Internal storage capacity

Each extracting / processing / manufacturing structure has an internal storage buffer. **Default capacity: 1000 units, user-adjustable.** When the internal buffer fills, the structure stops producing until downstream pulls.

## Sales Prices

Manufactured-goods prices are detailed in the per-tier section below. Previous diagram values ($30 furniture, $15 bldg supplies, etc.) are superseded by the new per-good price table on the right side of `supply-chain.png`. The "furniture" good has been renamed to **household**.

### Per-good pricing (from `resource-pricing.png`)

Each good in each tier has an explicit per-unit price (no fixed cross-tier ratio). All prices are user-adjustable via `levers.md`.

**Raw materials (extractor → processor):**

| Raw material | Price per unit |
|---|---|
| Wood | $2 |
| Iron-ore | $4 |
| Crops | $1 |
| Rock | $3 |
| Sand | $2 |
| Coal | $2 |

**Processed goods (processor → manufacturer, or processor → fuel storage):**

| Processed good | Price per unit |
|---|---|
| Lumber | $4 |
| Steel | $8 |
| Grain | $3 |
| Textiles | $2 |
| Aggregate | $6 |
| Silicate | $4 |
| Fuel | $8 |

**Manufactured goods (manufacturer → storage → commercial / regional):**

The `units` column is the count of processed-good input consumed per unit of manufactured output. Sale price is per unit of manufactured output. All goods now sit at a clean **2× markup** over input cost.

| Manufactured good | Input | Input units | Sale price | Input cost | Markup |
|---|---|---|---|---|---|
| Household | Lumber | 5 | $40 | $20 | 2× |
| Bldg supplies | Lumber | 9 | $72 | $36 | 2× |
| Metal goods | Steel | 3 | $48 | $24 | 2× |
| Food | Grain | 4 | $24 | $12 | 2× |
| Clothing | Textiles | 2 | $8 | $4 | 2× |
| Concrete | Aggregate | 5 | $60 | $30 | 2× |
| Glass goods | Silicate | 10 | $80 | $40 | 2× |

The previous `sporting goods` and `toys` manufactured goods have been removed — the alpha-1 manufactured set is now seven goods, all with consistent 2× markup. The 12× metal-goods anomaly from the prior pass is resolved.

**Water and wind:** sell price $0; consumed directly by utility structures. They are not bought or sold through the goods chain.

### Branched chains — resolved

Timber and arable-land chains produce multiple manufactured goods from a single upstream raw/processed good. The branched-chain pricing question is now resolved by **explicit per-good pricing** — wood has one price ($2) regardless of whether it ultimately becomes a household good or bldg supplies. Same for lumber, crops, grain, etc. Each downstream manufacturer's profit margin is just `sale price − (input units × input price)`.

### Branched chains — resolved

Timber and arable-land chains produce multiple manufactured goods from a single upstream raw/processed good. The branched-chain pricing question is now resolved by **explicit per-good pricing** — wood has one price ($2) regardless of whether it ultimately becomes a toy or a piece of furniture. Same for lumber, crops, grain, etc. The downstream manufacturer's profit margin varies based on the gap between its input cost and its output price.

### Naming change

The "furniture" manufactured good has been renamed to **household**.
