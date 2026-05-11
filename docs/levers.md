# User-Adjustable Levers

The full set of tunable parameters in alpha-1. Each lever has a default value defined in the relevant doc; this file is the index. The player can adjust any of these in-game via the Levers UI (see `ui-and-player.md`).

## Taxes

- **Income tax rate** (% of agent wages) — default 5%
- **Property tax rate** (% of structure value per month) — default 0.5%
  - Commercial structures
  - Industrial structures
  - (Residential and treasury-funded structures are exempt)
- **Sales tax rate** (% of commercial revenue per month) — default 3%
- **Import upcharge** (% over local price) — default 25%
- **Region.Treasury overflow purchase cap** (units per resource per month) — default 50,000

## Wages (per structure-category × education tier)

Each employer category has its own wage table. All categories share the same defaults; each is independently adjustable.

| Tier | Default wage |
|---|---|
| Uneducated | $2,000/mo |
| Primary | $3,500/mo |
| Secondary | $4,500/mo |
| College | $7,000/mo |

Adjustable per-category: commercial, industrial, civic, healthcare, education, utility.

## Rent (per residential structure type)

| Type | Default rent |
|---|---|
| House | $800/mo |
| Apartment | $1,400/mo |
| Townhouse | $1,800/mo |
| Condo | $2,800/mo |
| Affordable housing | $500/mo (treasury-subsidized) |

## Utility Cost (per structure type, fixed monthly value)

**Residential:**
| Type | Default |
|---|---|
| House | $200 |
| Apartment | $350 |
| Townhouse | $450 |
| Condo | $700 |
| Affordable housing | $200 |

**Commercial:**
| Type | Default |
|---|---|
| Shop | $2,000 |
| Marketplace | $4,000 |

**Industrial — extractors:** $1,000–$1,500 per type
**Industrial — processors:** $3,000–$5,000 per type (heavy industry $5,000)
**Industrial — manufacturers:** $4,000–$6,000 per type (heavy industry $6,000)
**Industrial — storage:** $1,000 per structure

Full table in `economy.md`.

## Structure Values (drives property tax)

Per-structure-type placeholder values until the spatial layer lands. Full table in `economy.md`. Examples:
- Shop: $200,000
- Marketplace: $400,000
- Forest extractor: $80,000
- Smelter: $400,000
- Metal goods factory: $500,000

## Treasury Upkeep (treasury-funded structures, monthly)

| Structure | Default |
|---|---|
| Police station | $30,000 |
| Fire station | $30,000 |
| Town hall | $90,000 |
| Clinic | $60,000 |
| Hospital | $250,000 |
| Primary school | $60,000 |
| Secondary school | $90,000 |
| College | $180,000 |
| Generator | $80,000 |
| Well | $50,000 |
| Affordable housing subsidy | $20,000 |

## Construction Money Cost (treasury-funded structures, one-time)

| Structure | Default |
|---|---|
| Civic (police, fire, town hall) | $10,000 |
| Healthcare / education / utility / affordable housing | $20,000–$50,000 per type |

(Residential / commercial / industrial structures cost goods only — no money fee.)

## Goods Prices

**Raw materials** (sales price per unit, manufacturer pays this):
| Good | Default |
|---|---|
| Wood | $2 |
| Iron-ore | $4 |
| Crops | $1 |
| Rock | $3 |
| Sand | $2 |
| Coal | $2 |

**Processed goods** (sales price per unit, manufacturer pays this; storage in fuel chain):
| Good | Default |
|---|---|
| Lumber | $4 |
| Steel | $8 |
| Grain | $3 |
| Textiles | $2 |
| Aggregate | $6 |
| Silicate | $4 |
| Fuel | $8 |

**Manufactured goods** (sales price per unit, storage pays manufacturer at 80% of price):
| Good | Default |
|---|---|
| Household | $40 |
| Bldg supplies | $72 |
| Metal goods | $48 |
| Food | $24 |
| Clothing | $8 |
| Concrete | $60 |
| Glass goods | $80 |

**Recipe input units per manufactured good** (units of processed input per unit of manufactured output):
- Per `supply-chain.md` — household 5 lumber, bldg supplies 9 lumber, metal goods 3 steel, food 4 grain, clothing 2 textiles, concrete 5 aggregate, glass goods 10 silicate.

**Storage pass-through fee** — default **20%** (storage buys at 80% of manufactured price, sells at full).

## Service Structure Capacities

| Structure | Capacity |
|---|---|
| Primary school | 1,000 seats |
| Secondary school | 1,500 seats |
| College | 2,500 seats |
| Clinic | 2,500 agents |
| Hospital | 12,500 agents |
| Police station | 5,000 agents |
| Fire station | 5,000 agents |
| Town hall | 25,000 agents |
| Generator | 10,000 (agents + structures) |
| Well | 10,000 (agents + structures) |
| Affordable housing | 40 agents per structure |

## Industrial Structure Parameters

- **Internal storage capacity** (per extracting / processing / manufacturing structure) — default 1,000 units
- **Storage workforce** — default 10 workers
- **Job mix per industrial structure** (per 100 workers) — 15% college / 20% secondary / 40% primary / 25% uneducated
- **Standard worker count** for extractors / processors / manufacturers — TBD; treated as 100 for default ratio

## Environmental

- **Climate / nature degradation rates** per industrial structure type — full table in `structures.md`
- **Restoration recovery rates** per restoration structure type — full table in `structures.md`
- **Climate / nature floor** — default 0.05 (cannot drop below this)
- **Climate / nature → immigration coefficient** — default 0.3 each (additive)
- **Climate / nature → emigration coefficient** — default 0.3 each (additive)

## Emigration

- **Worst-of service satisfaction threshold** — default 60% (below triggers emigration pressure)
- **Worst-of service rate scale** — default 0.02 (max 1.2% emigration/mo at 0% satisfaction)

## Demographics

- **Lifespan** — default 60 game-years (21,600 days)
- **Age band thresholds** (in days) — baby 0–1,800 / primary-school 1,800–3,960 / secondary-school 3,960–5,760 / college 5,760–7,560 / working 7,560–21,600 / death at 21,600
- **Education durations** — primary 6 game-years (2,160 days), secondary 5 game-years (1,800 days), college 5 game-years (1,800 days)
- **Birth rate** — default 0.5% of working-age population per month
- **Settler count N** — default 50 (one-time burst when first residential zone created)
- **Settler distribution** — default 60% uneducated, 40% primary
- **Bootstrap goods stock** — default 200 bldg supplies, 100 concrete, 40 glass goods, 0 metal goods

## Immigration

- **Mass-immigration cap formula** — default `max(50, 1% × city_population)` per month
- **Immigrant starting savings** (by tier):
  - Uneducated: $1,800
  - Primary: $3,000
  - Secondary: $4,000
  - College: $6,000

## Treasury & Game

- **Starting city treasury** — default $500,000 (player can override at new-game config)
- **Bankruptcy game-over duration** — default 6 consecutive negative months
- **Default game speed** — default 1 tick / second (1×)
- **Autosave interval** — default every 10 game-years (3,600 ticks)

## Structure Operation

- **"Non-profitable" trigger** — default 2 consecutive unprofitable months → structure inactive
- **Demand pool backlog cap** (per pool) — TBD per pool
- **Backlog saturation duration** (K ticks before satisfaction reads 0%) — TBD

## Notification Thresholds

- **Treasury low warning threshold** — default $50,000
- **Climate low warning threshold** — default 0.2
- **Nature low warning threshold** — default 0.2
- **Population drop warning threshold** — default 5% per month
- **Backlog saturation warning duration** — TBD (linked to demand pool saturation)

## Notes

- Levers marked with concrete defaults are implemented values.
- Levers marked TBD need defaults before code can run with them — see `open-questions.md`.
- Levers take effect at the next applicable settlement after the change (per `time-and-pacing.md`).
- Some levers have downstream coordination requirements (e.g., immigrant starting savings is derived from per-tier expenses; if rent or COL changes, recalculate). Coordinated levers noted in their source docs.
