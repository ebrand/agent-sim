# Economy

Alpha-1 design. The economic layer adds money flows on top of the existing goods, demand, and labor systems. The diagram in `economy.png` is the authoritative summary; this document is its prose form.

## Treasuries

There are **two treasury entities**:

### City Treasury

- Starts at **$500,000**.
- Funds all civic / healthcare / education / utility construction and upkeep.
- Funds the **affordable housing rent subsidy** (the gap between tenant rent and the structure's actual operating cost).
- All city treasury inflows/outflows denominated in dollars.
- **Bankruptcy:** if the city treasury hits zero, treasury-funded upkeep stops. Affected services fall to zero effective service, feeding worst-of emigration.
- **Game over:** if the city treasury remains in a **negative balance for 6 consecutive months**, the game ends.

### regional treasury (NEW — functionally infinite)

A separate, regional-level treasury — the financial counterpart to the regional goods reservoir.

**Functionally infinite for accounting purposes** — there is no balance to track. Inflows go into a black hole; outflows pull from infinite funds.

- **Inflows (sink, not tracked):**
  - Licensing fees from service-only commercial structures (ASCAP, franchise, marketing).
  - Payments from local commercial structures buying from the regional goods reservoir.
- **Outflows (source, not tracked):**
  - Purchases of overflow from local industrial storage (paid at full manufactured-goods price).
  - Other future regional uses, when designed.
- The regional treasury is **separate from the city treasury** — money does not flow between them.

**Cap on regional purchases: 50,000 units per resource per month** (default). Once the cap is hit for a given resource, further overflow has nowhere to go and the local industrial chain back-pressures (manufacturer fills internal buffer → processor fills → extractor fills → chain stalls).

Generous default that rarely binds for typical city sizes; user-adjustable per resource via `levers.md`.

The infinite-balance simplification (combined with the per-resource purchase cap) keeps the regional layer from becoming a bookkeeping burden while still preventing infinite-overflow scenarios.

## Money Flow

### Inflows to treasury

| Source | Type | Calculation |
|---|---|---|
| Agents | Income tax | **5% flat** (default), user-adjustable |
| Agents | Rent | Fixed per residential structure type (house $800 / apartment $1,400 / townhouse $1,800 / condo $2,800 / affordable housing $500) |
| Agents | Utilities | Fixed per residential structure type (house $200 / apartment $350 / townhouse $450 / condo $700 / affordable housing $200) |
| Commercial structures | Sales tax | **3% of commercial revenue per month** |
| Commercial structures | Property tax | **0.5% of structure value per month** |
| Commercial structures | Utilities | Per-structure type (placeholder values below) |
| Industrial structures | Property tax | **0.5% of structure value per month** |
| Industrial structures | Utilities | Per-structure type (placeholder values below) |

**Structure value** = `cell intrinsic LV × resource_adjustment × externality_adjustment` (per `region.md`). Until the spatial layer lands, each structure type has a placeholder fixed value below. When the spatial layer lands, the placeholder becomes a base value multiplied by per-cell LV adjustments.

### Placeholder structure values (drives property tax at 0.5%/month)

**Commercial:**

| Structure | Value | Monthly property tax (0.5%) |
|---|---|---|
| Shop | $200,000 | $1,000 |
| Marketplace | $400,000 | $2,000 |

**Industrial — extractors:**

| Structure | Value | Monthly property tax |
|---|---|---|
| Forest extractor | $80,000 | $400 |
| Mine | $150,000 | $750 |
| Coal mine | $150,000 | $750 |
| Quarry | $100,000 | $500 |
| Sand pit | $80,000 | $400 |
| Farm | $100,000 | $500 |

**Industrial — processors:**

| Structure | Value | Monthly property tax |
|---|---|---|
| Sawmill | $200,000 | $1,000 |
| Smelter | $400,000 | $2,000 (heavy industry) |
| Mill | $200,000 | $1,000 |
| Aggregate plant | $200,000 | $1,000 |
| Silicate plant | $200,000 | $1,000 |
| Fuel refinery | $400,000 | $2,000 (heavy industry) |

**Industrial — manufacturers:**

| Structure | Value | Monthly property tax |
|---|---|---|
| Household factory | $300,000 | $1,500 |
| Bldg supplies factory | $300,000 | $1,500 |
| Metal goods factory | $500,000 | $2,500 (heavy industry) |
| Food packing plant | $300,000 | $1,500 |
| Clothing factory | $300,000 | $1,500 |
| Concrete plant | $300,000 | $1,500 |
| Glass works | $300,000 | $1,500 |

**Industrial — storage:**

| Structure | Value | Monthly property tax |
|---|---|---|
| Storage | $80,000 | $400 |
| Fuel storage | $80,000 | $400 |

**Residential structures** do not pay property tax (folded into rent — see Inflows table). When the spatial layer lands, residential structures will still have a value for display and possibly other purposes, but no separate property-tax line.

**Treasury-funded structures** (civic, healthcare, education, utility, affordable housing) do not pay property tax — they're funded by the treasury, so taxing them would just be moving money in a circle.

All values user-adjustable via `levers.md`.

### Commercial / industrial utility costs (per structure per month)

Placeholder values calibrated to roughly 5% of expected revenue per structure. All values user-adjustable via `levers.md`.

**Commercial:**

| Structure | Monthly utility cost |
|---|---|
| Shop | $2,000 |
| Marketplace | $4,000 |

**Industrial — extractors:**

| Structure | Monthly utility cost |
|---|---|
| Forest extractor | $1,000 |
| Mine | $1,500 |
| Coal mine | $1,500 |
| Quarry | $1,000 |
| Sand pit | $1,000 |
| Farm | $1,000 |

**Industrial — processors:**

| Structure | Monthly utility cost |
|---|---|
| Sawmill | $3,000 |
| Smelter | $5,000 (heavy industry) |
| Mill | $3,000 |
| Aggregate plant | $3,000 |
| Silicate plant | $3,000 |
| Fuel refinery | $5,000 (heavy industry) |

**Industrial — manufacturers:**

| Structure | Monthly utility cost |
|---|---|
| Household factory | $4,000 |
| Bldg supplies factory | $4,000 |
| Metal goods factory | $6,000 (heavy industry) |
| Food packing plant | $4,000 |
| Clothing factory | $4,000 |
| Concrete plant | $4,000 |
| Glass works | $4,000 |

**Industrial — storage:**

| Structure | Monthly utility cost |
|---|---|
| Storage | $500 |
| Fuel storage | $500 |

(Reduced from $1,000 each — at $1,000 storage required ~3 different manufacturers feeding it to break even, which was an unrealistic early-game expectation. At $500, 2 manufacturers can sustain a storage.)

**Residential property tax does not exist as a separate line.** Neither residential structures nor agents pay a discrete property tax. What was previously a structural residential property tax has been folded into the rent agents pay to treasury — rent is the single residential housing-cost line.

### Outflows from treasury

| Destination | Type |
|---|---|
| Civic structures | Construction + upkeep |
| Healthcare structures | Construction + upkeep |
| Education structures | Construction + upkeep |
| Utility structures | Construction + upkeep |
| Affordable housing | Construction + monthly subsidy (covers gap between $500 rent and operating cost) |

**Construction money cost** (in addition to consuming construction goods at manufactured-good prices):

| Category | Construction fee |
|---|---|
| Residential / commercial / industrial | $0 (player-owned; goods cost only) |
| Civic structure (police, fire, town hall) | $10,000 flat |
| Healthcare / education / utility / affordable housing | $20,000–$50,000 per type |

**Monthly upkeep** (treasury-paid, settled on day 1):

| Structure | Monthly upkeep |
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
| Affordable housing | $20,000 (rent subsidy gap) |

A 50k city with ~4 hospitals, ~2 town halls, ~4 schools, ~5 generators+wells, ~5 police+fire, ~2 affordable housing has roughly **$2.5M/month** treasury upkeep.

**Treasury revenue note.** Even with the lowered tax rates above (5% income / 0.5% property / 3% sales), the treasury runs a large structural surplus because rent flows entirely to the treasury. For a 50k city with average rent ~$1,500 × 50k = ~$75M/month from rent alone, plus residential utilities (~$20M), plus income tax (~$10M), plus commercial / industrial taxes (~$3M), total revenue is ~$108M/month against ~$2.5M upkeep. Treasury bankruptcy is therefore mainly a **population-collapse scenario** — if population drops dramatically (emigration spiral), rent and tax revenue evaporates and upkeep can't be sustained. Normal operation will accumulate cash. This is intentional alpha-1 behavior; deeper rebalance is a tuning task once the sim runs.

### Bypassed by money flow

- **Industrial structures** earn from selling goods up the chain (see below) and do not receive treasury funds.

(Residential structures pay property tax to treasury per the table above. The funding source for that tax is implicit — conceptually it comes out of the rent the resident agents pay. Residential structures do not have a separate revenue line.)

## Agent Money

Agents earn **wages** from any structure that employs them. Each employer category sources wages differently:

| Employer | Wage source |
|---|---|
| Commercial | Revenue from goods sold to agents |
| Industrial | Revenue from goods sold up the chain (extractor → processor → storage → commercial) |
| Civic, healthcare, education, utility | Treasury upkeep |

**Wage rates are defined per (structure-category × education level).** All structure categories that employ agents (commercial, industrial, civic, healthcare, education, utility) **start at the same default wage table** by education level, but each category is independently user-configurable.

Default wages (per agent per month). **Single fixed value per tier — no per-agent variance.**

| Education level | Wage |
|---|---|
| Uneducated | $2,000 |
| Primary | $3,500 |
| Secondary | $4,500 |
| College | $7,000 |

### Settlement cadence

Per `time-and-pacing.md`, settlement events are spread across 5 days of the month:

- **Day 1:** treasury upkeep paid out, agent rent, wage installment 1 (with income tax). Sub-order: outflows before inflows (so treasury upkeep paid before rent collected; agent rent paid before wage received).
- **Day 8:** licensing fees (service-only commercial → regional treasury).
- **Day 15:** utilities (agent / commercial / industrial → city treasury), wage installment 2 (with income tax). Same outflows-before-inflows sub-order.
- **Day 22:** sales tax (commercial → city treasury).
- **Day 30:** property tax (commercial / industrial → city treasury), end-of-month profitability check (after all settlements complete).

Property tax is **monthly** (not yearly). Goods sales transactions accrue daily as buyers pull; structures' cash balances grow continuously over the month.

### Monthly accounting

```
disposable income = gross wage − income tax − monthly expenses
```

Income tax is withheld with wages (5% flat across both installments). The remainder is allocated to monthly expenses; what's left is disposable income retained by the agent.

Default expense breakdown, expressed as % of gross wage in the **tier-matched case** (agent lives in housing matching their education level). Percentages sum to exactly 100%.

| Category | Class | % of wage (tier-matched) | Paid to |
|---|---|---|---|
| Income tax | Withholding | 5% | Treasury |
| Rent | Housing | 40% | Treasury |
| Utilities | Housing | 10% | Treasury |
| Food | COL | 15% | Commercial |
| Clothing | COL | 5% | Commercial |
| Household | COL | 10% | Commercial |
| Entertainment | COL | 5% | Commercial |
| **Disposable** | | 10% | (agent retains) |

Concrete monthly values, by education tier:

| Item | No edu ($2,000) | Primary ($3,500) | Secondary ($4,500) | College ($7,000) |
|---|---|---|---|---|
| Income tax | $100 | $175 | $225 | $350 |
| Rent (tier-matched) | $800 | $1,400 | $1,800 | $2,800 |
| Utilities | $200 | $350 | $450 | $700 |
| Food | $300 | $525 | $675 | $1,050 |
| Clothing | $100 | $175 | $225 | $350 |
| Household | $200 | $350 | $450 | $700 |
| Entertainment | $100 | $175 | $225 | $350 |
| Disposable | $200 | $350 | $450 | $700 |

### Rent and utilities are land-value-driven, not wage-percentage

The 40% / 10% figures above are descriptive — they hold when an agent lives in housing matched to their education tier. The actual underlying values are:

- **Rent** is fixed per **residential structure type** (not per cell LV in the simplified model). Each residential structure type has a baked-in monthly rent — see `structures.md`:

  | Residential type | Monthly rent | Capacity |
  |---|---|---|
  | House | $800 | 4 |
  | Apartment | $1,400 | 40 |
  | Townhouse | $1,800 | 12 |
  | Condo | $2,800 | 25 |
  | Affordable housing | $500 (treasury-subsidized) | ~30 |

  Rent is paid monthly by the tenant to the treasury. Rent implicitly includes what was previously the residential property tax — there is no separate property-tax line for residential.

  Agents do not migrate between residential types in-city. The single emigration check operates from the agent's current housing — if expenses there are unaffordable, the agent tries affordable housing once and otherwise emigrates.

- **Utility cost** is a percentage of the residential structure's land value (rate user-adjustable, see `levers.md`). Same model is used to charge commercial and industrial structures their utility costs.

- **Property tax** for non-residential structures (commercial, industrial) is a percentage of the structure's value, which scales with the LV of the cell. A factory built in an LV4 cell pays substantially more property tax than the same factory in an LV1 cell.

### The single emigration rule (radical simplification)

At end of each month, every agent runs **one check**:

```
if (wage + savings) >= monthly_expenses:
    stay (and add disposable income to savings)
else:
    one attempt to move to affordable housing if eligible AND seat available
    if move succeeds: re-run the check next month from cheaper rent
    otherwise: emigrate next tick (savings retained, agent record returns to regional reservoir)
```

That's the entire shortfall flow. The radical simplification removes:
- 3-month wageless queue
- Wage cuts (top-down or bottom-up)
- Re-instatement
- Fired-for-cause randomness
- ±5% wage variance
- COL cut sequence
- Homeless state and homeless shelter
- LV migration (agents moving between LV tiers)
- Hire priority and FIFO for affordable housing residents
- 6-month affordable housing stay timer

What remains: a single monthly check; affordable housing as a one-time second chance; emigration as the only outcome of insolvency.

### Job loss

Agents lose their job in **one** way: their employer **goes inactive**. A structure goes inactive when its monthly revenue can't cover its costs at the day-30 profitability check. All employees of the inactive structure become wageless at start of next month and run the standard emigration check.

There are no slow-motion wage cuts, no per-worker reductions, no fired-for-cause noise. A structure either operates fully or sits dormant. See `structures.md`.

### Savings buffer

Disposable income is held by the agent as a savings buffer. Savings are drawn against any monthly shortfall (wage + savings vs. expenses).

Savings parameters: alpha-1 default is uncapped, retained by the agent on emigration. Cap and interest TBD.

### When commercial structures don't exist

If an agent has COL spending budgeted ($300/mo on food, etc.) but no commercial structure of the matching sub-type exists, the spending **fails silently**. The money is *not* lost — it stays in the agent's disposable income / savings. Commercial revenue simply doesn't happen because there's no commercial entity to receive it. Sales tax, licensing fees, and commercial wages are correspondingly zero for that COL category.

This means agents in a commercial-less city accumulate savings rapidly but produce no commercial-side tax revenue. Notification fires: "Commercial demand unmet — build commercial zones."

### Affordable housing — one-attempt-to-move

Affordable housing is a treasury-subsidized residential structure with $500 rent. An agent failing the monthly check tries to move there once. If a seat is available and the agent is eligible (employed at wage strictly under $2,000, OR wageless because their employer went inactive), they move and re-run the check next month from the cheaper rent. Otherwise they emigrate.

No 6-month stay timer, no hire priority — affordable housing is purely a one-time savings-extension tool that delays emigration by some months.

## Industrial Goods Pricing

Industrial structures earn money by selling goods **to the next stage in the chain** (see `supply-chain.md` for the full chain):

- Extractors sell raw materials to processors.
- Processors sell processed goods to manufacturers (or to fuel storage, in the coal → fuel case).
- Manufacturers sell manufactured goods to storage.
- Storage sells to commercial structures and to the regional goods reservoir as overflow.

### Sales prices

Per-good sales prices are now defined explicitly per intermediate good (see `supply-chain.md` for the full table). All prices are user-adjustable via `levers.md`.

The previous fixed 25% / 50% / 100% rule has been replaced by explicit per-good prices, which naturally handles branched chains (timber, arable land) — each intermediate good has one price regardless of which downstream manufacturer buys it.


This is what closes the money loop: agents earn wages from structures, agents spend at commercial (and pay rent / utilities / income tax to treasury), commercial buys from industrial storage, industrial pays agents and taxes.

## Taxes

| Tax | Base | Rate |
|---|---|---|
| Income tax | Agent wages | **TBD** |
| Property tax | Land value of structure | **TBD** |
| Sales tax | Commercial revenue | **TBD** |

All rates TBD. Likely player-tunable in a later iteration.

## Import Upcharge

Imports cost **25% more per unit** than the equivalent local manufactured-good price. This makes imports a fallback rather than a primary supply, preserving the incentive to build local industrial chains. The 25% upcharge is **user-adjustable** — players can tune to make local production more or less competitive.

Buyers (commercial, construction) pay the import upcharge price when drawing from the import reservoir. The import upcharge is revenue to the off-region world (i.e., disappears from the city's economy); it is not a treasury inflow.

## Open Items

- **Land value** is required for rent and property tax. Comes from the deferred spatial layer. Alpha uses a placeholder fixed value per structure type.
- **Wage table** values: TBD via calibration tests once the rest of the economy is callable.
- **Tax rates** (income, property, sales): TBD. All user-adjustable.
- **Utility cost rates** (per-capita, per-commercial, per-industrial): TBD. User-adjustable.
- **Per-capita commercial demand factors** per manufactured good: TBD.
- **Branched-chain intermediate pricing** (timber, arable land): TBD — see `supply-chain.md`.
- **Rent function** (placeholder per residential type until spatial layer): TBD values.
