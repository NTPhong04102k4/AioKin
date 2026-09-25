using System.Reflection;
using AioKin.Common;
using AioKin.Controllers.Auth;
using AioKin.Data;
using AioKin.Middleware;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Setup;
using AioKin.Services.Auth.Admin;
using AioKin.Services.Auth.Biometric;
using AioKin.Services.Auth.Email;
using AioKin.Services.Auth.OAuth;
using AioKin.Services.Auth.Otp;
using AioKin.Services.Auth.PasswordUser;
using AioKin.Services.Auth.Permissions;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.StaffManagement;
using AioKin.Services.Auth.Token;
using AioKin.Services.Auth.User;
using AioKin.Services.Common.Cache;
using AioKin.Services.Content;
using AioKin.Services.Family;
using AioKin.Services.Vault;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

// Ten scheme cookie tam cho SSO. Khong dung IdentityConstants.ExternalScheme vi du an
// khong tham chieu ASP.NET Core Identity. Hang so nam trong AuthController vi chinh no
// goi AuthenticateAsync/SignOutAsync voi scheme nay — hai noi khai bao roi lech ten thi
// build van qua ma luong SSO chet luc chay.
const string ExternalAuthScheme = AuthController.ExternalCookieScheme;

// Ten policy CORS — khai o day de UseCors() sau nay khong go lai chuoi.
const string CorsPolicy = "AioKinCors";

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ─── MVC ──────────────────────────────────────────────────────────────────────

// AddControllersAsServices dua controller vao DI container. Mac dinh chung duoc kich hoat
// ngoai container, nen mot dependency quen dang ky chi lo ra thanh 500 luc co request that;
// dang ky kieu nay thi ValidateOnBuild (bat san o Development) bat duoc ngay luc khoi dong.
builder.Services.AddControllers().AddControllersAsServices();
builder.Services.AddHttpClient();

// FamilyContext doc danh tinh nguoi goi tu HttpContext. Khong dang ky dong nay thi container
// nem loi luc khoi dong (ValidateOnBuild), khong phai luc co request that.
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();

// Loi validation tra ve cung khuon OperationResult nhu moi phan hoi loi khac, thay vi
// ProblemDetails mac dinh — client chi phai viet mot bo xu ly loi.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var messages = context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m));

        return new UnprocessableEntityObjectResult(
            OperationResult.Fail("ValidationError", string.Join("; ", messages)));
    };
});

// Chan brute-force cho cac endpoint auth. Controller tham chieu policy bang ten, nen
// dong nay va UseRateLimiter() phia duoi phai di cung nhau.
builder.Services.AddAioKinRateLimiting();

// Reverse proxy (Render, Railway, nginx) dat scheme va IP that vao X-Forwarded-*.
// Khong xu ly thi rate limiting gom moi client vao mot phan vung theo IP cua proxy, va
// redirect OAuth sinh ra URL http:// thay vi https://.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ─── Swagger ──────────────────────────────────────────────────────────────────

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AioKin API",
        Version = "v1",
        Description = "Backend cua app Android com.ntp.aiokin."
    });

    options.EnableAnnotations();

    // XML doc chi ton tai khi GenerateDocumentationFile=true (dang bat trong csproj).
    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        In = ParameterLocation.Header,
        Description = "Dan access token vao day; khong can tu go tien to Bearer."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ─── Database ─────────────────────────────────────────────────────────────────

var connectionString = ResolvePostgresConnectionString(config)
    ?? throw new InvalidOperationException(
        "Thieu chuoi ket noi Postgres. Dat ConnectionStrings:DefaultConnection, "
        + "ConnectionStrings:Postgres hoac bien moi truong DATABASE_URL.");

builder.Services.AddDbContext<AioKinDbContext>(options =>
    options.UseNpgsql(connectionString)
           // Entity viet PascalCase, bang/cot trong Postgres la snake_case. AioKinDbContext
           // da gia dinh dieu do — vi du HasFilter("email IS NOT NULL") go thang ten cot
           // snake_case — nen bo dong nay di la moi filter index tro ten cot khong ton tai.
           .UseSnakeCaseNamingConvention());

// ─── Redis / cache ────────────────────────────────────────────────────────────
//
// Ba ban cai cua IRedisService, chon theo cau hinh co san: khong co Redis thi app van chay
// duoc tren may dev thay vi chet luc khoi dong.

builder.Services.AddMemoryCache();

var upstashRestUrl = config["UPSTASH_REDIS_REST_URL"] ?? config["Redis:RestUrl"];
var redisConnection = config["Redis:ConnectionString"] ?? config["REDIS_URL"];

if (!string.IsNullOrWhiteSpace(upstashRestUrl))
{
    // Host chan ket noi TCP ra ngoai (nhieu nen tang serverless) — di duong REST.
    builder.Services.AddHttpClient<IRedisService, UpstashRedisRestService>();
}
else
{
    // Thu ket noi that ngay tai day thay vi chi xem chuoi ket noi co ton tai hay khong.
    // Mot chuoi ket noi tro toi Redis khong chay van tao duoc ConnectionMultiplexer khi
    // AbortOnConnectFail=false, roi moi thao tac deu that bai lang le — dang ky se tra
    // 500 voi ly do "khong luu duoc", chang he nhac gi den Redis.
    var multiplexer = await TryConnectRedisAsync(redisConnection);

    if (multiplexer is not null)
    {
        builder.Services.AddSingleton(multiplexer);
        builder.Services.AddSingleton<IRedisService, RedisService>();
    }
    else if (builder.Environment.IsProduction())
    {
        // Production ma roi ve MemoryCache thi OTP va refresh token chi song trong mot
        // tien trinh: mat sach khi restart, va instance thu hai khong nhin thay cua
        // instance thu nhat. Dung han con hon chay sai.
        throw new InvalidOperationException(
            "Khong ket noi duoc Redis va moi truong la Production. "
            + "Dat Redis:ConnectionString (hoac UPSTASH_REDIS_REST_URL) truoc khi chay.");
    }
    else
    {
        builder.Services.AddSingleton<IRedisService, MemoryCacheRedisService>();
    }
}

// ─── Email ────────────────────────────────────────────────────────────────────

builder.Services.AddOptions<BrevoOptions>()
    .Bind(config.GetSection(BrevoOptions.SectionName))
    .ValidateDataAnnotations();

var brevo = config.GetSection(BrevoOptions.SectionName).Get<BrevoOptions>();

if (brevo?.IsConfigured == true)
{
    builder.Services.AddHttpClient<IEmailService, BrevoEmailService>(http =>
    {
        // BrevoEmailService goi duong dan tuong doi "smtp/email", nen BaseAddress phai ket
        // thuc bang "/": thieu dau gach cuoi thi Uri nuot mat doan "/v3".
        http.BaseAddress = new Uri(brevo.BaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("api-key", brevo.ApiKey);
        http.Timeout = TimeSpan.FromSeconds(15);
    });
}
else
{
    // Chua co API key thi ghi noi dung email ra log thay vi gui — luong dang ky va quen mat
    // khau van chay duoc tu dau den cuoi tren may dev.
    builder.Services.AddSingleton<IEmailService, LoggingEmailService>();
}

// ─── Service tang Auth ────────────────────────────────────────────────────────
//
// Scoped cho cung vong doi voi AioKinDbContext. AccessTokenService khong cham database nhung
// van de Scoped cho dong nhat — gia tao no gan nhu bang khong.

builder.Services.AddScoped<IAccessTokenService, AccessTokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<ITemporaryPasswordService, TemporaryPasswordService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISuperAdminGuardService, SuperAdminGuardService>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddScoped<IStaffManagementService, StaffManagementService>();
builder.Services.AddScoped<IOAuthService, OAuthService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IBiometricAuthService, BiometricAuthService>();

// ─── Service tang Content ─────────────────────────────────────────────────────
//
// Chi doc, va deu cham AioKinDbContext — Scoped de dung mot DbContext voi phan con lai
// cua request thay vi mo them ket noi rieng.

builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IFamilyContext, FamilyContext>();
builder.Services.AddScoped<IFamilyService, FamilyService>();
builder.Services.AddScoped<ISpaceContext, SpaceContext>();
builder.Services.AddScoped<ISpaceService, SpaceService>();

// ─── Xac thuc ─────────────────────────────────────────────────────────────────

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = OpaqueAccessTokenAuthenticationHandler.SchemeName;
    options.DefaultChallengeScheme = OpaqueAccessTokenAuthenticationHandler.SchemeName;
});

// Opaque access token: xem chu thich trong OpaqueAccessTokenAuthenticationHandler.
authentication.AddScheme<AuthenticationSchemeOptions, OpaqueAccessTokenAuthenticationHandler>(
    OpaqueAccessTokenAuthenticationHandler.SchemeName, _ => { });

// Cookie tam giu danh tinh giua luc nha cung cap redirect ve va luc OAuthService doc ho so.
// No song vai giay, khong phai phien dang nhap — phien that la access token opaque cap sau do.
authentication.AddCookie(ExternalAuthScheme, options =>
{
    options.Cookie.Name = "aiokin.external";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
});

// Google/Facebook chi dang ky khi co credential: AddGoogle/AddFacebook nem ngay luc khoi
// dong neu ClientId rong, nen dang ky vo dieu kien se lam app khong boot duoc tren may
// chua cau hinh SSO.
var googleClientId = config["Google:ClientId"];
var googleClientSecret = config["Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = ExternalAuthScheme;
        // OAuthService doc userinfo endpoint bang chinh access token nay.
        options.SaveTokens = true;
    });
}

var facebookAppId = config["Facebook:AppId"];
var facebookAppSecret = config["Facebook:AppSecret"];
if (!string.IsNullOrWhiteSpace(facebookAppId) && !string.IsNullOrWhiteSpace(facebookAppSecret))
{
    authentication.AddFacebook(options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = facebookAppSecret;
        options.SignInScheme = ExternalAuthScheme;
        // OAuthService goi Graph API bang access token nay.
        options.SaveTokens = true;
    });
}

builder.Services.AddAuthorization();

// ─── CORS ─────────────────────────────────────────────────────────────────────
//
// App Android khong bi CORS rang buoc, nhung web app dung chung bo rule CASL thi co.

// appsettings khai "AllowedOrigins" la mot chuoi ngan cach bang dau phay, khong phai mang
// JSON — Get<string[]>() tren mot chuoi tra ve null va CORS se am tham roi ve AllowAnyOrigin,
// nen phai tu tach. Van chap nhan dang mang de doi sang no sau nay khong pha cau hinh cu.
var allowedOrigins = config.GetSection("AllowedOrigins").Get<string[]>()
    ?? (config["AllowedOrigins"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// Frontend:Origin la origin cua web app dung chung bo rule CASL; gop vao cho khoi phai khai
// cung mot dia chi o hai noi.
var frontendOrigin = config["Frontend:Origin"];
if (!string.IsNullOrWhiteSpace(frontendOrigin))
    allowedOrigins = [.. allowedOrigins, frontendOrigin.TrimEnd('/')];

allowedOrigins = [.. allowedOrigins.Distinct(StringComparer.OrdinalIgnoreCase)];

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    }
    else
    {
        // Chua cau hinh origin nao — mo cho may dev, nhung khong kem AllowCredentials vi
        // trinh duyet tu choi cap "*" di cung credentials.
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    }
}));

var app = builder.Build();

// ─── Khoi tao database ────────────────────────────────────────────────────────

// Dev: tu migrate/seed cho tien, khong ai khac dung chung DB. Ngoai Dev (staging/prod):
// chi chay khi RUN_MIGRATIONS=true duoc set tuong minh luc deploy — tranh nhieu instance
// cung migrate mot luc va tranh mot lan migrate loi keo sap ca app dang chay on dinh.
// Quy trinh chuan: chay `dotnet ef database update` (hoac set RUN_MIGRATIONS=true mot lan)
// nhu mot buoc rieng TRUOC khi deploy, tach khoi vong doi khoi dong app.
var shouldMigrateOnStartup = app.Environment.IsDevelopment()
    || string.Equals(config["RUN_MIGRATIONS"], "true", StringComparison.OrdinalIgnoreCase);

if (shouldMigrateOnStartup)
{
    await InitializeDatabaseAsync(app);
}
else
{
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogInformation(
        "Bo qua migrate/seed luc khoi dong ({Environment}, RUN_MIGRATIONS khong phai 'true'). "
        + "Chay migration nhu buoc rieng truoc khi deploy: dotnet ef database update.",
        app.Environment.EnvironmentName);
}

// ─── Pipeline ─────────────────────────────────────────────────────────────────

// Truoc moi thu: khoi phuc scheme va IP that tu proxy, de cac tang sau nhin thay dung
// client chu khong phai nhin thay proxy.
app.UseForwardedHeaders();

// Dat dau tien: chi cai nam truoc moi bat duoc ngoai le cua cai nam sau.
app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "AioKin API v1"));
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicy);

// Sau UseCors de phan hoi 429 van mang header CORS — thieu no thi trinh duyet bao loi
// CORS thay vi hien dung "ban thao tac qua nhanh". Truoc UseAuthentication de tu choi
// som, khoi ton cong tra cuu Redis cho request se bi chan.
app.UseRateLimiter();

// UseAuthentication truoc UseAuthorization — thu tu bat buoc cua ASP.NET Core.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.Run();

// ─── Ham ho tro ───────────────────────────────────────────────────────────────

// Mo ket noi Redis mot lan luc khoi dong de biet chac no dung duoc. Tra null neu khong
// ket noi duoc, de phia goi quyet dinh: dev thi roi ve MemoryCache, production thi dung.
static async Task<IConnectionMultiplexer?> TryConnectRedisAsync(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return null;

    try
    {
        var options = ConfigurationOptions.Parse(connectionString);

        // Van giu AbortOnConnectFail=false cho vong doi ve sau: mot lan mat ket noi thoang
        // qua khong duoc lam hong multiplexer da tao.
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 3000;
        options.ConnectRetry = 1;

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);

        // ConnectAsync van tra ve multiplexer ke ca khi chua bat tay duoc — PING moi phan
        // biet duoc "co ket noi" voi "co doi tuong ket noi".
        await multiplexer.GetDatabase().PingAsync();
        return multiplexer;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Redis] Khong ket noi duoc ({ex.GetType().Name}: {ex.Message}).");
        return null;
    }
}

// Chuoi ket noi Postgres, uu tien cau hinh tuong minh roi moi den DATABASE_URL.
// Nhieu nha cung cap (Supabase, Railway, Render) chi phat URL kieu
// postgres://user:pass@host:port/db — dang ma Npgsql khong doc duoc — nen doi sang
// dang key=value ngay tai day thay vi bat nguoi trien khai tu viet lai.
static string? ResolvePostgresConnectionString(IConfiguration config)
{
    var direct = config.GetConnectionString("DefaultConnection")
        ?? config.GetConnectionString("Postgres");

    if (!string.IsNullOrWhiteSpace(direct))
        return direct;

    var url = config["DATABASE_URL"];
    if (string.IsNullOrWhiteSpace(url))
        return null;

    if (!url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        // Da la dang key=value san.
        return url;
    }

    var uri = new Uri(url);
    var credentials = uri.UserInfo.Split(':', 2);

    var parts = new List<string>
    {
        $"Host={uri.Host}",
        $"Port={(uri.Port > 0 ? uri.Port : 5432)}",
        $"Database={uri.AbsolutePath.TrimStart('/')}",
        $"Username={Uri.UnescapeDataString(credentials[0])}"
    };

    if (credentials.Length > 1)
        parts.Add($"Password={Uri.UnescapeDataString(credentials[1])}");

    // Managed Postgres gan nhu luon bat TLS, va thuong bang chung chi tu ky.
    parts.Add("SSL Mode=Require");
    parts.Add("Trust Server Certificate=true");

    return string.Join(";", parts);
}

// Dung schema roi seed du lieu toi thieu, truoc khi nhan request dau tien: khong co role
// va SuperAdmin thi khong ai dang nhap duoc de tao chung.
static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var db = services.GetRequiredService<AioKinDbContext>();

    try
    {
        // Chua sinh migration nao thi MigrateAsync() khong tao ra bang nao, va buoc seed
        // ngay sau se chet vi thieu bang — EnsureCreated dung schema thang tu model.
        // Sinh migration dau tien (dotnet ef migrations add ...) thi nhanh nay tu tat.
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            logger.LogWarning(
                "Chua co EF migration nao — dung EnsureCreated() de tao schema. "
                + "Truoc khi len production hay chay: dotnet ef migrations add InitialCreate");
            await db.Database.EnsureCreatedAsync();
        }

        await DbSeeder.SeedAsync(db, app.Configuration, logger);
    }
    catch (Exception ex)
    {
        // Nem lai: chay tiep voi database hong thi moi request deu 500, va nguyen nhan that
        // su bi chon o day chu khong hien ra o cho nao khac.
        logger.LogCritical(ex, "Khoi tao database that bai — kiem tra chuoi ket noi Postgres.");
        throw;
    }
}

/// <summary>
/// Top-level statement sinh ra class Program voi pham vi internal, ma
/// WebApplicationFactory&lt;Program&gt; thi can no public. Khai bao partial nay chi de mo
/// pham vi — khong them thanh vien nao.
/// </summary>
public partial class Program { }
