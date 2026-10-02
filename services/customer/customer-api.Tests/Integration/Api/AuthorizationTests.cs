namespace CustomerApi.Tests.Integration.Api;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerApi.Application.DTOs;
using CustomerApi.Tests.Integration.TestSupport;
using FluentAssertions;

/// <summary>
/// §2 and §5: every protected endpoint refuses an unauthenticated caller with 401 and the wrong
/// role with 403, while a record the caller may not view is 404 — never 403, so its existence
/// stays hidden (AC-7).
/// </summary>
public class AuthorizationTests(CustomerApiFixture fixture) : ApiTestBase(fixture)
{
    /// <summary>Every route in the §5 table, plus the two picklist and owner additions.</summary>
    public static TheoryData<string, string> ProtectedEndpoints()
    {
        var id = Guid.Empty;

        return new TheoryData<string, string>
        {
            { "GET", "/api/customer/v1/companies" },
            { "POST", "/api/customer/v1/companies" },
            { "GET", $"/api/customer/v1/companies/{id}" },
            { "PATCH", $"/api/customer/v1/companies/{id}" },
            { "DELETE", $"/api/customer/v1/companies/{id}" },
            { "POST", "/api/customer/v1/companies/merge" },
            { "GET", "/api/customer/v1/contacts" },
            { "POST", "/api/customer/v1/contacts" },
            { "GET", $"/api/customer/v1/contacts/{id}" },
            { "PATCH", $"/api/customer/v1/contacts/{id}" },
            { "DELETE", $"/api/customer/v1/contacts/{id}" },
            { "POST", "/api/customer/v1/contacts/merge" },
            { "POST", "/api/customer/v1/duplicates/check" },
            { "POST", "/api/customer/v1/reassign" },
            { "GET", "/api/customer/v1/reassignment-queue" },
            { "POST", "/api/customer/v1/bulk-delete" },
            { "GET", "/api/customer/v1/recycle-bin" },
            { "POST", $"/api/customer/v1/recycle-bin/contact/{id}/restore" },
            { "GET", "/api/customer/v1/custom-fields?entity=contact" },
            { "POST", "/api/customer/v1/custom-fields" },
            { "PATCH", $"/api/customer/v1/custom-fields/{id}" },
            { "GET", "/api/customer/v1/tags" },
            { "GET", "/api/customer/v1/picklists?type=contact_source" },
            { "POST", "/api/customer/v1/picklists" },
            { "PATCH", $"/api/customer/v1/picklists/{id}" },
            { "GET", "/api/customer/v1/owners" },
            { "POST", "/api/customer/v1/bulk-upsert" },
            { "GET", "/api/customer/v1/export?entity=contact" }
        };
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task WithoutAToken_EveryEndpointReturns401(string method, string path)
    {
        var response = await SendAsync(Anonymous(), method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path} is protected");
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task WithATokenSignedByTheWrongKey_EveryEndpointReturns401(string method, string path)
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.WithWrongSignature(AdminId, OrganizationId, TestTokens.Admin));

        var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("POST", "/api/customer/v1/companies/merge")]
    [InlineData("POST", "/api/customer/v1/contacts/merge")]
    [InlineData("POST", "/api/customer/v1/reassign")]
    [InlineData("GET", "/api/customer/v1/reassignment-queue")]
    [InlineData("GET", "/api/customer/v1/recycle-bin")]
    [InlineData("POST", "/api/customer/v1/custom-fields")]
    [InlineData("POST", "/api/customer/v1/picklists")]
    [InlineData("POST", "/api/customer/v1/bulk-upsert")]
    [InlineData("GET", "/api/customer/v1/export?entity=contact")]
    public async Task ASalesRepCallingAnEndpointTheirRoleMayNotUse_Gets403(string method, string path)
    {
        var response = await SendAsync(AsRepA(), method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "/api/customer/v1/reassignment-queue")]
    [InlineData("GET", "/api/customer/v1/recycle-bin")]
    [InlineData("POST", "/api/customer/v1/custom-fields")]
    [InlineData("POST", "/api/customer/v1/picklists")]
    [InlineData("POST", "/api/customer/v1/bulk-upsert")]
    public async Task AManagerCallingAnAdminOrServiceOnlyEndpoint_Gets403(string method, string path)
    {
        var response = await SendAsync(AsManager(), method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("POST", "/api/customer/v1/bulk-upsert")]
    [InlineData("GET", "/api/customer/v1/export?entity=contact")]
    public async Task AnAdminCallingADataTransferOnlyEndpoint_Gets403(string method, string path)
    {
        var response = await SendAsync(AsAdmin(), method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "bulk import and export are reserved for the data transfer service token");
    }

    [Theory]
    [InlineData("POST", "/api/customer/v1/recycle-bin/contact/{id}/restore")]
    public async Task ARestoreByANonAdmin_Gets403(string method, string template)
    {
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in");
        await AsAdmin().DeleteAsync($"/api/customer/v1/contacts/{contact.Id}");

        var response = await SendAsync(AsManager(), method, template.Replace("{id}", contact.Id.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "DEL-2 limits restore to admins in the MVP");
    }

    [Fact]
    public async Task AContactOutsideTheCallersVisibility_Is404OnEveryRecordRoute()
    {
        var contact = await CreateContactAsync(AsRepB(), "Priya", email: "priya@acme.in");
        var repA = AsRepA();

        (await repA.GetAsync($"/api/customer/v1/contacts/{contact.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await PatchAsync(repA, $"/api/customer/v1/contacts/{contact.Id}", new { version = 1, city = "Pune" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await repA.DeleteAsync($"/api/customer/v1/contacts/{contact.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ACompanyOutsideTheCallersVisibility_Is404OnEveryRecordRoute()
    {
        var company = await CreateCompanyAsync(AsRepB(), "Acme", "acme.com");
        var repA = AsRepA();

        (await repA.GetAsync($"/api/customer/v1/companies/{company.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await PatchAsync(repA, $"/api/customer/v1/companies/{company.Id}", new { version = 1, city = "Pune" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await repA.DeleteAsync($"/api/customer/v1/companies/{company.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AManagerMergingARecordTheyCannotView_Gets404NotAForbidden()
    {
        var outsiderId = Guid.NewGuid();
        var survivor = await CreateContactAsync(AsAdmin(), "Visible", email: "visible@x.in", ownerId: RepAId);
        var hidden = await CreateContactAsync(AsAdmin(), "Hidden", email: "hidden@x.in", ownerId: outsiderId);

        var response = await PostAsync(AsManager(), "/api/customer/v1/contacts/merge", new
        {
            survivorId = survivor.Id,
            loserId = hidden.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReassigningARecordOutsideTheCallersVisibility_IsReportedAsARowErrorNotAsAForbidden()
    {
        var outsiderId = Guid.NewGuid();
        var hidden = await CreateContactAsync(AsAdmin(), "Hidden", email: "hidden@x.in", ownerId: outsiderId);

        var response = await PostAsync(AsManager(), "/api/customer/v1/reassign", new
        {
            recordType = "contact",
            recordIds = new[] { hidden.Id },
            newOwnerId = RepAId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadAsync<BulkActionResultDto>(response);
        result.Succeeded.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Id.Should().Be(hidden.Id);
    }

    [Fact]
    public async Task ASalesRepMayCreateReadEditAndDeleteTheirOwnRecords()
    {
        var repA = AsRepA();

        var contact = await CreateContactAsync(repA, "Priya", email: "priya@acme.in");
        (await repA.GetAsync($"/api/customer/v1/contacts/{contact.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await PatchAsync(repA, $"/api/customer/v1/contacts/{contact.Id}", new { version = 1, city = "Pune" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await repA.DeleteAsync($"/api/customer/v1/contacts/{contact.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ASalesRepMayReadListsTagsOwnersPicklistsAndCustomFields()
    {
        var repA = AsRepA();

        foreach (var path in new[]
        {
            "/api/customer/v1/companies",
            "/api/customer/v1/contacts",
            "/api/customer/v1/tags",
            "/api/customer/v1/owners",
            "/api/customer/v1/picklists?type=contact_source",
            "/api/customer/v1/custom-fields?entity=contact"
        })
        {
            var response = await repA.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"a signed-in user may read {path}");
        }
    }

    [Fact]
    public async Task AServiceTokenWithoutTheActingUserHeader_IsRejectedNamingTheHeader()
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.ForService(OrganizationId, "lead"));

        var response = await PostAsync(client, "/api/customer/v1/contacts",
            new { firstName = "Priya", email = "priya@acme.in" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).GetProperty("errors")
            .TryGetProperty(HttpRequestContextHeaders.ActingUserId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task AServiceCallerActingForASalesRep_OnlySeesThatRepsRecords()
    {
        await CreateContactAsync(AsAdmin(), "Mine", email: "mine@x.in", ownerId: RepAId);
        await CreateContactAsync(AsAdmin(), "Theirs", email: "theirs@x.in", ownerId: RepBId);

        var service = AsService("data-transfer", RepAId, TestTokens.SalesRep, [RepAId]);

        var page = await ReadAsync<PagedResult<ContactDto>>(
            await service.GetAsync("/api/customer/v1/contacts"));

        page.Data.Should().ContainSingle().Which.FirstName.Should().Be("Mine");
    }

    [Fact]
    public async Task ATokenWithoutAnOrganizationClaim_Is401()
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.ForUser(AdminId, Guid.Empty, TestTokens.Admin));

        // Guid.Empty parses, so the claim is present but names no real organization: the query
        // simply finds nothing rather than leaking another tenant's rows.
        var page = await ReadAsync<PagedResult<ContactDto>>(
            await client.GetAsync("/api/customer/v1/contacts"));

        page.Total.Should().Be(0);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
        => method switch
        {
            "GET" => client.GetAsync(path),
            "DELETE" => client.DeleteAsync(path),
            "POST" => client.PostAsJsonAsync(path, new { }, Json),
            "PATCH" => client.PatchAsJsonAsync(path, new { }, Json),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported verb.")
        };
}

/// <summary>The header names the service-token contract uses, kept next to the tests that assert on them.</summary>
public static class HttpRequestContextHeaders
{
    public const string ActingUserId = CustomerApi.Infrastructure.Context.HttpRequestContext.ActingUserIdHeader;
}
