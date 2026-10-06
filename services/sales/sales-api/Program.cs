using SalesApi.Application.Services;
using SalesApi.Infrastructure.Context;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Messaging;
using SalesApi.Infrastructure.Repositories;
using SalesApi.Infrastructure.Services;
using SalesApi.Middleware;
using SalesApi.Settings;
using SalesApi.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<IdentitySettings>(builder.Configuration.GetSection("Identity"));
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<SalesSettings>(builder.Configuration.GetSection("Sales"));
builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Workers"));

var connectionString = builder.Configuration.GetConnectionString("SalesDb")!;

// sales_db only (CLAUDE.md rule 1). The schema is owned by services/sales/db/migrations
// and applied by Flyway; EF never migrates it.
builder.Services.AddDbContext<SalesDbContext>(opts =>
    opts.UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddScoped<IDealRepository, DealRepository>();
builder.Services.AddScoped<ILookupRepository, LookupRepository>();

builder.Services.AddScoped<IDealService, DealService>();
builder.Services.AddScoped<IPipelineService, PipelineService>();
builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<ILossReasonService, LossReasonService>();
builder.Services.AddScoped<ICustomFieldService, CustomFieldService>();
builder.Services.AddScoped<IBulkImportService, BulkImportService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IRecycleBinService, RecycleBinService>();
builder.Services.AddScoped<IReassignmentService, ReassignmentService>();
builder.Services.AddScoped<IDsrService, DsrService>();
builder.Services.AddScoped<IPurgeService, PurgeService>();
builder.Services.AddScoped<IInboundEventProcessor, InboundEventProcessor>();

// Nothing in the request path talks to the broker (CLAUDE.md rule 3).
builder.Services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
builder.Services.AddHostedService<OutboxRelay>();
builder.Services.AddHostedService<EventConsumer>();
builder.Services.AddHostedService<PurgeWorker>();
builder.Services.AddHostedService<StaleDealsWorker>();

// SalesSeeder: creates the default pipeline on first startup (AC-1).
builder.Services.AddHostedService<SalesSeeder>();

// Identity JWKS — the only permitted synchronous cross-service call (docs/architecture.md §6).
builder.Services.AddHttpClient<IJwksProvider, JwksProvider>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();

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
            ValidateIssuerSigningKey = true,
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "role"
        };
    });

// PostConfigure injects the JWKS provider without calling BuildServiceProvider() inside the lambda.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .PostConfigure<IJwksProvider>((options, jwks) =>
    {
        options.TokenValidationParameters.IssuerSigningKeyResolver =
            (_, _, kid, _) => jwks.GetSigningKeys(kid);
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy(Policies.AnyAuthenticatedUser, p => p.RequireAuthenticatedUser());
    opts.AddPolicy(Policies.AdminOnly, p => p.RequireClaim("role", "admin"));
    opts.AddPolicy(Policies.ManagerOrAbove, p => p.RequireClaim("role", "admin", "manager"));
    opts.AddPolicy(Policies.ServiceOnly, p => p.RequireClaim("role", "service"));
    opts.AddPolicy(Policies.UserOrService, p => p.RequireClaim("role", "admin", "manager", "sales_rep", "service"));
});

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CRM Sales API", Version = "v1" });
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

// NFR-9: the broker is not a health dependency.
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "sales-db");

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:3004"])
        .AllowAnyMethod()
        .AllowAnyHeader()));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();

/// <summary>
/// Declared so the test project's <c>WebApplicationFactory&lt;Program&gt;</c> can reach the entry point.
/// </summary>
public partial class Program;

/// <summary>Authorization policy names.</summary>
public static class Policies
{
    public const string AnyAuthenticatedUser = "AnyAuthenticatedUser";
    public const string AdminOnly = "AdminOnly";
    public const string ManagerOrAbove = "ManagerOrAbove";

    /// <summary>Data transfer service endpoints: <c>POST /bulk-upsert</c>, <c>GET /export</c>.</summary>
    public const string ServiceOnly = "ServiceOnly";

    /// <summary>A signed-in user or a service acting for one (e.g. during deal creation from a conversion).</summary>
    public const string UserOrService = "UserOrService";
}
