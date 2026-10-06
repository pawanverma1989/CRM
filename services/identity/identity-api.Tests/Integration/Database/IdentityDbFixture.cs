namespace IdentityApi.Tests.Integration.Database;
using IdentityApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

/// <summary>
/// One real PostgreSQL 14 container. Mirrors <c>db/00_create_databases.sql</c> (non-superuser
/// <c>identity_svc</c> owning <c>identity_db</c>) and then applies every
/// <c>services/identity/db/migrations/V*.sql</c> as <c>identity_svc</c> — never as the superuser.
/// </summary>
public sealed class IdentityDbFixture : IAsyncLifetime
{
    private const string Role = "identity_svc";
    private const string Database = "identity_db";
    private const string Password = "identity_test_pw";

    private PostgreSqlContainer _container = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:14-alpine")
            .Build();
        await _container.StartAsync();

        var admin = _container.GetConnectionString();
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            foreach (var sql in new[]
            {
                $"CREATE ROLE {Role} LOGIN PASSWORD '{Password}'",
                $"CREATE DATABASE {Database} OWNER {Role}",
                $"REVOKE ALL ON DATABASE {Database} FROM PUBLIC"
            })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(admin)
        {
            Database = Database,
            Username = Role,
            Password = Password
        }.ConnectionString;

        await using var svc = new NpgsqlConnection(ConnectionString);
        await svc.OpenAsync();
        foreach (var file in Directory.GetFiles(MigrationsDirectory(), "V*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(file), svc);
            try { await command.ExecuteNonQueryAsync(); }
            catch (PostgresException ex)
            {
                throw new InvalidOperationException($"Migration {Path.GetFileName(file)} failed as {Role}: {ex.MessageText}", ex);
            }
        }

        // The type cache was loaded before V1 created citext; refresh it for later connections.
        await svc.ReloadTypesAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public IdentityDbContext CreateContext()
        => new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private static string MigrationsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "services", "identity", "db", "migrations");
            if (Directory.Exists(candidate)) return candidate;

            var sibling = Path.Combine(directory.FullName, "db", "migrations");
            if (Directory.Exists(sibling) && Directory.GetFiles(sibling, "V1__*.sql").Length > 0
                && File.Exists(Path.Combine(directory.FullName, "identity-api", "identity-api.csproj")))
                return sibling;

            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find services/identity/db/migrations above " + AppContext.BaseDirectory);
    }
}
