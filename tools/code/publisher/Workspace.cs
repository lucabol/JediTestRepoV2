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
using System.Threading;
using System.Threading.Tasks;

namespace publisher;

public delegate ValueTask PutWorkspaces(CancellationToken cancellationToken);
public delegate Option<WorkspaceName> TryParseWorkspaceName(FileInfo file);
public delegate bool IsWorkspaceNameInSourceControl(WorkspaceName name);
public delegate ValueTask PutWorkspace(WorkspaceName name, CancellationToken cancellationToken);
public delegate ValueTask<Option<WorkspaceDto>> FindWorkspaceDto(WorkspaceName name, CancellationToken cancellationToken);
public delegate ValueTask PutWorkspaceInApim(WorkspaceName name, WorkspaceDto dto, CancellationToken cancellationToken);
public delegate ValueTask DeleteWorkspaces(CancellationToken cancellationToken);
public delegate ValueTask DeleteWorkspace(WorkspaceName name, CancellationToken cancellationToken);
public delegate ValueTask DeleteWorkspaceFromApim(WorkspaceName name, CancellationToken cancellationToken);

internal static class WorkspacePublisherModule
{
    public static void ConfigurePutWorkspaces(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseWorkspaceName(builder);
        ConfigureIsWorkspaceNameInSourceControl(builder);
        ConfigurePutWorkspace(builder);

        builder.Services.TryAddSingleton(GetPutWorkspaces);
    }

    private static PutWorkspaces GetPutWorkspaces(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseWorkspaceName>();
        var isNameInSourceControl = provider.GetRequiredService<IsWorkspaceNameInSourceControl>();
        var put = provider.GetRequiredService<PutWorkspace>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(PutWorkspaces));

            logger.LogInformation("Putting workspaces...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(isNameInSourceControl)
                    .Distinct()
                    .IterParallel(put.Invoke, cancellationToken);
        };
    }

    private static void ConfigureTryParseWorkspaceName(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetTryParseWorkspaceName);
    }

    private static TryParseWorkspaceName GetTryParseWorkspaceName(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return file => from informationFile in WorkspaceInformationFile.TryParse(file, serviceDirectory)
                       select informationFile.Parent.Name;
    }

    private static void ConfigureIsWorkspaceNameInSourceControl(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetArtifactFiles(builder);
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetIsWorkspaceNameInSourceControl);
    }

    private static IsWorkspaceNameInSourceControl GetIsWorkspaceNameInSourceControl(IServiceProvider provider)
    {
        var getArtifactFiles = provider.GetRequiredService<GetArtifactFiles>();
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return doesInformationFileExist;

        bool doesInformationFileExist(WorkspaceName name)
        {
            var artifactFiles = getArtifactFiles();
            var informationFile = WorkspaceInformationFile.From(name, serviceDirectory);
            return artifactFiles.Contains(informationFile.ToFileInfo());
        }
    }

    private static void ConfigurePutWorkspace(IHostApplicationBuilder builder)
    {
        ConfigureFindWorkspaceDto(builder);
        ConfigurePutWorkspaceInApim(builder);

        builder.Services.TryAddSingleton(GetPutWorkspace);
    }

    private static PutWorkspace GetPutWorkspace(IServiceProvider provider)
    {
        var findDto = provider.GetRequiredService<FindWorkspaceDto>();
        var putInApim = provider.GetRequiredService<PutWorkspaceInApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(PutWorkspace))
                                       ?.AddTag("workspace.name", name);

            var dtoOption = await findDto(name, cancellationToken);
            await dtoOption.IterTask(async dto => await putInApim(name, dto, cancellationToken));
        };
    }

    private static void ConfigureFindWorkspaceDto(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);
        CommonModule.ConfigureTryGetFileContents(builder);

        builder.Services.TryAddSingleton(GetFindWorkspaceDto);
    }

    private static FindWorkspaceDto GetFindWorkspaceDto(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var tryGetFileContents = provider.GetRequiredService<TryGetFileContents>();

        return async (name, cancellationToken) =>
        {
            var informationFile = WorkspaceInformationFile.From(name, serviceDirectory);
            var contentsOption = await tryGetFileContents(informationFile.ToFileInfo(), cancellationToken);

            return from contents in contentsOption
                   select contents.ToObjectFromJson<WorkspaceDto>();
        };
    }

    private static void ConfigurePutWorkspaceInApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetPutWorkspaceInApim);
    }

    private static PutWorkspaceInApim GetPutWorkspaceInApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, cancellationToken) =>
        {
            logger.LogInformation("Putting workspace {WorkspaceName}...", name);

            await WorkspaceUri.From(name, serviceUri)
                              .PutDto(dto, pipeline, cancellationToken);
        };
    }

    public static void ConfigureDeleteWorkspaces(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseWorkspaceName(builder);
        ConfigureIsWorkspaceNameInSourceControl(builder);
        ConfigureDeleteWorkspace(builder);

        builder.Services.TryAddSingleton(GetDeleteWorkspaces);
    }

    private static DeleteWorkspaces GetDeleteWorkspaces(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseWorkspaceName>();
        var isNameInSourceControl = provider.GetRequiredService<IsWorkspaceNameInSourceControl>();
        var delete = provider.GetRequiredService<DeleteWorkspace>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteWorkspaces));

            logger.LogInformation("Deleting workspaces...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(name => isNameInSourceControl(name) is false)
                    .Distinct()
                    .IterParallel(delete.Invoke, cancellationToken);
        };
    }

    private static void ConfigureDeleteWorkspace(IHostApplicationBuilder builder)
    {
        ConfigureDeleteWorkspaceFromApim(builder);

        builder.Services.TryAddSingleton(GetDeleteWorkspace);
    }

    private static DeleteWorkspace GetDeleteWorkspace(IServiceProvider provider)
    {
        var deleteFromApim = provider.GetRequiredService<DeleteWorkspaceFromApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteWorkspace))
                                       ?.AddTag("workspace.name", name);

            await deleteFromApim(name, cancellationToken);
        };
    }

    private static void ConfigureDeleteWorkspaceFromApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetDeleteWorkspaceFromApim);
    }

    private static DeleteWorkspaceFromApim GetDeleteWorkspaceFromApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, cancellationToken) =>
        {
            logger.LogInformation("Deleting workspace {WorkspaceName}...", name);

            await WorkspaceUri.From(name, serviceUri)
                              .Delete(pipeline, cancellationToken);
        };
    }
}
