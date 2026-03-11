using Azure.Core.Pipeline;
using common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace extractor;

public delegate ValueTask ExtractApiResolvers(ApiName apiName, CancellationToken cancellationToken);
public delegate IAsyncEnumerable<(ApiResolverName Name, ApiResolverDto Dto)> ListApiResolvers(ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask WriteApiResolverArtifacts(ApiResolverName name, ApiResolverDto dto, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask WriteApiResolverInformationFile(ApiResolverName name, ApiResolverDto dto, ApiName apiName, CancellationToken cancellationToken);

internal static class ApiResolverModule
{
    public static void ConfigureExtractApiResolvers(IHostApplicationBuilder builder)
    {
        ConfigureListApiResolvers(builder);
        ConfigureWriteApiResolverArtifacts(builder);

        builder.Services.TryAddSingleton(GetExtractApiResolvers);
    }

    private static ExtractApiResolvers GetExtractApiResolvers(IServiceProvider provider)
    {
        var list = provider.GetRequiredService<ListApiResolvers>();
        var writeArtifacts = provider.GetRequiredService<WriteApiResolverArtifacts>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(ExtractApiResolvers));

            logger.LogInformation("Extracting resolvers for GraphQL API {ApiName}...", apiName);

            await list(apiName, cancellationToken)
                    .IterParallel(async resource => await writeArtifacts(resource.Name, resource.Dto, apiName, cancellationToken),
                                  cancellationToken);
        };
    }

    private static void ConfigureListApiResolvers(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetListApiResolvers);
    }

    private static ListApiResolvers GetListApiResolvers(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();

        return (apiName, cancellationToken) =>
        {
            var resolversUri = ApiResolversUri.From(apiName, serviceUri);
            return resolversUri.List(pipeline, cancellationToken);
        };
    }

    private static void ConfigureWriteApiResolverArtifacts(IHostApplicationBuilder builder)
    {
        ConfigureWriteApiResolverInformationFile(builder);

        builder.Services.TryAddSingleton(GetWriteApiResolverArtifacts);
    }

    private static WriteApiResolverArtifacts GetWriteApiResolverArtifacts(IServiceProvider provider)
    {
        var writeInformationFile = provider.GetRequiredService<WriteApiResolverInformationFile>();

        return async (name, dto, apiName, cancellationToken) =>
            await writeInformationFile(name, dto, apiName, cancellationToken);
    }

    private static void ConfigureWriteApiResolverInformationFile(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetWriteApiResolverInformationFile);
    }

    private static WriteApiResolverInformationFile GetWriteApiResolverInformationFile(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, apiName, cancellationToken) =>
        {
            var informationFile = ApiResolverInformationFile.From(name, apiName, serviceDirectory);

            logger.LogInformation("Writing API resolver information file {ApiResolverInformationFile}...", informationFile);
            await informationFile.WriteDto(dto, cancellationToken);
        };
    }
}
