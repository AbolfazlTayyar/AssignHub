using System.Text.Json.Serialization;
using AssignHub.Api.Contracts;
using AssignHub.Api.Middleware;
using AssignHub.Api.Queue;
using AssignHub.Api.Startup;
using AssignHub.Application;
using AssignHub.Infrastructure;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.AddHostedService<QueueSweeper>();

builder.Services
    .AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)))
    .ConfigureApiBehaviorOptions(o =>
    {
        // SPEC §9.0: bare status codes (404, 415, ...) get the envelope from UseStatusCodePages below.
        o.SuppressMapClientErrors = true;
        o.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
            ApiResponse.Failure(ModelStateErrors.From(context.ModelState)));
    });
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseStatusCodePages(context => context.HttpContext.Response.WriteAsJsonAsync(
    ApiResponse.Failure(StatusMessages.For(context.HttpContext.Response.StatusCode)),
    context.HttpContext.RequestAborted));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "AssignHub"));
}

app.MapControllers();

await app.InitializeDatabaseAsync(app.Lifetime.ApplicationStopping);
await app.RunAsync();
