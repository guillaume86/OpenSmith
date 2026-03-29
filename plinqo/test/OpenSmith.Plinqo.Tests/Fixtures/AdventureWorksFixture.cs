using Microsoft.Data.SqlClient;
using SchemaExplorer;
using Testcontainers.MsSql;

namespace OpenSmith.Plinqo.Tests.Fixtures;

public class AdventureWorksFixture : IAsyncLifetime
{
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "plinqo-test-data");

    private static readonly string BakPath = Path.Combine(CacheDir, "AdventureWorks2022.bak");

    private const string DownloadUrl =
        "https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorks2022.bak";

    private readonly MsSqlContainer _container;
    private readonly List<string> _tempDirs = [];

    private DatabaseSchema? _schema;

    public AdventureWorksFixture()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

    public string ConnectionString
    {
        get
        {
            var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
            {
                InitialCatalog = "AdventureWorks2022",
                TrustServerCertificate = true,
            };
            return builder.ConnectionString;
        }
    }

    public DatabaseSchema Schema => _schema ??= new SqlSchemaProvider().GetDatabaseSchema(ConnectionString);

    public string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PlinqoTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public async Task InitializeAsync()
    {
        await EnsureBakDownloadedAsync();
        await _container.StartAsync();
        await CopyBakToContainerAsync();
        await RestoreDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private static async Task EnsureBakDownloadedAsync()
    {
        if (File.Exists(BakPath))
            return;

        Directory.CreateDirectory(CacheDir);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await http.GetAsync(DownloadUrl);
        response.EnsureSuccessStatusCode();

        var tempFile = BakPath + ".tmp";
        await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await response.Content.CopyToAsync(fs);
        }

        File.Move(tempFile, BakPath, overwrite: true);
    }

    private async Task CopyBakToContainerAsync()
    {
        var bakBytes = await File.ReadAllBytesAsync(BakPath);
        await _container.CopyAsync(bakBytes, "/var/opt/mssql/backup/AdventureWorks2022.bak");
    }

    private async Task RestoreDatabaseAsync()
    {
        const string restoreSql = """
            RESTORE DATABASE [AdventureWorks2022]
            FROM DISK = '/var/opt/mssql/backup/AdventureWorks2022.bak'
            WITH MOVE 'AdventureWorks2022' TO '/var/opt/mssql/data/AdventureWorks2022.mdf',
                 MOVE 'AdventureWorks2022_log' TO '/var/opt/mssql/data/AdventureWorks2022_log.ldf'
            """;

        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(restoreSql, connection) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}
