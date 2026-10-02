namespace LeadApi.Settings;

/// <summary>
/// Lead service limits and business defaults. Bound from the <c>Lead</c> section of appsettings.
/// </summary>
public class LeadSettings
{
    /// <summary>DEL-3: records soft-deleted longer ago than this are hard-deleted by the nightly purge.</summary>
    public int RecycleBinRetentionDays { get; set; } = 30;

    /// <summary>Maximum leads per bulk assign call.</summary>
    public int MaxBulkRecords { get; set; } = 500;

    /// <summary>Maximum rows per bulk-upsert call.</summary>
    public int MaxImportRowsPerCall { get; set; } = 1000;

    /// <summary>CF-7: maximum active custom field definitions per entity type.</summary>
    public int MaxActiveCustomFields { get; set; } = 50;

    /// <summary>LST-1.</summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>LST-1.</summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>CNV-5: maximum conversion saga retries before marking failed.</summary>
    public int MaxConversionRetries { get; set; } = 5;

    /// <summary>WEB-4: maximum public form submissions per IP per hour.</summary>
    public int WebFormSubmissionsPerIpPerHour { get; set; } = 10;

    /// <summary>Days to look back for duplicate detection on web form submissions.</summary>
    public int DuplicateCheckWindowDays { get; set; } = 30;

    /// <summary>Default country code for phone normalisation.</summary>
    public string DefaultPhoneCountryCode { get; set; } = "91";

    /// <summary>Interval between nightly purge checks.</summary>
    public int PurgeIntervalHours { get; set; } = 24;
}
