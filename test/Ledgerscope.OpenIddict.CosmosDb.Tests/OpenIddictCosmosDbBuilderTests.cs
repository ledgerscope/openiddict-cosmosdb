/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos;
using Moq;
using Ledgerscope.OpenIddict.CosmosDb.Models;
using Xunit;

namespace Ledgerscope.OpenIddict.CosmosDb.Tests;

public class OpenIddictCosmosDbBuilderTests
{
    [Fact]
    public void UseCosmosDb_UsesDefaultContainerNames()
    {
        var services = CreateServices();
        CreateBuilder(services);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIddictCosmosDbOptions>>().CurrentValue;

        Assert.Equal("OpenIddictApplications", options.ApplicationsContainerName);
        Assert.Equal("OpenIddictAuthorizations", options.AuthorizationsContainerName);
        Assert.Equal("OpenIddictScopes", options.ScopesContainerName);
        Assert.Equal("OpenIddictTokens", options.TokensContainerName);
    }

    [Fact]
    public void Constructor_ThrowsAnExceptionForNullServices()
    {
        // Arrange
        var services = (IServiceCollection) null!;

        // Act and assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OpenIddictCosmosDbBuilder(services));

        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void ReplaceDefaultApplicationEntity_StoreIsCorrectlyReplaced()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act
        builder.ReplaceDefaultApplicationEntity<CustomApplication>();

        // Assert
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictApplicationStore<CustomApplication>) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbApplicationStore<CustomApplication>));
    }

    [Fact]
    public void ReplaceDefaultAuthorizationEntity_StoreIsCorrectlyReplaced()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act
        builder.ReplaceDefaultAuthorizationEntity<CustomAuthorization>();

        // Assert
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictAuthorizationStore<CustomAuthorization>) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbAuthorizationStore<CustomAuthorization>));
    }

    [Fact]
    public void ReplaceDefaultScopeEntity_StoreIsCorrectlyReplaced()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act
        builder.ReplaceDefaultScopeEntity<CustomScope>();

        // Assert
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictScopeStore<CustomScope>) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbScopeStore<CustomScope>));
    }

    [Fact]
    public void ReplaceDefaultTokenEntity_StoreIsCorrectlyReplaced()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act
        builder.ReplaceDefaultTokenEntity<CustomToken>();

        // Assert
        Assert.Contains(services, service =>
            service.Lifetime is ServiceLifetime.Singleton &&
            service.ServiceType == typeof(IOpenIddictTokenStore<CustomToken>) &&
            service.ImplementationType == typeof(OpenIddictCosmosDbTokenStore<CustomToken>));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SetApplicationsContainerName_ThrowsAnExceptionForNullOrEmptyContainerName(string? name)
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act and assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => builder.SetApplicationsContainerName(name!));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void SetApplicationsContainerName_ContainerNameIsCorrectlySet()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act
        builder.SetApplicationsContainerName("custom_container");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIddictCosmosDbOptions>>().CurrentValue;

        Assert.Equal("custom_container", options.ApplicationsContainerName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SetAuthorizationsContainerName_ThrowsAnExceptionForNullOrEmptyContainerName(string? name)
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act and assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => builder.SetAuthorizationsContainerName(name!));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void SetAuthorizationsContainerName_ContainerNameIsCorrectlySet()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act
        builder.SetAuthorizationsContainerName("custom_container");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIddictCosmosDbOptions>>().CurrentValue;

        Assert.Equal("custom_container", options.AuthorizationsContainerName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SetScopesContainerName_ThrowsAnExceptionForNullOrEmptyContainerName(string? name)
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act and assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => builder.SetScopesContainerName(name!));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void SetScopesContainerName_ContainerNameIsCorrectlySet()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act
        builder.SetScopesContainerName("custom_container");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIddictCosmosDbOptions>>().CurrentValue;

        Assert.Equal("custom_container", options.ScopesContainerName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SetTokensContainerName_ThrowsAnExceptionForNullOrEmptyContainerName(string? name)
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act and assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => builder.SetTokensContainerName(name!));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void SetTokensContainerName_ContainerNameIsCorrectlySet()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act
        builder.SetTokensContainerName("custom_container");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIddictCosmosDbOptions>>().CurrentValue;

        Assert.Equal("custom_container", options.TokensContainerName);
    }

    [Fact]
    public void UseDatabase_ThrowsAnExceptionForNullDatabase()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);

        // Act and assert
        var exception = Assert.Throws<ArgumentNullException>(delegate
        {
            return builder.UseDatabase(database: null!);
        });

        Assert.Equal("database", exception.ParamName);
    }

    [Fact]
    public void UseDatabase_SetsDatabaseInOptions()
    {
        // Arrange
        var services = CreateServices();
        var builder = CreateBuilder(services);
        var database = Mock.Of<Database>();

        // Act
        builder.UseDatabase(database);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIddictCosmosDbOptions>>().CurrentValue;

        Assert.Equal(database, options.Database);
    }

    private static OpenIddictCosmosDbBuilder CreateBuilder(IServiceCollection services)
        => services.AddOpenIddict().AddCore().UseCosmosDb();

    private static IServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddOptions();

        return services;
    }

    public class CustomApplication : OpenIddictCosmosDbApplication;
    public class CustomAuthorization : OpenIddictCosmosDbAuthorization;
    public class CustomScope : OpenIddictCosmosDbScope;
    public class CustomToken : OpenIddictCosmosDbToken;
}
