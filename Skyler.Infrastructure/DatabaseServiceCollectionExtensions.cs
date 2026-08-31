using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Skyler.Infrastructure;

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddSkylerDatabase(
        this IServiceCollection services,
        string connectionString,
        string contentRootPath)
    {
        var resolvedConnectionString = ResolveConnectionString(connectionString, contentRootPath);

        services.AddDbContext<SkylerDbContext>(options =>
            options.UseSqlite(resolvedConnectionString));

        return services;
    }

    private static string ResolveConnectionString(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("The SkylerDatabase connection string needs a Data Source.");
        }

        if (!Path.IsPathRooted(builder.DataSource) && builder.DataSource != ":memory:")
        {
            builder.DataSource = Path.GetFullPath(
                Path.Combine(contentRootPath, builder.DataSource));
        }

        if (builder.DataSource != ":memory:")
        {
            var databaseDirectory = Path.GetDirectoryName(builder.DataSource);
            if (!string.IsNullOrEmpty(databaseDirectory))
            {
                Directory.CreateDirectory(databaseDirectory);
            }
        }

        return builder.ToString();
    }
}
