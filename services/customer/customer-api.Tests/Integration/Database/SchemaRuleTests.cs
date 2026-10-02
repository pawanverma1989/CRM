namespace CustomerApi.Tests.Integration.Database;
using CustomerApi.Tests.Integration.TestSupport;
using FluentAssertions;

/// <summary>
/// The rules the database itself enforces (CLAUDE.md "Working in this repo", architecture §9.4).
/// These run against V1 + V2 applied to a fresh database as <c>customer_svc</c>, so they also prove
/// the migrations apply cleanly.
/// </summary>
public class SchemaRuleTests(CustomerApiFixture fixture) : ApiTestBase(fixture)
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string NotNullViolation = "23502";

    private Task<int> InsertContactAsync(
        Guid id, string firstName, string? email = null, string? phone = null,
        Guid? sourceLeadId = null, string[]? tags = null, string? country = null)
        => Fixture.ExecuteAsync(
            """
            INSERT INTO contacts (id, organization_id, first_name, email, phone, source_lead_id, tags, country)
            VALUES (@id, @org, @first, @email, @phone, @lead, @tags, @country)
            """,
            ("id", id),
            ("org", OrganizationId),
            ("first", firstName),
            ("email", (object?)email ?? DBNull.Value),
            ("phone", (object?)phone ?? DBNull.Value),
            ("lead", (object?)sourceLeadId ?? DBNull.Value),
            ("tags", tags ?? []),
            ("country", (object?)country ?? DBNull.Value));

    private Task<int> InsertCompanyAsync(
        Guid id, string name, string? domain = null, string[]? tags = null,
        string? website = null, string? gstin = null, string? country = null, int? employeeCount = null)
        => Fixture.ExecuteAsync(
            """
            INSERT INTO companies (id, organization_id, name, domain, tags, website, gstin, country, employee_count)
            VALUES (@id, @org, @name, @domain, @tags, @website, @gstin, @country, @employees)
            """,
            ("id", id),
            ("org", OrganizationId),
            ("name", name),
            ("domain", (object?)domain ?? DBNull.Value),
            ("tags", tags ?? []),
            ("website", (object?)website ?? DBNull.Value),
            ("gstin", (object?)gstin ?? DBNull.Value),
            ("country", (object?)country ?? DBNull.Value),
            ("employees", (object?)employeeCount ?? DBNull.Value));

    [Fact]
    public async Task ChkContactsEmailOrPhone_RefusesAContactWithNeither()
    {
        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO contacts (organization_id, first_name) VALUES (@org, 'Priya')",
            ("org", OrganizationId));

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("chk_contacts_email_or_phone");
    }

    [Fact]
    public async Task ChkContactsEmailOrPhone_AcceptsEitherOne()
    {
        await InsertContactAsync(Guid.NewGuid(), "WithEmail", email: "a@acme.in");
        await InsertContactAsync(Guid.NewGuid(), "WithPhone", phone: "9876543210");

        (await Fixture.ScalarAsync<long>("SELECT count(*) FROM contacts")).Should().Be(2);
    }

    [Fact]
    public async Task UqContactsEmail_RefusesASecondLiveContactWithTheSameEmailIgnoringCase()
    {
        await InsertContactAsync(Guid.NewGuid(), "Priya", email: "priya@acme.in");

        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO contacts (organization_id, first_name, email) VALUES (@org, 'Priyanka', 'PRIYA@ACME.IN')",
            ("org", OrganizationId));

        error.SqlState.Should().Be(UniqueViolation);
        error.ConstraintName.Should().Be("uq_contacts_email");
    }

    [Fact]
    public async Task UqContactsEmail_IsNotBlockedByADeletedOrAMergedCopy()
    {
        var deleted = Guid.NewGuid();
        var merged = Guid.NewGuid();
        var survivor = Guid.NewGuid();

        await InsertContactAsync(survivor, "Survivor", email: "survivor@acme.in");
        await InsertContactAsync(deleted, "Deleted", email: "shared@acme.in");
        await Fixture.ExecuteAsync("UPDATE contacts SET deleted_at = now() WHERE id = @id", ("id", deleted));

        // A deleted record frees its email.
        await InsertContactAsync(merged, "Merged", email: "shared@acme.in");
        await Fixture.ExecuteAsync(
            "UPDATE contacts SET deleted_at = now(), merged_into_id = @s WHERE id = @id",
            ("s", survivor), ("id", merged));

        // So does a merged one.
        await InsertContactAsync(Guid.NewGuid(), "Live", email: "shared@acme.in");

        (await Fixture.ScalarAsync<long>(
            "SELECT count(*) FROM contacts WHERE email = 'shared@acme.in'")).Should().Be(3);
    }

    [Fact]
    public async Task UqContactsEmail_IsScopedToTheOrganization()
    {
        await InsertContactAsync(Guid.NewGuid(), "Mine", email: "priya@acme.in");

        await Fixture.ExecuteAsync(
            "INSERT INTO contacts (organization_id, first_name, email) VALUES (@org, 'Theirs', 'priya@acme.in')",
            ("org", OtherOrganizationId));

        (await Fixture.ScalarAsync<long>("SELECT count(*) FROM contacts")).Should().Be(2);
    }

    [Fact]
    public async Task UqCompaniesDomain_RefusesASecondLiveCompanyWithTheSameDomain()
    {
        await InsertCompanyAsync(Guid.NewGuid(), "Acme", "acme.com");

        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO companies (organization_id, name, domain) VALUES (@org, 'Acme Ltd', 'ACME.COM')",
            ("org", OrganizationId));

        error.SqlState.Should().Be(UniqueViolation);
        error.ConstraintName.Should().Be("uq_companies_domain");
    }

    [Fact]
    public async Task UqContactsSourceLead_RefusesTwoContactsForTheSameConvertedLead()
    {
        var leadId = Guid.NewGuid();
        await InsertContactAsync(Guid.NewGuid(), "First", email: "first@acme.in", sourceLeadId: leadId);

        var error = await Fixture.ExpectRejectionAsync(
            """
            INSERT INTO contacts (organization_id, first_name, email, source_lead_id)
            VALUES (@org, 'Second', 'second@acme.in', @lead)
            """,
            ("org", OrganizationId), ("lead", leadId));

        error.SqlState.Should().Be(UniqueViolation);
        error.ConstraintName.Should().Be("uq_contacts_source_lead");
    }

    [Theory]
    [InlineData("VIP")]
    [InlineData(" vip")]
    [InlineData("")]
    public async Task TagsAreValid_RefusesTagsThatAreNotTrimmedLowerCaseAndNonEmpty(string tag)
    {
        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO companies (organization_id, name, tags) VALUES (@org, 'Acme', @tags)",
            ("org", OrganizationId), ("tags", new[] { tag }));

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("chk_companies_tags");
    }

    [Fact]
    public async Task TagsAreValid_RefusesATagLongerThan40CharactersAndMoreThan20PerRecord()
    {
        var tooLong = await Fixture.ExpectRejectionAsync(
            "INSERT INTO companies (organization_id, name, tags) VALUES (@org, 'Acme', @tags)",
            ("org", OrganizationId), ("tags", new[] { new string('a', 41) }));
        tooLong.SqlState.Should().Be(CheckViolation);

        var tooMany = await Fixture.ExpectRejectionAsync(
            "INSERT INTO contacts (organization_id, first_name, email, tags) VALUES (@org, 'Priya', 'p@acme.in', @tags)",
            ("org", OrganizationId),
            ("tags", Enumerable.Range(1, 21).Select(i => $"tag{i}").ToArray()));
        tooMany.SqlState.Should().Be(CheckViolation);
        tooMany.ConstraintName.Should().Be("chk_contacts_tags");
    }

    [Fact]
    public async Task ChkCompaniesGstin_RefusesAMalformedGstin()
    {
        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO companies (organization_id, name, gstin) VALUES (@org, 'Acme', '27ABCDE')",
            ("org", OrganizationId));

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("chk_companies_gstin");

        await InsertCompanyAsync(Guid.NewGuid(), "Acme", gstin: "27AAPFU0939F1ZV");
        (await Fixture.ScalarAsync<long>("SELECT count(*) FROM companies")).Should().Be(1);
    }

    [Fact]
    public async Task ChkCompaniesWebsite_RequiresAnHttpScheme()
    {
        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO companies (organization_id, name, website) VALUES (@org, 'Acme', 'acme.com')",
            ("org", OrganizationId));

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("chk_companies_website");

        await InsertCompanyAsync(Guid.NewGuid(), "Acme", website: "https://acme.com");
    }

    [Theory]
    [InlineData("companies", "chk_companies_country")]
    [InlineData("contacts", "chk_contacts_country")]
    public async Task ChkCountry_RequiresTwoUpperCaseLetters(string table, string constraint)
    {
        var sql = table == "companies"
            ? "INSERT INTO companies (organization_id, name, country) VALUES (@org, 'Acme', 'India')"
            : "INSERT INTO contacts (organization_id, first_name, email, country) VALUES (@org, 'Priya', 'p@acme.in', 'in')";

        var error = await Fixture.ExpectRejectionAsync(sql, ("org", OrganizationId));

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task Country_DefaultsToIndia()
    {
        await Fixture.ExecuteAsync(
            "INSERT INTO companies (organization_id, name) VALUES (@org, 'Acme')", ("org", OrganizationId));
        await Fixture.ExecuteAsync(
            "INSERT INTO contacts (organization_id, first_name, phone) VALUES (@org, 'Priya', '9876543210')",
            ("org", OrganizationId));

        (await Fixture.ScalarAsync<string>("SELECT country FROM companies")).Should().Be("IN");
        (await Fixture.ScalarAsync<string>("SELECT country FROM contacts")).Should().Be("IN");
    }

    [Fact]
    public async Task EmployeeCount_CannotBeNegative()
    {
        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO companies (organization_id, name, employee_count) VALUES (@org, 'Acme', -1)",
            ("org", OrganizationId));

        error.SqlState.Should().Be(CheckViolation);
    }

    [Fact]
    public async Task BumpVersion_RaisesTheVersionWhenTheCallerLeavesItAlone()
    {
        var id = Guid.NewGuid();
        await InsertCompanyAsync(id, "Acme");

        (await Fixture.ScalarAsync<int>("SELECT version FROM companies WHERE id = @id", ("id", id)))
            .Should().Be(1);

        await Fixture.ExecuteAsync("UPDATE companies SET city = 'Pune' WHERE id = @id", ("id", id));

        (await Fixture.ScalarAsync<int>("SELECT version FROM companies WHERE id = @id", ("id", id)))
            .Should().Be(2, "a raw update still gets a correct version for event ordering");
    }

    [Fact]
    public async Task BumpVersion_LeavesTheApplicationsOwnVersionAlone()
    {
        var id = Guid.NewGuid();
        await InsertContactAsync(id, "Priya", email: "priya@acme.in");

        // Optimistic locking, as the application writes it.
        var affected = await Fixture.ExecuteAsync(
            "UPDATE contacts SET city = 'Pune', version = 2 WHERE id = @id AND version = 1", ("id", id));

        affected.Should().Be(1);
        (await Fixture.ScalarAsync<int>("SELECT version FROM contacts WHERE id = @id", ("id", id)))
            .Should().Be(2, "the trigger must not double-bump and leave the in-memory copy stale");
    }

    [Fact]
    public async Task SetUpdatedAt_MovesUpdatedAtForwardOnEveryUpdate()
    {
        var id = Guid.NewGuid();
        await InsertCompanyAsync(id, "Acme");

        await Fixture.ExecuteAsync(
            "UPDATE companies SET updated_at = timestamptz '2000-01-01', city = 'Pune' WHERE id = @id",
            ("id", id));

        var updatedAt = await Fixture.ScalarAsync<DateTime>(
            "SELECT updated_at FROM companies WHERE id = @id", ("id", id));

        updatedAt.Year.Should().BeGreaterThan(2000, "the trigger overrides whatever the caller sent");
    }

    [Fact]
    public async Task UqReassignmentOpen_AllowsOneOpenEntryPerRecordAndANewOneOnceResolved()
    {
        var recordId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        await Fixture.ExecuteAsync(
            """
            INSERT INTO reassignment_queue (organization_id, record_type, record_id, previous_owner_id)
            VALUES (@org, 'contact', @record, @owner)
            """,
            ("org", OrganizationId), ("record", recordId), ("owner", ownerId));

        var error = await Fixture.ExpectRejectionAsync(
            """
            INSERT INTO reassignment_queue (organization_id, record_type, record_id, previous_owner_id)
            VALUES (@org, 'contact', @record, @owner)
            """,
            ("org", OrganizationId), ("record", recordId), ("owner", ownerId));

        error.SqlState.Should().Be(UniqueViolation);
        error.ConstraintName.Should().Be("uq_reassignment_open");

        await Fixture.ExecuteAsync(
            "UPDATE reassignment_queue SET resolved_at = now() WHERE record_id = @record", ("record", recordId));

        await Fixture.ExecuteAsync(
            """
            INSERT INTO reassignment_queue (organization_id, record_type, record_id, previous_owner_id)
            VALUES (@org, 'contact', @record, @owner)
            """,
            ("org", OrganizationId), ("record", recordId), ("owner", ownerId));

        (await Fixture.ScalarAsync<long>("SELECT count(*) FROM reassignment_queue")).Should().Be(2);
    }

    [Fact]
    public async Task ContactsCompanyId_IsSetToNullWhenTheCompanyRowIsDeleted()
    {
        var companyId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        await InsertCompanyAsync(companyId, "Acme", "acme.com");
        await Fixture.ExecuteAsync(
            """
            INSERT INTO contacts (id, organization_id, company_id, first_name, email)
            VALUES (@id, @org, @company, 'Priya', 'priya@acme.in')
            """,
            ("id", contactId), ("org", OrganizationId), ("company", companyId));

        await Fixture.ExecuteAsync("DELETE FROM companies WHERE id = @id", ("id", companyId));

        var orphaned = await Fixture.ScalarAsync<long>(
            "SELECT count(*) FROM contacts WHERE id = @id AND company_id IS NULL", ("id", contactId));
        orphaned.Should().Be(1);
    }

    [Theory]
    [InlineData("INSERT INTO picklists (organization_id, list_type, value) VALUES (@org, 'not_a_list', 'X')")]
    [InlineData("INSERT INTO picklists (organization_id, list_type, value) VALUES (@org, 'company_industry', '   ')")]
    public async Task Picklists_EnforceTheirListTypeAndValueChecks(string sql)
    {
        var error = await Fixture.ExpectRejectionAsync(sql, ("org", OrganizationId));
        error.SqlState.Should().Be(CheckViolation);
    }

    [Fact]
    public async Task Picklists_AreUniquePerOrganizationAndListType()
    {
        await Fixture.ExecuteAsync(
            "INSERT INTO picklists (organization_id, list_type, value) VALUES (@org, 'company_industry', 'Technology')",
            ("org", OrganizationId));

        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO picklists (organization_id, list_type, value) VALUES (@org, 'company_industry', 'Technology')",
            ("org", OrganizationId));

        error.SqlState.Should().Be(UniqueViolation);

        // The same value in the other list, and in another organization, is fine.
        await Fixture.ExecuteAsync(
            "INSERT INTO picklists (organization_id, list_type, value) VALUES (@org, 'contact_source', 'Technology')",
            ("org", OrganizationId));
        await Fixture.ExecuteAsync(
            "INSERT INTO picklists (organization_id, list_type, value) VALUES (@org, 'company_industry', 'Technology')",
            ("org", OtherOrganizationId));
    }

    [Theory]
    [InlineData("Region", "text")]
    [InlineData("region one", "text")]
    [InlineData("region", "colour")]
    public async Task CustomFieldDefinitions_EnforceTheKeyFormatAndTheTypeList(string key, string type)
    {
        var error = await Fixture.ExpectRejectionAsync(
            """
            INSERT INTO custom_field_definitions (organization_id, entity_type, field_key, label, field_type)
            VALUES (@org, 'contact', @key, 'Region', @type)
            """,
            ("org", OrganizationId), ("key", key), ("type", type));

        error.SqlState.Should().Be(CheckViolation);
    }

    [Fact]
    public async Task CustomFieldDefinitions_AreUniquePerOrganizationEntityAndKey()
    {
        await Fixture.ExecuteAsync(
            """
            INSERT INTO custom_field_definitions (organization_id, entity_type, field_key, label, field_type)
            VALUES (@org, 'contact', 'region', 'Region', 'text')
            """,
            ("org", OrganizationId));

        var error = await Fixture.ExpectRejectionAsync(
            """
            INSERT INTO custom_field_definitions (organization_id, entity_type, field_key, label, field_type)
            VALUES (@org, 'contact', 'region', 'Region again', 'text')
            """,
            ("org", OrganizationId));

        error.SqlState.Should().Be(UniqueViolation);

        // The same key on the other record type is a different field.
        await Fixture.ExecuteAsync(
            """
            INSERT INTO custom_field_definitions (organization_id, entity_type, field_key, label, field_type)
            VALUES (@org, 'company', 'region', 'Region', 'text')
            """,
            ("org", OrganizationId));
    }

    [Fact]
    public async Task MergeHistory_OnlyAcceptsTheTwoKnownEntityTypes()
    {
        var error = await Fixture.ExpectRejectionAsync(
            """
            INSERT INTO merge_history (organization_id, entity_type, survivor_id, loser_id)
            VALUES (@org, 'deal', @a, @b)
            """,
            ("org", OrganizationId), ("a", Guid.NewGuid()), ("b", Guid.NewGuid()));

        error.SqlState.Should().Be(CheckViolation);
    }

    [Fact]
    public async Task ProcessedEvents_RejectsTheSameEventIdTwice()
    {
        var eventId = Guid.NewGuid();

        await Fixture.ExecuteAsync(
            "INSERT INTO processed_events (event_id, event_type) VALUES (@id, 'user.updated')", ("id", eventId));

        var error = await Fixture.ExpectRejectionAsync(
            "INSERT INTO processed_events (event_id, event_type) VALUES (@id, 'user.updated')", ("id", eventId));

        error.SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task OutboxEvents_RequireAPayload()
    {
        var error = await Fixture.ExpectRejectionAsync(
            """
            INSERT INTO outbox_events (organization_id, aggregate_type, aggregate_id, event_type)
            VALUES (@org, 'contact', @id, 'contact.created')
            """,
            ("org", OrganizationId), ("id", Guid.NewGuid()));

        error.SqlState.Should().Be(NotNullViolation);
    }

    [Fact]
    public async Task TheRequiredExtensionsAreInstalled()
    {
        var extensions = await Fixture.ScalarAsync<long>(
            "SELECT count(*) FROM pg_extension WHERE extname IN ('pgcrypto', 'citext', 'pg_trgm')");

        extensions.Should().Be(3);
    }

    [Fact]
    public async Task TrigramSimilarity_IsAvailableForFuzzyNameMatching()
    {
        var similarity = await Fixture.ScalarAsync<float>("SELECT similarity('Priya Sharma', 'Priya Sharna')");

        similarity.Should().BeGreaterThan(0.4f);
    }
}
