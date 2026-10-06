namespace SalesApi.Tests.Services;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalesApi.Application.Events;
using SalesApi.Application.Services;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Xunit;

public class InboundEventProcessorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // ── Context factory ────────────────────────────────────────────────────────

    private static SalesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SalesDbContext(options);
    }

    // ── Shared setup helpers ───────────────────────────────────────────────────

    private static (
        InboundEventProcessor processor,
        Mock<ILookupRepository> mockLookup,
        Mock<IDsrService> mockDsr,
        Mock<IUnitOfWork> mockUow,
        SalesDbContext context)
        BuildProcessor(SalesDbContext? ctx = null)
    {
        var context = ctx ?? CreateContext();
        var mockLookup = new Mock<ILookupRepository>();
        var mockDsr = new Mock<IDsrService>();
        var mockUow = new Mock<IUnitOfWork>();

        // Mock transaction scope
        var mockTx = new Mock<ITransactionScope>();
        mockTx.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockTx.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);
        mockUow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(mockTx.Object);

        // Default: event is newly claimed (not a duplicate)
        mockUow.Setup(u => u.ExecuteSqlAsync(It.IsAny<FormattableString>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(1);

        // SaveChangesAsync delegates to real context so InMemory data is persisted
        mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
               .Returns((CancellationToken ct) => context.SaveChangesAsync(ct));

        // Lookup defaults
        mockLookup.Setup(l => l.GetCustomerRefAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((CustomerRef?)null);
        mockLookup.Setup(l => l.GetUserRefAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((UserRef?)null);

        var processor = new InboundEventProcessor(
            context,
            mockLookup.Object,
            mockDsr.Object,
            mockUow.Object,
            TimeProvider.System,
            NullLogger<InboundEventProcessor>.Instance);

        return (processor, mockLookup, mockDsr, mockUow, context);
    }

    private static JsonElement BuildPayload(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static InboundEvent BuildEvent(
        string eventType,
        Guid? aggregateId = null,
        object? payload = null,
        int version = 1)
    {
        var payloadElement = payload is null
            ? JsonDocument.Parse("{}").RootElement.Clone()
            : BuildPayload(payload);

        return new InboundEvent(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            OrganizationId: OrgId,
            AggregateId: aggregateId ?? Guid.NewGuid(),
            Version: version,
            Payload: payloadElement);
    }

    private static Deal BuildDeal(Guid? ownerId = null, string status = "open")
        => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrgId,
            PipelineId = Guid.NewGuid(),
            StageId = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = "Test Deal",
            Status = status,
            Version = 1,
            StageEnteredAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    // ── Test cases ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_ReturnsIgnored_ForUnknownEventType()
    {
        var (processor, _, _, _, _) = BuildProcessor();

        var inbound = BuildEvent("widget.clicked");
        var result = await processor.ProcessAsync(inbound, CancellationToken.None);

        result.Should().Be(InboundResult.Ignored);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsDuplicate_WhenEventAlreadyProcessed()
    {
        var (processor, _, _, mockUow, _) = BuildProcessor();

        // Simulate "INSERT INTO processed_events ... ON CONFLICT DO NOTHING" returning 0 rows
        mockUow.Setup(u => u.ExecuteSqlAsync(It.IsAny<FormattableString>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(0);

        var inbound = BuildEvent(EventTypes.CompanyCreated, Guid.NewGuid());
        var result = await processor.ProcessAsync(inbound, CancellationToken.None);

        result.Should().Be(InboundResult.Duplicate);
    }

    [Fact]
    public async Task ProcessAsync_HandlesCompanyCreated_UpsertingCustomerRef()
    {
        var (processor, mockLookup, _, _, _) = BuildProcessor();
        var companyId = Guid.NewGuid();
        var inbound = BuildEvent(EventTypes.CompanyCreated, companyId,
            payload: new { name = "Acme Corp" });

        var result = await processor.ProcessAsync(inbound, CancellationToken.None);

        result.Should().Be(InboundResult.Handled);
        mockLookup.Verify(l => l.UpsertCustomerRef(
            It.Is<CustomerRef>(r => r.EntityType == "company" && r.Id == companyId && r.DisplayName == "Acme Corp")),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_HandlesContactMerged_RepointingDealContacts()
    {
        // Arrange: when the survivor already exists as a DealContact for the same deal,
        // the processor removes the loser row (avoids composite-key mutation) and updates
        // the deal's primary_contact_id to the survivor.
        var context = CreateContext();
        var (processor, mockLookup, _, _, _) = BuildProcessor(context);

        var loserId = Guid.NewGuid();
        var survivorId = Guid.NewGuid();
        var deal = BuildDeal();
        deal.PrimaryContactId = loserId;

        context.Deals.Add(deal);
        // Seed the loser contact row
        context.DealContacts.Add(new DealContact
        {
            DealId = deal.Id,
            ContactId = loserId,
            IsPrimary = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        // Seed the survivor contact row so "survivorExists" is true → loser row is removed
        context.DealContacts.Add(new DealContact
        {
            DealId = deal.Id,
            ContactId = survivorId,
            IsPrimary = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        // contact ref for survivor (upserted as part of ContactMerged handling)
        mockLookup.Setup(l => l.GetCustomerRefAsync("contact", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((CustomerRef?)null);

        var inbound = BuildEvent(EventTypes.ContactMerged, survivorId,
            payload: new { loser_id = loserId, survivor_id = survivorId, name = "Jane Doe" });

        var result = await processor.ProcessAsync(inbound, CancellationToken.None);

        result.Should().Be(InboundResult.Handled);

        // Loser row was removed; only the survivor row remains for this deal
        var remainingContacts = await context.DealContacts
            .Where(dc => dc.DealId == deal.Id)
            .ToListAsync();
        remainingContacts.Should().ContainSingle(dc => dc.ContactId == survivorId);
        remainingContacts.Should().NotContain(dc => dc.ContactId == loserId);

        // deals.primary_contact_id was updated to the survivor
        var updatedDeal = await context.Deals.FirstOrDefaultAsync(d => d.Id == deal.Id);
        updatedDeal!.PrimaryContactId.Should().Be(survivorId);
    }

    [Fact]
    public async Task ProcessAsync_HandlesActivityLogged_UpdatingLastActivityAt()
    {
        var context = CreateContext();
        var (processor, _, _, _, _) = BuildProcessor(context);

        var deal = BuildDeal();
        deal.Status = "open";
        deal.LastActivityAt = null;
        context.Deals.Add(deal);
        await context.SaveChangesAsync();

        var inbound = BuildEvent(EventTypes.ActivityLogged, Guid.NewGuid(),
            payload: new { deal_id = deal.Id, organization_id = OrgId });

        var result = await processor.ProcessAsync(inbound, CancellationToken.None);

        result.Should().Be(InboundResult.Handled);

        var updatedDeal = await context.Deals.FirstOrDefaultAsync(d => d.Id == deal.Id);
        updatedDeal!.LastActivityAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_HandlesUserDeactivated_AddingReassignmentQueueEntries()
    {
        var context = CreateContext();
        var (processor, mockLookup, _, _, _) = BuildProcessor(context);

        var deactivatedUserId = Guid.NewGuid();
        var deal = BuildDeal(ownerId: deactivatedUserId);
        deal.Status = "open";
        deal.DeletedAt = null;
        context.Deals.Add(deal);
        await context.SaveChangesAsync();

        // User ref not yet in local store (will be added)
        mockLookup.Setup(l => l.GetUserRefAsync(deactivatedUserId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync((UserRef?)null);

        var inbound = BuildEvent(EventTypes.UserDeactivated, deactivatedUserId,
            payload: new { user_id = deactivatedUserId, organization_id = OrgId, status = "deactivated" });

        var result = await processor.ProcessAsync(inbound, CancellationToken.None);

        result.Should().Be(InboundResult.Handled);

        var queueEntry = await context.ReassignmentQueue
            .FirstOrDefaultAsync(r => r.DealId == deal.Id);
        queueEntry.Should().NotBeNull();
        queueEntry!.DeactivatedUserId.Should().Be(deactivatedUserId);
    }

    [Fact]
    public async Task ProcessAsync_HandlesUserUpdated_IgnoringOutOfOrderVersion()
    {
        var (processor, mockLookup, _, _, _) = BuildProcessor();
        var userId = Guid.NewGuid();

        // Existing user ref with higher version (version 5)
        var existingRef = new UserRef
        {
            UserId = userId,
            OrganizationId = OrgId,
            DisplayName = "Alice Smith",
            IsActive = true,
            SourceVersion = 5,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        mockLookup.Setup(l => l.GetUserRefAsync(userId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(existingRef);

        // Inbound event has lower version (3)
        var inbound = BuildEvent(EventTypes.UserUpdated, userId,
            payload: new { user_id = userId, first_name = "Alice", last_name = "Jones" },
            version: 3); // lower than existing version 5

        var result = await processor.ProcessAsync(inbound, CancellationToken.None);

        result.Should().Be(InboundResult.Handled);

        // The existing ref should NOT have been updated (out-of-order)
        existingRef.DisplayName.Should().Be("Alice Smith");
        mockLookup.Verify(l => l.AddUserRef(It.IsAny<UserRef>()), Times.Never);
    }
}
