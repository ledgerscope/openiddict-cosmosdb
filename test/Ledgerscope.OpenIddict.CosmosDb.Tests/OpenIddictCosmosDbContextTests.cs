/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace Ledgerscope.OpenIddict.CosmosDb.Tests;

public class OpenIddictCosmosDbContextTests
{
    [Fact]
    public async Task GetDatabaseAsync_ThrowsAnExceptionForCanceledToken()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var options = Mock.Of<IOptionsMonitor<OpenIddictCosmosDbOptions>>();
        var token = new CancellationToken(canceled: true);

        var context = new OpenIddictCosmosDbContext(options, provider);

        // Act and assert
        var exception = await Assert.ThrowsAsync<TaskCanceledException>(async delegate
        {
            await context.GetDatabaseAsync(token);
        });

        Assert.Equal(token, exception.CancellationToken);
    }

    [Fact]
    public async Task GetDatabaseAsync_PrefersDatabaseRegisteredInOptionsToDatabaseRegisteredInDependencyInjectionContainer()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<Database>());

        var provider = services.BuildServiceProvider();

        var database = Mock.Of<Database>();
        var options = Mock.Of<IOptionsMonitor<OpenIddictCosmosDbOptions>>(
            mock => mock.CurrentValue == new OpenIddictCosmosDbOptions
            {
                Database = database
            });

        var context = new OpenIddictCosmosDbContext(options, provider);

        // Act and assert
        Assert.Same(database, await context.GetDatabaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetDatabaseAsync_ThrowsAnExceptionWhenDatabaseCannotBeFound()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var options = Mock.Of<IOptionsMonitor<OpenIddictCosmosDbOptions>>(
            mock => mock.CurrentValue == new OpenIddictCosmosDbOptions
            {
                Database = null
            });

        var context = new OpenIddictCosmosDbContext(options, provider);

        // Act and assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async delegate
        {
            await context.GetDatabaseAsync(CancellationToken.None);
        });

        Assert.Equal(OpenIddictCosmosDbContext.DatabaseNotFoundMessage, exception.Message);
    }

    [Fact]
    public async Task GetDatabaseAsync_UsesDatabaseRegisteredInDependencyInjectionContainer()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<Database>());

        var database = Mock.Of<Database>();
        services.AddSingleton(database);

        var provider = services.BuildServiceProvider();

        var options = Mock.Of<IOptionsMonitor<OpenIddictCosmosDbOptions>>(
            mock => mock.CurrentValue == new OpenIddictCosmosDbOptions
            {
                Database = null
            });

        var context = new OpenIddictCosmosDbContext(options, provider);

        // Act and assert
        Assert.Same(database, await context.GetDatabaseAsync(CancellationToken.None));
    }
}
