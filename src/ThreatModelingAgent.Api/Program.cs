using System.Security.Claims;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using Serilog;
using Serilog.Events;
using ThreatModelingAgent.Api.Errors;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Infrastructure;
using ThreatModelingAgent.Infrastructure.Persistence;

// ── Bootstrap Serilog before anything else so startup errors are captured ──
// Note: CreateLogger() (not CreateBootstrapLogger()) is used intentionally.
// CreateBootstrapLogger() produces a ReloadableLogger that UseSerilog() later
// freezes via a direct Log.Logger reference — not through DI. When multiple
// WebApplicationFactory instances run in the same process (parallel xUnit
// integration test classes), they race to set and freeze the same static
// Log.Logger, causing "The logger is already frozen." CreateLogger() produces
// a plain Logger; UseSerilog() skips the Freeze() path entirely.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog structured logging (CLAUDE.md §10.2) ────────────────────────
    builder.Host.UseSerilog((ctx, services, config) =>
    {
        config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "api")
            .WriteTo.Console();

        var aiConnectionString = ctx.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrEmpty(aiConnectionString))
            config.WriteTo.ApplicationInsights(aiConnectionString,
                TelemetryConverter.Traces);
    });

    // ── Validate required config at startup — fail closed (CLAUDE.md §4.3) ──
    var workosClientId = builder.Configuration["WorkOS:ClientId"]
        ?? throw new InvalidOperationException("WorkOS:ClientId is required.");
    var workosIssuer = builder.Configuration["WorkOS:Issuer"]
        ?? throw new InvalidOperationException("WorkOS:Issuer is required.");

    static string NormalizeNoTrailingSlash(string value) => value.Trim().TrimEnd('/');
    var configuredIssuer = NormalizeNoTrailingSlash(workosIssuer);
    var configuredClientIssuerPrefix = "/user_management/client_";

    var clientIssuer = configuredIssuer.Contains(configuredClientIssuerPrefix, StringComparison.OrdinalIgnoreCase)
        ? configuredIssuer
        : $"{configuredIssuer}/user_management/{workosClientId}";

    var platformIssuer = configuredIssuer.Contains(configuredClientIssuerPrefix, StringComparison.OrdinalIgnoreCase)
        ? configuredIssuer[..configuredIssuer.IndexOf(configuredClientIssuerPrefix, StringComparison.OrdinalIgnoreCase)]
        : configuredIssuer;

    var validIssuers = new[] { platformIssuer, clientIssuer };

    // ── Infrastructure (DB, repos, audit logger) ────────────────────────────
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddHttpContextAccessor();

    // ── WorkOS HTTP client — explicit timeout (CLAUDE.md §9.8) ─────────────
    builder.Services.AddHttpClient("WorkOS", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(15);
    });

    // ── Tenant context — scoped, populated from JWT by middleware ───────────
    builder.Services.AddScoped<TenantContext>();
    builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

    // ── JWT authentication — WorkOS JWKS (CLAUDE.md §8.1) ──────────────────
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = clientIssuer;
            options.Audience = workosClientId;
            options.MetadataAddress = $"{clientIssuer}/.well-known/openid-configuration";
            options.RequireHttpsMetadata = true; // MUST NOT disable (CLAUDE.md §11.4)
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuers = validIssuers,
                ValidateAudience = true,
                ValidAudience = workosClientId,
                // WorkOS tokens may carry client_id while aud can be omitted depending on flow.
                AudienceValidator = (audiences, securityToken, _) =>
                {
                    if (audiences?.Any(a => string.Equals(a, workosClientId, StringComparison.Ordinal)) == true)
                        return true;

                    var clientIdClaim = securityToken switch
                    {
                        JsonWebToken jwt => jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value,
                        _ => null
                    };

                    return string.Equals(clientIdClaim, workosClientId, StringComparison.Ordinal);
                },
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = ctx =>
                {
                    var correlationId = ctx.HttpContext.Items["CorrelationId"];
                    Log.Warning(
                        "JWT authentication failed. CorrelationId={CorrelationId} ErrorType={ErrorType}",
                        correlationId,
                        ctx.Exception.GetType().Name); // type only — not message (may contain token)
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        // PlatformAdmin policy: accept both role naming variants and permission claim.
        // Used exclusively by AdminController. TenantContextMiddleware is defence-in-depth:
        // it also rejects admin tokens on org-scoped routes before the controller even runs.
        options.AddPolicy("PlatformAdmin", policy =>
            policy.RequireAssertion(ctx => ctx.User.IsPlatformAdmin()));
    });

    // ── FluentValidation ────────────────────────────────────────────────────
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // ── Rate limiting — app-layer fixed window (CLAUDE.md §9.1, OD-6) ──────
    static string GetRateLimitPartitionKey(HttpContext context)
    {
        // Prefer authenticated user identity when available so test/automation traffic
        // does not collapse into one shared loopback bucket. Fallback remains per-IP.
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
            return $"user:{userId}";

        var ip = context.Connection.RemoteIpAddress?.ToString();
        return $"ip:{(string.IsNullOrWhiteSpace(ip) ? "unknown" : ip)}";
    }

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.Headers.RetryAfter = "60";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new
            {
                code = "RATE_LIMIT_EXCEEDED",
                message = "Too many requests. Please retry after 60 seconds."
            }, ct);
        };

        // General API: 60 req/min per caller partition.
        options.AddPolicy("api", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: GetRateLimitPartitionKey(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 60,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));

        // Job submission / sensitive operations: 10 req/min per caller partition.
        options.AddPolicy("strict", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: GetRateLimitPartitionKey(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 10,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));
    });

    // ── Exception handling — no internal detail to clients (CLAUDE.md §7.6) ─
    builder.Services.AddExceptionHandler<ProblemDetailsErrorHandler>();
    builder.Services.AddProblemDetails();

    // ── Request body size enforced at middleware (CLAUDE.md §9.7) ───────────
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.Limits.MaxRequestBodySize = 11 * 1024 * 1024; // 11 MB; endpoint max is 10 MB
    });

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    // CORS for browser frontend (local dev + configured origins in higher envs)
    var corsOrigins =
        builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? [];
    if (corsOrigins.Length == 0 && builder.Environment.IsDevelopment())
    {
        corsOrigins =
        [
            "http://localhost:5173",
            "https://localhost:5173",
            "http://localhost:4173",
            "https://localhost:4173"
        ];
    }

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy =>
        {
            if (corsOrigins.Length == 0)
                return;

            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });

    var app = builder.Build();

    // ── Middleware pipeline — ORDER MATTERS ──────────────────────────────────

    // 1. Correlation ID first — all subsequent logs include it (CLAUDE.md §10.5)
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 2. Security headers on every response (CLAUDE.md §11)
    app.UseMiddleware<SecurityHeadersMiddleware>();

    // 3. Exception handler — before anything that can throw
    app.UseExceptionHandler();
    // 4. HTTPS redirect (CLAUDE.md ?11.4)
    app.UseHttpsRedirection();

    // 4.5 CORS for browser-based frontend clients
    app.UseCors("Frontend");

    // 5. Authentication ? validates JWT against WorkOS JWKS
    app.UseAuthentication();

    // 6. Rate limiting (after auth so partitions can use user claims)
    app.UseRateLimiter();

    // 7. Tenant context ? reads org_id from the now-validated JWT
    app.UseMiddleware<TenantContextMiddleware>();

    // 8. Authorization
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "API failed to start.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
