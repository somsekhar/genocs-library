using Genocs.Core.Builders;
using Genocs.MessageBrokers.RabbitMQ.Clients;
using Genocs.MessageBrokers.RabbitMQ.Contexts;
using Genocs.MessageBrokers.RabbitMQ.Conventions;
using Genocs.MessageBrokers.RabbitMQ.Initializers;
using Genocs.MessageBrokers.RabbitMQ.Internals;
using Genocs.MessageBrokers.RabbitMQ.Plugins;
using Genocs.MessageBrokers.RabbitMQ.Publishers;
using Genocs.MessageBrokers.RabbitMQ.Serializers;
using Genocs.MessageBrokers.RabbitMQ.Subscribers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Genocs.MessageBrokers.RabbitMQ;

/// <summary>
/// RabbitMQ support helper.
/// </summary>
public static class Extensions
{
    private const string SectionName = "rabbitmq";
    private const string RegistryName = "messageBrokers.rabbitmq";

    /// <summary>
    /// AddRabbitMq extension method.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="sectionName">the default section name.</param>
    /// <param name="plugins">The plugin action method.</param>
    /// <param name="connectionFactoryConfigurator">The configurator.</param>
    /// <param name="serializer">The serializer.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Raised when configuration is incorrect.</exception>
    public static async Task<IGenocsBuilder> AddRabbitMQAsync(
                                            this IGenocsBuilder builder,
                                            string sectionName = SectionName,
                                            Func<IRabbitMqPluginsRegistry, IRabbitMqPluginsRegistry>? plugins = null,
                                            Action<ConnectionFactory>? connectionFactoryConfigurator = null,
                                            IRabbitMQSerializer? serializer = null)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            sectionName = SectionName;
        }

        var options = builder.GetOptions<RabbitMQOptions>(sectionName);
        builder.Services.AddSingleton(options);
        if (!builder.TryRegister(RegistryName))
        {
            return await Task.FromResult(builder);
        }

        if (options.HostNames is null || !options.HostNames.Any())
        {
            throw new ArgumentException("RabbitMQ hostnames are not specified.", nameof(options.HostNames));
        }

        ILogger<IRabbitMQClient> logger;
        using (var serviceProvider = builder.Services.BuildServiceProvider())
        {
            logger = serviceProvider.GetRequiredService<ILogger<IRabbitMQClient>>();
        }

        builder.Services.AddSingleton<IContextProvider, ContextProvider>();
        builder.Services.AddSingleton<ICorrelationContextAccessor>(new CorrelationContextAccessor());
        builder.Services.AddSingleton<IMessagePropertiesAccessor>(new MessagePropertiesAccessor());
        builder.Services.AddSingleton<IConventionsBuilder, ConventionsBuilder>();
        builder.Services.AddSingleton<IConventionsProvider, ConventionsProvider>();
        builder.Services.AddSingleton<IConventionsRegistry, ConventionsRegistry>();
        builder.Services.AddSingleton<IRabbitMQClient, RabbitMQClient>();
        builder.Services.AddSingleton<IBusPublisher, RabbitMQPublisher>();
        builder.Services.AddSingleton<IBusSubscriber, RabbitMQSubscriber>();
        builder.Services.AddSingleton<MessageSubscribersChannel>();
        builder.Services.AddTransient<RabbitMqExchangeInitializer>();
        builder.Services.AddHostedService<RabbitMqBackgroundService>();
        builder.AddInitializer<RabbitMqExchangeInitializer>();

        if (serializer is not null)
        {
            builder.Services.AddSingleton(serializer);
        }
        else
        {
            builder.Services.AddSingleton<IRabbitMQSerializer, SystemTextJsonJsonRabbitMQSerializer>();
        }

        var pluginsRegistry = new RabbitMqPluginsRegistry();
        builder.Services.AddSingleton<IRabbitMqPluginsRegistryAccessor>(pluginsRegistry);
        builder.Services.AddSingleton<IRabbitMqPluginsExecutor, RabbitMqPluginsExecutor>();
        plugins?.Invoke(pluginsRegistry);

        var connectionFactory = new ConnectionFactory
        {
            Port = options.Port,
            VirtualHost = options.VirtualHost,
            UserName = options.Username,
            Password = options.Password,
            RequestedHeartbeat = options.RequestedHeartbeat,
            RequestedConnectionTimeout = options.RequestedConnectionTimeout,
            SocketReadTimeout = options.SocketReadTimeout,
            SocketWriteTimeout = options.SocketWriteTimeout,
            RequestedChannelMax = options.RequestedChannelMax,
            RequestedFrameMax = options.RequestedFrameMax,
            ContinuationTimeout = options.ContinuationTimeout,
            HandshakeContinuationTimeout = options.HandshakeContinuationTimeout,
            NetworkRecoveryInterval = options.NetworkRecoveryInterval,
            Ssl = options.Ssl is null
                ? new SslOption()
                : new SslOption(options.Ssl.ServerName, options.Ssl.CertificatePath, options.Ssl.Enabled)
        };
        ConfigureSsl(connectionFactory, options, logger);
        connectionFactoryConfigurator?.Invoke(connectionFactory);

        logger.LogDebug($"Connecting to RabbitMQ: '{string.Join(", ", options.HostNames)}'...");
        var consumerConnection = await connectionFactory.CreateConnectionAsync(options.HostNames.ToList(), $"{options.ConnectionName}_consumer");
        var producerConnection = await connectionFactory.CreateConnectionAsync(options.HostNames.ToList(), $"{options.ConnectionName}_producer");
        logger.LogDebug($"Connected to RabbitMQ: '{string.Join(", ", options.HostNames)}'.");
        builder.Services.AddSingleton(new ConsumerConnection(consumerConnection));
        builder.Services.AddSingleton(new ProducerConnection(producerConnection));

        ((IRabbitMqPluginsRegistryAccessor)pluginsRegistry).Get().ToList().ForEach(p =>
            builder.Services.AddTransient(p.PluginType));

        return builder;
    }

    private static void ConfigureSsl(
                                    ConnectionFactory connectionFactory,
                                    RabbitMQOptions options,
                                    ILogger<IRabbitMQClient> logger)
    {
        if (options.Ssl is null || string.IsNullOrWhiteSpace(options.Ssl.ServerName))
        {
            connectionFactory.Ssl = new SslOption();
            return;
        }

        connectionFactory.Ssl = new SslOption(
                                              options.Ssl.ServerName,
                                              options.Ssl.CertificatePath,
                                              options.Ssl.Enabled);

        logger.LogDebug($"RabbitMQ SSL is: {(options.Ssl.Enabled ? "enabled" : "disabled")}, " +
                        $"server: '{options.Ssl.ServerName}', client certificate: '{options.Ssl.CertificatePath}', " +
                        $"CA certificate: '{options.Ssl.CaCertificatePath}'.");

        if (string.IsNullOrWhiteSpace(options.Ssl.CaCertificatePath))
        {
            return;
        }

        connectionFactory.Ssl.CertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            if (chain is null)
            {
                return false;
            }

            chain = new X509Chain();
            var certificate2 = new X509Certificate2(certificate);
            var signerCertificate2 = new X509Certificate2(options.Ssl.CaCertificatePath);
            chain.ChainPolicy.ExtraStore.Add(signerCertificate2);
            chain.Build(certificate2);
            var ignoredStatuses = Enumerable.Empty<X509ChainStatusFlags>();
            if (options.Ssl.X509IgnoredStatuses?.Any() is true)
            {
                logger.LogDebug("Ignored X509 certificate chain statuses: " +
                                $"{string.Join(", ", options.Ssl.X509IgnoredStatuses)}.");
                ignoredStatuses = options.Ssl.X509IgnoredStatuses
                    .Select(s => Enum.Parse<X509ChainStatusFlags>(s, true));
            }

            var statuses = chain.ChainStatus.ToList();
            logger.LogDebug("Received X509 certificate chain statuses: " +
                            $"{string.Join(", ", statuses.Select(x => x.Status))}");

            bool isValid = statuses.All(chainStatus => chainStatus.Status == X509ChainStatusFlags.NoError
                                                      || ignoredStatuses.Contains(chainStatus.Status));
            if (!isValid)
            {
                logger.LogError(string.Join(
                                            Environment.NewLine,
                                            statuses.Select(s => $"{s.Status} - {s.StatusInformation}")));
            }

            return isValid;
        };
    }

    public static IGenocsBuilder AddExceptionToMessageMapper<T>(this IGenocsBuilder builder)
        where T : class, IExceptionToMessageMapper
    {
        builder.Services.AddSingleton<IExceptionToMessageMapper, T>();
        return builder;
    }

    public static IGenocsBuilder AddExceptionToFailedMessageMapper<T>(this IGenocsBuilder builder)
        where T : class, IExceptionToFailedMessageMapper
    {
        builder.Services.AddSingleton<IExceptionToFailedMessageMapper, T>();
        return builder;
    }

    public static IBusSubscriber UseRabbitMQ(this IApplicationBuilder app)
        => new RabbitMQSubscriber(app.ApplicationServices.GetRequiredService<MessageSubscribersChannel>());
}