using Azure.Core.Pipeline;
using common;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace publisher;

public delegate ValueTask PutWorkspaceApiReleases(CancellationToken cancellationToken);
public delegate Option<(WorkspaceApiReleaseName Name, ApiName ApiName, WorkspaceName WorkspaceName)> TryParseWorkspaceApiReleaseName(FileInfo file);
public delegate bool IsWorkspaceApiReleaseNameInSourceControl(WorkspaceApiReleaseName name, ApiName apiName, WorkspaceName workspaceName);
public delegate ValueTask PutWorkspaceApiRelease(WorkspaceApiReleaseName name, ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);
public delegate ValueTask<Option<WorkspaceApiReleaseDto>> FindWorkspaceApiReleaseDto(WorkspaceApiReleaseName name, ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);
public delegate ValueTask PutWorkspaceApiReleaseInApim(WorkspaceApiReleaseName name, WorkspaceApiReleaseDto dto, ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);
public delegate ValueTask DeleteWorkspaceApiReleases(CancellationToken cancellationToken);
public delegate ValueTask DeleteWorkspaceApiRelease(WorkspaceApiReleaseName name, ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);
public delegate ValueTask DeleteWorkspaceApiReleaseFromApim(WorkspaceApiReleaseName name, ApiName apiName, WorkspaceName workspaceName, CancellationToken cancellationToken);

internal static class WorkspaceApiReleaseModule
{
    public static void ConfigurePutWorkspaceApiReleases(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseWorkspaceApiReleaseName(builder);
        ConfigureIsWorkspaceApiReleaseNameInSourceControl(builder);
        ConfigurePutWorkspaceApiRelease(builder);

        builder.Services.TryAddSingleton(GetPutWorkspaceApiReleases);
    }

    private static PutWorkspaceApiReleases GetPutWorkspaceApiReleases(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseWorkspaceApiReleaseName>();
        var isNameInSourceControl = provider.GetRequiredService<IsWorkspaceApiReleaseNameInSourceControl>();
        var put = provider.GetRequiredService<PutWorkspaceApiRelease>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(PutWorkspaceApiReleases));

            logger.LogInformation("Putting workspace API releases...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(release => isNameInSourceControl(release.Name, release.ApiName, release.WorkspaceName))
                    .Distinct()
                    .IterParallel(async release => await put(release.Name, release.ApiName, release.WorkspaceName, cancellationToken), cancellationToken);
        };
    }

    private static void ConfigureTryParseWorkspaceApiReleaseName(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetTryParseWorkspaceApiReleaseName);
    }

    private static TryParseWorkspaceApiReleaseName GetTryParseWorkspaceApiReleaseName(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return file => from informationFile in WorkspaceApiReleaseInformationFile.TryParse(file, serviceDirectory)
                       select (informationFile.Parent.Name,
                               informationFile.Parent.Parent.Parent.Name,
                               informationFile.Parent.Parent.Parent.Parent.Parent.Name);
    }

    private static void ConfigureIsWorkspaceApiReleaseNameInSourceControl(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetArtifactFiles(builder);
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetIsWorkspaceApiReleaseNameInSourceControl);
    }

    private static IsWorkspaceApiReleaseNameInSourceControl GetIsWorkspaceApiReleaseNameInSourceControl(IServiceProvider provider)
    {
        var getArtifactFiles = provider.GetRequiredService<GetArtifactFiles>();
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return doesInformationFileExist;

        bool doesInformationFileExist(WorkspaceApiReleaseName name, ApiName apiName, WorkspaceName workspaceName)
        {
            var artifactFiles = getArtifactFiles();
            var informationFile = WorkspaceApiReleaseInformationFile.From(name, apiName, workspaceName, serviceDirectory);

            return artifactFiles.Contains(informationFile.ToFileInfo());
        }
    }

    private static void ConfigurePutWorkspaceApiRelease(IHostApplicationBuilder builder)
    {
        ConfigureFindWorkspaceApiReleaseDto(builder);
        ConfigurePutWorkspaceApiReleaseInApim(builder);

        builder.Services.TryAddSingleton(GetPutWorkspaceApiRelease);
    }

    private static PutWorkspaceApiRelease GetPutWorkspaceApiRelease(IServiceProvider provider)
    {
        var findDto = provider.GetRequiredService<FindWorkspaceApiReleaseDto>();
        var putInApim = provider.GetRequiredService<PutWorkspaceApiReleaseInApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, apiName, workspaceName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(PutWorkspaceApiRelease))
                                       ?.AddTag("workspace_api_release.name", name)
                                       ?.AddTag("api.name", apiName)
                                       ?.AddTag("workspace.name", workspaceName);

            var dtoOption = await findDto(name, apiName, workspaceName, cancellationToken);
            await dtoOption.IterTask(async dto => await putInApim(name, dto, apiName, workspaceName, cancellationToken));
        };
    }

    private static void ConfigureFindWorkspaceApiReleaseDto(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);
        CommonModule.ConfigureTryGetFileContents(builder);

        builder.Services.TryAddSingleton(GetFindWorkspaceApiReleaseDto);
    }

    private static FindWorkspaceApiReleaseDto GetFindWorkspaceApiReleaseDto(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var tryGetFileContents = provider.GetRequiredService<TryGetFileContents>();

        return async (name, apiName, workspaceName, cancellationToken) =>
        {
            var informationFile = WorkspaceApiReleaseInformationFile.From(name, apiName, workspaceName, serviceDirectory);
            var contentsOption = await tryGetFileContents(informationFile.ToFileInfo(), cancellationToken);

            return from contents in contentsOption
                   select contents.ToObjectFromJson<WorkspaceApiReleaseDto>();
        };
    }

    public static void ConfigurePutWorkspaceApiReleaseInApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetPutWorkspaceApiReleaseInApim);
    }

    private static PutWorkspaceApiReleaseInApim GetPutWorkspaceApiReleaseInApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, apiName, workspaceName, cancellationToken) =>
        {
            logger.LogInformation("Putting API release {WorkspaceApiReleaseName} in API {ApiName} in workspace {WorkspaceName}...", name, apiName, workspaceName);

            await WorkspaceApiReleaseUri.From(name, apiName, workspaceName, serviceUri)
                                        .PutDto(dto, pipeline, cancellationToken);
        };
    }

    public static void ConfigureDeleteWorkspaceApiReleases(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseWorkspaceApiReleaseName(builder);
        ConfigureIsWorkspaceApiReleaseNameInSourceControl(builder);
        ConfigureDeleteWorkspaceApiRelease(builder);

        builder.Services.TryAddSingleton(GetDeleteWorkspaceApiReleases);
    }

    private static DeleteWorkspaceApiReleases GetDeleteWorkspaceApiReleases(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseWorkspaceApiReleaseName>();
        var isNameInSourceControl = provider.GetRequiredService<IsWorkspaceApiReleaseNameInSourceControl>();
        var delete = provider.GetRequiredService<DeleteWorkspaceApiRelease>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteWorkspaceApiReleases));

            logger.LogInformation("Deleting workspace API releases...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(release => isNameInSourceControl(release.Name, release.ApiName, release.WorkspaceName) is false)
                    .Distinct()
                    .IterParallel(async release => await delete(release.Name, release.ApiName, release.WorkspaceName, cancellationToken), cancellationToken);
        };
    }

    private static void ConfigureDeleteWorkspaceApiRelease(IHostApplicationBuilder builder)
    {
        ConfigureDeleteWorkspaceApiReleaseFromApim(builder);

        builder.Services.TryAddSingleton(GetDeleteWorkspaceApiRelease);
    }

    private static DeleteWorkspaceApiRelease GetDeleteWorkspaceApiRelease(IServiceProvider provider)
    {
        var deleteFromApim = provider.GetRequiredService<DeleteWorkspaceApiReleaseFromApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, apiName, workspaceName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteWorkspaceApiRelease))
                                       ?.AddTag("workspace_api_release.name", name)
                                       ?.AddTag("api.name", apiName)
                                       ?.AddTag("workspace.name", workspaceName);

            await deleteFromApim(name, apiName, workspaceName, cancellationToken);
        };
    }

    public static void ConfigureDeleteWorkspaceApiReleaseFromApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetDeleteWorkspaceApiReleaseFromApim);
    }

    private static DeleteWorkspaceApiReleaseFromApim GetDeleteWorkspaceApiReleaseFromApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, apiName, workspaceName, cancellationToken) =>
        {
            logger.LogInformation("Deleting API release {WorkspaceApiReleaseName} from API {ApiName} in workspace {WorkspaceName}...", name, apiName, workspaceName);

            await WorkspaceApiReleaseUri.From(name, apiName, workspaceName, serviceUri)
                                        .Delete(pipeline, cancellationToken);
        };
    }
}