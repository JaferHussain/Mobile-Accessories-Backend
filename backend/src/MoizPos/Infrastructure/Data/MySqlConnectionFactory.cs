using System.Data.Common;
using MoizPos.Application.Abstractions;
using MySqlConnector;

namespace MoizPos.Infrastructure.Data;

/// <inheritdoc />
public sealed class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A database connection string is required. Set ConnectionStrings:Default.",
                nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            // Never leak a half-opened connection back to the pool on failure.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
