/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Core;
using Ledgerscope.OpenIddict.CosmosDb;
using Ledgerscope.OpenIddict.CosmosDb.Models;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Exposes the necessary methods required to configure the OpenIddict Azure Cosmos DB services.
/// </summary>
public sealed class OpenIddictCosmosDbBuilder
{
    /// <summary>
    /// Initializes a new instance of <see cref="OpenIddictCosmosDbBuilder"/>.
    /// </summary>
    /// <param name="services">The services container.</param>
    public OpenIddictCosmosDbBuilder(IServiceCollection services)
        => Services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>
    /// Gets the services container.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection Services { get; }

    /// <summary>
    /// Enables validation of options during application startup.
    /// </summary>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder ValidateOnStart()
    {
        Services.AddOptionsWithValidateOnStart<OpenIddictCosmosDbOptions>();

        return this;
    }

    /// <summary>
    /// Amends the default OpenIddict Azure Cosmos DB configuration.
    /// </summary>
    /// <param name="configuration">The delegate used to configure the OpenIddict options.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public OpenIddictCosmosDbBuilder Configure(Action<OpenIddictCosmosDbOptions> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Services.Configure(configuration);

        return this;
    }

    /// <summary>
    /// Configures OpenIddict to use the specified entity as the default application entity.
    /// </summary>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder ReplaceDefaultApplicationEntity<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TApplication>()
        where TApplication : OpenIddictCosmosDbApplication
    {
        Services.Replace(ServiceDescriptor.Scoped<IOpenIddictApplicationManager>(static provider =>
            provider.GetRequiredService<OpenIddictApplicationManager<TApplication>>()));

        Services.Replace(ServiceDescriptor.Singleton<
            IOpenIddictApplicationStore<TApplication>, OpenIddictCosmosDbApplicationStore<TApplication>>());

        return this;
    }

    /// <summary>
    /// Configures OpenIddict to use the specified entity as the default authorization entity.
    /// </summary>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder ReplaceDefaultAuthorizationEntity<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAuthorization>()
        where TAuthorization : OpenIddictCosmosDbAuthorization
    {
        Services.Replace(ServiceDescriptor.Scoped<IOpenIddictAuthorizationManager>(static provider =>
            provider.GetRequiredService<OpenIddictAuthorizationManager<TAuthorization>>()));

        Services.Replace(ServiceDescriptor.Singleton<
            IOpenIddictAuthorizationStore<TAuthorization>, OpenIddictCosmosDbAuthorizationStore<TAuthorization>>());

        return this;
    }

    /// <summary>
    /// Configures OpenIddict to use the specified entity as the default scope entity.
    /// </summary>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder ReplaceDefaultScopeEntity<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TScope>()
        where TScope : OpenIddictCosmosDbScope
    {
        Services.Replace(ServiceDescriptor.Scoped<IOpenIddictScopeManager>(static provider =>
            provider.GetRequiredService<OpenIddictScopeManager<TScope>>()));

        Services.Replace(ServiceDescriptor.Singleton<
            IOpenIddictScopeStore<TScope>, OpenIddictCosmosDbScopeStore<TScope>>());

        return this;
    }

    /// <summary>
    /// Configures OpenIddict to use the specified entity as the default token entity.
    /// </summary>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder ReplaceDefaultTokenEntity<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TToken>()
        where TToken : OpenIddictCosmosDbToken
    {
        Services.Replace(ServiceDescriptor.Scoped<IOpenIddictTokenManager>(static provider =>
            provider.GetRequiredService<OpenIddictTokenManager<TToken>>()));

        Services.Replace(ServiceDescriptor.Singleton<
            IOpenIddictTokenStore<TToken>, OpenIddictCosmosDbTokenStore<TToken>>());

        return this;
    }

    /// <summary>
    /// Replaces the default applications container name (by default, openiddict.applications).
    /// </summary>
    /// <param name="name">The container name.</param>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder SetApplicationsContainerName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Configure(options => options.ApplicationsContainerName = name);
    }

    /// <summary>
    /// Replaces the default authorizations container name (by default, openiddict.authorizations).
    /// </summary>
    /// <param name="name">The container name.</param>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder SetAuthorizationsContainerName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Configure(options => options.AuthorizationsContainerName = name);
    }

    /// <summary>
    /// Replaces the default scopes container name (by default, openiddict.scopes).
    /// </summary>
    /// <param name="name">The container name.</param>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder SetScopesContainerName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Configure(options => options.ScopesContainerName = name);
    }

    /// <summary>
    /// Replaces the default tokens container name (by default, openiddict.tokens).
    /// </summary>
    /// <param name="name">The container name.</param>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder SetTokensContainerName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Configure(options => options.TokensContainerName = name);
    }

    /// <summary>
    /// Configures the Azure Cosmos DB stores to use the specified database
    /// instead of retrieving it from the dependency injection container.
    /// </summary>
    /// <param name="database">The <see cref="Database"/>.</param>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public OpenIddictCosmosDbBuilder UseDatabase(Database database)
    {
        ArgumentNullException.ThrowIfNull(database);

        return Configure(options => options.Database = database);
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals([NotNullWhen(true)] object? obj) => base.Equals(obj);

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => base.GetHashCode();

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override string? ToString() => base.ToString();
}
