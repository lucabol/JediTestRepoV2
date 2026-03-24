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

public delegate ValueTask ExtractWorkspaceApiReleases(ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);
public delegate IAsyncEnumerable<(WorkspaceApiReleaseName Name, WorkspaceApiReleaseDto Dto)> ListWorkspaceApiReleases(ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);
public delegate ValueTask WriteWorkspaceApiReleaseArtifacts(WorkspaceApiReleaseName name, WorkspaceApiReleaseDto dto, ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);
public delegate ValueTask WriteWorkspaceApiReleaseInformationFile(WorkspaceApiReleaseName name, WorkspaceApiReleaseDto dto, ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);

internal static class WorkspaceApiReleaseModule
{
    public static void ConfigureExtractWorkspaceApiReleases(IHostApplicationBuilder builder)
    {
        ConfigureListWorkspaceApiReleases(builder);
        ConfigureWriteWorkspaceApiReleaseArtifacts(builder);

        builder.Services.TryAddSingleton(GetExtractWorkspaceApiReleases);
    }

    private static ExtractWorkspaceApiReleases GetExtractWorkspaceApiReleases(IServiceProvider provider)
    {
        var list = provider.GetRequiredService<ListWorkspaceApiReleases>();
        var writeArtifacts = provider.GetRequiredService<WriteWorkspaceApiReleaseArtifacts>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (apiName, workspaceName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(ExtractWorkspaceApiReleases));

            logger.LogInformation("Extracting releases for API {ApiName} in workspace {WorkspaceName}...", apiName, workspaceName);

            await list(apiName, workspaceName, cancellationToken)
                    .IterParallel(async resource => await writeArtifacts(resource.Name, resource.Dto, apiName, workspaceName, cancellationToken),
                                  cancellationToken);
        };
    }

    private static void ConfigureListWorkspaceApiReleases(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetListWorkspaceApiReleases);
    }

    private static ListWorkspaceApiReleases GetListWorkspaceApiReleases(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();

        return (apiName, workspaceName, cancellationToken) =>
        {
            var releasesUri = WorkspaceApiReleasesUri.From(apiName, workspaceName, serviceUri);
            return releasesUri.List(pipeline, cancellationToken);
        };
    }

    private static void ConfigureWriteWorkspaceApiReleaseArtifacts(IHostApplicationBuilder builder)
    {
        ConfigureWriteWorkspaceApiReleaseInformationFile(builder);

        builder.Services.TryAddSingleton(GetWriteWorkspaceApiReleaseArtifacts);
    }

    private static WriteWorkspaceApiReleaseArtifacts GetWriteWorkspaceApiReleaseArtifacts(IServiceProvider provider)
    {
        var writeInformationFile = provider.GetRequiredService<WriteWorkspaceApiReleaseInformationFile>();

        return async (name, dto, apiName, workspaceName, cancellationToken) =>
            await writeInformationFile(name, dto, apiName, workspaceName, cancellationToken);
    }

    private static void ConfigureWriteWorkspaceApiReleaseInformationFile(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetWriteWorkspaceApiReleaseInformationFile);
    }

    private static WriteWorkspaceApiReleaseInformationFile GetWriteWorkspaceApiReleaseInformationFile(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, apiName, workspaceName, cancellationToken) =>
        {
            var informationFile = WorkspaceApiReleaseInformationFile.From(name, apiName, workspaceName, serviceDirectory);

            logger.LogInformation("Writing workspace API release information file {WorkspaceApiReleaseInformationFile}...", informationFile);
            await informationFile.WriteDto(dto, cancellationToken);
        };
    }
}
