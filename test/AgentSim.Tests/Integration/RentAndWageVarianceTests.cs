using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M18: tier-based rent (800/1400/1800/2800), utility = 10% of rent, ±5% wage variance, and
/// the new utility-distribution structures.
/// </summary>
public class RentAndWageVarianceTests
{
    [Theory]
    [InlineData(EducationTier.Uneducated, 450, 45)]
    [InlineData(EducationTier.Primary, 800, 80)]
    [InlineData(EducationTier.Secondary, 1_000, 100)]
    [InlineData(EducationTier.College, 1_550, 155)]
    public void Rent_AndUtility_AreTierBased(EducationTier tier, int expectedRent, int expectedUtility)
    {
        var house = new Structure
        {
            Id = 1, Type = StructureType.House, ZoneId = 1,
            ResidentialCapacity = 4,
        };
        var agent = new Agent { Id = 1, EducationTier = tier, AgeDays = 30 * 360 };

        Assert.Equal(expectedRent, Rent.RentForAgent(agent, house));
        Assert.Equal(expectedUtility, Rent.UtilityForAgent(agent, house));
    }

    [Fact]
    public void AffordableHousing_ChargesFlatRateRegardlessOfTier()
    {
        var ah = new Structure
        {
            Id = 1, Type = StructureType.AffordableHousing, ZoneId = 1,
            ResidentialCapacity = 40,
        };
        foreach (EducationTier tier in Enum.GetValues<EducationTier>())
        {
            var agent = new Agent { Id = 1, EducationTier = tier, AgeDays = 30 * 360 };
            Assert.Equal(Rent.AffordableHousingRent, Rent.RentForAgent(agent, ah));
            Assert.Equal((int)(Rent.AffordableHousingRent * Rent.UtilityFractionOfRent),
                Rent.UtilityForAgent(agent, ah));
        }
    }

    [Fact]
    public void HiredAgent_HasWageWithin5PctOfTierBase()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var commZone = sim.CreateCommercialZone();
        var shop = sim.PlaceCommercialStructure(commZone.Id, StructureType.Shop, CommercialSector.Retail);
        shop.ConstructionTicks = shop.RequiredConstructionTicks;

        sim.Tick(1);  // trigger hiring

        Assert.NotEmpty(shop.EmployeeIds);
        foreach (var id in shop.EmployeeIds)
        {
            var agent = sim.State.City.Agents[id];
            var baseWage = Wages.MonthlyWage(agent.CurrentJobTier!.Value);
            var min = (int)(baseWage * Wages.WageVarianceMin);
            var max = (int)(baseWage * (Wages.WageVarianceMin + Wages.WageVarianceSpread));
            Assert.InRange(agent.CurrentWage, min, max);
        }
    }

    [Fact]
    public void WageVariance_IsDeterministicBySeed()
    {
        Sim Build()
        {
            var s = Sim.Create(new SimConfig { Seed = 42 });
            s.CreateResidentialZone();
            var cz = s.CreateCommercialZone();
            var shop = s.PlaceCommercialStructure(cz.Id, StructureType.Shop, CommercialSector.Retail);
            shop.ConstructionTicks = shop.RequiredConstructionTicks;
            s.Tick(1);
            return s;
        }

        var a = Build();
        var b = Build();

        var wagesA = a.State.City.Agents.Values
            .Where(x => x.CurrentWage > 0).Select(x => x.CurrentWage).OrderBy(w => w).ToList();
        var wagesB = b.State.City.Agents.Values
            .Where(x => x.CurrentWage > 0).Select(x => x.CurrentWage).OrderBy(w => w).ToList();

        Assert.Equal(wagesA, wagesB);
    }

    [Fact]
    public void WageVariance_ProducesActualSpread_AcrossAgents()
    {
        // Hire many agents; wage variance should produce a range of distinct values, not all equal.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var commZone = sim.CreateCommercialZone();
        var market = sim.PlaceCommercialStructure(commZone.Id, StructureType.Marketplace, CommercialSector.Retail);
        market.ConstructionTicks = market.RequiredConstructionTicks;

        sim.Tick(1);

        var primaryWages = sim.State.City.Agents.Values
            .Where(a => a.CurrentJobTier == EducationTier.Primary && a.CurrentWage > 0)
            .Select(a => a.CurrentWage)
            .Distinct()
            .ToList();

        Assert.True(primaryWages.Count >= 2,
            $"Wage variance should produce ≥2 distinct primary-tier wages with multiple hires. Got {primaryWages.Count} distinct.");
    }

    [Fact]
    public void ElectricityDistribution_CanBePlaced_AndHasCapacity()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 2_000_000 });
        var s = sim.PlaceServiceStructure(StructureType.ElectricityDistribution);

        Assert.False(s.Operational);
        Assert.Equal(Services.ElectricityDistributionCapacity, s.ServiceCapacity);
        Assert.Equal(StructureCategory.Utility, s.Category);
    }

    [Fact]
    public void WaterDistribution_CanBePlaced_AndHasCapacity()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 2_000_000 });
        var s = sim.PlaceServiceStructure(StructureType.WaterDistribution);

        Assert.False(s.Operational);
        Assert.Equal(Services.WaterDistributionCapacity, s.ServiceCapacity);
        Assert.Equal(StructureCategory.Utility, s.Category);
    }

    [Fact]
    public void UtilityDistribution_HasUpkeep_AndIsTreasuryFunded()
    {
        Assert.True(Upkeep.IsTreasuryFunded(StructureType.ElectricityDistribution));
        Assert.True(Upkeep.IsTreasuryFunded(StructureType.WaterDistribution));
        Assert.Equal(Upkeep.ElectricityDistribution, Upkeep.MonthlyCost(StructureType.ElectricityDistribution));
        Assert.Equal(Upkeep.WaterDistribution, Upkeep.MonthlyCost(StructureType.WaterDistribution));
    }
}
