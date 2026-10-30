using AssignHub.Application.Abstractions;
using AssignHub.Infrastructure.Persistence;
using AssignHub.Infrastructure.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssignHub.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AssignHubDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

            // SPEC §6.1: transient errors and deadlock victims (1205) are retried by the execution strategy.
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
        });

        services.AddOptions<AssignmentOptions>()
            .Bind(configuration.GetSection(AssignmentOptions.SectionName))
            .Validate(o => o.MaxCandidateRefreshes >= 1, "Assignment:MaxCandidateRefreshes must be at least 1.")
            .ValidateOnStart();

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IRequestStore, RequestStore>();
        services.AddScoped<IExpertStore, ExpertStore>();
        services.AddScoped<IAssignmentStore, AssignmentStore>();

        return services;
    }
}
