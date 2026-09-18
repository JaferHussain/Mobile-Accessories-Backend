using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using MoizPos.Api.Authorization;
using MoizPos.Migrator;
using MoizPos.Api.Controllers;
using MoizPos.Api.Middleware;
using MoizPos.Application.Abstractions;
using MoizPos.Application.Contracts.Auth;
using MoizPos.Application.Contracts.Common;
using MoizPos.Application.Services;
using MoizPos.Application.Time;
using MoizPos.Domain.Errors;
using MoizPos.Infrastructure.Auth;
using MoizPos.Infrastructure.Backup;
using MoizPos.Infrastructure.Data;
using MoizPos.Infrastructure.Repositories;
using MoizPos.Application.Documents;
using MoizPos.Infrastructure.Documents;
using MoizPos.Infrastructure.Storage;
using QuestPDF.Infrastructure;
using Serilog;

// QuestPDF Community licence: free below USD 1M annual revenue (research.md R3).
QuestPDF.Settings.License = LicenseType.Community;

// The database migrator lives in this same project (Constitution: schema changes only ever reach
// a database through it). Reached as `dotnet run --project backend/src/MoizPos -- migrate`, it
// applies the scripts and exits without ever starting the web host.
if (args.Length > 0 && args[0].Equals("migrate", StringComparison.OrdinalIgnoreCase))
{
    return MigratorCli.Run(args[1..]);
}

var builder = WebApplication.CreateBuilder(args);

// A per-machine settings file. Added last, so it overrides everything before it — including
// user-secrets — which makes it the single place to look when asking "what is this machine
// actually using?". It is gitignored, which is the point: THIS machine's connection string lives
// in the project as an ordinary settings file, visible and editable, but never pushed.
// Optional — nothing breaks if it does not exist.
//
// The integration test host MUST opt out via SkipMachineLocalSettings. Its own connection string
// arrives through ConfigureAppConfiguration, which is applied AFTER this file, and the string is
// read a few lines below to build the connection factory — so without the opt-out this file wins
// and the whole suite runs against whatever database this machine happens to name. That is not
// hypothetical: it pointed the suite at the live server once, and 201 tests failed at login
// because their users existed in the test database instead.
if (!builder.Configuration.GetValue<bool>("SkipMachineLocalSettings"))
{
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        optional: true,
        reloadOnChange: true);
}

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// ---------------------------------------------------------------- configuration
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. Set it with: " +
        "dotnet user-secrets set \"ConnectionStrings:Default\" \"Server=localhost;Database=moizpos;...\"");

var jwtOptions = new JwtOptions
{
    Issuer = builder.Configuration["Jwt:Issuer"] ?? "MoizPos",
    Audience = builder.Configuration["Jwt:Audience"] ?? "MoizPosCounter",
    Key = builder.Configuration["Jwt:Key"] ?? string.Empty,
    AccessTokenMinutes = int.TryParse(builder.Configuration["Jwt:AccessTokenMinutes"], out var m) ? m : 60,
    RefreshTokenDays = int.TryParse(builder.Configuration["Jwt:RefreshTokenDays"], out var d) ? d : 30,
};

jwtOptions.Validate();

DapperConfig.Apply();

// ---------------------------------------------------------------- services
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<PeriodResolver>();
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new MySqlConnectionFactory(connectionString));
builder.Services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

builder.Services.AddSingleton<IStockMovementWriter, StockMovementWriter>();
builder.Services.AddSingleton<IAuditWriter, AuditWriter>();
builder.Services.AddSingleton<IPurchaseWriteRepository, PurchaseWriteRepository>();
builder.Services.AddSingleton<IStockWriteRepository, StockWriteRepository>();
builder.Services.AddSingleton<IInvoiceWriteRepository, InvoiceWriteRepository>();
builder.Services.AddSingleton<ICustomerPaymentWriteRepository, CustomerPaymentWriteRepository>();
builder.Services.AddSingleton<IReturnWriteRepository, ReturnWriteRepository>();

builder.Services.AddSingleton<IPdfRenderer, PdfRenderer>();
builder.Services.AddSingleton<IPdfRendererPort, PdfRendererAdapter>();

builder.Services.AddSingleton(new DocumentOptions
{
    PublicBaseUrl = builder.Configuration["Documents:PublicBaseUrl"] ?? string.Empty,
    ShareLinkExpiryDays =
        int.TryParse(builder.Configuration["Documents:ShareLinkExpiryDays"], out var shareDays)
            ? shareDays
            : 30,
    Shop = new ShopDetails
    {
        Name = builder.Configuration["Shop:Name"] ?? "Moiz Mobile & Corporation",
        Location = builder.Configuration["Shop:Location"] ?? "Danwran Lodhran",
        ContactNumber = builder.Configuration["Shop:ContactNumber"],
    },
});

var backupOptions = new BackupOptions
{
    Directory = builder.Configuration["Backup:Directory"] ?? "backups",
    RetainDays = int.TryParse(builder.Configuration["Backup:RetainDays"], out var retain) ? retain : 30,
    RunAtLocalHour = int.TryParse(builder.Configuration["Backup:RunAtLocalHour"], out var hour) ? hour : 2,
    // Empty means the MySQL client tools are already on PATH, which is the server case.
    ToolsDirectory = builder.Configuration["Backup:ToolsDirectory"] ?? string.Empty,
};

builder.Services.AddSingleton(backupOptions);

builder.Services.AddSingleton<IBackupService>(provider => new BackupService(
    backupOptions,
    connectionString,
    provider.GetRequiredService<IClock>(),
    provider.GetRequiredService<PeriodResolver>()));

builder.Services.AddSingleton(provider => new BackupSchedule(
    backupOptions.RunAtLocalHour, provider.GetRequiredService<PeriodResolver>()));

builder.Services.AddHostedService<BackupHostedService>();

builder.Services.AddSingleton(new ImageStorageOptions
{
    ProductImageRoot = builder.Configuration["Storage:ProductImageRoot"] ?? "content/products",
    MaxImageBytes = long.TryParse(builder.Configuration["Storage:MaxImageBytes"], out var maxBytes)
        ? maxBytes
        : 2 * 1024 * 1024,
});

builder.Services.AddSingleton<IImageStorageService>(provider => new ImageStorageService(
    provider.GetRequiredService<ImageStorageOptions>(),
    provider.GetRequiredService<IWebHostEnvironment>().ContentRootPath));

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IBrandRepository, BrandRepository>();
builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<IStockMovementRepository, StockMovementRepository>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IBrandService, BrandService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IInvoiceReadRepository, InvoiceReadRepository>();
builder.Services.AddScoped<ILedgerRepository, LedgerRepository>();
builder.Services.AddScoped<ICustomerLedgerService, CustomerLedgerService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IReturnService, ReturnService>();
builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
builder.Services.AddScoped<IReportRepository, ReportRepository>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IAuditRepository, AuditRepository>();
builder.Services.AddScoped<IDocumentTokenRepository, DocumentTokenRepository>();
builder.Services.AddScoped<ICustomerPaymentReadRepository, CustomerPaymentReadRepository>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<IAuthService>(provider => new AuthService(
    provider.GetRequiredService<IUserRepository>(),
    provider.GetRequiredService<IRefreshTokenRepository>(),
    provider.GetRequiredService<IPasswordHasher>(),
    provider.GetRequiredService<ITokenService>(),
    provider.GetRequiredService<IClock>(),
    jwtOptions.RefreshTokenDays));

// ---------------------------------------------------------------- auth
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            // No grace period on expiry: the default 5 minutes would silently extend every token.
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(Roles.Admin))
    // Authenticated by default: an endpoint must opt out with [AllowAnonymous], so forgetting
    // an attribute fails closed rather than exposing data (FR-038).
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// ---------------------------------------------------------------- mvc
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        // Enums travel as names, not numbers: contracts/openapi.yaml documents
        // "Cash"/"Partial"/"Credit", the counter sends those strings, and a number in a stored
        // JSON payload would be meaningless to anyone reading it later.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddFluentValidationAutoValidation();

// BOTH assemblies. Request contracts live in Application (LoginRequest, ProductUpsertRequest)
// and in Api next to their controllers (CreateInvoiceRequest, RestoreBackupRequest, ...).
// Scanning only one silently leaves the other half's rules unenforced.
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<RestoreBackupValidator>();

// Model-binding and validation failures must use the same envelope as everything else.
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                new ErrorDetail(entry.Key, error.ErrorMessage)))
            .ToList();

        var body = ApiResponse<object>.Fail(
            ErrorCodes.ValidationFailed,
            "One or more fields are invalid.",
            details,
            context.HttpContext.TraceIdentifier);

        return new BadRequestObjectResult(body);
    });

// The public document endpoint is the only unauthenticated data route, so it is the only one a
// stranger can probe. Rate limiting caps how fast tokens could be guessed.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter(PublicDocumentsController.RateLimitPolicy, limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string CounterCorsPolicy = "CounterApp";
builder.Services.AddCors(options =>
    options.AddPolicy(CounterCorsPolicy, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

// ---------------------------------------------------------------- pipeline
app.UseExceptionEnvelope();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors(CounterCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(ApiResponse<object>.Ok(new { status = "healthy" })))
   .AllowAnonymous()
   .WithName("Health");

// ---------------------------------------------------------------- first run
await using (var scope = app.Services.CreateAsyncScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

    if (await seeder.SeedAsync())
    {
        // Uses the application's logger, not Serilog's static Log — the static logger is never
        // initialised here, so a warning sent there would silently vanish.
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MoizPos.Startup")
            .LogWarning(
                "Created the default administrator '{Username}' with password '{Password}'. " +
                "CHANGE THIS PASSWORD before the shop uses the system.",
                DatabaseSeeder.DefaultUsername,
                DatabaseSeeder.DefaultPassword);
    }
}

app.Run();

return 0;

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
