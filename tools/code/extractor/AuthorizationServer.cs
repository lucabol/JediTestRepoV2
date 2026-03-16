using Azure.Core.Pipeline;
using common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace extractor;

public delegate ValueTask ExtractAuthorizationServers(CancellationToken cancellationToken);
public delegate IAsyncEnumerable<(AuthorizationServerName Name, AuthorizationServerDto Dto)> ListAuthorizationServers(CancellationToken cancellationToken);
public delegate ValueTask WriteAuthorizationServerArtifacts(AuthorizationServerName name, AuthorizationServerDto dto, CancellationToken cancellationToken);
public delegate ValueTask WriteAuthorizationServerInformationFile(AuthorizationServerName name, AuthorizationServerDto dto, CancellationToken cancellationToken);

internal static class AuthorizationServerModule
{
    public static void ConfigureExtractAuthorizationServers(IHostApplicationBuilder builder)
    {
        ConfigureListAuthorizationServers(builder);
        ConfigureWriteAuthorizationServerArtifacts(builder);

        builder.Services.TryAddSingleton(GetExtractAuthorizationServers);
    }

    private static ExtractAuthorizationServers GetExtractAuthorizationServers(IServiceProvider provider)
    {
        var list = provider.GetRequiredService<ListAuthorizationServers>();
        var writeArtifacts = provider.GetRequiredService<WriteAuthorizationServerArtifacts>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(ExtractAuthorizationServers));

            logger.LogInformation("Extracting authorization servers...");

            await list(cancellationToken)
                    .IterParallel(async resource => await writeArtifacts(resource.Name, resource.Dto, cancellationToken),
                                  cancellationToken);
        };
    }

    private static void ConfigureListAuthorizationServers(IHostApplicationBuilder builder)
    {
        ConfigurationModule.ConfigureFindConfigurationNamesFactory(builder);
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetListAuthorizationServers);
    }

    private static ListAuthorizationServers GetListAuthorizationServers(IServiceProvider provider)
    {
        var findConfigurationNamesFactory = provider.GetRequiredService<FindConfigurationNamesFactory>();
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();

        var findConfigurationNames = findConfigurationNamesFactory.Create<AuthorizationServerName>();

        return cancellationToken =>
            findConfigurationNames()
                .Map(names => listFromSet(names, cancellationToken))
                .IfNone(() => listAll(cancellationToken));

        IAsyncEnumerable<(AuthorizationServerName, AuthorizationServerDto)> listFromSet(IEnumerable<AuthorizationServerName> names, CancellationToken cancellationToken) =>
            names.Select(name => AuthorizationServerUri.From(name, serviceUri))
                 .ToAsyncEnumerable()
                 .Choose(async uri =>
                 {
                     var dtoOption = await uri.TryGetDto(pipeline, cancellationToken);
                     return dtoOption.Map(dto => (uri.Name, dto));
                 });

        IAsyncEnumerable<(AuthorizationServerName, AuthorizationServerDto)> listAll(CancellationToken cancellationToken)
        {
            var authorizationServersUri = AuthorizationServersUri.From(serviceUri);
            return authorizationServersUri.List(pipeline, cancellationToken);
        }
    }

    private static void ConfigureWriteAuthorizationServerArtifacts(IHostApplicationBuilder builder)
    {
        ConfigureWriteAuthorizationServerInformationFile(builder);

        builder.Services.TryAddSingleton(GetWriteAuthorizationServerArtifacts);
    }

    private static WriteAuthorizationServerArtifacts GetWriteAuthorizationServerArtifacts(IServiceProvider provider)
    {
        var writeInformationFile = provider.GetRequiredService<WriteAuthorizationServerInformationFile>();

        return async (name, dto, cancellationToken) =>
            await writeInformationFile(name, dto, cancellationToken);
    }

    private static void ConfigureWriteAuthorizationServerInformationFile(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetWriteAuthorizationServerInformationFile);
    }

    private static WriteAuthorizationServerInformationFile GetWriteAuthorizationServerInformationFile(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, cancellationToken) =>
        {
            var informationFile = AuthorizationServerInformationFile.From(name, serviceDirectory);

            logger.LogInformation("Writing authorization server information file {AuthorizationServerInformationFile}...", informationFile);
            await informationFile.WriteDto(dto, cancellationToken);
        };
    }
}
