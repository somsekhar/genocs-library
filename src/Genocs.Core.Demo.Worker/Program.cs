using Genocs.Core.Demo.Contracts;
using Genocs.Core.Demo.Domain.Aggregates;
using Genocs.Core.Demo.Worker;
using Genocs.Core.Demo.Worker.Consumers;
using Genocs.Core.Demo.Worker.Handlers;
using Genocs.Core.Domain.Repositories;
using Genocs.Core.Interfaces;
using Genocs.Monitoring;
using Genocs.Persistence.MongoDb.Extensions;
using Genocs.ServiceBusAzure.Options;
using Genocs.ServiceBusAzure.Queues;
using Genocs.ServiceBusAzure.Queues.Interfaces;
using Genocs.ServiceBusAzure.Topics;
using Genocs.ServiceBusAzure.Topics.Interfaces;
using MassTransit;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using Serilog;
using Serilog.Events;
using System.Reflection;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();


IHost host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, lc) =>
    {
        lc.WriteTo.Console();

        string? applicationInsightsConnectionString = ctx.Configuration.GetConnectionString("ApplicationInsights");

        if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
        {
            lc.WriteTo.ApplicationInsights(new TelemetryConfiguration
            {
                ConnectionString = applicationInsightsConnectionString
            }, TelemetryConverter.Traces);
        }
    })
    .ConfigureServices((hostContext, services) =>
    {
        // Run the hosted service 
        services.AddHostedService<MassTransitConsoleHostedService>();

        // It adds the MongoDb Repository to the project and register all the Domain Objects with the standard interface
        services.AddMongoDatabase(hostContext.Configuration);

        // It registers the repositories that has been overridden
        // No need in whenever only 
        services.RegisterRepositories(Assembly.GetExecutingAssembly());

        RegisterCustomMongoRepository(services, hostContext.Configuration);

        ConfigureMassTransit(services, hostContext.Configuration);
        //ConfigureAzureServiceBusTopic(services, hostContext.Configuration);
        //ConfigureAzureServiceBusQueue(services, hostContext.Configuration);

        services.AddCustomOpenTelemetry(hostContext.Configuration);
    })
    .Build();

await host.RunAsync();

Log.CloseAndFlush();


static IServiceCollection ConfigureMassTransit(IServiceCollection services, IConfiguration configuration)
{
    services.TryAddSingleton(KebabCaseEndpointNameFormatter.Instance);
    services.AddMassTransit(cfg =>
    {
        // Consumer configuration
        cfg.AddConsumersFromNamespaceContaining<SubmitOrderConsumer>();

        // Set the transport
        cfg.UsingRabbitMq(ConfigureBus);
    });

    return services;
}

static IServiceCollection RegisterCustomMongoRepository(IServiceCollection services, IConfiguration configuration)
{
    services.AddScoped<IRepository<Order, ObjectId>, Genocs.Persistence.MongoDb.Repositories.MongoDbRepository<Order>>();

    return services;
}

static void ConfigureBus(IBusRegistrationContext context, IRabbitMqBusFactoryConfigurator configurator)
{
    //configurator.UseMessageData(new MongoDbMessageDataRepository("mongodb://127.0.0.1", "attachments"));

    //configurator.ReceiveEndpoint(KebabCaseEndpointNameFormatter.Instance.Consumer<RoutingSlipBatchEventConsumer>(), e =>
    //{
    //    e.PrefetchCount = 20;

    //    e.Batch<RoutingSlipCompleted>(b =>
    //    {
    //        b.MessageLimit = 10;
    //        b.TimeLimit = TimeSpan.FromSeconds(5);

    //        b.Consumer<RoutingSlipBatchEventConsumer, RoutingSlipCompleted>(context);
    //    });
    //});

    // This configuration allow to handle the Scheduling
    configurator.UseMessageScheduler(new Uri("queue:quartz"));

    // This configuration will configure the Activity Definition
    configurator.ConfigureEndpoints(context);
}

static void ConfigureAzureServiceBusTopic(IServiceCollection services, IConfiguration configuration)
{
    services.Configure<AzureServiceBusTopicSettings>(configuration.GetSection(AzureServiceBusTopicSettings.Position));

    services.AddSingleton<IAzureServiceBusTopic, AzureServiceBusTopic>();

    services.AddScoped<IEventHandler<DemoEvent>, DemoEventHandler>();

    var topicBus = services.BuildServiceProvider().GetRequiredService<IAzureServiceBusTopic>();
    topicBus.Subscribe<DemoEvent, IEventHandler<DemoEvent>>();

}

static void ConfigureAzureServiceBusQueue(IServiceCollection services, IConfiguration configuration)
{
    services.Configure<AzureServiceBusQueueSettings>(configuration.GetSection(AzureServiceBusQueueSettings.Position));

    services.AddSingleton<IAzureServiceBusQueue, AzureServiceBusQueue>();

    services.AddScoped<ICommandHandler<DemoCommand>, DemoCommandHandler>();

    var queueBus = services.BuildServiceProvider().GetRequiredService<IAzureServiceBusQueue>();
    queueBus.Consume<DemoCommand, ICommandHandler<DemoCommand>>();
}
