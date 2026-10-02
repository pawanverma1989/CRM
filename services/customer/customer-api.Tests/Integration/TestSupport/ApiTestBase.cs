namespace CustomerApi.Tests.Integration.TestSupport;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Services;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Shared arrangement for the end-to-end tests: a clean database, one organization and four known
/// users (an admin, a manager who can see both reps, and two sales reps who can only see
/// themselves), plus the small helpers every test needs.
/// </summary>
[Collection(CustomerApiCollection.Name)]
public abstract class ApiTestBase(CustomerApiFixture fixture) : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected CustomerApiFixture Fixture { get; } = fixture;

    protected Guid OrganizationId { get; } = Guid.NewGuid();

    /// <summary>A second organization, used to prove that nothing leaks across the tenant boundary.</summary>
    protected Guid OtherOrganizationId { get; } = Guid.NewGuid();

    protected Guid AdminId { get; } = Guid.NewGuid();
    protected Guid ManagerId { get; } = Guid.NewGuid();
    protected Guid RepAId { get; } = Guid.NewGuid();
    protected Guid RepBId { get; } = Guid.NewGuid();

    public virtual async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
        await SeedUserRefsAsync();
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    // ── clients ────────────────────────────────────────────────────────────────

    protected HttpClient Anonymous() => Fixture.Factory.CreateClient();

    protected HttpClient AsAdmin() => As(AdminId, TestTokens.Admin);

    /// <summary>A manager of a team containing both reps (§2: own + their teams' records + unowned).</summary>
    protected HttpClient AsManager() => As(ManagerId, TestTokens.Manager, [ManagerId, RepAId, RepBId]);

    protected HttpClient AsRepA() => As(RepAId, TestTokens.SalesRep, [RepAId]);

    protected HttpClient AsRepB() => As(RepBId, TestTokens.SalesRep, [RepBId]);

    protected HttpClient As(Guid userId, string role, Guid[]? visibleOwnerIds = null, Guid? organizationId = null)
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.ForUser(userId, organizationId ?? OrganizationId, role, visibleOwnerIds));
        return client;
    }

    /// <summary>
    /// A service caller, as the lead or data transfer service would call: a service token plus the
    /// documented X-Acting-* headers naming the user the work is done for.
    /// </summary>
    protected HttpClient AsService(
        string serviceName, Guid actingUserId, string actingRole = TestTokens.SalesRep, Guid[]? visibleOwnerIds = null)
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.ForService(OrganizationId, serviceName));
        client.DefaultRequestHeaders.Add(HttpRequestContext.ActingUserIdHeader, actingUserId.ToString());
        client.DefaultRequestHeaders.Add(HttpRequestContext.ActingUserRoleHeader, actingRole);

        if (visibleOwnerIds is { Length: > 0 })
            client.DefaultRequestHeaders.Add(
                HttpRequestContext.VisibleOwnerIdsHeader, string.Join(',', visibleOwnerIds));

        return client;
    }

    // ── HTTP helpers ───────────────────────────────────────────────────────────

    protected static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body)
        => client.PostAsJsonAsync(url, body, Json);

    protected static Task<HttpResponseMessage> PatchAsync(HttpClient client, string url, object body)
        => client.PatchAsJsonAsync(url, body, Json);

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(body, Json)
            ?? throw new InvalidOperationException($"Could not read a {typeof(T).Name} from: {body}");
    }

    /// <summary>The RFC 7807 problem body, so a test can assert on the named conflicting record.</summary>
    protected static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
        => JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    // ── arrangement ────────────────────────────────────────────────────────────

    protected async Task<CompanyDto> CreateCompanyAsync(
        HttpClient client, string name, string? domain = null, Guid? ownerId = null, string[]? tags = null)
    {
        var response = await PostAsync(client, "/api/customer/v1/companies", new
        {
            name,
            domain,
            ownerId,
            tags
        });

        response.EnsureSuccessStatusCode();
        return await ReadAsync<CompanyDto>(response);
    }

    protected async Task<ContactDto> CreateContactAsync(
        HttpClient client,
        string firstName,
        string? lastName = null,
        string? email = null,
        string? phone = null,
        Guid? companyId = null,
        Guid? ownerId = null,
        string[]? tags = null)
    {
        var response = await PostAsync(client, "/api/customer/v1/contacts", new
        {
            firstName,
            lastName,
            email,
            phone,
            companyId,
            ownerId,
            tags
        });

        response.EnsureSuccessStatusCode();
        return await ReadAsync<ContactDto>(response);
    }

    /// <summary>Owner names come from the local <c>user_refs</c> copy, which identity events fill.</summary>
    protected async Task SeedUserRefsAsync()
    {
        await Fixture.WithDbAsync(async db =>
        {
            db.UserRefs.AddRange(
                new UserRef { UserId = AdminId, OrganizationId = OrganizationId, DisplayName = "Admin One", IsActive = true, SourceVersion = 1, UpdatedAt = DateTimeOffset.UtcNow },
                new UserRef { UserId = ManagerId, OrganizationId = OrganizationId, DisplayName = "Manager North", IsActive = true, SourceVersion = 1, UpdatedAt = DateTimeOffset.UtcNow },
                new UserRef { UserId = RepAId, OrganizationId = OrganizationId, DisplayName = "Rep A", IsActive = true, SourceVersion = 1, UpdatedAt = DateTimeOffset.UtcNow },
                new UserRef { UserId = RepBId, OrganizationId = OrganizationId, DisplayName = "Rep B", IsActive = true, SourceVersion = 1, UpdatedAt = DateTimeOffset.UtcNow });

            await db.SaveChangesAsync();
        });
    }

    protected Task<Guid> SeedPicklistAsync(string listType, string value)
        => Fixture.WithDbAsync(async db =>
        {
            var entry = new Picklist
            {
                Id = Guid.NewGuid(),
                OrganizationId = OrganizationId,
                ListType = listType,
                Value = value,
                SortOrder = 10,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Picklists.Add(entry);
            await db.SaveChangesAsync();
            return entry.Id;
        });

    /// <summary>Moves a record's <c>deleted_at</c> back in time, to test the retention window.</summary>
    protected Task AgeDeletionAsync(string table, Guid id, int days)
        => Fixture.ExecuteAsync(
            $"UPDATE {table} SET deleted_at = now() - make_interval(days => @days) WHERE id = @id",
            ("days", days), ("id", id));

    // ── outbox assertions ──────────────────────────────────────────────────────

    protected Task<List<OutboxEvent>> OutboxAsync(Guid? aggregateId = null, string? eventType = null)
        => Fixture.WithDbAsync(db =>
        {
            var query = db.OutboxEvents.AsNoTracking().AsQueryable();
            if (aggregateId is { } id) query = query.Where(e => e.AggregateId == id);
            if (eventType is not null) query = query.Where(e => e.EventType == eventType);
            return query.OrderBy(e => e.OccurredAt).ToListAsync();
        });

    // ── consumers and background work ──────────────────────────────────────────

    /// <summary>
    /// Delivers an inbound event the way the broker would, but without one: the consumer's
    /// plumbing is one thin layer over <see cref="IInboundEventProcessor"/>, which is where the
    /// idempotency ledger and the handlers live (§6.2).
    /// </summary>
    protected async Task<InboundResult> DeliverAsync(
        string eventType,
        object payload,
        Guid? eventId = null,
        int version = 0,
        Guid? aggregateId = null,
        Guid? organizationId = null)
    {
        var envelope = new
        {
            event_id = eventId ?? Guid.NewGuid(),
            event_type = eventType,
            organization_id = organizationId ?? OrganizationId,
            aggregate_type = eventType.Split('.')[0],
            aggregate_id = aggregateId ?? Guid.Empty,
            version,
            occurred_at = DateTimeOffset.UtcNow,
            actor_id = (Guid?)null,
            payload
        };

        using var scope = Fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IInboundEventProcessor>();
        return await processor.ProcessAsync(JsonSerializer.Serialize(envelope), eventType, CancellationToken.None);
    }

    /// <summary>Runs the nightly retention purge on demand (DEL-3).</summary>
    protected async Task<PurgeOutcome> RunPurgeAsync()
    {
        using var scope = Fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPurgeService>().RunAsync(CancellationToken.None);
    }

    protected static JsonElement Envelope(OutboxEvent row)
        => JsonSerializer.Deserialize<JsonElement>(row.Payload);

    protected static JsonElement Payload(OutboxEvent row)
        => Envelope(row).GetProperty("payload");
}
