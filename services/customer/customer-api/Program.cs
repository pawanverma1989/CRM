using CustomerApi.Application.Json;
using CustomerApi.Application.Services;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Health;
using CustomerApi.Infrastructure.Messaging;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Infrastructure.Services;
using CustomerApi.Middleware;
using CustomerApi.Workers;
using CustomerApi.Settings;
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
builder.Services.Configure<CustomerSettings>(builder.Configuration.GetSection("Customer"));
builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Workers"));

var connectionString = builder.Configuration.GetConnectionString("CustomerDb")!;

// customer_db only, as customer_svc (CLAUDE.md rule 1, NFR-6). The schema is owned by
// services/customer/db/migrations and applied by Flyway; EF never migrates it.
builder.Services.AddDbContext<CustomerDbContext>(opts =>
    opts.UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IContactRepository, ContactRepository>();
builder.Services.AddScoped<ILookupRepository, LookupRepository>();
builder.Services.AddScoped<IReassignmentRepository, ReassignmentRepository>();

builder.Services.AddScoped<IReferenceDataLoader, ReferenceDataLoader>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IContactService, ContactService>();
builder.Services.AddScoped<IDuplicateService, DuplicateService>();
builder.Services.AddScoped<IMergeService, MergeService>();
builder.Services.AddScoped<IOwnershipService, OwnershipService>();
builder.Services.AddScoped<IRecycleBinService, RecycleBinService>();
builder.Services.AddScoped<ICustomFieldService, CustomFieldService>();
builder.Services.AddScoped<IPicklistService, PicklistService>();
builder.Services.AddScoped<IBulkImportService, BulkImportService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IDsrService, DsrService>();
builder.Services.AddScoped<IPurgeService, PurgeService>();
builder.Services.AddScoped<IInboundEventProcessor, InboundEventProcessor>();

// Nothing in the request path talks to the broker (CLAUDE.md rule 3): the relay publishes, the
// consumer receives, and both tolerate RabbitMQ being down (NFR-9).
builder.Services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
builder.Services.AddHostedService<OutboxRelay>();
builder.Services.AddHostedService<EventConsumer>();
builder.Services.AddHostedService<PurgeWorker>();

// One of the few permitted synchronous cross-service calls: fetching identity's public JWKS
// (docs/architecture.md §6). No private signing key is ever injected into this service.
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

// PostConfigure injects the JWKS provider once the container exists, so the key resolver never
// calls BuildServiceProvider() from inside the options lambda.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .PostConfigure<IJwksProvider>((options, jwks) =>
    {
        options.TokenValidationParameters.IssuerSigningKeyResolver =
            (_, _, kid, _) => jwks.GetSigningKeys(kid);
    });

builder.Services.AddAuthorization(opts =>
{
    // §2: admins see and may do everything; managers may merge, reassign and bulk-edit what they
    // can see; every signed-in user may read lists and create records.
    opts.AddPolicy(Policies.AnyAuthenticatedUser, p => p.RequireAuthenticatedUser());
    opts.AddPolicy(Policies.AdminOnly, p => p.RequireClaim("role", "admin"));
    opts.AddPolicy(Policies.ManagerOrAbove, p => p.RequireClaim("role", "admin", "manager"));
    opts.AddPolicy(Policies.ServiceOnly, p => p.RequireClaim("role", "service"));
    opts.AddPolicy(Policies.UserOrService, p => p.RequireClaim("role", "admin", "manager", "sales_rep", "service"));
});

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = CustomerJson.Api.PropertyNamingPolicy;
        o.JsonSerializerOptions.DefaultIgnoreCondition = CustomerJson.Api.DefaultIgnoreCondition;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CRM Customer API", Version = "v1" });
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

// NFR-9: the broker is deliberately not a health dependency — saves must succeed with RabbitMQ
// down, with events waiting in outbox_events, so /health must stay green. The "events" checks
// (outbox lag, dead letters) are excluded from /health and served on /health/events instead.
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "customer-db")
    .AddEventPipelineChecks("customer");

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:3001"])
        .AllowAnyMethod()
        .AllowAnyHeader()));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapServiceHealthChecks();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();

/// <summary>
/// Declared so the test project's <c>WebApplicationFactory&lt;Program&gt;</c> can reach the entry
/// point; the top-level statements above are its body.
/// </summary>
public partial class Program;

/// <summary>Authorization policy names (§2, §5).</summary>
public static class Policies
{
    public const string AnyAuthenticatedUser = "AnyAuthenticatedUser";
    public const string AdminOnly = "AdminOnly";
    public const string ManagerOrAbove = "ManagerOrAbove";

    /// <summary>Data transfer service endpoints: <c>POST /bulk-upsert</c>, <c>GET /export</c>.</summary>
    public const string ServiceOnly = "ServiceOnly";

    /// <summary>A signed-in user or a service acting for one (e.g. lead conversion calling POST /contacts).</summary>
    public const string UserOrService = "UserOrService";
}
