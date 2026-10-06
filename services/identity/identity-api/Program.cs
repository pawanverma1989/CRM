using FluentValidation;
using FluentValidation.AspNetCore;
using IdentityApi.Application.Interfaces;
using IdentityApi.Application.Services;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using IdentityApi.Infrastructure.HealthChecks;
using IdentityApi.Infrastructure.Messaging;
using IdentityApi.Infrastructure.Repositories;
using IdentityApi.Infrastructure.Services;
using IdentityApi.Middleware;
using IdentityApi.Settings;
using IdentityApi.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Workers"));
builder.Services.AddSingleton(TimeProvider.System);

var connectionString = builder.Configuration.GetConnectionString("IdentityDb")!;

builder.Services.AddDbContext<IdentityDbContext>(opts =>
    opts.UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton<IRsaKeyProvider, RsaKeyProvider>();
builder.Services.AddScoped<ITokenService, JwtService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITeamRepository, TeamRepository>();
builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IEmailService, StubEmailService>();

// Outbox relay → crm.identity.events. Nothing in the request path talks to the broker (CLAUDE.md rule 3).
builder.Services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
builder.Services.AddHostedService<OutboxRelay>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>()
                    .LogWarning("JWT auth failed: {Error}", ctx.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

// PostConfigure injects the RSA key provider after the DI container is built —
// avoids the BuildServiceProvider() anti-pattern in the key resolver.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .PostConfigure<IRsaKeyProvider>((options, keyProvider) =>
    {
        options.TokenValidationParameters.IssuerSigningKey =
            new RsaSecurityKey(keyProvider.GetPublicKey()) { KeyId = jwtSettings.KeyId };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminOnly", p => p.RequireClaim("role", "admin"));
    opts.AddPolicy("ManagerOrAbove", p => p.RequireClaim("role", "admin", "manager"));
});

builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CRM Identity API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

// "/health" (docker healthcheck) runs only the untagged liveness checks; the outbox-lag check is
// tagged "events" and served at "/health/events" so a broker outage never marks the container unhealthy.
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "identity-db")
    .AddCheck<OutboxLagHealthCheck>(OutboxLagHealthCheck.Name, tags: [OutboxLagHealthCheck.Tag]);

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])
        .AllowAnyMethod()
        .AllowAnyHeader()));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = r => !r.Tags.Contains(OutboxLagHealthCheck.Tag)
});
app.MapHealthChecks("/health/events", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains(OutboxLagHealthCheck.Tag),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = HealthJsonWriter.WriteAsync
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();

/// <summary>Declared so the test project's <c>WebApplicationFactory&lt;Program&gt;</c> can reach the entry point.</summary>
public partial class Program;
