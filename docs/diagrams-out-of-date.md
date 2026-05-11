# Diagrams — Out of Date

Punch list for diagram updates after the alpha-1 simplification pass. The text docs are the source of truth; these diagrams are illustrative and have lagged behind several rounds of design changes.

None of these block coding the simulation — they're illustrative material. Update when convenient.

## `resource-pricing.png` — current

Matches `supply-chain.md` and `goods-and-recipes.md`. **No changes needed.**

## `education.png` — minor

**Issue:** the diagram labels tier-to-tier progression with "% chance," which suggests a fixed probability model.

**Current spec:** education progression is **seats-driven** — every qualifying agent advances if seats exist in the next tier; the rest enter the workforce at their current education level (per `agents.md`).

**Update:** replace `% chance` labels with `if seats available` or similar to reflect seats-driven progression.

## `legend.png` — medium

Three issues:

### 1. Natural resources list is outdated

**Diagram lists:** fresh water, iron ore / coal / copper, oil, rare earth minerals, forest, arable land.

**Current spec (per `supply-chain.md` and `region.md`):** timber, ore, arable land, stone, sand, coal, water, wind.

**Update:** replace the natural-resources list with the current 8-resource set.

### 2. Agent pays property tax

**Diagram economy panel shows:** agents pay `utils, prop tax, rent/mortg, income tax` to treasury.

**Current spec:** agents pay rent, utilities, and income tax. **Property tax was removed at agent level** during the simplification (folded into rent for residential; commercial/industrial pay it as structures, not agents). "Mortgage" was always shorthand for rent and should just say rent.

**Update:** remove `prop tax` from agent → treasury arrows. Change `rent/mortg` to `rent`.

### 3. Education `% chance` (same as `education.png`)

**Update:** rename `% chance` labels to `if seats available`.

## `supply-chain.png` — medium

**Issue:** the right-side pricing table shows the old higher prices that were rebalanced down (household $80 → $40, bldg supplies $144 → $72, metal goods $720 → $48, etc.).

**Current spec:** `resource-pricing.png` is the authoritative pricing grid with the rebalanced 2× markup values across all manufactured goods.

**Update options:**
- A. Strip the right-side pricing table from `supply-chain.png` and reference `resource-pricing.png` for prices.
- B. Update the right-side table to match `resource-pricing.png`.

Option A keeps a single source of truth (recommended). The left-side flow (extraction → processing → fuel storage / manufacturing → storage → commercial / regional) is correct and should stay.

## `agent-income-expense.png` — medium

**Issues:** income tax shown at 10% with values $200/$350/$450/$700; disposable shown at 5% with values $100/$175/$225/$350.

**Current spec (per `economy.md`):** income tax is **5%**, disposable is **10%**. The 5% income-tax cut went entirely to disposable.

**Updates:**
- Income tax row: 10% → **5%**; values $200/$350/$450/$700 → **$100/$175/$225/$350**
- Disposable row: 5% → **10%**; values $100/$175/$225/$350 → **$200/$350/$450/$700**

All other rows (rent, utilities, food, clothing, household, entertainment) remain correct.

## `economy.png` — medium

Combines two outdated panels:

### 1. Agent income/expense panel (bottom-left)

Same updates as `agent-income-expense.png` above (income tax 10% → 5%, disposable 5% → 10%, swap absolute values).

### 2. Agent property tax row

The diagram shows an agent property tax line at 10% under "varies by land value."

**Current spec:** agents do not pay property tax. **Remove the property-tax row** from the agent income/expense panel.

The other panels in `economy.png` (top-left money flows, top-right commercial sub-types, bottom-right demand system) are correct and should remain.

## `agent-sim.png` — major

This is the master diagram and has fallen significantly behind. Multiple structural updates needed:

### 1. Industrial chain expanded to 4 tiers

**Diagram shows:** extraction → processing → storage (3-tier).

**Current spec:** extraction → processing → **manufacturing** → storage (4-tier). Plus a parallel **fuel storage** branch for coal-derived fuel that feeds utility structures.

**Update:** add manufacturing as a distinct tier between processing and storage; add fuel storage as a parallel structure type with its own demand pool.

### 2. Utility structure category missing

**Diagram has no utility structure category** in the demand-pool listing.

**Current spec:** utility (generator, well) is a treasury-funded structure category alongside civic/healthcare/education. Has a utility demand pool driven per agent + per commercial/industrial structure.

**Update:** add utility demand pool and utility structures to the right-side demand panel.

### 3. Residential demand per-type, not per-LV

**Diagram shows:** residential demand cap with house, apartment building.

**Current spec:** residential demand pool tracks per-residential-structure-type demand (house / apartment / townhouse / condo / affordable housing). LV is per cell, not per structure type — does not affect rent (only property tax for non-residential and utility cost).

**Update:** expand residential row to show all 5 residential types; remove any LV-tier-based rent labeling.

### 4. Commercial sub-types

**Diagram shows:** business, shopping, sports, technology, entertainment.

**Current spec:** confirmed correct for sub-type variety. **Add note:** entertainment (and sports, depending on classification) is **service-only** — pays licensing fees instead of consuming goods. Other sub-types are goods-backed.

### 5. Other minor items

- Agent reservoir tier counts (primary / secondary / college shown at 10k each) — current spec is **60k total across four tiers** (uneducated + primary + secondary + college). Update to show four tiers.
- City agent cap — current spec says total 60k across city + reservoir, not separate city cap.

## Summary

| Diagram | Effort | Impact |
|---|---|---|
| `resource-pricing.png` | None — current | — |
| `education.png` | Minor (1 label rename) | Low |
| `legend.png` | Medium (3 changes) | Low — illustrative |
| `supply-chain.png` | Medium (strip or update pricing table) | Medium |
| `agent-income-expense.png` | Medium (numbers swap) | Medium |
| `economy.png` | Medium (numbers + 1 row removal) | Medium |
| `agent-sim.png` | Major (structural redraw) | High — this is the master diagram |

Recommendation: prioritize `agent-sim.png` as the master overview. Fix others opportunistically.
