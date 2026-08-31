using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Skyler.Infrastructure;

public static class DatabaseInitializer
{
    public static async Task EnsureCreatedSafelyAsync(
        this SkylerDbContext database,
        CancellationToken cancellationToken = default)
    {
        var connectionString = database.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The database connection string is unavailable.");
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

        if (dataSource == ":memory:")
        {
            await database.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureAnalysisColumnsAsync(database, cancellationToken);
            return;
        }

        await using var initializationLock = await AcquireLockAsync(
            $"{dataSource}.init.lock",
            cancellationToken);

        await database.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureAnalysisColumnsAsync(database, cancellationToken);
    }

    private static async Task EnsureAnalysisColumnsAsync(
        SkylerDbContext database,
        CancellationToken cancellationToken)
    {
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"WorkEvidenceAnalyses\");";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    columns.Add(reader.GetString(1));
                }
            }

            if (columns.Remove("SuggestedAction"))
            {
                await BackupBeforeRemovingSuggestedActionAsync(database, cancellationToken);
                await database.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE \"WorkEvidenceAnalyses\" DROP COLUMN \"SuggestedAction\";",
                    cancellationToken);
            }

            await AddColumnIfMissingAsync(
                database,
                columns,
                "AutomationOpportunity",
                cancellationToken);
            await AddColumnIfMissingAsync(
                database,
                columns,
                "AutomationApprovedAtUtc",
                cancellationToken);
            await AddColumnIfMissingAsync(
                database,
                columns,
                "ApprovedTimeFreedMinutes",
                cancellationToken);
            await AddColumnIfMissingAsync(
                database,
                columns,
                "AnalysisVersion",
                cancellationToken);
            await AddColumnIfMissingAsync(
                database,
                columns,
                "InferredRole",
                cancellationToken);
            await AddColumnIfMissingAsync(
                database,
                columns,
                "RoleConfidence",
                cancellationToken);
            await AddColumnIfMissingAsync(
                database,
                columns,
                "RoleRationale",
                cancellationToken);

            await EnsureWorkActivityColumnsAsync(database, cancellationToken);
            await EnsureWorkEvidenceColumnsAsync(database, cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task BackupBeforeRemovingSuggestedActionAsync(
        SkylerDbContext database,
        CancellationToken cancellationToken)
    {
        var source = (SqliteConnection)database.Database.GetDbConnection();
        var builder = new SqliteConnectionStringBuilder(source.ConnectionString);
        if (builder.DataSource == ":memory:")
        {
            return;
        }

        var backupPath = $"{builder.DataSource}.before-suggested-action-removal.bak";
        if (File.Exists(backupPath))
        {
            return;
        }

        await using var backup = new SqliteConnection($"Data Source={backupPath}");
        await backup.OpenAsync(cancellationToken);
        source.BackupDatabase(backup);
    }

    private static async Task EnsureWorkEvidenceColumnsAsync(
        SkylerDbContext database,
        CancellationToken cancellationToken)
    {
        var connection = database.Database.GetDbConnection();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"WorkEvidence\");";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("IsAbsence"))
        {
            await database.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"WorkEvidence\" ADD COLUMN \"IsAbsence\" INTEGER NOT NULL DEFAULT 0;",
                cancellationToken);
        }
    }

    private static async Task EnsureWorkActivityColumnsAsync(
        SkylerDbContext database,
        CancellationToken cancellationToken)
    {
        var connection = database.Database.GetDbConnection();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"WorkActivities\");";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("AutomationApprovedAtUtc"))
        {
            await database.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"WorkActivities\" ADD COLUMN \"AutomationApprovedAtUtc\" TEXT NULL;",
                cancellationToken);
        }
    }

    private static async Task AddColumnIfMissingAsync(
        SkylerDbContext database,
        ISet<string> columns,
        string columnName,
        CancellationToken cancellationToken)
    {
        if (columns.Contains(columnName))
        {
            return;
        }

        var sql = columnName switch
        {
            "AutomationOpportunity" =>
                "ALTER TABLE \"WorkEvidenceAnalyses\" ADD COLUMN \"AutomationOpportunity\" TEXT NULL;",
            "AutomationApprovedAtUtc" =>
                "ALTER TABLE \"WorkEvidenceAnalyses\" ADD COLUMN \"AutomationApprovedAtUtc\" TEXT NULL;",
            "ApprovedTimeFreedMinutes" =>
                "ALTER TABLE \"WorkEvidenceAnalyses\" ADD COLUMN \"ApprovedTimeFreedMinutes\" INTEGER NULL;",
            "AnalysisVersion" =>
                "ALTER TABLE \"WorkEvidenceAnalyses\" ADD COLUMN \"AnalysisVersion\" INTEGER NOT NULL DEFAULT 1;",
            "InferredRole" =>
                "ALTER TABLE \"WorkEvidenceAnalyses\" ADD COLUMN \"InferredRole\" TEXT NULL;",
            "RoleConfidence" =>
                "ALTER TABLE \"WorkEvidenceAnalyses\" ADD COLUMN \"RoleConfidence\" REAL NOT NULL DEFAULT 0;",
            "RoleRationale" =>
                "ALTER TABLE \"WorkEvidenceAnalyses\" ADD COLUMN \"RoleRationale\" TEXT NOT NULL DEFAULT '';",
            _ => throw new ArgumentOutOfRangeException(nameof(columnName), columnName, null)
        };

        await database.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        columns.Add(columnName);
    }

    private static async Task<FileStream> AcquireLockAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}
