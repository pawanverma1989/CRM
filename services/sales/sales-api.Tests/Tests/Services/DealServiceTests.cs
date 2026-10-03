namespace SalesApi.Tests.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SalesApi.Application.DTOs;
using SalesApi.Application.Events;
using SalesApi.Application.Exceptions;
using SalesApi.Application.Services;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Context;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using SalesApi.Settings;
using Xunit;

public class DealServiceTests
{
    // ── Shared IDs ─────────────────────────────────────────────────────────────
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PipelineId = Guid.NewGuid();
    private static readonly Guid StageId = Guid.NewGuid();
    private static readonly Guid WonStageId = Guid.NewGuid();
    private static readonly Guid LostStageId = Guid.NewGuid();
    private static readonly Guid LossReasonId = Guid.NewGuid();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (
        DealService svc,
        Mock<IDealRepository> mockDeals,
        Mock<ILookupRepository> mockLookup,
        Mock<IOutboxWriter> mockOutbox,
        Mock<IUnitOfWork> mockUow,
        Mock<IRequestContext> mockCtx)
        BuildService(string role = "admin", Guid[]? visibleOwners = null)
    {
        var mockDeals = new Mock<IDealRepository>();
        var mockLookup = new Mock<ILookupRepository>();
        var mockOutbox = new Mock<IOutboxWriter>();
        var mockUow = new Mock<IUnitOfWork>();
        var mockCtx = new Mock<IRequestContext>();
        var clock = TimeProvider.System;
        var settings = Options.Create(new SalesSettings
        {
            DefaultPageSize = 50,
            MaxPageSize = 200,
            MaxBulkRecords = 500,
            StaleDaysWithoutActivity = 14,
            StaleDaysInStage = 30
        });

        mockCtx.Setup(c => c.OrganizationId).Returns(OrgId);
        mockCtx.Setup(c => c.ActorUserId).Returns(UserId);
        mockCtx.Setup(c => c.Role).Returns(role);
        mockCtx.Setup(c => c.IsAdmin).Returns(role == "admin");
        mockCtx.Setup(c => c.IsManagerOrAbove).Returns(role is "admin" or "manager");
        mockCtx.Setup(c => c.IsService).Returns(false);
        mockCtx.Setup(c => c.VisibleOwnerIds).Returns(visibleOwners);

        // Default: ExecuteSqlAsync returns 1 (success)
        mockUow.Setup(u => u.ExecuteSqlAsync(It.IsAny<FormattableString>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(1);
        mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(1);

        // Minimal lookup mocks for DTO building
        mockLookup.Setup(l => l.GetPipelineAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((Pipeline?)null);
        mockLookup.Setup(l => l.GetUserRefAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((UserRef?)null);
        mockLookup.Setup(l => l.GetCustomerRefAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((CustomerRef?)null);
        mockLookup.Setup(l => l.GetLossReasonAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((LossReason?)null);

        var svc = new DealService(
            mockDeals.Object,
            mockLookup.Object,
            mockOutbox.Object,
            mockUow.Object,
            mockCtx.Object,
            clock,
            settings,
            NullLogger<DealService>.Instance);

        return (svc, mockDeals, mockLookup, mockOutbox, mockUow, mockCtx);
    }

    private static Pipeline BuildPipeline(Guid? wonStage = null, Guid? lostStage = null, bool stageActive = true)
    {
        var stage = new PipelineStage
        {
            Id = StageId,
            PipelineId = PipelineId,
            Name = "Qualified",
            SortOrder = 1,
            StageType = "open",
            IsActive = stageActive
        };
        var pipeline = new Pipeline
        {
            Id = PipelineId,
            OrganizationId = OrgId,
            Name = "Default",
            Stages = [stage]
        };
        if (wonStage.HasValue)
            pipeline.Stages.Add(new PipelineStage
            {
                Id = wonStage.Value,
                PipelineId = PipelineId,
                Name = "Won",
                SortOrder = 10,
                StageType = "won",
                IsActive = true
            });
        if (lostStage.HasValue)
            pipeline.Stages.Add(new PipelineStage
            {
                Id = lostStage.Value,
                PipelineId = PipelineId,
                Name = "Lost",
                SortOrder = 11,
                StageType = "lost",
                IsActive = true
            });
        return pipeline;
    }

    private static Deal BuildDeal(
        Guid? ownerId = null,
        string status = "open",
        int version = 1,
        Guid? stageId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrgId,
            PipelineId = PipelineId,
            StageId = stageId ?? StageId,
            OwnerId = ownerId,
            Name = "Test Deal",
            Amount = 1000m,
            Currency = "INR",
            Status = status,
            Version = version,
            StageEnteredAt = DateTimeOffset.UtcNow.AddDays(-5),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    // ── CreateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsExistingDeal_WhenSourceLeadIdAlreadyExists()
    {
        var (svc, mockDeals, mockLookup, _, _, _) = BuildService();
        var leadId = Guid.NewGuid();
        var existing = BuildDeal();
        var existingPipeline = BuildPipeline();

        mockDeals.Setup(d => d.FindBySourceLeadIdAsync(leadId, OrgId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existing);
        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(existingPipeline);

        var request = new CreateDealRequest(PipelineId, StageId, "New Deal", SourceLeadId: leadId);
        var result = await svc.CreateAsync(request, CancellationToken.None);

        result.Id.Should().Be(existing.Id);
    }

    [Fact]
    public async Task CreateAsync_ThrowsValidationException_WhenPipelineNotFound()
    {
        var (svc, _, mockLookup, _, _, _) = BuildService();
        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync((Pipeline?)null);

        var request = new CreateDealRequest(PipelineId, StageId, "New Deal");
        var act = () => svc.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*Pipeline not found*");
    }

    [Fact]
    public async Task CreateAsync_ThrowsValidationException_WhenStageNotFoundInPipeline()
    {
        var (svc, _, mockLookup, _, _, _) = BuildService();
        var pipeline = BuildPipeline();
        var wrongStageId = Guid.NewGuid();

        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        var request = new CreateDealRequest(PipelineId, wrongStageId, "New Deal");
        var act = () => svc.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*Stage does not belong*");
    }

    [Fact]
    public async Task CreateAsync_ThrowsValidationException_WhenStageInactive()
    {
        var (svc, _, mockLookup, _, _, _) = BuildService();
        var pipeline = BuildPipeline(stageActive: false);

        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        var request = new CreateDealRequest(PipelineId, StageId, "New Deal");
        var act = () => svc.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task CreateAsync_AddsDealAndSaves_WhenValid()
    {
        var (svc, mockDeals, mockLookup, _, mockUow, _) = BuildService();
        var pipeline = BuildPipeline();

        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        var request = new CreateDealRequest(PipelineId, StageId, "New Deal");
        await svc.CreateAsync(request, CancellationToken.None);

        mockDeals.Verify(d => d.Add(It.Is<Deal>(deal => deal.Name == "New Deal")), Times.Once);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ThrowsNotFound_WhenDealMissing()
    {
        var (svc, mockDeals, _, _, _, _) = BuildService();
        mockDeals.Setup(d => d.GetAsync(It.IsAny<Guid>(), It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Deal?)null);

        var request = new UpdateDealRequest(Version: 1);
        var act = () => svc.UpdateAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_ThrowsConflict_WhenVersionMismatch()
    {
        var (svc, mockDeals, _, _, _, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, version: 2);
        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        var request = new UpdateDealRequest(Version: 1);
        var act = () => svc.UpdateAsync(deal.Id, request, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*version*");
    }

    [Fact]
    public async Task UpdateAsync_ThrowsForbidden_WhenSalesRepEditsOthersDeal()
    {
        var (svc, mockDeals, _, _, _, _) = BuildService(role: "sales_rep");
        var ownerId = Guid.NewGuid(); // different from UserId
        var deal = BuildDeal(ownerId: ownerId, version: 1);
        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        var request = new UpdateDealRequest(Version: 1);
        var act = () => svc.UpdateAsync(deal.Id, request, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFieldsAndSaves_WhenValid()
    {
        var (svc, mockDeals, _, mockOutbox, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, version: 1);
        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        var request = new UpdateDealRequest(Version: 1, Name: "Updated Name", Amount: 2000m);
        await svc.UpdateAsync(deal.Id, request, CancellationToken.None);

        deal.Name.Should().Be("Updated Name");
        deal.Amount.Should().Be(2000m);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── MoveAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveAsync_Noop_WhenSameStage()
    {
        var (svc, mockDeals, _, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId);
        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        var request = new MoveDealRequest(StageId: deal.StageId); // same stage
        await svc.MoveAsync(deal.Id, request, CancellationToken.None);

        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MoveAsync_ThrowsValidationException_WhenStageFromWrongPipeline()
    {
        var (svc, mockDeals, mockLookup, _, _, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId);
        var newStageId = Guid.NewGuid();
        var wrongPipelineStage = new PipelineStage
        {
            Id = newStageId,
            PipelineId = Guid.NewGuid(), // different pipeline
            Name = "Wrong Stage",
            StageType = "open",
            IsActive = true
        };

        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockLookup.Setup(l => l.GetStageAsync(newStageId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(wrongPipelineStage);

        var request = new MoveDealRequest(StageId: newStageId);
        var act = () => svc.MoveAsync(deal.Id, request, CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*does not belong to the deal's pipeline*");
    }

    [Fact]
    public async Task MoveAsync_SavesStageChange_WhenValid()
    {
        var (svc, mockDeals, mockLookup, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId);
        var newStageId = Guid.NewGuid();
        var newStage = new PipelineStage
        {
            Id = newStageId,
            PipelineId = PipelineId, // same pipeline
            Name = "Proposal",
            StageType = "open",
            IsActive = true
        };

        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockLookup.Setup(l => l.GetStageAsync(newStageId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(newStage);

        var request = new MoveDealRequest(StageId: newStageId);
        await svc.MoveAsync(deal.Id, request, CancellationToken.None);

        deal.StageId.Should().Be(newStageId);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── MarkWonAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkWonAsync_Noop_WhenAlreadyWon()
    {
        var (svc, mockDeals, _, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, status: "won");
        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        await svc.MarkWonAsync(deal.Id, new MarkWonRequest(), CancellationToken.None);

        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkWonAsync_ThrowsValidationException_WhenNoWonStage()
    {
        var (svc, mockDeals, mockLookup, _, _, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, status: "open");
        var stagesWithNoWon = new List<PipelineStage>
        {
            new() { Id = StageId, PipelineId = PipelineId, StageType = "open", IsActive = true, Name = "Qual" }
        };

        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockLookup.Setup(l => l.ListStagesAsync(PipelineId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(stagesWithNoWon);

        var act = () => svc.MarkWonAsync(deal.Id, new MarkWonRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*no active 'won' stage*");
    }

    [Fact]
    public async Task MarkWonAsync_SetsStageAndSaves_WhenValid()
    {
        var (svc, mockDeals, mockLookup, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, status: "open");
        var closeDate = new DateOnly(2026, 12, 31);
        var stages = new List<PipelineStage>
        {
            new() { Id = StageId, PipelineId = PipelineId, StageType = "open", IsActive = true, Name = "Qual" },
            new() { Id = WonStageId, PipelineId = PipelineId, StageType = "won", IsActive = true, Name = "Won" }
        };

        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockLookup.Setup(l => l.ListStagesAsync(PipelineId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(stages);

        await svc.MarkWonAsync(deal.Id, new MarkWonRequest(CloseDate: closeDate), CancellationToken.None);

        deal.StageId.Should().Be(WonStageId);
        deal.ClosedAt.Should().NotBeNull();
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── MarkLostAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkLostAsync_ThrowsValidationException_WhenLossReasonNotFound()
    {
        var (svc, mockDeals, mockLookup, _, _, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, status: "open");
        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockLookup.Setup(l => l.GetLossReasonAsync(LossReasonId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync((LossReason?)null);

        var act = () => svc.MarkLostAsync(deal.Id, new MarkLostRequest(LossReasonId), CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*Loss reason not found*");
    }

    [Fact]
    public async Task MarkLostAsync_SetsStageAndLossReason_WhenValid()
    {
        var (svc, mockDeals, mockLookup, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, status: "open");
        var lossReason = new LossReason { Id = LossReasonId, OrganizationId = OrgId, Name = "Price" };
        var stages = new List<PipelineStage>
        {
            new() { Id = StageId, PipelineId = PipelineId, StageType = "open", IsActive = true, Name = "Qual" },
            new() { Id = LostStageId, PipelineId = PipelineId, StageType = "lost", IsActive = true, Name = "Lost" }
        };

        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockLookup.Setup(l => l.GetLossReasonAsync(LossReasonId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(lossReason);
        mockLookup.Setup(l => l.ListStagesAsync(PipelineId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(stages);

        await svc.MarkLostAsync(deal.Id, new MarkLostRequest(LossReasonId, "Competitor was cheaper"), CancellationToken.None);

        deal.StageId.Should().Be(LostStageId);
        deal.LossReasonId.Should().Be(LossReasonId);
        deal.LossNotes.Should().Be("Competitor was cheaper");
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ReopenAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReopenAsync_Noop_WhenAlreadyOpen()
    {
        var (svc, mockDeals, _, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, status: "open");
        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        await svc.ReopenAsync(deal.Id, new ReopenDealRequest(StageId), CancellationToken.None);

        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReopenAsync_ThrowsValidationException_WhenStageTypeNotOpen()
    {
        var (svc, mockDeals, mockLookup, _, _, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, status: "lost");
        var wonStage = new PipelineStage
        {
            Id = WonStageId,
            PipelineId = PipelineId,
            StageType = "won",
            IsActive = true,
            Name = "Won"
        };

        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockLookup.Setup(l => l.GetStageAsync(WonStageId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(wonStage);

        var act = () => svc.ReopenAsync(deal.Id, new ReopenDealRequest(WonStageId), CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*Reopen requires an 'open' stage*");
    }

    [Fact]
    public async Task ReopenAsync_ClearsClosedFields_WhenValid()
    {
        var (svc, mockDeals, mockLookup, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId, status: "lost");
        deal.ClosedAt = DateTimeOffset.UtcNow.AddDays(-1);
        deal.LossReasonId = LossReasonId;
        deal.LossNotes = "Old notes";
        var reopenStageId = Guid.NewGuid();
        var openStage = new PipelineStage
        {
            Id = reopenStageId,
            PipelineId = PipelineId,
            StageType = "open",
            IsActive = true,
            Name = "Qualification"
        };

        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockLookup.Setup(l => l.GetStageAsync(reopenStageId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(openStage);

        await svc.ReopenAsync(deal.Id, new ReopenDealRequest(reopenStageId), CancellationToken.None);

        deal.ClosedAt.Should().BeNull();
        deal.LossReasonId.Should().BeNull();
        deal.LossNotes.Should().BeNull();
        deal.StageId.Should().Be(reopenStageId);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── DeleteAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SetsDeletedAt_WhenValid()
    {
        var (svc, mockDeals, _, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId);
        mockDeals.Setup(d => d.GetAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        await svc.DeleteAsync(deal.Id, CancellationToken.None);

        deal.DeletedAt.Should().NotBeNull();
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsNotFound_WhenDealMissing()
    {
        var (svc, mockDeals, _, _, _, _) = BuildService();
        mockDeals.Setup(d => d.GetAsync(It.IsAny<Guid>(), It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Deal?)null);

        var act = () => svc.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── ReassignAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignAsync_ThrowsForbidden_WhenSalesRep()
    {
        var (svc, _, _, _, _, _) = BuildService(role: "sales_rep");

        var request = new BulkReassignRequest(new List<Guid> { Guid.NewGuid() }, NewOwnerId: null);
        var act = () => svc.ReassignAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*Only an admin or manager*");
    }

    [Fact]
    public async Task ReassignAsync_SkipsNonOpenDeals()
    {
        var (svc, mockDeals, _, _, _, _) = BuildService(role: "manager");
        var dealId = Guid.NewGuid();
        var deal = BuildDeal(ownerId: UserId, status: "won");
        deal.Id = dealId;

        mockDeals.Setup(d => d.GetAsync(dealId, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        var request = new BulkReassignRequest(new List<Guid> { dealId }, NewOwnerId: null);
        var result = await svc.ReassignAsync(request, CancellationToken.None);

        result.Reassigned.Should().Be(0);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task ReassignAsync_ReassignsOpenDeals_AndPublishesEvents()
    {
        var (svc, mockDeals, _, mockOutbox, mockUow, _) = BuildService(role: "manager");
        var dealId = Guid.NewGuid();
        var deal = BuildDeal(ownerId: UserId, status: "open");
        deal.Id = dealId;
        var newOwnerId = Guid.NewGuid();

        mockDeals.Setup(d => d.GetAsync(dealId, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);
        mockOutbox.Setup(o => o.Add(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<object?>()))
            .Returns(new OutboxEvent { Id = Guid.NewGuid() });

        var request = new BulkReassignRequest(new List<Guid> { dealId }, NewOwnerId: newOwnerId);
        var result = await svc.ReassignAsync(request, CancellationToken.None);

        result.Reassigned.Should().Be(1);
        result.Skipped.Should().Be(0);
        deal.OwnerId.Should().Be(newOwnerId);
        mockOutbox.Verify(o => o.Add(
            EventTypes.DealReassigned, It.IsAny<string>(), deal.Id,
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<object?>()), Times.Once);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ReplaceContactsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceContactsAsync_ThrowsValidationException_WhenMoreThanOnePrimary()
    {
        var (svc, mockDeals, _, _, _, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId);
        deal.DealContacts = [];
        mockDeals.Setup(d => d.GetWithDetailsAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        var contacts = new List<DealContactEntry>
        {
            new(Guid.NewGuid(), null, IsPrimary: true),
            new(Guid.NewGuid(), null, IsPrimary: true)
        };
        var request = new ReplaceDealContactsRequest(contacts);
        var act = () => svc.ReplaceContactsAsync(deal.Id, request, CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*At most one contact can be primary*");
    }

    [Fact]
    public async Task ReplaceContactsAsync_ReplacesContacts_WhenValid()
    {
        var (svc, mockDeals, _, _, mockUow, _) = BuildService();
        var deal = BuildDeal(ownerId: UserId);
        var oldContactId = Guid.NewGuid();
        deal.DealContacts =
        [
            new DealContact { DealId = deal.Id, ContactId = oldContactId, IsPrimary = true }
        ];

        mockDeals.Setup(d => d.GetWithDetailsAsync(deal.Id, It.IsAny<IRequestContext>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(deal);

        var newContactId = Guid.NewGuid();
        var contacts = new List<DealContactEntry>
        {
            new(newContactId, "Decision Maker", IsPrimary: true)
        };
        var request = new ReplaceDealContactsRequest(contacts);
        await svc.ReplaceContactsAsync(deal.Id, request, CancellationToken.None);

        deal.DealContacts.Should().HaveCount(1);
        deal.DealContacts[0].ContactId.Should().Be(newContactId);
        deal.DealContacts[0].IsPrimary.Should().BeTrue();
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
