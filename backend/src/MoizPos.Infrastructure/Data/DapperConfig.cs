using System.Data;
using Dapper;

namespace MoizPos.Infrastructure.Data;

/// <summary>
/// Global Dapper setup. Call <see cref="Apply"/> once at startup and once in test setup.
/// </summary>
public static class DapperConfig
{
    private static bool _applied;
    private static readonly object Gate = new();

    public static void Apply()
    {
        lock (Gate)
        {
            if (_applied)
            {
                return;
            }

            // The schema uses snake_case (quantity_on_hand); the entities use PascalCase
            // (QuantityOnHand). This maps between them without an attribute on every property.
            DefaultTypeMap.MatchNamesWithUnderscores = true;

            // MySQL DATETIME has no offset. Everything is stored in UTC (research.md R6), so
            // stamp the Kind on the way out — otherwise DateTimeKind.Unspecified silently
            // becomes local time the first time anything formats it.
            SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
            SqlMapper.AddTypeHandler(new NullableUtcDateTimeHandler());

            _applied = true;
        }
    }

    /// <summary>Test seam: allows re-applying configuration in a fresh assembly load context.</summary>
    internal static void ResetForTesting()
    {
        lock (Gate)
        {
            _applied = false;
        }
    }
}

/// <summary>Reads and writes <see cref="DateTime"/> as UTC, never local.</summary>
internal sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override DateTime Parse(object value) =>
        DateTime.SpecifyKind((DateTime)value, DateTimeKind.Utc);

    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.DbType = DbType.DateTime2;
        parameter.Value = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Unspecified is treated as already-UTC: every write path in this system produces
            // UTC, so converting would corrupt it.
            _ => value,
        };
    }
}

/// <inheritdoc cref="UtcDateTimeHandler" />
internal sealed class NullableUtcDateTimeHandler : SqlMapper.TypeHandler<DateTime?>
{
    public override DateTime? Parse(object value) =>
        value is DateTime dateTime
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : null;

    public override void SetValue(IDbDataParameter parameter, DateTime? value)
    {
        parameter.DbType = DbType.DateTime2;

        if (value is null)
        {
            parameter.Value = DBNull.Value;
            return;
        }

        parameter.Value = value.Value.Kind == DateTimeKind.Local
            ? value.Value.ToUniversalTime()
            : value.Value;
    }
}
