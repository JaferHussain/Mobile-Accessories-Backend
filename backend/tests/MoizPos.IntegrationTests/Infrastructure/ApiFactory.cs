using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MoizPos.Domain.Enums;
using MoizPos.Infrastructure.Auth;

namespace MoizPos.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API — real middleware, real authorization, real database — pointed at the test
/// schema. Endpoint behaviour such as "Staff gets 403" is only meaningful when exercised through
/// the actual pipeline (FR-040).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly DatabaseFixture _database = new();

    public string ConnectionString => _database.ConnectionString;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        // Touch the client once so the host builds eagerly and configuration errors surface here
        // rather than inside an unrelated test.
        _ = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting writes into host configuration, which the WebApplicationBuilder reads before
        // Program.cs resolves the connection string. ConfigureAppConfiguration alone is applied
        // too late here and the developer's user-secrets would win — pointing these tests at the
        // live shop database.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _database.ConnectionString,

            // Keeps a developer's appsettings.{Environment}.local.json out of the test host.
            // That file is added after this one and would otherwise decide which database the
            // suite writes to — including a live server.
            ["SkipMachineLocalSettings"] = "true",
            ["Jwt:Key"] = "integration-test-signing-key-at-least-32-chars",
            ["Jwt:Issuer"] = "MoizPos",
            ["Jwt:Audience"] = "MoizPosCounter",
            ["Jwt:AccessTokenMinutes"] = "60",
            ["Jwt:RefreshTokenDays"] = "30",
        };

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(settings));
    }

    /// <summary>Creates a user directly in the database and returns their plaintext password.</summary>
    public async Task<(long Id, string Username, string Password)> CreateUserAsync(
        UserRole role,
        string password = "Test@12345")
    {
        var username = $"u_{Guid.NewGuid():N}"[..20];
        var hash = new PasswordHasher(iterations: 1000).Hash(password);

        await using var connection = await _database.OpenAsync();

        var id = await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO users (username, full_name, password_hash, role, is_active, created_at_utc)
            VALUES (@username, 'Integration Test User', @hash, @role, TRUE, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """,
            new { username, hash, role = role.ToString() });

        return (id, username, password);
    }

    /// <summary>
    /// Returns the id of a category with this name, creating it if the suite has not already.
    /// Products carry a category foreign key, so every test that inserts a product needs one;
    /// INSERT IGNORE keeps parallel collections from racing on the unique name.
    /// </summary>
    public async Task<long> EnsureCategoryAsync(string name = "Cables")
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT IGNORE INTO categories (name, created_at_utc) VALUES (@name, UTC_TIMESTAMP(6));",
            new { name });

        return await connection.ExecuteScalarAsync<long>(
            "SELECT id FROM categories WHERE name = @name;", new { name });
    }

    /// <summary>The brand equivalent of <see cref="EnsureCategoryAsync"/>.</summary>
    public async Task<long> EnsureBrandAsync(string name = "Baseus")
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT IGNORE INTO brands (name, created_at_utc) VALUES (@name, UTC_TIMESTAMP(6));",
            new { name });

        return await connection.ExecuteScalarAsync<long>(
            "SELECT id FROM brands WHERE name = @name;", new { name });
    }

    public Task<MySqlConnector.MySqlConnection> OpenDatabaseAsync() => _database.OpenAsync();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
