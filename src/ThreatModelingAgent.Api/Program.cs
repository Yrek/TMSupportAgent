using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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
    var workosJwksUri = builder.Configuration["WorkOS:JwksUri"]
        ?? throw new InvalidOperationException("WorkOS:JwksUri is required.");
    var workosIssuer = builder.Configuration["WorkOS:Issuer"]
        ?? throw new InvalidOperationException("WorkOS:Issuer is required.");

    // ── Infrastructure (DB, repos, audit logger) ────────────────────────────
    builder.Services.AddInfrastructure(builder.Configuration);

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
            options.Authority = workosIssuer;
            options.Audience = workosClientId;
            options.MetadataAddress = workosJwksUri;
            options.RequireHttpsMetadata = true; // MUST NOT disable (CLAUDE.md §11.4)
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuer = workosIssuer,
                ValidateAudience = true,
                ValidAudience = workosClientId,
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
        // PlatformAdmin policy: JWT must carry role = "platform:admin"
        // WorkOS may map this to ClaimTypes.Role or the raw "role" claim depending on version.
        // Used exclusively by AdminController. TenantContextMiddleware is defence-in-depth:
        // it also rejects admin tokens on org-scoped routes before the controller even runs.
        options.AddPolicy("PlatformAdmin", policy =>
            policy.RequireAssertion(ctx =>
                ctx.User.HasClaim(System.Security.Claims.ClaimTypes.Role, "platform:admin") ||
                ctx.User.HasClaim("role", "platform:admin")));
    });

    // ── FluentValidation ────────────────────────────────────────────────────
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // ── Rate limiting — app-layer fixed window (CLAUDE.md §9.1, OD-6) ──────
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

        // General API: 60 req/min per IP
        options.AddFixedWindowLimiter("api", o =>
        {
            o.Window = TimeSpan.FromMinutes(1);
            o.PermitLimit = 60;
            o.QueueLimit = 0;
            o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        });

        // Job submission / sensitive operations: 10 req/min per IP (CLAUDE.md §9.1)
        options.AddFixedWindowLimiter("strict", o =>
        {
            o.Window = TimeSpan.FromMinutes(1);
            o.PermitLimit = 10;
            o.QueueLimit = 0;
            o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        });
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

    var app = builder.Build();

    // ── Middleware pipeline — ORDER MATTERS ──────────────────────────────────

    // 1. Correlation ID first — all subsequent logs include it (CLAUDE.md §10.5)
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 2. Security headers on every response (CLAUDE.md §11)
    app.UseMiddleware<SecurityHeadersMiddleware>();

    // 3. Exception handler — before anything that can throw
    app.UseExceptionHandler();

    // 4. Rate limiting
    app.UseRateLimiter();

    // 5. HTTPS redirect (CLAUDE.md §11.4)
    app.UseHttpsRedirection();

    // 6. Authentication — validates JWT against WorkOS JWKS
    app.UseAuthentication();

    // 7. Tenant context — reads org_id from the now-validated JWT
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
