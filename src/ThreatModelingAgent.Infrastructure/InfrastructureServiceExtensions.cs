using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Infrastructure.Messaging;
using ThreatModelingAgent.Infrastructure.Persistence;
using ThreatModelingAgent.Infrastructure.Persistence.Repositories;
using ThreatModelingAgent.Infrastructure.Services;

namespace ThreatModelingAgent.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is missing. " +
                "Application cannot start without a database connection. (CLAUDE.md §4.3 Fail Secure)");

        services.AddScoped<RlsSessionInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(sp.GetRequiredService<RlsSessionInterceptor>());
        });

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IArchitectureRepository, ArchitectureRepository>();
        services.AddScoped<IThreatRepository, ThreatRepository>();
        services.AddScoped<IIdpConfigRepository, IdpConfigRepository>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddSingleton<IBlobStorage, AzureBlobStorageService>();
        services.AddSingleton<IJobQueue, ServiceBusJobQueue>();
        services.AddScoped<IWorkOsClient, WorkOsHttpClient>();

        return services;
    }
}
