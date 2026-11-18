/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using Microsoft.Extensions.DependencyInjection;
using Ledgerscope.OpenIddict.CosmosDb.Models;
using Xunit;

namespace Ledgerscope.OpenIddict.CosmosDb.Tests;

public class OpenIddictCosmosDbExtensionsTests
{
    [Fact]
    public void UseCosmosDb_ThrowsAnExceptionForNullBuilder()
    {
        // Arrange
        var builder = (OpenIddictCoreBuilder) null!;

        // Act and assert
        var exception = Assert.Throws<ArgumentNullException>(builder.UseCosmosDb);

        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void UseCosmosDb_ThrowsAnExceptionForNullConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new OpenIddictCoreBuilder(services);

        // Act and assert
        var exception = Assert.Throws<ArgumentNullException>(() => builder.UseCosmosDb(configuration: null!));

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void UseCosmosDb_RegistersUntypedManagers()
    {
        // Arrange
        var services = new ServiceCollection().AddOptions();
        var builder = new OpenIddictCoreBuilder(services);

        // Act
        builder.UseCosmosDb();

        // Assert
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Scoped &&
            service.ServiceType == typeof(IOpenIddictApplicationManager) &&
            service.ImplementationFactory is not null);
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Scoped &&
            service.ServiceType == typeof(IOpenIddictAuthorizationManager) &&
            service.ImplementationFactory is not null);
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Scoped &&
            service.ServiceType == typeof(IOpenIddictScopeManager) &&
            service.ImplementationFactory is not null);
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Scoped &&
            service.ServiceType == typeof(IOpenIddictTokenManager) &&
            service.ImplementationFactory is not null);
    }

    [Fact]
    public void UseCosmosDb_RegistersCosmosDbStores()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new OpenIddictCoreBuilder(services);

        // Act
        builder.UseCosmosDb();

        // Assert
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictApplicationStore<OpenIddictCosmosDbApplication>) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbApplicationStore));
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictAuthorizationStore<OpenIddictCosmosDbAuthorization>) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbAuthorizationStore));
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictScopeStore<OpenIddictCosmosDbScope>) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbScopeStore));
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictTokenStore<OpenIddictCosmosDbToken>) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbTokenStore));
    }

    [Fact]
    public void UseCosmosDb_RegistersCosmosDbContext()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new OpenIddictCoreBuilder(services);

        // Act
        builder.UseCosmosDb();

        // Assert
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictCosmosDbContext) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbContext));
    }
}
