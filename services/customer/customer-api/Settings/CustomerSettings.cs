namespace CustomerApi.Settings;

/// <summary>
/// Every limit in the requirements is a configurable default (§10, assumptions).
/// Bound from the <c>Customer</c> section of appsettings.
/// </summary>
public class CustomerSettings
{
    /// <summary>DEL-3: records soft-deleted longer ago than this are hard-deleted by the nightly purge.</summary>
    public int RecycleBinRetentionDays { get; set; } = 30;

    /// <summary>DEL-4 / OWN-2: maximum records per bulk delete or reassign call.</summary>
    public int MaxBulkRecords { get; set; } = 500;

    /// <summary>§5: maximum rows per bulk-upsert call.</summary>
    public int MaxImportRowsPerCall { get; set; } = 1000;

    /// <summary>Rows saved per <c>SaveChangesAsync</c> during bulk upsert (NFR-3).</summary>
    public int BulkUpsertChunkSize { get; set; } = 200;

    /// <summary>CF-7: maximum active custom field definitions per entity type.</summary>
    public int MaxActiveCustomFields { get; set; } = 50;

    /// <summary>TAG-2.</summary>
    public int MaxTagsPerRecord { get; set; } = 20;

    /// <summary>LST-1.</summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>LST-1.</summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>DUP-2 / DUP-3: pg_trgm similarity threshold for soft duplicate warnings.</summary>
    public double SimilarityThreshold { get; set; } = 0.4;

    /// <summary>DUP-2 / DUP-3: maximum soft-duplicate warnings returned.</summary>
    public int MaxDuplicateWarnings { get; set; } = 10;

    /// <summary>DSR-2: where access-request exports are written. Local path for the MVP.</summary>
    public string DsrExportPath { get; set; } = "/var/crm/dsr-exports";

    /// <summary>Interval between nightly purge checks.</summary>
    public int PurgeIntervalHours { get; set; } = 24;

    /// <summary>Default country code for phone normalisation (CON-4).</summary>
    public string DefaultPhoneCountryCode { get; set; } = "91";

    /// <summary>Default ISO 3166 country for addresses (§4).</summary>
    public string DefaultCountry { get; set; } = "IN";
}
