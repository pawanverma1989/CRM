namespace CustomerApi.Tests.Integration.Api;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Events;
using CustomerApi.Application.Services;
using CustomerApi.Tests.Integration.TestSupport;
using FluentAssertions;

/// <summary>§9 acceptance criteria AC-10 to AC-18: custom fields, ownership, the recycle bin, the
/// nightly purge, idempotent lead conversion, erasure and bulk import.</summary>
public class AcceptanceCriteriaLifecycleTests(CustomerApiFixture fixture) : ApiTestBase(fixture)
{
    private async Task<CustomFieldDefinitionDto> CreateRegionFieldAsync(bool required = true)
    {
        var response = await PostAsync(AsAdmin(), "/api/customer/v1/custom-fields", new
        {
            entityType = "contact",
            fieldKey = "region",
            label = "Region",
            fieldType = "select",
            options = new[] { "north", "south" },
            isRequired = required,
            sortOrder = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadAsync<CustomFieldDefinitionDto>(response);
    }

    [Fact]
    public async Task AC10_SavingAContactWithAValueOutsideASelectFieldsOptions_IsRejectedNamingTheField()
    {
        await CreateRegionFieldAsync();

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/contacts", new
        {
            firstName = "Priya",
            email = "priya@acme.in",
            customFields = new Dictionary<string, object> { ["region"] = "west" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errors = (await ProblemAsync(response)).GetProperty("errors");
        errors.TryGetProperty("customFields.region", out var messages).Should().BeTrue(
            "CF-3 requires the offending field to be named");
        messages[0].GetString().Should().Contain("west");
    }

    [Fact]
    public async Task AC10_ARequiredCustomFieldMissingOnANewContact_IsRejectedNamingTheField()
    {
        await CreateRegionFieldAsync();

        var response = await PostAsync(AsAdmin(), "/api/customer/v1/contacts", new
        {
            firstName = "Priya",
            email = "priya@acme.in"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).GetProperty("errors")
            .TryGetProperty("customFields.region", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AC11_ReactivatingADeactivatedCustomField_BringsTheStoredValuesBack()
    {
        var definition = await CreateRegionFieldAsync();

        var contact = await ReadAsync<ContactDto>(await PostAsync(AsAdmin(), "/api/customer/v1/contacts", new
        {
            firstName = "Priya",
            email = "priya@acme.in",
            customFields = new Dictionary<string, object> { ["region"] = "north" }
        }));

        contact.CustomFields.GetProperty("region").GetString().Should().Be("north");

        // CF-5: deactivating hides the field from forms.
        var deactivated = await PatchAsync(
            AsAdmin(), $"/api/customer/v1/custom-fields/{definition.Id}", new { isActive = false });
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);

        var forForms = await ReadAsync<List<CustomFieldDefinitionDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/custom-fields?entity=contact"));
        forForms.Should().BeEmpty();

        // CF-4: an unrelated edit is not blocked and does not drop the stored value.
        var edited = await PatchAsync(
            AsAdmin(), $"/api/customer/v1/contacts/{contact.Id}", new { version = 1, city = "Pune" });
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<ContactDto>(edited)).CustomFields.GetProperty("region").GetString().Should().Be("north");

        var reactivated = await PatchAsync(
            AsAdmin(), $"/api/customer/v1/custom-fields/{definition.Id}", new { isActive = true });
        reactivated.StatusCode.Should().Be(HttpStatusCode.OK);

        var backOnForms = await ReadAsync<List<CustomFieldDefinitionDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/custom-fields?entity=contact"));
        backOnForms.Should().ContainSingle().Which.FieldKey.Should().Be("region");

        var reloaded = await ReadAsync<ContactDto>(
            await AsAdmin().GetAsync($"/api/customer/v1/contacts/{contact.Id}"));
        reloaded.CustomFields.GetProperty("region").GetString().Should().Be("north");
    }

    [Fact]
    public async Task AC12_UserDeactivatedArrivingTwice_QueuesEachRecordOnlyOnce()
    {
        var leaver = Guid.NewGuid();
        await DeliverAsync(EventTypes.UserCreated,
            new { user_id = leaver, first_name = "Leaver", last_name = "One", status = "active" },
            aggregateId: leaver);

        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in", ownerId: leaver);
        var company = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com", ownerId: leaver);

        var eventId = Guid.NewGuid();
        var payload = new { user_id = leaver, first_name = "Leaver", last_name = "One", status = "deactivated" };

        (await DeliverAsync(EventTypes.UserDeactivated, payload, eventId: eventId, aggregateId: leaver))
            .Should().Be(InboundResult.Handled);

        // The same event again: the processed_events ledger makes it a no-op.
        (await DeliverAsync(EventTypes.UserDeactivated, payload, eventId: eventId, aggregateId: leaver))
            .Should().Be(InboundResult.Duplicate);

        // A different event id for the same fact: uq_reassignment_open makes that a no-op too.
        (await DeliverAsync(EventTypes.UserDeactivated, payload, aggregateId: leaver))
            .Should().Be(InboundResult.Handled);

        var queue = await ReadAsync<List<ReassignmentQueueItemDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/reassignment-queue"));

        queue.Should().HaveCount(2);
        queue.Count(i => i.RecordId == contact.Id).Should().Be(1);
        queue.Count(i => i.RecordId == company.Id).Should().Be(1);
        queue.Should().OnlyContain(i => i.PreviousOwnerId == leaver);
        queue.Single(i => i.RecordId == contact.Id).Name.Should().Be("Priya");

        // OWN-3: the records stay visible and editable until reassigned.
        (await AsAdmin().GetAsync($"/api/customer/v1/contacts/{contact.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Reassigning clears the queue entry.
        var reassign = await PostAsync(AsAdmin(), "/api/customer/v1/reassign", new
        {
            recordType = "contact",
            recordIds = new[] { contact.Id },
            newOwnerId = RepAId
        });
        reassign.StatusCode.Should().Be(HttpStatusCode.OK);

        var remaining = await ReadAsync<List<ReassignmentQueueItemDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/reassignment-queue"));
        remaining.Should().ContainSingle().Which.RecordId.Should().Be(company.Id);
    }

    [Fact]
    public async Task AC13_RestoringAContactDeletedTenDaysAgo_MakesItLiveAgainAndPublishesContactRestored()
    {
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in");
        await AsAdmin().DeleteAsync($"/api/customer/v1/contacts/{contact.Id}");
        await AgeDeletionAsync("contacts", contact.Id, 10);

        var bin = await ReadAsync<PagedResult<RecycleBinItemDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/recycle-bin?type=contact"));
        bin.Data.Should().ContainSingle().Which.Id.Should().Be(contact.Id);

        var restore = await AsAdmin().PostAsync(
            $"/api/customer/v1/recycle-bin/contact/{contact.Id}/restore", content: null);

        restore.StatusCode.Should().Be(HttpStatusCode.OK);

        var live = await AsAdmin().GetAsync($"/api/customer/v1/contacts/{contact.Id}");
        live.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<ContactDto>(live)).DeletedAt.Should().BeNull();

        var restored = await OutboxAsync(contact.Id, EventTypes.ContactRestored);
        restored.Should().HaveCount(1);
        Payload(restored[0]).GetProperty("id").GetGuid().Should().Be(contact.Id);
    }

    [Fact]
    public async Task AC14_RestoringAContactWhoseEmailIsNowTaken_IsRefusedNamingTheConflictingContact()
    {
        var original = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in");
        await AsAdmin().DeleteAsync($"/api/customer/v1/contacts/{original.Id}");

        var replacement = await CreateContactAsync(AsAdmin(), "Priyanka", "Singh", "priya@acme.in");

        var restore = await AsAdmin().PostAsync(
            $"/api/customer/v1/recycle-bin/contact/{original.Id}/restore", content: null);

        restore.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var conflicting = (await ProblemAsync(restore)).GetProperty("conflictingRecord");
        conflicting.GetProperty("id").GetGuid().Should().Be(replacement.Id);
        conflicting.GetProperty("name").GetString().Should().Be("Priyanka Singh");
    }

    [Fact]
    public async Task AC14_RestoringACompanyWhoseDomainIsNowTaken_IsRefusedNamingTheConflictingCompany()
    {
        var original = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com");
        await AsAdmin().DeleteAsync($"/api/customer/v1/companies/{original.Id}");

        var replacement = await CreateCompanyAsync(AsAdmin(), "Acme Limited", "acme.com");

        var restore = await AsAdmin().PostAsync(
            $"/api/customer/v1/recycle-bin/company/{original.Id}/restore", content: null);

        restore.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemAsync(restore)).GetProperty("conflictingRecord")
            .GetProperty("id").GetGuid().Should().Be(replacement.Id);
    }

    [Fact]
    public async Task AC15_AContactDeletedBeyondTheRetentionWindow_IsPurgedAndPublishesContactPurged()
    {
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in");
        await AsAdmin().DeleteAsync($"/api/customer/v1/contacts/{contact.Id}");
        await AgeDeletionAsync("contacts", contact.Id, 31);

        var outcome = await RunPurgeAsync();
        outcome.Contacts.Should().Be(1);

        var rows = await Fixture.ScalarAsync<long>(
            "SELECT count(*) FROM contacts WHERE id = @id", ("id", contact.Id));
        rows.Should().Be(0, "DEL-3 removes it permanently");

        var purged = await OutboxAsync(contact.Id, EventTypes.ContactPurged);
        purged.Should().HaveCount(1);
        Payload(purged[0]).GetProperty("reason").GetString().Should().Be("retention");
    }

    [Fact]
    public async Task AC15_ARecordDeletedBeyondTheWindow_IsNotOfferedAsRestorableEvenBeforeThePurgeRuns()
    {
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in");
        await AsAdmin().DeleteAsync($"/api/customer/v1/contacts/{contact.Id}");
        await AgeDeletionAsync("contacts", contact.Id, 31);

        var bin = await ReadAsync<PagedResult<RecycleBinItemDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/recycle-bin?type=contact"));

        bin.Data.Should().BeEmpty("the retention window is enforced by the query, not by the purge job");
        bin.Total.Should().Be(0);

        var restore = await AsAdmin().PostAsync(
            $"/api/customer/v1/recycle-bin/contact/{contact.Id}/restore", content: null);

        restore.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC15_PurgingACompany_RemovesItAndPublishesCompanyPurged()
    {
        var company = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com");
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in", companyId: company.Id);

        await AsAdmin().DeleteAsync($"/api/customer/v1/companies/{company.Id}");
        await AgeDeletionAsync("companies", company.Id, 31);

        var outcome = await RunPurgeAsync();
        outcome.Companies.Should().Be(1);

        (await OutboxAsync(company.Id, EventTypes.CompanyPurged)).Should().HaveCount(1);

        // The contact survives the purge, unlinked since the soft delete (COM-5).
        (await AsAdmin().GetAsync($"/api/customer/v1/contacts/{contact.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AC15_PurgingASurvivorThatAMergeLoserStillPointsAt_DoesNotFail()
    {
        var survivor = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in");
        var loser = await CreateContactAsync(AsAdmin(), "Priya", "S", "p.sharma@acme.in");

        await PostAsync(AsAdmin(), "/api/customer/v1/contacts/merge",
            new { survivorId = survivor.Id, loserId = loser.Id });

        await AsAdmin().DeleteAsync($"/api/customer/v1/contacts/{survivor.Id}");
        await AgeDeletionAsync("contacts", survivor.Id, 31);

        var outcome = await RunPurgeAsync();
        outcome.Contacts.Should().Be(1);

        var loserPointer = await Fixture.ScalarAsync<object>(
            "SELECT coalesce(merged_into_id::text, 'null') FROM contacts WHERE id = @id", ("id", loser.Id));
        loserPointer.Should().Be("null");
    }

    [Fact]
    public async Task AC16_TheLeadServiceConvertingTheSameLeadTwice_GetsOneContactBack()
    {
        var leadId = Guid.NewGuid();
        var lead = AsService("lead", RepAId);

        var first = await PostAsync(lead, "/api/customer/v1/contacts", new
        {
            firstName = "Priya",
            email = "priya@acme.in",
            sourceLeadId = leadId
        });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadAsync<ContactDto>(first);
        created.SourceLeadId.Should().Be(leadId);
        created.OwnerId.Should().Be(RepAId, "the service acts for the converting user");

        var second = await PostAsync(lead, "/api/customer/v1/contacts", new
        {
            firstName = "Priya",
            email = "priya@acme.in",
            sourceLeadId = leadId
        });

        second.StatusCode.Should().Be(HttpStatusCode.OK, "a repeat conversion returns the existing contact");
        (await ReadAsync<ContactDto>(second)).Id.Should().Be(created.Id);

        var count = await Fixture.ScalarAsync<long>(
            "SELECT count(*) FROM contacts WHERE source_lead_id = @lead", ("lead", leadId));
        count.Should().Be(1);

        (await OutboxAsync(created.Id, EventTypes.ContactCreated)).Should().HaveCount(1,
            "the retry must not publish a second contact.created");
    }

    [Fact]
    public async Task AC17_ErasingAContactThatWasMerged_RemovesBothCopiesAndReportsTwoRecords()
    {
        var survivor = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in");
        var loser = await CreateContactAsync(AsAdmin(), "Priya", "S", "p.sharma@acme.in");

        await PostAsync(AsAdmin(), "/api/customer/v1/contacts/merge",
            new { survivorId = survivor.Id, loserId = loser.Id });

        var dsrId = Guid.NewGuid();
        var result = await DeliverAsync(EventTypes.DsrErasureRequested, new
        {
            dsr_id = dsrId,
            subject_refs = new[] { new { entity_type = "contact", id = survivor.Id } },
            requester_email = "priya@acme.in"
        }, aggregateId: dsrId);

        result.Should().Be(InboundResult.Handled);

        var remaining = await Fixture.ScalarAsync<long>(
            "SELECT count(*) FROM contacts WHERE id = ANY(@ids)",
            ("ids", new[] { survivor.Id, loser.Id }));
        remaining.Should().Be(0);

        var completed = await OutboxAsync(dsrId, EventTypes.DsrErasureCompleted);
        completed.Should().HaveCount(1);

        var payload = Payload(completed[0]);
        payload.GetProperty("service").GetString().Should().Be("customer");
        payload.GetProperty("records_affected").GetInt32().Should().Be(2);

        (await OutboxAsync(survivor.Id, EventTypes.ContactPurged)).Should().HaveCount(1);
        (await OutboxAsync(loser.Id, EventTypes.ContactPurged)).Should().HaveCount(1);

        var history = await Fixture.ScalarAsync<long>("SELECT count(*) FROM merge_history");
        history.Should().Be(0, "the merge record named the erased person");
    }

    [Fact]
    public async Task AC17_ErasureIsIdempotent_ASecondDeliveryChangesNothing()
    {
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in");

        var dsrId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new { dsr_id = dsrId, subject_refs = new[] { new { entity_type = "contact", id = contact.Id } } };

        (await DeliverAsync(EventTypes.DsrErasureRequested, payload, eventId: eventId, aggregateId: dsrId))
            .Should().Be(InboundResult.Handled);

        (await DeliverAsync(EventTypes.DsrErasureRequested, payload, eventId: eventId, aggregateId: dsrId))
            .Should().Be(InboundResult.Duplicate);

        (await OutboxAsync(dsrId, EventTypes.DsrErasureCompleted)).Should().HaveCount(1);
    }

    [Fact]
    public async Task DSR2_AnAccessRequest_WritesAJsonExportAndRepliesWithItsLocation()
    {
        var contact = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in");

        var dsrId = Guid.NewGuid();
        await DeliverAsync(EventTypes.DsrAccessRequested, new
        {
            dsr_id = dsrId,
            subject_refs = new[] { new { entity_type = "contact", id = contact.Id } }
        }, aggregateId: dsrId);

        var completed = await OutboxAsync(dsrId, EventTypes.DsrAccessCompleted);
        completed.Should().HaveCount(1);

        var payload = Payload(completed[0]);
        payload.GetProperty("records_affected").GetInt32().Should().Be(1);

        var fileLocation = payload.GetProperty("file_location").GetString()!;
        File.Exists(fileLocation).Should().BeTrue();

        var document = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(fileLocation));
        document.GetProperty("contacts").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).Should().Contain(contact.Id);
    }

    [Fact]
    public async Task AC18_ImportingAThousandRowsWithThreeBadEmails_Saves997AndReportsThreeRowErrors()
    {
        var importer = AsService("data-transfer", AdminId, TestTokens.Admin);

        var rows = new List<object>();
        for (var i = 0; i < 1000; i++)
        {
            var bad = i is 100 or 500 or 900;
            rows.Add(new
            {
                firstName = $"Person{i}",
                lastName = "Imported",
                email = bad ? $"not-an-email-{i}" : $"person{i}@acme.in"
            });
        }

        var stopwatch = Stopwatch.StartNew();
        var response = await PostAsync(importer, "/api/customer/v1/bulk-upsert", new
        {
            entityType = "contact",
            duplicateStrategy = "skip",
            rows
        });
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadAsync<BulkUpsertResponse>(response);
        result.Created.Should().Be(997);
        result.Failed.Should().Be(3);
        result.Rows.Should().HaveCount(1000);
        result.Rows.Where(r => r.Status == "error").Select(r => r.Index)
            .Should().BeEquivalentTo(new[] { 100, 500, 900 });
        result.Rows.First(r => r.Status == "error").Message.Should().Contain("email");

        var saved = await Fixture.ScalarAsync<long>("SELECT count(*) FROM contacts");
        saved.Should().Be(997);

        var events = await Fixture.ScalarAsync<long>(
            "SELECT count(*) FROM outbox_events WHERE event_type = 'contact.created'");
        events.Should().Be(997, "an import publishes one event per record");

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "NFR-3");
    }

    [Fact]
    public async Task BulkUpsert_HonoursTheSkipUpdateAndCreateStrategies()
    {
        var importer = AsService("data-transfer", AdminId, TestTokens.Admin);
        var existing = await CreateContactAsync(AsAdmin(), "Priya", "Sharma", "priya@acme.in");

        var skipped = await ReadAsync<BulkUpsertResponse>(await PostAsync(importer, "/api/customer/v1/bulk-upsert", new
        {
            entityType = "contact",
            duplicateStrategy = "skip",
            rows = new[] { new { firstName = "Priyanka", email = "priya@acme.in" } }
        }));

        skipped.Skipped.Should().Be(1);
        skipped.Rows[0].Id.Should().Be(existing.Id);

        var updated = await ReadAsync<BulkUpsertResponse>(await PostAsync(importer, "/api/customer/v1/bulk-upsert", new
        {
            entityType = "contact",
            duplicateStrategy = "update",
            rows = new[] { new { firstName = "Priyanka", email = "priya@acme.in", city = "Pune" } }
        }));

        updated.Updated.Should().Be(1);

        var reloaded = await ReadAsync<ContactDto>(
            await AsAdmin().GetAsync($"/api/customer/v1/contacts/{existing.Id}"));
        reloaded.FirstName.Should().Be("Priyanka");
        reloaded.City.Should().Be("Pune");
        reloaded.LastName.Should().Be("Sharma", "a column the import did not carry is left alone");
        reloaded.Version.Should().Be(2);

        var refused = await ReadAsync<BulkUpsertResponse>(await PostAsync(importer, "/api/customer/v1/bulk-upsert", new
        {
            entityType = "contact",
            duplicateStrategy = "create",
            rows = new[] { new { firstName = "Third", email = "priya@acme.in" } }
        }));

        refused.Failed.Should().Be(1);
        refused.Rows[0].Message.Should().Contain("already uses this email");
    }

    [Fact]
    public async Task BulkUpsert_RejectsTheSameEmailTwiceWithinOneImport()
    {
        var importer = AsService("data-transfer", AdminId, TestTokens.Admin);

        var result = await ReadAsync<BulkUpsertResponse>(await PostAsync(importer, "/api/customer/v1/bulk-upsert", new
        {
            entityType = "contact",
            duplicateStrategy = "skip",
            rows = new[]
            {
                new { firstName = "First", email = "priya@acme.in" },
                new { firstName = "Second", email = "PRIYA@acme.in" }
            }
        }));

        result.Created.Should().Be(1);
        result.Failed.Should().Be(1);
        result.Rows[1].Message.Should().Contain("more than once");
    }

    [Fact]
    public async Task BulkUpsert_RefusesMoreRowsThanTheConfiguredLimit()
    {
        var importer = AsService("data-transfer", AdminId, TestTokens.Admin);
        var rows = Enumerable.Range(0, 1001)
            .Select(i => new { firstName = $"P{i}", email = $"p{i}@acme.in" })
            .ToArray();

        var response = await PostAsync(importer, "/api/customer/v1/bulk-upsert",
            new { entityType = "contact", duplicateStrategy = "skip", rows });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Export_PagesRecordsWithTheActingUsersVisibility()
    {
        await CreateContactAsync(AsAdmin(), "Mine", email: "mine@x.in", ownerId: RepAId);
        await CreateContactAsync(AsAdmin(), "Theirs", email: "theirs@x.in", ownerId: RepBId);

        var exporter = AsService("data-transfer", RepAId, TestTokens.SalesRep, [RepAId]);

        var page = await ReadAsync<PagedResult<ContactDto>>(
            await exporter.GetAsync("/api/customer/v1/export?entity=contact&page=1&pageSize=500"));

        page.Data.Should().ContainSingle().Which.FirstName.Should().Be("Mine");
        page.PageSize.Should().Be(500, "an export page may be as large as an import call");
    }

    [Fact]
    public async Task Export_RefusesAPageLargerThanTheImportLimit()
    {
        var exporter = AsService("data-transfer", AdminId, TestTokens.Admin);

        var response = await exporter.GetAsync("/api/customer/v1/export?entity=contact&pageSize=5000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CF7_TheActiveCustomFieldLimitIsEnforced()
    {
        // The configured limit is 50; create them and then try one more.
        for (var i = 0; i < 50; i++)
        {
            var response = await PostAsync(AsAdmin(), "/api/customer/v1/custom-fields", new
            {
                entityType = "contact",
                fieldKey = $"extra_{i}",
                label = $"Extra {i}",
                fieldType = "text"
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var tooMany = await PostAsync(AsAdmin(), "/api/customer/v1/custom-fields", new
        {
            entityType = "contact",
            fieldKey = "one_too_many",
            label = "One too many",
            fieldType = "text"
        });

        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The limit is per record type, so a company field is still allowed.
        var company = await PostAsync(AsAdmin(), "/api/customer/v1/custom-fields", new
        {
            entityType = "company",
            fieldKey = "segment",
            label = "Segment",
            fieldType = "text"
        });

        company.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CF6_AFieldsKeyAndTypeCannotChangeButItsLabelOptionsAndOrderCan()
    {
        var definition = await CreateRegionFieldAsync(required: false);

        var patched = await ReadAsync<CustomFieldDefinitionDto>(await PatchAsync(
            AsAdmin(), $"/api/customer/v1/custom-fields/{definition.Id}", new
            {
                label = "Sales Region",
                options = new[] { "north", "south", "west" },
                sortOrder = 5,
                // Deliberately sent, deliberately ignored: CF-6.
                fieldKey = "something_else",
                fieldType = "number"
            }));

        patched.Label.Should().Be("Sales Region");
        patched.Options.Should().BeEquivalentTo(new[] { "north", "south", "west" });
        patched.SortOrder.Should().Be(5);
        patched.FieldKey.Should().Be("region");
        patched.FieldType.Should().Be("select");
    }

    [Fact]
    public async Task CF1_AFieldKeyMustMatchTheDatabaseFormatAndBeUniquePerRecordType()
    {
        var badKey = await PostAsync(AsAdmin(), "/api/customer/v1/custom-fields", new
        {
            entityType = "contact",
            fieldKey = "Region One",
            label = "Region",
            fieldType = "text"
        });
        badKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var selectWithoutOptions = await PostAsync(AsAdmin(), "/api/customer/v1/custom-fields", new
        {
            entityType = "contact",
            fieldKey = "region",
            label = "Region",
            fieldType = "select"
        });
        selectWithoutOptions.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await CreateRegionFieldAsync();

        var duplicate = await PostAsync(AsAdmin(), "/api/customer/v1/custom-fields", new
        {
            entityType = "contact",
            fieldKey = "region",
            label = "Region again",
            fieldType = "text"
        });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Picklists_AreSeededWithTheAgreedDefaultsOnFirstRead()
    {
        var industries = await ReadAsync<List<PicklistDto>>(
            await AsRepA().GetAsync("/api/customer/v1/picklists?type=company_industry"));

        industries.Select(p => p.Value).Should().Contain(new[] { "Technology", "Manufacturing", "Other" });
        industries.Should().OnlyContain(p => p.IsActive && p.ListType == "company_industry");

        var sources = await ReadAsync<List<PicklistDto>>(
            await AsRepA().GetAsync("/api/customer/v1/picklists?type=contact_source"));

        sources.Select(p => p.Value).Should().Contain(new[] { "Website", "Referral", "Import" });

        // A second read must not seed a second copy.
        var again = await ReadAsync<List<PicklistDto>>(
            await AsRepA().GetAsync("/api/customer/v1/picklists?type=company_industry"));
        again.Should().HaveCount(industries.Count);
    }

    [Fact]
    public async Task Picklists_CanBeAddedRenamedAndDeactivatedByAnAdmin()
    {
        var created = await ReadAsync<PicklistDto>(await PostAsync(AsAdmin(), "/api/customer/v1/picklists", new
        {
            listType = "company_industry",
            value = "Agriculture"
        }));

        created.Value.Should().Be("Agriculture");
        created.IsActive.Should().BeTrue();

        var duplicate = await PostAsync(AsAdmin(), "/api/customer/v1/picklists", new
        {
            listType = "company_industry",
            value = "Agriculture"
        });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var renamed = await ReadAsync<PicklistDto>(await PatchAsync(
            AsAdmin(), $"/api/customer/v1/picklists/{created.Id}",
            new { value = "Agriculture & Food", sortOrder = 1 }));
        renamed.Value.Should().Be("Agriculture & Food");

        var deactivated = await ReadAsync<PicklistDto>(await PatchAsync(
            AsAdmin(), $"/api/customer/v1/picklists/{created.Id}", new { isActive = false }));
        deactivated.IsActive.Should().BeFalse();

        var active = await ReadAsync<List<PicklistDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/picklists?type=company_industry"));
        active.Should().NotContain(p => p.Id == created.Id);

        var withInactive = await ReadAsync<List<PicklistDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/picklists?type=company_industry&includeInactive=true"));
        withInactive.Should().Contain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task Picklists_RejectAnUnknownListType()
    {
        var response = await AsAdmin().GetAsync("/api/customer/v1/picklists?type=not_a_list");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RecycleBin_ListsBothRecordTypesWithTheirPurgeDate()
    {
        var company = await CreateCompanyAsync(AsAdmin(), "Acme", "acme.com");
        var contact = await CreateContactAsync(AsAdmin(), "Priya", email: "priya@acme.in");

        await AsAdmin().DeleteAsync($"/api/customer/v1/companies/{company.Id}");
        await AsAdmin().DeleteAsync($"/api/customer/v1/contacts/{contact.Id}");

        var bin = await ReadAsync<PagedResult<RecycleBinItemDto>>(
            await AsAdmin().GetAsync("/api/customer/v1/recycle-bin"));

        bin.Total.Should().Be(2);
        bin.Data.Should().Contain(i => i.Id == company.Id && i.RecordType == "company");
        bin.Data.Should().Contain(i => i.Id == contact.Id && i.RecordType == "contact");
        bin.Data.Should().OnlyContain(i => i.PurgeAfter > i.DeletedAt);
    }

    [Fact]
    public async Task UserRefs_OnlyMoveForwardWhenAnEventIsNewer()
    {
        var userId = Guid.NewGuid();

        await DeliverAsync(EventTypes.UserCreated,
            new { user_id = userId, first_name = "Original", last_name = "Name", status = "active" },
            version: 2, aggregateId: userId);

        // An older event arriving late must be ignored (CLAUDE.md rule 4).
        await DeliverAsync(EventTypes.UserUpdated,
            new { user_id = userId, first_name = "Stale", last_name = "Name", status = "active" },
            version: 1, aggregateId: userId);

        var owners = await ReadAsync<List<OwnerDto>>(await AsAdmin().GetAsync("/api/customer/v1/owners"));
        owners.Single(o => o.Id == userId).DisplayName.Should().Be("Original Name");

        await DeliverAsync(EventTypes.UserUpdated,
            new { user_id = userId, first_name = "Renamed", last_name = "Name", status = "active" },
            version: 3, aggregateId: userId);

        var updated = await ReadAsync<List<OwnerDto>>(await AsAdmin().GetAsync("/api/customer/v1/owners"));
        updated.Single(o => o.Id == userId).DisplayName.Should().Be("Renamed Name");
    }

    [Fact]
    public async Task AnEventThisServiceDoesNotConsume_IsIgnoredWithoutBeingRecorded()
    {
        var result = await DeliverAsync("deal.stage_changed", new { deal_id = Guid.NewGuid() });

        result.Should().Be(InboundResult.Ignored);
        (await Fixture.ScalarAsync<long>("SELECT count(*) FROM processed_events")).Should().Be(0);
    }
}
