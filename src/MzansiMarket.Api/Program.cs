using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;
using MzansiMarket.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var configuredConnectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["DATABASE_URL"];

if (string.IsNullOrWhiteSpace(configuredConnectionString)
    && builder.Environment.IsEnvironment("Testing"))
{
    configuredConnectionString = "Host=localhost;Database=unused;Username=postgres;Password=test-only";
}

if (string.IsNullOrWhiteSpace(configuredConnectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings__DefaultConnection or DATABASE_URL. " +
        "Database credentials must not be committed to source control.");
}

var connectionString = PostgresConnectionString.Normalize(configuredConnectionString);

builder.Services.AddDbContext<MarketplaceDbContext>(options =>
    options.UseNpgsql(connectionString, postgres =>
    {
        postgres.SetPostgresVersion(18, 0);
        postgres.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
    }));

builder.Services.AddDataProtection()
    .SetApplicationName("MzansiMarket.Api")
    .PersistKeysToDbContext<MarketplaceDbContext>();

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<MarketplaceDbContext>();

builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
{
    options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
    options.RefreshTokenExpiration = TimeSpan.FromDays(14);
});

builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
builder.Services.AddScoped<IAuthorizationHandler, MarketplaceAuthorizationHandler>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("CustomerWeb", policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permitLimit = builder.Configuration.GetValue<int?>("RateLimiting:AuthenticationPermitLimit") ?? 10;
    options.AddPolicy("authentication", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<MarketplaceDbContext>("postgres");

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseCors("CustomerWeb");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "Mzansi Market API",
    database = "PostgreSQL",
    environment = app.Environment.EnvironmentName
})).AllowAnonymous();
app.MapHealthChecks("/health/database").AllowAnonymous();
app.MapAuthEndpoints();
app.MapCatalogueEndpoints();
app.MapAccountEndpoints();
app.MapCartEndpoints();
app.MapCheckoutEndpoints();
app.MapPaymentEndpoints();
app.MapFulfilmentEndpoints();

app.Run();

public partial class Program;
