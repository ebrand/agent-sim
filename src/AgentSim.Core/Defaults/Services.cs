using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Service-structure capacities (civic / healthcare / utility) and worst-of emigration parameters.
/// Per `structures.md` and `feedback-loops.md`.
/// </summary>
public static class Services
{
    // === Service capacities (agents served per structure) ===
    public const int PoliceStationCapacity = 5_000;
    public const int FireStationCapacity = 5_000;
    public const int TownHallCapacity = 25_000;
    public const int ClinicCapacity = 2_500;
    public const int HospitalCapacity = 12_500;
    public const int GeneratorCapacity = 10_000;
    public const int WellCapacity = 10_000;
    public const int ElectricityDistributionCapacity = 5_000;
    public const int WaterDistributionCapacity = 5_000;

    /// <summary>Per-type capacity. 0 for non-service types. Education is handled via Structure.SeatCapacity.</summary>
    public static int CapacityFor(StructureType type) => type switch
    {
        StructureType.PoliceStation => PoliceStationCapacity,
        StructureType.FireStation => FireStationCapacity,
        StructureType.TownHall => TownHallCapacity,
        StructureType.Clinic => ClinicCapacity,
        StructureType.Hospital => HospitalCapacity,
        StructureType.Generator => GeneratorCapacity,
        StructureType.Well => WellCapacity,
        StructureType.ElectricityDistribution => ElectricityDistributionCapacity,
        StructureType.WaterDistribution => WaterDistributionCapacity,
        _ => 0,
    };

    // === Worst-of service emigration parameters (per `feedback-loops.md` / `levers.md`) ===

    /// <summary>Satisfaction percent threshold below which emigration pressure begins. Default 60%.</summary>
    public const double EmigrationThresholdPercent = 60.0;

    /// <summary>Scale on the (threshold - satisfaction)/100 fraction. Default 0.02 → 1.2%/mo at 0%.</summary>
    public const double EmigrationScale = 0.02;
}
