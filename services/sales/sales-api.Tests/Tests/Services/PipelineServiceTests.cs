namespace SalesApi.Tests.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalesApi.Application.DTOs;
using SalesApi.Application.Events;
using SalesApi.Application.Exceptions;
using SalesApi.Application.Services;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Context;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Xunit;

public class PipelineServiceTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PipelineId = Guid.NewGuid();
    private static readonly Guid StageId = Guid.NewGuid();

    private static (
        PipelineService svc,
        Mock<ILookupRepository> mockLookup,
        Mock<IDealRepository> mockDeals,
        Mock<IOutboxWriter> mockOutbox,
        Mock<IUnitOfWork> mockUow,
        Mock<IRequestContext> mockCtx)
        BuildService(string role = "admin")
    {
        var mockLookup = new Mock<ILookupRepository>();
        var mockDeals = new Mock<IDealRepository>();
        var mockOutbox = new Mock<IOutboxWriter>();
        var mockUow = new Mock<IUnitOfWork>();
        var mockCtx = new Mock<IRequestContext>();
        var clock = TimeProvider.System;

        mockCtx.Setup(c => c.OrganizationId).Returns(OrgId);
        mockCtx.Setup(c => c.ActorUserId).Returns(UserId);
        mockCtx.Setup(c => c.Role).Returns(role);
        mockCtx.Setup(c => c.IsAdmin).Returns(role == "admin");
        mockCtx.Setup(c => c.IsManagerOrAbove).Returns(role is "admin" or "manager");
        mockCtx.Setup(c => c.IsService).Returns(false);

        mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(1);
        mockOutbox.Setup(o => o.Add(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<object?>()))
            .Returns(new OutboxEvent { Id = Guid.NewGuid() });

        var svc = new PipelineService(
            mockLookup.Object,
            mockDeals.Object,
            mockOutbox.Object,
            mockUow.Object,
            mockCtx.Object,
            clock,
            NullLogger<PipelineService>.Instance);

        return (svc, mockLookup, mockDeals, mockOutbox, mockUow, mockCtx);
    }

    private static Pipeline BuildPipeline(
        IReadOnlyList<PipelineStage>? stages = null,
        Guid? id = null)
    {
        var pipeline = new Pipeline
        {
            Id = id ?? PipelineId,
            OrganizationId = OrgId,
            Name = "Default Pipeline",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Stages = stages is null
                ? [new PipelineStage { Id = StageId, PipelineId = PipelineId, Name = "Qualified", SortOrder = 1, StageType = "open", IsActive = true }]
                : [.. stages]
        };
        return pipeline;
    }

    // ── ListAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsPipelines_ForOrganization()
    {
        var (svc, mockLookup, _, _, _, _) = BuildService();
        var pipelines = new List<Pipeline> { BuildPipeline() };
        mockLookup.Setup(l => l.ListPipelinesAsync(OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipelines);

        var result = await svc.ListAsync(CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(PipelineId);
    }

    // ── GetAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ThrowsNotFound_WhenPipelineDoesNotExist()
    {
        var (svc, mockLookup, _, _, _, _) = BuildService();
        mockLookup.Setup(l => l.GetPipelineAsync(It.IsAny<Guid>(), OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync((Pipeline?)null);

        var act = () => svc.GetAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── CreateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ThrowsForbidden_WhenNotAdmin()
    {
        var (svc, _, _, _, _, _) = BuildService(role: "manager");

        var act = () => svc.CreateAsync(new CreatePipelineRequest("New Pipeline"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*Only admins may manage pipelines*");
    }

    [Fact]
    public async Task CreateAsync_AddsPipelineAndSaves_WhenAdmin()
    {
        var (svc, mockLookup, _, _, mockUow, _) = BuildService(role: "admin");

        await svc.CreateAsync(new CreatePipelineRequest("New Pipeline", IsDefault: false), CancellationToken.None);

        mockLookup.Verify(l => l.AddPipeline(It.Is<Pipeline>(p => p.Name == "New Pipeline")), Times.Once);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ThrowsForbidden_WhenNotAdmin()
    {
        var (svc, _, _, _, _, _) = BuildService(role: "sales_rep");

        var act = () => svc.UpdateAsync(PipelineId, new UpdatePipelineRequest("Renamed", null), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesNameAndSaves()
    {
        var (svc, mockLookup, _, _, mockUow, _) = BuildService(role: "admin");
        var pipeline = BuildPipeline();
        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        await svc.UpdateAsync(PipelineId, new UpdatePipelineRequest("Renamed Pipeline", null), CancellationToken.None);

        pipeline.Name.Should().Be("Renamed Pipeline");
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AddStageAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddStageAsync_ThrowsForbidden_WhenNotAdmin()
    {
        var (svc, _, _, _, _, _) = BuildService(role: "manager");

        var act = () => svc.AddStageAsync(PipelineId,
            new CreateStageRequest("New Stage", 5, 50, "open"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*Only admins may manage pipeline stages*");
    }

    [Fact]
    public async Task AddStageAsync_ThrowsConflict_WhenSecondWonStageAdded()
    {
        var (svc, mockLookup, _, _, _, _) = BuildService(role: "admin");
        var existingWonStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            PipelineId = PipelineId,
            Name = "Won",
            StageType = "won",
            IsActive = true
        };
        var pipeline = BuildPipeline(stages: [existingWonStage]);
        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        var act = () => svc.AddStageAsync(PipelineId,
            new CreateStageRequest("Second Won", 10, 100, "won"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*won*");
    }

    [Fact]
    public async Task AddStageAsync_ThrowsConflict_WhenSecondLostStageAdded()
    {
        var (svc, mockLookup, _, _, _, _) = BuildService(role: "admin");
        var existingLostStage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            PipelineId = PipelineId,
            Name = "Lost",
            StageType = "lost",
            IsActive = true
        };
        var pipeline = BuildPipeline(stages: [existingLostStage]);
        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        var act = () => svc.AddStageAsync(PipelineId,
            new CreateStageRequest("Second Lost", 11, 0, "lost"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*lost*");
    }

    [Fact]
    public async Task AddStageAsync_AddsStageAndSaves_WhenValid()
    {
        var (svc, mockLookup, _, _, mockUow, _) = BuildService(role: "admin");
        var pipeline = BuildPipeline(); // no won/lost stages
        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        var result = await svc.AddStageAsync(PipelineId,
            new CreateStageRequest("Won", 10, 100, "won"), CancellationToken.None);

        result.Name.Should().Be("Won");
        result.StageType.Should().Be("won");
        mockLookup.Verify(l => l.AddStage(It.Is<PipelineStage>(s => s.Name == "Won")), Times.Once);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateStageAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStageAsync_ThrowsConflict_WhenDeactivatingStageWithOpenDeals()
    {
        var (svc, mockLookup, mockDeals, _, _, _) = BuildService(role: "admin");
        var stage = new PipelineStage
        {
            Id = StageId,
            PipelineId = PipelineId,
            Name = "Proposal",
            StageType = "open",
            IsActive = true
        };

        mockLookup.Setup(l => l.GetStageAsync(StageId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(stage);
        mockDeals.Setup(d => d.HasOpenDealsInStageAsync(StageId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

        var act = () => svc.UpdateStageAsync(StageId, new UpdateStageRequest(null, null, null, IsActive: false), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*open deals*");
    }

    [Fact]
    public async Task UpdateStageAsync_AllowsDeactivation_WhenNoOpenDeals()
    {
        var (svc, mockLookup, mockDeals, _, mockUow, _) = BuildService(role: "admin");
        var stage = new PipelineStage
        {
            Id = StageId,
            PipelineId = PipelineId,
            Name = "Proposal",
            StageType = "open",
            IsActive = true
        };

        mockLookup.Setup(l => l.GetStageAsync(StageId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(stage);
        mockDeals.Setup(d => d.HasOpenDealsInStageAsync(StageId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        await svc.UpdateStageAsync(StageId, new UpdateStageRequest(null, null, null, IsActive: false), CancellationToken.None);

        stage.IsActive.Should().BeFalse();
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ReorderStagesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReorderStagesAsync_ThrowsValidationException_WhenStageNotInPipeline()
    {
        var (svc, mockLookup, _, _, _, _) = BuildService(role: "admin");
        var pipeline = BuildPipeline(); // has only StageId
        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        var foreignStageId = Guid.NewGuid(); // not in this pipeline
        var request = new ReorderStagesRequest(new List<Guid> { StageId, foreignStageId });
        var act = () => svc.ReorderStagesAsync(PipelineId, request, CancellationToken.None);

        await act.Should().ThrowAsync<SalesValidationException>()
            .WithMessage("*does not belong to pipeline*");
    }

    [Fact]
    public async Task ReorderStagesAsync_UpdatesSortOrder_WhenValid()
    {
        var (svc, mockLookup, _, _, mockUow, _) = BuildService(role: "admin");
        var stageA = new PipelineStage { Id = StageId, PipelineId = PipelineId, Name = "A", SortOrder = 2, StageType = "open", IsActive = true };
        var stageBId = Guid.NewGuid();
        var stageB = new PipelineStage { Id = stageBId, PipelineId = PipelineId, Name = "B", SortOrder = 1, StageType = "open", IsActive = true };
        var pipeline = BuildPipeline(stages: [stageA, stageB]);
        mockLookup.Setup(l => l.GetPipelineAsync(PipelineId, OrgId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(pipeline);

        // New order: B first, A second
        var request = new ReorderStagesRequest(new List<Guid> { stageBId, StageId });
        await svc.ReorderStagesAsync(PipelineId, request, CancellationToken.None);

        stageB.SortOrder.Should().Be(1);
        stageA.SortOrder.Should().Be(2);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
