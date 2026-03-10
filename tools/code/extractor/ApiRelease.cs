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

public delegate ValueTask ExtractApiReleases(ApiName apiName, CancellationToken cancellationToken);
public delegate IAsyncEnumerable<(ApiReleaseName Name, ApiReleaseDto Dto)> ListApiReleases(ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask WriteApiReleaseArtifacts(ApiReleaseName name, ApiReleaseDto dto, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask WriteApiReleaseInformationFile(ApiReleaseName name, ApiReleaseDto dto, ApiName apiName, CancellationToken cancellationToken);

internal static class ApiReleaseModule
{
    public static void ConfigureExtractApiReleases(IHostApplicationBuilder builder)
    {
        ConfigureListApiReleases(builder);
        ConfigureWriteApiReleaseArtifacts(builder);

        builder.Services.TryAddSingleton(GetExtractApiReleases);
    }

    private static ExtractApiReleases GetExtractApiReleases(IServiceProvider provider)
    {
        var list = provider.GetRequiredService<ListApiReleases>();
        var writeArtifacts = provider.GetRequiredService<WriteApiReleaseArtifacts>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(ExtractApiReleases));

            logger.LogInformation("Extracting releases for API {ApiName}...", apiName);

            await list(apiName, cancellationToken)
                    .IterParallel(async resource => await writeArtifacts(resource.Name, resource.Dto, apiName, cancellationToken),
                                  cancellationToken);
        };
    }

    private static void ConfigureListApiReleases(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetListApiReleases);
    }

    private static ListApiReleases GetListApiReleases(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();

        return (apiName, cancellationToken) =>
        {
            var releasesUri = ApiReleasesUri.From(apiName, serviceUri);
            return releasesUri.List(pipeline, cancellationToken);
        };
    }

    private static void ConfigureWriteApiReleaseArtifacts(IHostApplicationBuilder builder)
    {
        ConfigureWriteApiReleaseInformationFile(builder);

        builder.Services.TryAddSingleton(GetWriteApiReleaseArtifacts);
    }

    private static WriteApiReleaseArtifacts GetWriteApiReleaseArtifacts(IServiceProvider provider)
    {
        var writeInformationFile = provider.GetRequiredService<WriteApiReleaseInformationFile>();

        return async (name, dto, apiName, cancellationToken) =>
            await writeInformationFile(name, dto, apiName, cancellationToken);
    }

    private static void ConfigureWriteApiReleaseInformationFile(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetWriteApiReleaseInformationFile);
    }

    private static WriteApiReleaseInformationFile GetWriteApiReleaseInformationFile(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, apiName, cancellationToken) =>
        {
            var informationFile = ApiReleaseInformationFile.From(name, apiName, serviceDirectory);

            logger.LogInformation("Writing API release information file {ApiReleaseInformationFile}...", informationFile);
            await informationFile.WriteDto(dto, cancellationToken);
        };
    }
}
