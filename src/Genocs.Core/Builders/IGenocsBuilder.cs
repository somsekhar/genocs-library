using Genocs.Common.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Genocs.Core.Builders;

/// <summary>
/// The Application builder.
/// </summary>
public interface IGenocsBuilder
{
    /// <summary>
    /// Get the service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Get the configuration.
    /// </summary>
    IConfiguration? Configuration { get; }

    WebApplicationBuilder? WebApplicationBuilder { get; }

    /// <summary>
    /// try to register a service by name.
    /// </summary>
    /// <param name="name">Name of the service trying to register.</param>
    /// <returns></returns>
    bool TryRegister(string name);

    /// <summary>
    /// Build the actions based on the service provider.
    /// </summary>
    /// <param name="execute"></param>
    void AddBuildAction(Action<IServiceProvider> execute);

    void AddInitializer(IInitializer initializer);

    void AddInitializer<TInitializer>()
        where TInitializer : IInitializer;

    IServiceProvider Build();
}