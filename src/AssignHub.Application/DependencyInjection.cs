using AssignHub.Application.Experts;
using AssignHub.Application.Queue;
using AssignHub.Application.Requests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssignHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<QueueOptions>()
            .Bind(configuration.GetSection(QueueOptions.SectionName))
            .Validate(o => o.AgingThreshold >= TimeSpan.Zero, "Queue:AgingThreshold must not be negative.")
            .Validate(o => o.SweepInterval > TimeSpan.Zero, "Queue:SweepInterval must be positive.")
            .Validate(o => o.BatchSize >= 1, "Queue:BatchSize must be at least 1.")
            .ValidateOnStart();

        services.AddScoped<QueueDrainer>();
        services.AddScoped<RequestService>();
        services.AddScoped<ExpertService>();

        return services;
    }
}
