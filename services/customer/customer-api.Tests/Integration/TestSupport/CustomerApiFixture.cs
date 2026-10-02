namespace CustomerApi.Tests.Integration.TestSupport;
using CustomerApi.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

/// <summary>
/// One real PostgreSQL 14 container per test run, with <c>V1</c> then <c>V2</c> applied from
/// <c>services/customer/db/migrations</c> as the <c>customer_svc</c> role — the same image and role
/// compose uses. Nothing is mocked: the tests exercise the migrations, the CHECK constraints, the
/// partial unique indexes and the triggers (architecture §9.4).
/// </summary>
public sealed class CustomerApiFixture : IAsyncLifetime
{
    private const string Role = "customer_svc";
    private const string Database = "customer_db";
    private const string Password = "customer_test_pw";

    private PostgreSqlContainer _container = null!;
    private WebApplicationFactory<Program>? _factory;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>DSR-2 exports are written here instead of /var/crm/dsr-exports.</summary>
    public string DsrExportPath { get; } =
        Path.Combine(Path.GetTempPath(), "crm-customer-tests", Guid.NewGuid().ToString("N"));

    public WebApplicationFactory<Program> Factory => _factory
        ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:14-alpine")
            .WithDatabase(Database)
            .WithUsername(Role)
            .WithPassword(Password)
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString() + ";Pooling=true;MaxPoolSize=30";

        await ApplyMigrationsAsync();

        Directory.CreateDirectory(DsrExportPath);

        // Configuration reaches the app as environment variables, exactly as compose supplies it:
        // WebApplication.CreateBuilder reads them before any of Program's own code runs.
        Environment.SetEnvironmentVariable("ConnectionStrings__CustomerDb", ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestTokens.Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestTokens.Audience);
        Environment.SetEnvironmentVariable("Customer__DsrExportPath", DsrExportPath);

        // The broker and the nightly purge are driven directly by the tests that cover them.
        Environment.SetEnvironmentVariable("Workers__OutboxRelayEnabled", "false");
        Environment.SetEnvironmentVariable("Workers__ConsumerEnabled", "false");
        Environment.SetEnvironmentVariable("Workers__PurgeEnabled", "false");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                // The real app resolves signing keys from identity's JWKS endpoint; the tests sign
                // their own tokens instead. Registered last, so this post-configure wins.
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.TokenValidationParameters.IssuerSigningKeyResolver = null;
                        options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
                    })));

        // Force the host to build now, so a configuration mistake fails here and not inside a test.
        _ = _factory.Services.GetRequiredService<IConfiguration>();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _container.DisposeAsync();

        try { if (Directory.Exists(DsrExportPath)) Directory.Delete(DsrExportPath, recursive: true); }
        catch (IOException) { /* a leftover temp directory is not worth failing a test run over */ }
    }

    /// <summary>A scope with the application's own services, for arranging and asserting directly against the database.</summary>
    public IServiceScope CreateScope() => Factory.Services.CreateScope();

    public async Task<T> WithDbAsync<T>(Func<CustomerDbContext, Task<T>> work)
    {
        using var scope = CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<CustomerDbContext>());
    }

    public async Task WithDbAsync(Func<CustomerDbContext, Task> work)
    {
        using var scope = CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<CustomerDbContext>());
    }

    /// <summary>Empties every table so each test starts from a known state.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE contacts, companies, custom_field_definitions, merge_history, picklists,
                     reassignment_queue, user_refs, outbox_events, processed_events
            RESTART IDENTITY CASCADE;
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Runs a statement that the schema is expected to refuse, and hands back the PostgreSQL error
    /// so a test can assert on the constraint name and the SQLSTATE.
    /// </summary>
    public async Task<PostgresException> ExpectRejectionAsync(
        string sql, params (string Name, object Value)[] parameters)
    {
        try
        {
            await ExecuteAsync(sql, parameters);
        }
        catch (PostgresException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The database accepted a statement it should have refused.");
    }

    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private async Task ApplyMigrationsAsync()
    {
        var directory = MigrationsDirectory();

        foreach (var file in Directory.GetFiles(directory, "V*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            var sql = await File.ReadAllTextAsync(file);
            var result = await _container.ExecScriptAsync(sql);

            if (result.ExitCode != 0 || result.Stderr.Contains("ERROR", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Migration {Path.GetFileName(file)} failed (exit {result.ExitCode}): {result.Stderr}");
        }
    }

    private static string MigrationsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "db", "migrations");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "V1*.sql").Length > 0)
                return candidate;

            var fromRepoRoot = Path.Combine(directory.FullName, "services", "customer", "db", "migrations");
            if (Directory.Exists(fromRepoRoot)) return fromRepoRoot;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find services/customer/db/migrations above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// All integration tests share one container and one host, and xUnit runs a collection's classes
/// one at a time — which is what makes <see cref="CustomerApiFixture.ResetAsync"/> safe.
/// </summary>
[CollectionDefinition(Name)]
public class CustomerApiCollection : ICollectionFixture<CustomerApiFixture>
{
    public const string Name = "customer-api";
}
