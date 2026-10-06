namespace SalesApi.Settings;

/// <summary>
/// Sales service limits and business defaults. Bound from the <c>Sales</c> section of appsettings.
/// </summary>
public class SalesSettings
{
    /// <summary>DEL-1: records soft-deleted longer ago than this are hard-deleted by the nightly purge.</summary>
    public int RecycleBinRetentionDays { get; set; } = 30;

    /// <summary>Maximum deals per bulk reassign call.</summary>
    public int MaxBulkRecords { get; set; } = 500;

    /// <summary>Maximum rows per bulk-upsert call.</summary>
    public int MaxImportRowsPerCall { get; set; } = 1000;

    /// <summary>Maximum active custom field definitions per entity type.</summary>
    public int MaxActiveCustomFields { get; set; } = 50;

    /// <summary>LST-1.</summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>LST-1.</summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>BRD-5: days without activity before a deal is considered stale.</summary>
    public int StaleDaysWithoutActivity { get; set; } = 14;

    /// <summary>BRD-5: days in same stage before a deal is considered stale.</summary>
    public int StaleDaysInStage { get; set; } = 30;

    /// <summary>Interval between nightly purge checks.</summary>
    public int PurgeIntervalHours { get; set; } = 24;
}
