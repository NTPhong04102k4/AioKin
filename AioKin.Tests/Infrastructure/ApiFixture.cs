using AioKin.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Tao mot database Postgres rieng cho moi lan chay test, khoi dong app that tren do, roi
/// xoa database luc ket thuc.
///
/// Dung Postgres that chu khong phai InMemory provider: migration EF, index co dieu kien
/// (HasFilter) va cot GENERATED chi lo loi tren Postgres that. CI da cap san service
/// postgres:16-alpine nen day khong phai phu thuoc moi.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"aiokin_test_{Guid.NewGuid():N}";
    private WebApplicationFactory<Program>? _factory;

    public HttpClient Client { get; private set; } = default!;

    public string ConnectionString => BuildConnectionString(_databaseName);

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(BuildConnectionString("postgres")))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"""CREATE DATABASE "{_databaseName}" """, admin);
            await create.ExecuteNonQueryAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Development: Redis va Brevo deu duoc phep roi ve ban cai thay the. O
            // Production app se dung khoi dong khi thieu chung — dung o day thi moi test
            // deu do vi mot ly do khong lien quan gi den thu dang test.
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);

            // App tu choi khoi dong neu khoa ngan hon 32 byte. Chuoi nay chi ton tai trong
            // test — no khong ky token nao ra ngoai tien trinh test.
            builder.UseSetting("Jwt:Key", "aiokin-test-signing-key-32-bytes-minimum!!");
            builder.UseSetting("Jwt:Issuer", "aiokin-test");
            builder.UseSetting("Jwt:Audience", "aiokin-test");
        });

        // Tao client la thu that su khoi dong app, va app chay MigrateAsync + DbSeeder luc
        // khoi dong. Sau dong nay, schema va du lieu seed da san sang.
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();

        // Pool con giu ket noi toi database vua roi thi DROP se bao "database is being
        // accessed by other users". Dong pool truoc, va van dung WITH (FORCE) cho chac.
        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(BuildConnectionString("postgres"));
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"""DROP DATABASE IF EXISTS "{_databaseName}" WITH (FORCE)""", admin);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Scope de lay DbContext hoac service bat ky ra kiem tra truc tiep.</summary>
    public IServiceScope CreateScope()
        => _factory!.Services.CreateScope();

    /// <summary>DbContext moi trong mot scope moi. Nho dispose scope kem theo.</summary>
    public static AioKinDbContext Db(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<AioKinDbContext>();

    private static string BuildConnectionString(string database)
    {
        var host = Environment.GetEnvironmentVariable("TEST_PG_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("TEST_PG_PORT") ?? "5432";
        var user = Environment.GetEnvironmentVariable("TEST_PG_USER") ?? "postgres";
        var password = Environment.GetEnvironmentVariable("TEST_PG_PASSWORD") ?? "postgres";

        return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
    }
}
