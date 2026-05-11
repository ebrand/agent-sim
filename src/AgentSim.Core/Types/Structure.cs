namespace AgentSim.Core.Types;

/// <summary>
/// A constructed (or under-construction) structure in the city.
/// </summary>
public sealed class Structure
{
    public required long Id { get; init; }
    public required StructureType Type { get; init; }
    public required long ZoneId { get; init; }

    /// <summary>
    /// Construction progress in ticks. When equal to required build duration, the structure is operational.
    /// </summary>
    public int ConstructionTicks { get; set; }

    /// <summary>Total ticks required to complete construction. Default 7 = 1 game-week. Tuning
    /// target: enough cost to make placement feel real, short enough to keep early-game responsive.</summary>
    public int RequiredConstructionTicks { get; init; } = 7;

    public bool Operational => ConstructionTicks >= RequiredConstructionTicks;
    public bool UnderConstruction => !Operational;

    /// <summary>Whether the structure has gone inactive due to unprofitability (relevant for commercial / industrial).</summary>
    public bool Inactive { get; set; }

    /// <summary>Number of consecutive months this structure has been inactive. Drives auto-reactivation.</summary>
    public int InactiveMonths { get; set; }

    /// <summary>Whether the previous month was unprofitable (warning state before going inactive).</summary>
    public bool UnprofitableWarning { get; set; }

    /// <summary>Capacity in agent count (residential structures only). 0 for non-residential.</summary>
    public required int ResidentialCapacity { get; init; }

    /// <summary>Current resident IDs, for residential structures. Empty for non-residential.</summary>
    public List<long> ResidentIds { get; } = new();

    /// <summary>Job slots by required education tier (commercial / industrial / civic / etc.). Empty for residential.</summary>
    public Dictionary<EducationTier, int> JobSlots { get; init; } = new();

    /// <summary>Currently employed agent IDs (anywhere with jobs).</summary>
    public List<long> EmployeeIds { get; } = new();

    /// <summary>Per-tier count of currently filled slots — derived from EmployeeIds but cached for fast checks.</summary>
    public Dictionary<EducationTier, int> FilledSlots { get; } = new();

    /// <summary>Structure's cash balance — used for commercial/industrial to fund wages, taxes, utilities.</summary>
    public int CashBalance { get; set; }

    /// <summary>Revenue accumulated this month (reset at end of month).</summary>
    public int MonthlyRevenue { get; set; }

    /// <summary>Expenses accumulated this month (reset at end of month). Used for profitability check (M5+).</summary>
    public int MonthlyExpenses { get; set; }

    /// <summary>Internal buffer of raw materials (extractor output / processor input).</summary>
    public Dictionary<RawMaterial, int> RawStorage { get; } = new();

    /// <summary>Internal buffer of processed goods (processor output / manufacturer input).</summary>
    public Dictionary<ProcessedGood, int> ProcessedStorage { get; } = new();

    /// <summary>Internal buffer of manufactured goods (manufacturer output / storage holdings).</summary>
    public Dictionary<ManufacturedGood, int> ManufacturedStorage { get; } = new();

    /// <summary>Capacity of internal storage for raw / processed / manufactured goods (single shared cap per type).</summary>
    public int InternalStorageCapacity { get; init; }

    /// <summary>Number of student seats (education structures). 0 for non-education.</summary>
    public int SeatCapacity { get; init; }

    /// <summary>IDs of agents currently enrolled in this education structure.</summary>
    public List<long> EnrolledStudentIds { get; } = new();

    /// <summary>Service capacity in agents served per month (civic / healthcare / utility). 0 for non-service.</summary>
    public int ServiceCapacity { get; init; }

    /// <summary>Optional flavor name (M12 CorporateHq: "ExxonMobil"-style label; null for others).</summary>
    public string? Name { get; init; }

    /// <summary>The industry this CorporateHq belongs to. Null for non-HQ structures.</summary>
    public IndustryType? Industry { get; init; }

    /// <summary>The CorporateHq that owns this industrial structure. Null for non-industrial or
    /// for the HQ itself.</summary>
    public long? OwnerHqId { get; set; }

    /// <summary>IDs of industrial structures this HQ owns. Empty for non-HQ.</summary>
    public List<long> OwnedStructureIds { get; } = new();

    public StructureCategory Category => Type.Category();
}
