# Goods and Construction Recipes

The simulation's goods set follows the four-tier supply chain (see `supply-chain.md` for the authoritative definition). Construction recipes consume **manufactured goods** — the priced output of the chain.

## Goods Set (Summary)

Authoritative source: `supply-chain.md`. Brief summary:

- **Natural resources** (regional): timber, ore, arable land, stone, sand, coal, water, wind.
- **Raw materials** (extractor outputs): wood, iron-ore, crops, rock, sand, coal.
- **Processed goods** (processor outputs): lumber, steel, grain, textiles, aggregate, silicate, fuel.
- **Manufactured goods** (manufacturer outputs): household, bldg supplies, sporting goods, toys, metal goods, food, clothing, concrete, glass goods.

Construction recipes draw exclusively from the manufactured tier.

## Bootstrap Calibration

Settler count, housing strategy, and starting goods stock are linked.

- **Settler count N: 50.** One-time burst when first residential zone is created.
- **Bootstrap housing strategy: houses only.** Houses do not require metal goods, so the bootstrap stock does not need to include them — establishing metal goods as a gating resource for later expansion.
- **Houses needed:** 50 settlers ÷ 4 capacity per house = 13 houses.
- **Materials needed for 13 houses:** ~130 bldg supplies, ~65 concrete, ~26 glass goods.
- **Bootstrap goods reservoir starting stock (~1.5× buffer):**

  | Manufactured good | Stock |
  |-------------------|-------|
  | Bldg supplies     | 200   |
  | Concrete          | 100   |
  | Glass goods       | 40    |
  | Metal goods       | 0     |

**Industrial bootstrap relies on free imports.** The first extractor / processor / manufacturer structures pull their construction goods (including metal goods) from the import reservoir, since imports are currently free. This is a known consequence of the deferred economic layer. See `economy.md` and `open-questions.md`.

## Construction Recipes (Alpha-1 Proposal)

All quantities are inputs consumed during construction. **Build duration** is in months; one tick = one day, so 30 ticks = 1 month. See `time-and-pacing.md`. All goods listed are **manufactured-tier**.

**All structures take 3 months (90 ticks) to build.** A construction progress indicator is shown during the build period.

### Residential

| Structure           | Bldg supplies | Concrete | Metal goods | Glass goods | Capacity   | Rent  | Build months |
|---------------------|---------------|----------|-------------|-------------|------------|-------|--------------|
| House               | 10            | 5        | 0           | 2           | 4 agents   | $800  | 3            |
| Apartment           | 30            | 50       | 30          | 15          | 40 agents  | $1,400| 3            |
| Townhouse           | 18            | 25       | 15          | 8           | 12 agents  | $1,800| 3            |
| Condo               | 25            | 40       | 25          | 12          | 25 agents  | $2,800| 3            |
| Affordable housing  | 25            | 40       | 25          | 10          | 40 agents  | $500 (treasury-subsidized) | 3 |

Houses are accessible early; everything from apartment up gates on metal goods availability. Each residential type targets a specific wage tier — house for uneducated, apartment for primary, townhouse for secondary, condo for college. Affordable housing is the safety-net option ($500 rent, treasury-subsidized) — see `structures.md`.

### Commercial

| Structure   | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|-------------|---------------|----------|-------------|-------------|--------------|
| Shop        | 15            | 30       | 15          | 15          | 3            |
| Marketplace | 25            | 50       | 25          | 20          | 3            |

(For alpha-1, commercial structures share one demand pool; visual sub-types like shopping/business/entertainment are cosmetic only — see `structures.md`.)

### Industrial — Extractors

| Structure         | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|-------------------|---------------|----------|-------------|-------------|--------------|
| Forest extractor  | 10            | 5        | 5           | 0           | 6            |
| Mine              | 20            | 10       | 20          | 0           | 6            |
| Coal mine         | 20            | 10       | 20          | 0           | 6            |
| Quarry            | 15            | 10       | 15          | 0           | 6            |
| Sand pit          | 10            | 5        | 10          | 0           | 6            |
| Farm              | 15            | 5        | 5           | 0           | 6            |

### Industrial — Processors

| Structure             | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|-----------------------|---------------|----------|-------------|-------------|--------------|
| Sawmill               | 20            | 10       | 30          | 0           | 6            |
| Smelter               | 10            | 30       | 50          | 5           | 6            |
| Mill (grain/textiles) | 15            | 10       | 30          | 0           | 6            |
| Aggregate plant       | 10            | 0        | 30          | 0           | 6            |
| Silicate plant        | 15            | 20       | 40          | 0           | 6            |
| Fuel refinery         | 10            | 30       | 50          | 5           | 6            |

### Industrial — Manufacturers

| Structure              | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|------------------------|---------------|----------|-------------|-------------|--------------|
| Household factory      | 25            | 20       | 30          | 5           | 6            |
| Bldg supplies factory  | 25            | 20       | 30          | 5           | 6            |
| Metal goods factory    | 15            | 30       | 50          | 5           | 6            |
| Food packing plant     | 20            | 20       | 30          | 5           | 6            |
| Clothing factory       | 20            | 15       | 25          | 5           | 6            |
| Concrete plant         | 10            | 0        | 30          | 0           | 6            |
| Glass works            | 15            | 20       | 40          | 5           | 6            |

Manufacturer recipes deliberately avoid circular dependencies — concrete plant does not consume concrete, glass works does not consume glass goods.

### Industrial — Storage

| Structure     | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|---------------|---------------|----------|-------------|-------------|--------------|
| Storage       | 30            | 20       | 30          | 0           | 6            |
| Fuel storage  | 15            | 30       | 40          | 0           | 6            |

### Civic

| Structure     | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|---------------|---------------|----------|-------------|-------------|--------------|
| Town hall     | 50            | 80       | 40          | 20          | 3            |
| Fire station  | 20            | 40       | 20          | 10          | 3            |
| Police station| 20            | 40       | 20          | 10          | 3            |

### Healthcare

| Structure | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|-----------|---------------|----------|-------------|-------------|--------------|
| Clinic    | 30            | 60       | 30          | 25          | 6            |
| Hospital  | 80            | 150      | 80          | 60          | 6            |

### Education

| Structure         | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|-------------------|---------------|----------|-------------|-------------|--------------|
| Primary school    | 30            | 50       | 20          | 15          | 6            |
| Secondary school  | 50            | 80       | 40          | 30          | 6            |
| College           | 80            | 150      | 80          | 60          | 6            |

### Utility

| Structure | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|-----------|---------------|----------|-------------|-------------|--------------|
| Generator | 20            | 60       | 80          | 10          | 3            |
| Well      | 10            | 40       | 30          | 0           | 3            |

Utility structures are treasury-funded for construction and upkeep; they consume **fuel** drawn from fuel storage during operation.

### Restoration

| Structure            | Bldg supplies | Concrete | Metal goods | Glass goods | Build months |
|----------------------|---------------|----------|-------------|-------------|--------------|
| Park                 | 5             | 10       | 0           | 0           | 3            |
| Reforestation site   | 5             | 0        | 0           | 0           | 3            |
| Wetland restoration  | 5             | 5        | 0           | 0           | 3            |

## Design Notes

- **Houses skip metal goods.** Intentional — bootstrap stock can ship without metal goods, and the player must build (or import) them to scale beyond houses.
- **Manufacturer recipes avoid circular goods.** Concrete plant does not require concrete; glass works does not require glass goods.
- **Uniform 3-month build duration** for all structure types (90 ticks). At default 1× speed (1 tick/sec), every build takes 90 seconds real-time. A progress indicator shows construction status. See `time-and-pacing.md`.
- **Quantities are first-pass.** Once economy rates land (wages, taxes, utility costs), recipes will need re-balancing against the manufactured-goods sales prices in `supply-chain.md`.

## Open Issues

- These recipes have not been simulated; they will need adjustment.
- Free imports defeat recipe gating until the economic layer adds import pricing.
- Restoration recipes are intentionally cheap. May need scaling with city size.
