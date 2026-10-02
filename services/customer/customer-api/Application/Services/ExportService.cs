namespace CustomerApi.Application.Services;
using CustomerApi.Application.DTOs;
using CustomerApi.Settings;
using Microsoft.Extensions.Options;

/// <summary>
/// <c>GET /export</c> (§5, LST-1): paged records for the data transfer service's CSV export.
/// The service token names the exporting user in <c>X-Acting-User-Id</c>, so the same visibility
/// rule applies as if that user had opened the list themselves (NFR-6).
/// </summary>
public interface IExportService
{
    Task<PagedResult<CompanyDto>> CompaniesAsync(CompanyListQuery query, CancellationToken ct);
    Task<PagedResult<ContactDto>> ContactsAsync(ContactListQuery query, CancellationToken ct);
    int MaxPageSize { get; }
}

public class ExportService(
    ICompanyService companies,
    IContactService contacts,
    IOptions<CustomerSettings> settings) : IExportService
{
    /// <summary>An export page may be as large as an import call, which is what the CSV writer streams.</summary>
    public int MaxPageSize => settings.Value.MaxImportRowsPerCall;

    public Task<PagedResult<CompanyDto>> CompaniesAsync(CompanyListQuery query, CancellationToken ct)
        => companies.ListAsync(query, ct, MaxPageSize);

    public Task<PagedResult<ContactDto>> ContactsAsync(ContactListQuery query, CancellationToken ct)
        => contacts.ListAsync(query, ct, MaxPageSize);
}
