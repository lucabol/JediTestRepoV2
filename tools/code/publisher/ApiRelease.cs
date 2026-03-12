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

public delegate ValueTask PutApiReleases(CancellationToken cancellationToken);
public delegate Option<(ApiReleaseName Name, ApiName ApiName)> TryParseApiReleaseName(FileInfo file);
public delegate bool IsApiReleaseNameInSourceControl(ApiReleaseName name, ApiName apiName);
public delegate ValueTask PutApiRelease(ApiReleaseName name, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask<Option<ApiReleaseDto>> FindApiReleaseDto(ApiReleaseName name, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask PutApiReleaseInApim(ApiReleaseName name, ApiReleaseDto dto, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask DeleteApiReleases(CancellationToken cancellationToken);
public delegate ValueTask DeleteApiRelease(ApiReleaseName name, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask DeleteApiReleaseFromApim(ApiReleaseName name, ApiName apiName, CancellationToken cancellationToken);

internal static class ApiReleaseModule
{
    public static void ConfigurePutApiReleases(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseApiReleaseName(builder);
        ConfigureIsApiReleaseNameInSourceControl(builder);
        ConfigurePutApiRelease(builder);

        builder.Services.TryAddSingleton(GetPutApiReleases);
    }

    private static PutApiReleases GetPutApiReleases(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseApiReleaseName>();
        var isNameInSourceControl = provider.GetRequiredService<IsApiReleaseNameInSourceControl>();
        var put = provider.GetRequiredService<PutApiRelease>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(PutApiReleases));

            logger.LogInformation("Putting API releases...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(release => isNameInSourceControl(release.Name, release.ApiName))
                    .Distinct()
                    .IterParallel(async release => await put(release.Name, release.ApiName, cancellationToken), cancellationToken);
        };
    }

    private static void ConfigureTryParseApiReleaseName(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetTryParseApiReleaseName);
    }

    private static TryParseApiReleaseName GetTryParseApiReleaseName(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return file => from informationFile in ApiReleaseInformationFile.TryParse(file, serviceDirectory)
                       select (informationFile.Parent.Name, informationFile.Parent.Parent.Parent.Name);
    }

    private static void ConfigureIsApiReleaseNameInSourceControl(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetArtifactFiles(builder);
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetIsApiReleaseNameInSourceControl);
    }

    private static IsApiReleaseNameInSourceControl GetIsApiReleaseNameInSourceControl(IServiceProvider provider)
    {
        var getArtifactFiles = provider.GetRequiredService<GetArtifactFiles>();
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return doesInformationFileExist;

        bool doesInformationFileExist(ApiReleaseName name, ApiName apiName)
        {
            var artifactFiles = getArtifactFiles();
            var informationFile = ApiReleaseInformationFile.From(name, apiName, serviceDirectory);

            return artifactFiles.Contains(informationFile.ToFileInfo());
        }
    }

    private static void ConfigurePutApiRelease(IHostApplicationBuilder builder)
    {
        ConfigureFindApiReleaseDto(builder);
        ConfigurePutApiReleaseInApim(builder);

        builder.Services.TryAddSingleton(GetPutApiRelease);
    }

    private static PutApiRelease GetPutApiRelease(IServiceProvider provider)
    {
        var findDto = provider.GetRequiredService<FindApiReleaseDto>();
        var putInApim = provider.GetRequiredService<PutApiReleaseInApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(PutApiRelease))
                                       ?.AddTag("api_release.name", name)
                                       ?.AddTag("api.name", apiName);

            var dtoOption = await findDto(name, apiName, cancellationToken);
            await dtoOption.IterTask(async dto => await putInApim(name, dto, apiName, cancellationToken));
        };
    }

    private static void ConfigureFindApiReleaseDto(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);
        CommonModule.ConfigureTryGetFileContents(builder);

        builder.Services.TryAddSingleton(GetFindApiReleaseDto);
    }

    private static FindApiReleaseDto GetFindApiReleaseDto(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var tryGetFileContents = provider.GetRequiredService<TryGetFileContents>();

        return async (name, apiName, cancellationToken) =>
        {
            var informationFile = ApiReleaseInformationFile.From(name, apiName, serviceDirectory);
            var contentsOption = await tryGetFileContents(informationFile.ToFileInfo(), cancellationToken);

            return from contents in contentsOption
                   select contents.ToObjectFromJson<ApiReleaseDto>();
        };
    }

    public static void ConfigurePutApiReleaseInApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetPutApiReleaseInApim);
    }

    private static PutApiReleaseInApim GetPutApiReleaseInApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, apiName, cancellationToken) =>
        {
            logger.LogInformation("Putting API release {ApiReleaseName} in API {ApiName}...", name, apiName);

            await ApiReleaseUri.From(name, apiName, serviceUri)
                               .PutDto(dto, pipeline, cancellationToken);
        };
    }

    public static void ConfigureDeleteApiReleases(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseApiReleaseName(builder);
        ConfigureIsApiReleaseNameInSourceControl(builder);
        ConfigureDeleteApiRelease(builder);

        builder.Services.TryAddSingleton(GetDeleteApiReleases);
    }

    private static DeleteApiReleases GetDeleteApiReleases(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseApiReleaseName>();
        var isNameInSourceControl = provider.GetRequiredService<IsApiReleaseNameInSourceControl>();
        var delete = provider.GetRequiredService<DeleteApiRelease>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteApiReleases));

            logger.LogInformation("Deleting API releases...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(release => isNameInSourceControl(release.Name, release.ApiName) is false)
                    .Distinct()
                    .IterParallel(async release => await delete(release.Name, release.ApiName, cancellationToken), cancellationToken);
        };
    }

    private static void ConfigureDeleteApiRelease(IHostApplicationBuilder builder)
    {
        ConfigureDeleteApiReleaseFromApim(builder);

        builder.Services.TryAddSingleton(GetDeleteApiRelease);
    }

    private static DeleteApiRelease GetDeleteApiRelease(IServiceProvider provider)
    {
        var deleteFromApim = provider.GetRequiredService<DeleteApiReleaseFromApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteApiRelease))
                                       ?.AddTag("api_release.name", name)
                                       ?.AddTag("api.name", apiName);

            await deleteFromApim(name, apiName, cancellationToken);
        };
    }

    public static void ConfigureDeleteApiReleaseFromApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetDeleteApiReleaseFromApim);
    }

    private static DeleteApiReleaseFromApim GetDeleteApiReleaseFromApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, apiName, cancellationToken) =>
        {
            logger.LogInformation("Deleting API release {ApiReleaseName} from API {ApiName}...", name, apiName);

            await ApiReleaseUri.From(name, apiName, serviceUri)
                               .Delete(pipeline, cancellationToken);
        };
    }
}