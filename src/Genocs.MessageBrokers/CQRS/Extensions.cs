using Genocs.Core.Builders;
using Genocs.Core.CQRS.Commands;
using Genocs.Core.CQRS.Events;
using Genocs.MessageBrokers.CQRS.Dispatchers;
using Microsoft.Extensions.DependencyInjection;

namespace Genocs.MessageBrokers.CQRS;

public static class Extensions
{
    public static Task SendAsync<TCommand>(this IBusPublisher busPublisher, TCommand command, object messageContext)
        where TCommand : class, ICommand
        => busPublisher.PublishAsync(command, messageContext: messageContext);

    public static Task PublishAsync<TEvent>(this IBusPublisher busPublisher, TEvent @event, object messageContext)
        where TEvent : class, IEvent
        => busPublisher.PublishAsync(@event, messageContext: messageContext);

    public static IBusSubscriber SubscribeCommand<T>(this IBusSubscriber busSubscriber)
        where T : class, ICommand
        => busSubscriber.Subscribe<T>(async (serviceProvider, command, _) =>
        {
            using var scope = serviceProvider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<T>>().HandleAsync(command);
        });

    public static IBusSubscriber SubscribeEvent<T>(this IBusSubscriber busSubscriber)
        where T : class, IEvent
        => busSubscriber.Subscribe<T>(async (serviceProvider, @event, _) =>
        {
            using var scope = serviceProvider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IEventHandler<T>>().HandleAsync(@event);
        });

    public static IGenocsBuilder AddServiceBusCommandDispatcher(this IGenocsBuilder builder)
    {
        builder.Services.AddTransient<ICommandDispatcher, ServiceBusMessageDispatcher>();
        return builder;
    }

    public static IGenocsBuilder AddServiceBusEventDispatcher(this IGenocsBuilder builder)
    {
        builder.Services.AddTransient<IEventDispatcher, ServiceBusMessageDispatcher>();
        return builder;
    }
}