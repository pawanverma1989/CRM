namespace IdentityApi.Tests.Integration.Api;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

/// <summary>
/// Runs the real pipeline (JWT bearer, authorization policies, routing) for
/// <c>POST api/identity/v1/users/resync-events</c>. The service layer is mocked — its database
/// behaviour is covered by UserServiceTests and UserVersionIntegrationTests.
/// </summary>
public class ResyncEventsEndpointTests : IClassFixture<ResyncEventsEndpointTests.Factory>
{
    private const string Url = "/api/identity/v1/users/resync-events";
    private readonly Factory _factory;

    public ResyncEventsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.UserService.Reset();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public Mock<IUserService> UserService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development => ephemeral RSA signing key; no broker in tests.
            builder.UseEnvironment("Development");
            builder.UseSetting("Workers:OutboxRelayEnabled", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserService>();
                services.AddSingleton(UserService.Object);
            });
        }

        public string TokenFor(string role, Guid userId, Guid orgId)
        {
            using var scope = Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            return tokens.GenerateAccessToken(
                new User { Id = userId, OrganizationId = orgId, Role = role, Email = "x@example.com", Status = "active" },
                null);
        }
    }

    private HttpClient ClientWith(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task ResyncEvents_Unauthenticated_Returns401()
    {
        var response = await ClientWith(null).PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.UserService.Verify(s => s.ResyncUserEventsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("sales_rep")]
    public async Task ResyncEvents_NonAdmin_Returns403(string role)
    {
        var token = _factory.TokenFor(role, Guid.NewGuid(), Guid.NewGuid());

        var response = await ClientWith(token).PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.UserService.Verify(s => s.ResyncUserEventsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResyncEvents_Admin_Returns202WithQueued_AndIgnoresOrganizationInBody()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _factory.UserService
            .Setup(s => s.ResyncUserEventsAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        var token = _factory.TokenFor("admin", userId, orgId);

        var response = await ClientWith(token).PostAsJsonAsync(Url, new { organization_id = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("queued").GetInt32().Should().Be(3);
        _factory.UserService.Verify(s => s.ResyncUserEventsAsync(orgId, userId, It.IsAny<CancellationToken>()), Times.Once);
        _factory.UserService.Verify(s => s.ResyncUserEventsAsync(It.Is<Guid>(g => g != orgId), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
