namespace CustomerApi.Tests.Integration.Api;
using System.Net;
using System.Text.Json;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Events;
using CustomerApi.Tests.Integration.TestSupport;
using FluentAssertions;

/// <summary>§9 acceptance criteria AC-1 to AC-9, end to end over HTTP against a real database.</summary>
public class AcceptanceCriteriaCoreTests(CustomerApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task AC1_SalesRepCreatesContactWithEmailOnly_IsSavedOwnedByThemAndPublishesContactCreated()
    {
        var response = await PostAsync(AsRepA(), "/api/customer/v1/contacts", new
        {
            firstName = "Priya",
            email = "priya@acme.in"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var contact = await ReadAsync<ContactDto>(response);
        contact.OwnerId.Should().Be(RepAId, "a sales rep becomes the owner of what they create (OWN-1)");
        contact.OwnerName.Should().Be("Rep A");
        contact.Version.Should().Be(1);

        var events = await OutboxAsync(contact.Id, EventTypes.ContactCreated);
        events.Should().HaveCount(1);

        var envelope = Envelope(events[0]);
        envelope.GetProperty("event_type").GetString().Should().Be("contact.created");
        envelope.GetProperty("organization_id").GetGuid().Should().Be(OrganizationId);
        envelope.GetProperty("version").GetInt32().Should().Be(1);
        envelope.GetProperty("actor_id").GetGuid().Should().Be(RepAId);
        Payload(events[0]).GetProperty("first_name").GetString().Should().Be("Priya");
    }

    [Fact]
    public async Task AC2_CreateContactWithoutEmailOrPhone_IsRejectedNamingTheMissingFields()
    {
        var response = await PostAsync(AsRepA(), "/api/customer/v1/contacts", new { firstName = "Priya" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await ProblemAsync(response);
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("email", out _).Should().BeTrue();
        errors.TryGetProperty("phone", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AC3_CreateContactWithAnExistingEmailInDifferentCase_IsRejectedNamingTheExistingContact()
    {
        var existing = await CreateContactAsync(AsRepA(), "Priya", "Sharma", "priya@acme.in");

        var response = await PostAsync(AsRepA(), "/api/customer/v1/contacts", new
        {
            firstName = "Priyanka",
            email = "Priya@ACME.in"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await ProblemAsync(response);
        var conflicting = problem.GetProperty("conflictingRecord");
        conflicting.GetProperty("id").GetGuid().Should().Be(existing.Id);
        conflicting.GetProperty("name").GetString().Should().Be("Priya Sharma");
    }

    [Fact]
    public async Task AC4_CreateCompanyWithADomainThatNormalisesOntoAnExistingOne_IsRejectedAsADuplicate()
    {
        var existing = await CreateCompanyAsync(AsRepA(), "Acme", "https://www.Acme.com/");
        existing.Domain.Should().Be("acme.com", "COM-4 strips the scheme and www. and lower-cases");

        var response = await PostAsync(AsRepA(), "/api/customer/v1/companies", new
        {
            name = "Acme Corporation",
            domain = "acme.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await ProblemAsync(response);
        problem.GetProperty("conflictingRecord").GetProperty("id").GetGuid().Should().Be(existing.Id);
        problem.GetProperty("conflictingRecord").GetProperty("name").GetString().Should().Be("Acme");
    }

    [Fact]
    public async Task AC5_CheckingAContactWhoseNumberMatchesAnother_WarnsWithoutBlockingTheSave()
    {
        var existing = await CreateContactAsync(AsRepA(), "Priya", "Sharma", phone: "+91 98765 43210");
        existing.PhoneNormalized.Should().Be("+919876543210", "CON-4 stores the E.164 form");

        var check = await PostAsync(AsRepA(), "/api/customer/v1/duplicates/check", new
        {
            entityType = "contact",
            firstName = "Priyanka",
            phone = "9876543210"
        });

        check.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadAsync<DuplicateCheckResponse>(check);
        result.HasBlocking.Should().BeFalse("a shared phone number is a warning, not a hard duplicate");
        result.Matches.Should().Contain(m => m.Id == existing.Id && m.Reason == "same_phone" && !m.Blocking);

        // DUP-2: the user may save anyway.
        var save = await PostAsync(AsRepA(), "/api/customer/v1/contacts", new
        {
            firstName = "Priyanka",
            phone = "9876543210"
        });

        save.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AC6_TwoUsersSavingTheSameContactAtVersion3_TheSecondGets409Conflict()
    {
        var contact = await CreateContactAsync(AsManager(), "Priya", email: "priya@acme.in");

        // Take the contact to version 3.
        var first = await PatchAsync(AsManager(), $"/api/customer/v1/contacts/{contact.Id}",
            new { version = 1, jobTitle = "Buyer" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await PatchAsync(AsManager(), $"/api/customer/v1/contacts/{contact.Id}",
            new { version = 2, jobTitle = "Senior Buyer" });
        (await ReadAsync<ContactDto>(second)).Version.Should().Be(3);

        var winner = await PatchAsync(AsManager(), $"/api/customer/v1/contacts/{contact.Id}",
            new { version = 3, city = "Pune" });
        winner.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<ContactDto>(winner)).Version.Should().Be(4);

        var loser = await PatchAsync(AsManager(), $"/api/customer/v1/contacts/{contact.Id}",
            new { version = 3, city = "Mumbai" });
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AC7_SalesRepOpensAContactOwnedByAnotherRep_Gets404NotFound()
    {
        var contact = await CreateContactAsync(AsRepB(), "Priya", email: "priya@acme.in");

        var response = await AsRepA().GetAsync($"/api/customer/v1/contacts/{contact.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a record outside the caller's visibility must not reveal its existence with a 403");
    }

    [Fact]
    public async Task AC8_ManagerListsContacts_SeesOwnTeamAndUnownedContactsOnly()
    {
        var outsiderId = Guid.NewGuid();

        await CreateContactAsync(AsAdmin(), "Own", email: "own@x.in", ownerId: ManagerId);
        await CreateContactAsync(AsAdmin(), "TeamA", email: "a@x.in", ownerId: RepAId);
        await CreateContactAsync(AsAdmin(), "TeamB", email: "b@x.in", ownerId: RepBId);
        await CreateContactAsync(AsAdmin(), "Unowned", email: "unowned@x.in");
        await CreateContactAsync(AsAdmin(), "Outsider", email: "outsider@x.in", ownerId: outsiderId);

        var response = await AsManager().GetAsync("/api/customer/v1/contacts?pageSize=200");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await ReadAsync<PagedResult<ContactDto>>(response);
        page.Data.Select(c => c.FirstName).Should().BeEquivalentTo(new[] { "Own", "TeamA", "TeamB", "Unowned" });
        page.Total.Should().Be(4);
    }

    [Fact]
    public async Task AC9_AdminMergesTwoDuplicateContacts_MarksTheLoserMergedAndPublishesContactMerged()
    {
        var survivor = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in", tags: ["vip"]);
        var loser = await CreateContactAsync(AsAdmin(), "Priya", "S", "p.sharma@acme.in", tags: ["conference"]);

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/contacts/merge", new
        {
            survivorId = survivor.Id,
            loserId = loser.Id,
            fieldChoices = new Dictionary<string, string> { ["lastName"] = "survivor" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadAsync<MergeResultDto>(response);
        result.SurvivorId.Should().Be(survivor.Id);
        result.LoserId.Should().Be(loser.Id);
        result.Tags.Should().BeEquivalentTo(new[] { "vip", "conference" }, "DUP-5 combines the tags");

        await Fixture.WithDbAsync(async db =>
        {
            var merged = await db.Contacts.FindAsync(loser.Id);
            merged!.MergedIntoId.Should().Be(survivor.Id);
            merged.DeletedAt.Should().NotBeNull();
        });

        // The loser is gone from lists and from the recycle bin (a merge cannot be undone, DUP-6).
        var list = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts"));
        list.Data.Should().NotContain(c => c.Id == loser.Id);

        var bin = await ReadAsync<PagedResult<RecycleBinItemDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/recycle-bin?type=contact"));
        bin.Data.Should().NotContain(i => i.Id == loser.Id);

        // DUP-5: the event other services use to re-point their links.
        var merges = await OutboxAsync(survivor.Id, EventTypes.ContactMerged);
        merges.Should().HaveCount(1);

        var payload = Payload(merges[0]);
        payload.GetProperty("survivor_id").GetGuid().Should().Be(survivor.Id);
        payload.GetProperty("loser_id").GetGuid().Should().Be(loser.Id);

        // The merge history row DUP-5 requires.
        var historyCount = await Fixture.ScalarAsync<long>(
            "SELECT count(*) FROM merge_history WHERE survivor_id = @s AND loser_id = @l",
            ("s", survivor.Id), ("l", loser.Id));
        historyCount.Should().Be(1);
    }

    [Fact]
    public async Task AC9_MergingCompanies_MovesTheLosersContactsToTheSurvivor()
    {
        var survivor = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com");
        var loser = await CreateCompanyAsync(AsAdmin(), "Acme Corp", "acme-corp.com");
        var moved = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in", companyId: loser.Id);

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/companies/merge", new
        {
            survivorId = survivor.Id,
            loserId = loser.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadAsync<MergeResultDto>(response);
        result.MovedContactIds.Should().BeEquivalentTo(new[] { moved.Id });

        var reloaded = await ReadAsync<ContactDto>(
            await AsAdmin().GetAsync($"/api/customer/v1/contacts/{moved.Id}"));
        reloaded.CompanyId.Should().Be(survivor.Id);

        var mergedEvents = await OutboxAsync(survivor.Id, EventTypes.CompanyMerged);
        Payload(mergedEvents.Single()).GetProperty("moved_contact_ids")
            .EnumerateArray().Select(e => e.GetGuid()).Should().BeEquivalentTo(new[] { moved.Id });
    }

    [Fact]
    public async Task MergeWhereTheSurvivorTakesTheLosersEmail_SucceedsDespiteTheUniqueIndex()
    {
        var survivor = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "old@acme.in");
        var loser = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in");

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/contacts/merge", new
        {
            survivorId = survivor.Id,
            loserId = loser.Id,
            fieldChoices = new Dictionary<string, string> { ["email"] = "loser" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var reloaded = await ReadAsync<ContactDto>(
            await AsAdmin().GetAsync($"/api/customer/v1/contacts/{survivor.Id}"));
        reloaded.Email.Should().Be("priya@acme.in");
    }

    [Fact]
    public async Task DeletingACompany_KeepsItsContactsButUnlinksThem()
    {
        var company = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com");
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in", companyId: company.Id);

        var deleted = await AsAdmin().DeleteAsync($"/api/customer/v1/companies/{company.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reloaded = await ReadAsync<ContactDto>(
            await AsAdmin().GetAsync($"/api/customer/v1/contacts/{contact.Id}"));
        reloaded.CompanyId.Should().BeNull("COM-5 keeps the contact and unlinks it");

        // The unlink is a change, so consumers hear about it.
        var updates = await OutboxAsync(contact.Id, EventTypes.ContactUpdated);
        updates.Should().HaveCount(1);
        Payload(updates[0]).GetProperty("changes").EnumerateArray()
            .Select(c => c.GetProperty("field").GetString())
            .Should().Contain("company_id");
    }

    [Fact]
    public async Task UpdatingACompany_PublishesTheFullRecordAndTheChangedFieldsWithOldAndNewValues()
    {
        var company = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com");

        var response = await PatchAsync(AsAdmin(), $"/api/customer/v1/companies/{company.Id}",
            new { version = 1, name = "Acme Limited", city = "Pune" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updates = await OutboxAsync(company.Id, EventTypes.CompanyUpdated);
        var payload = Payload(updates.Single());

        payload.GetProperty("record").GetProperty("name").GetString().Should().Be("Acme Limited");

        var changes = payload.GetProperty("changes").EnumerateArray().ToList();
        var nameChange = changes.Single(c => c.GetProperty("field").GetString() == "name");
        nameChange.GetProperty("old_value").GetString().Should().Be("Acme");
        nameChange.GetProperty("new_value").GetString().Should().Be("Acme Limited");
        changes.Should().Contain(c => c.GetProperty("field").GetString() == "city");
    }

    [Fact]
    public async Task EveryWrite_StoresItsEventInTheSameTransaction()
    {
        var before = await Fixture.ScalarAsync<long>("SELECT count(*) FROM outbox_events");
        before.Should().Be(0);

        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in");
        await PatchAsync(AsAdmin(), $"/api/customer/v1/contacts/{contact.Id}", new { version = 1, city = "Pune" });
        await AsAdmin().DeleteAsync($"/api/customer/v1/contacts/{contact.Id}");

        var types = (await OutboxAsync(contact.Id)).Select(e => e.EventType).ToList();
        types.Should().Equal(new[] { EventTypes.ContactCreated, EventTypes.ContactUpdated, EventTypes.ContactDeleted });

        // NFR-5: nothing is published from request code — the relay has not run, so nothing is marked.
        (await OutboxAsync(contact.Id)).Should().OnlyContain(e => e.PublishedAt == null);
    }

    [Fact]
    public async Task OrganizationIdInTheRequestBody_IsIgnored()
    {
        var foreignOrganization = Guid.NewGuid();

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/companies", new
        {
            name = "Acme",
            organizationId = foreignOrganization
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync<CompanyDto>(response)).OrganizationId.Should().Be(OrganizationId,
            "organization_id comes only from the token (NFR-6)");
    }

    [Fact]
    public async Task ARecordFromAnotherOrganization_IsNotVisible()
    {
        var mine = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com");

        var otherAdmin = As(Guid.NewGuid(), TestTokens.Admin, organizationId: OtherOrganizationId);

        (await otherAdmin.GetAsync($"/api/customer/v1/companies/{mine.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var theirList = await ReadAsync<PagedResult<CompanyDto>>(
            await otherAdmin.GetAsync("/api/customer/v1/companies"));
        theirList.Total.Should().Be(0);

        // ... and the same domain is free in the other organization.
        var created = await PostAsync(otherAdmin, "/api/customer/v1/companies",
            new { name = "Acme", domain = "acme.com" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ASalesRepTryingToGiveARecordToSomeoneElse_IsForbidden()
    {
        var created = await PostAsync(AsRepA(), "/api/customer/v1/companies",
            new { name = "Acme", ownerId = RepBId });

        created.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var mine = await CreateCompanyAsync(AsRepA(), "Acme");
        var reassigned = await PatchAsync(AsRepA(), $"/api/customer/v1/companies/{mine.Id}",
            new { version = 1, ownerId = RepBId });

        reassigned.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task QuickSearch_MatchesPartialAndSlightlyMisspelledNames()
    {
        await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in");
        await CreateContactAsync(AsAdmin(), "Rahul", "Verma", "rahul@acme.in");

        var partial = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts?q=shar"));
        partial.Data.Should().ContainSingle().Which.FirstName.Should().Be("Priya");

        var misspelled = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts?q=Priya%20Sharna"));
        misspelled.Data.Should().ContainSingle().Which.FirstName.Should().Be("Priya");

        var byEmail = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts?q=rahul@"));
        byEmail.Data.Should().ContainSingle().Which.FirstName.Should().Be("Rahul");
    }

    [Fact]
    public async Task ListPageSize_DefaultsTo50AndIsCappedAt200()
    {
        var page = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts"));
        page.PageSize.Should().Be(50);

        var capped = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts?pageSize=500"));
        capped.PageSize.Should().Be(200);

        var explicitSize = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts?pageSize=200"));
        explicitSize.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task Tags_AreStoredLowerCaseTrimmedAndSuggestedByPrefix()
    {
        await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com", tags: ["  VIP ", "Key-Account"]);
        await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in", tags: ["vip", "newsletter"]);

        var suggestions = await ReadAsync<List<string>>(
            await AsAdmin().GetAsync("/api/customer/v1/tags?prefix=v"));
        suggestions.Should().Equal(new[] { "vip" });

        var all = await ReadAsync<List<string>>(await AsAdmin().GetAsync("/api/customer/v1/tags"));
        all.Should().BeEquivalentTo(new[] { "vip", "key-account", "newsletter" });

        var filtered = await ReadAsync<PagedResult<CompanyDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/companies?tags=vip&tags=key-account"));
        filtered.Data.Should().ContainSingle().Which.Tags.Should().BeEquivalentTo(new[] { "vip", "key-account" });
    }

    [Fact]
    public async Task TooManyTags_AreRejectedNamingTheField()
    {
        var tooMany = Enumerable.Range(1, 21).Select(i => $"tag{i}").ToArray();

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/companies",
            new { name = "Acme", tags = tooMany });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).GetProperty("errors").TryGetProperty("tags", out _).Should().BeTrue();
    }

    [Fact]
    public async Task IndustryAndSource_AcceptAPicklistIdAndReturnItsLabel()
    {
        var industryId = await SeedPicklistAsync("company_industry", "Technology");
        var sourceId = await SeedPicklistAsync("contact_source", "Referral");

        var company = await ReadAsync<CompanyDto>(await PostAsync(AsAdmin(), "/api/customer/v1/companies",
            new { name = "Acme", industryId }));
        company.IndustryId.Should().Be(industryId);
        company.Industry.Should().Be("Technology");

        var contact = await ReadAsync<ContactDto>(await PostAsync(AsAdmin(), "/api/customer/v1/contacts",
            new { firstName = "Priya", email = "priya@acme.in", sourceId }));
        contact.SourceId.Should().Be(sourceId);
        contact.Source.Should().Be("Referral");
    }

    [Fact]
    public async Task AnIndustryIdFromAnotherList_IsRejectedNamingTheField()
    {
        var sourceId = await SeedPicklistAsync("contact_source", "Referral");

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/companies",
            new { name = "Acme", industryId = sourceId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).GetProperty("errors")
            .TryGetProperty("industryId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CompanyDetail_CarriesItsContacts()
    {
        var company = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com");
        await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in", companyId: company.Id);

        var detail = await ReadAsync<CompanyDetailDto>(
            await AsAdmin().GetAsync($"/api/customer/v1/companies/{company.Id}"));

        detail.Company.Id.Should().Be(company.Id);
        detail.Contacts.Should().ContainSingle().Which.Email.Should().Be("priya@acme.in");
    }

    [Fact]
    public async Task InvalidStandardFields_AreRejectedOneResponseAtATimeNamingEveryBadField()
    {
        var response = await PostAsync(AsAdmin(), "/api/customer/v1/companies", new
        {
            name = "Acme",
            website = "acme.com",
            gstin = "NOTAGSTIN",
            country = "India",
            domain = "not a domain"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errors = (await ProblemAsync(response)).GetProperty("errors");
        errors.TryGetProperty("website", out _).Should().BeTrue();
        errors.TryGetProperty("gstin", out _).Should().BeTrue();
        errors.TryGetProperty("country", out _).Should().BeTrue();
        errors.TryGetProperty("domain", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AnIndianPostalCodeMustBeSixDigits()
    {
        var bad = await PostAsync(AsAdmin(), "/api/customer/v1/companies",
            new { name = "Acme", country = "IN", postalCode = "12" });

        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var good = await PostAsync(AsAdmin(), "/api/customer/v1/companies",
            new { name = "Acme", country = "IN", postalCode = "411001" });

        good.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task APatchThatChangesNothing_DoesNotBumpTheVersionOrPublishAnEvent()
    {
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in");

        var response = await PatchAsync(AsAdmin(), $"/api/customer/v1/contacts/{contact.Id}",
            new { version = 1, firstName = "Priya" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<ContactDto>(response)).Version.Should().Be(1);
        (await OutboxAsync(contact.Id, EventTypes.ContactUpdated)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnExplicitNullInAPatch_ClearsTheFieldWhileAnAbsentOneIsLeftAlone()
    {
        var contact = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in", phone: "9876543210");

        var cleared = await ReadAsync<ContactDto>(await PatchAsync(
            AsAdmin(), $"/api/customer/v1/contacts/{contact.Id}",
            new { version = 1, lastName = (string?)null }));

        cleared.LastName.Should().BeNull();
        cleared.Phone.Should().Be("9876543210", "a field the body does not mention is untouched");
    }

    [Fact]
    public async Task ReassigningRecordsInBulk_PublishesOneReassignedEventPerRecord()
    {
        var first = await CreateContactAsync(AsAdmin(), "One", email: "one@x.in", ownerId: RepAId);
        var second = await CreateContactAsync(AsAdmin(), "Two", email: "two@x.in", ownerId: RepAId);

        var response = await PostAsync(AsManager(), "/api/customer/v1/reassign", new
        {
            recordType = "contact",
            recordIds = new[] { first.Id, second.Id },
            newOwnerId = RepBId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<BulkActionResultDto>(response)).Succeeded.Should().Be(2);

        foreach (var id in new[] { first.Id, second.Id })
        {
            var events = await OutboxAsync(id, EventTypes.ContactReassigned);
            events.Should().HaveCount(1, "a bulk change publishes one event per record, not one per batch");

            var payload = Payload(events[0]);
            payload.GetProperty("from_owner_id").GetGuid().Should().Be(RepAId);
            payload.GetProperty("to_owner_id").GetGuid().Should().Be(RepBId);
        }
    }

    [Fact]
    public async Task ReassigningMoreRecordsThanTheLimit_IsRejected()
    {
        var tooMany = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray();

        var response = await PostAsync(AsManager(), "/api/customer/v1/reassign", new
        {
            recordType = "contact",
            recordIds = tooMany,
            newOwnerId = RepBId
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).GetProperty("errors")
            .TryGetProperty("recordIds", out _).Should().BeTrue();
    }

    [Fact]
    public async Task BulkDeletingMoreRecordsThanTheLimit_IsRejected()
    {
        var tooMany = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray();

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/bulk-delete", new
        {
            recordType = "contact",
            recordIds = tooMany
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkDelete_SoftDeletesEachRecordAndPublishesOneEventEach()
    {
        var first = await CreateContactAsync(AsAdmin(), "One", email: "one@x.in");
        var second = await CreateContactAsync(AsAdmin(), "Two", email: "two@x.in");

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/bulk-delete", new
        {
            recordType = "contact",
            recordIds = new[] { first.Id, second.Id }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<BulkActionResultDto>(response)).Succeeded.Should().Be(2);

        (await OutboxAsync(first.Id, EventTypes.ContactDeleted)).Should().HaveCount(1);
        (await OutboxAsync(second.Id, EventTypes.ContactDeleted)).Should().HaveCount(1);

        var list = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts"));
        list.Total.Should().Be(0);
    }

    [Fact]
    public async Task Owners_AreServedFromTheLocalUserRefsCopy()
    {
        var owners = await ReadAsync<List<OwnerDto>>(await AsRepA().GetAsync("/api/customer/v1/owners"));

        owners.Select(o => o.DisplayName).Should().Contain(new[] { "Admin One", "Manager North", "Rep A", "Rep B" });
        owners.Should().OnlyContain(o => o.IsActive);
    }

    [Fact]
    public async Task DuplicateCheckForACompanyName_WarnsWithoutBlocking()
    {
        var existing = await CreateCompanyAsync(AsAdmin(), "Acme Industries", "acme.com");

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/duplicates/check", new
        {
            entityType = "company",
            name = "Acme Industrie"
        });

        var result = await ReadAsync<DuplicateCheckResponse>(response);
        result.HasBlocking.Should().BeFalse();
        result.Matches.Should().Contain(m => m.Id == existing.Id && m.Reason == "similar_name");
    }

    [Fact]
    public async Task DuplicateCheckForAnEmailAlreadyInUse_ReportsABlockingMatch()
    {
        var existing = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in");

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/duplicates/check", new
        {
            entityType = "contact",
            email = "PRIYA@acme.in"
        });

        var result = await ReadAsync<DuplicateCheckResponse>(response);
        result.HasBlocking.Should().BeTrue();
        result.Matches.Should().Contain(m => m.Id == existing.Id && m.Reason == "same_email" && m.Blocking);
    }

    [Fact]
    public async Task CustomFieldFiltersOnAList_MatchStoredValues()
    {
        var definition = await ReadAsync<CustomFieldDefinitionDto>(await PostAsync(
            AsAdmin(), "/api/customer/v1/custom-fields", new
            {
                entityType = "contact",
                fieldKey = "region",
                label = "Region",
                fieldType = "select",
                options = new[] { "north", "south" }
            }));

        definition.FieldKey.Should().Be("region");

        await PostAsync(AsAdmin(), "/api/customer/v1/contacts", new
        {
            firstName = "North",
            email = "north@x.in",
            customFields = new Dictionary<string, object> { ["region"] = "north" }
        });

        await PostAsync(AsAdmin(), "/api/customer/v1/contacts", new
        {
            firstName = "South",
            email = "south@x.in",
            customFields = new Dictionary<string, object> { ["region"] = "south" }
        });

        var filtered = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts?customField=region:north"));

        filtered.Data.Should().ContainSingle().Which.FirstName.Should().Be("North");
    }

    [Fact]
    public async Task SortingByOwner_UsesTheOwnerNameFromTheLocalCopy()
    {
        await CreateContactAsync(AsAdmin(), "ForRepB", email: "b@x.in", ownerId: RepBId);
        await CreateContactAsync(AsAdmin(), "ForAdmin", email: "a@x.in", ownerId: AdminId);

        var ascending = await ReadAsync<PagedResult<ContactDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/contacts?sort=owner&direction=asc"));

        ascending.Data.Select(c => c.OwnerName).Should().Equal(new[] { "Admin One", "Rep B" });
    }

    [Fact]
    public async Task HealthCheck_IsServedWithoutATokenAndDoesNotDependOnTheBroker()
    {
        var response = await Anonymous().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "NFR-9: a broker outage must not fail the health probe");
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
