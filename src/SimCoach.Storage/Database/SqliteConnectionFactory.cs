using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SimCoach.Storage.Database;

/// <summary>
/// Opens SQLite connections with foreign-key enforcement on. The single place that builds the
/// connection string. Its static constructor installs the process-wide Dapper configuration
/// (snake_case ↔ PascalCase mapping + the <see cref="DateTimeOffset"/> handler); since every Dapper
/// call in this assembly goes through a repository that takes this factory, the CLR runs the static
/// constructor before the first query — no module initializer needed.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    static SqliteConnectionFactory()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    public SqliteConnectionFactory(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();
        _dbPath = options.DbPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DbPath,
            ForeignKeys = true, // FK enforcement is per-connection; carry it on every connection.
        }.ToString();
    }

    /// <summary>Opens a new connection, creating the parent directory on first use.</summary>
    public SqliteConnection Create()
    {
        string? directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Stores <see cref="DateTimeOffset"/> as round-trip ISO-8601 ("o") TEXT.</summary>
    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("o", CultureInfo.InvariantCulture);
        }

        public override DateTimeOffset Parse(object value) =>
            DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
