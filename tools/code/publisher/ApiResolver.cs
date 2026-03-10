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

public delegate ValueTask PutApiResolvers(CancellationToken cancellationToken);
public delegate ValueTask DeleteApiResolvers(CancellationToken cancellationToken);
public delegate Option<(ApiResolverName Name, ApiName ApiName)> TryParseApiResolverName(FileInfo file);
public delegate bool IsApiResolverNameInSourceControl(ApiResolverName name, ApiName apiName);
public delegate ValueTask PutApiResolver(ApiResolverName name, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask<Option<ApiResolverDto>> FindApiResolverDto(ApiResolverName name, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask PutApiResolverInApim(ApiResolverName name, ApiResolverDto dto, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask DeleteApiResolver(ApiResolverName name, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask DeleteApiResolverFromApim(ApiResolverName name, ApiName apiName, CancellationToken cancellationToken);

internal static class ApiResolverModule
{
    public static void ConfigurePutApiResolvers(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseApiResolverName(builder);
        ConfigureIsApiResolverNameInSourceControl(builder);
        ConfigurePutApiResolver(builder);

        builder.Services.TryAddSingleton(GetPutApiResolvers);
    }

    private static PutApiResolvers GetPutApiResolvers(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseApiResolverName>();
        var isNameInSourceControl = provider.GetRequiredService<IsApiResolverNameInSourceControl>();
        var put = provider.GetRequiredService<PutApiResolver>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(PutApiResolvers));

            logger.LogInformation("Putting API resolvers...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(resource => isNameInSourceControl(resource.Name, resource.ApiName))
                    .Distinct()
                    .IterParallel(put.Invoke, cancellationToken);
        };
    }

    private static void ConfigureTryParseApiResolverName(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetTryParseApiResolverName);
    }

    private static TryParseApiResolverName GetTryParseApiResolverName(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return file => from informationFile in ApiResolverInformationFile.TryParse(file, serviceDirectory)
                       select (informationFile.Parent.Name, informationFile.Parent.Parent.Parent.Name);
    }

    private static void ConfigureIsApiResolverNameInSourceControl(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetArtifactFiles(builder);
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetIsApiResolverNameInSourceControl);
    }

    private static IsApiResolverNameInSourceControl GetIsApiResolverNameInSourceControl(IServiceProvider provider)
    {
        var getArtifactFiles = provider.GetRequiredService<GetArtifactFiles>();
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return doesInformationFileExist;

        bool doesInformationFileExist(ApiResolverName name, ApiName apiName)
        {
            var artifactFiles = getArtifactFiles();
            var informationFile = ApiResolverInformationFile.From(name, apiName, serviceDirectory);

            return artifactFiles.Contains(informationFile.ToFileInfo());
        }
    }

    private static void ConfigurePutApiResolver(IHostApplicationBuilder builder)
    {
        ConfigureFindApiResolverDto(builder);
        ConfigurePutApiResolverInApim(builder);

        builder.Services.TryAddSingleton(GetPutApiResolver);
    }

    private static PutApiResolver GetPutApiResolver(IServiceProvider provider)
    {
        var findDto = provider.GetRequiredService<FindApiResolverDto>();
        var putInApim = provider.GetRequiredService<PutApiResolverInApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(PutApiResolver))
                                       ?.AddTag("api.name", apiName)
                                       ?.AddTag("api_resolver.name", name);

            var dtoOption = await findDto(name, apiName, cancellationToken);
            await dtoOption.IterTask(async dto => await putInApim(name, dto, apiName, cancellationToken));
        };
    }

    private static void ConfigureFindApiResolverDto(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);
        CommonModule.ConfigureTryGetFileContents(builder);

        builder.Services.TryAddSingleton(GetFindApiResolverDto);
    }

    private static FindApiResolverDto GetFindApiResolverDto(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var tryGetFileContents = provider.GetRequiredService<TryGetFileContents>();

        return async (name, apiName, cancellationToken) =>
        {
            var informationFile = ApiResolverInformationFile.From(name, apiName, serviceDirectory);
            var contentsOption = await tryGetFileContents(informationFile.ToFileInfo(), cancellationToken);

            return from contents in contentsOption
                   select contents.ToObjectFromJson<ApiResolverDto>();
        };
    }

    private static void ConfigurePutApiResolverInApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetPutApiResolverInApim);
    }

    private static PutApiResolverInApim GetPutApiResolverInApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, apiName, cancellationToken) =>
        {
            logger.LogInformation("Adding resolver {ApiResolverName} to API {ApiName}...", name, apiName);

            var resourceUri = ApiResolverUri.From(name, apiName, serviceUri);
            await resourceUri.PutDto(dto, pipeline, cancellationToken);
        };
    }

    public static void ConfigureDeleteApiResolvers(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseApiResolverName(builder);
        ConfigureIsApiResolverNameInSourceControl(builder);
        ConfigureDeleteApiResolver(builder);

        builder.Services.TryAddSingleton(GetDeleteApiResolvers);
    }

    private static DeleteApiResolvers GetDeleteApiResolvers(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseApiResolverName>();
        var isNameInSourceControl = provider.GetRequiredService<IsApiResolverNameInSourceControl>();
        var delete = provider.GetRequiredService<DeleteApiResolver>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteApiResolvers));

            logger.LogInformation("Deleting API resolvers...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(resource => isNameInSourceControl(resource.Name, resource.ApiName) is false)
                    .Distinct()
                    .IterParallel(delete.Invoke, cancellationToken);
        };
    }

    private static void ConfigureDeleteApiResolver(IHostApplicationBuilder builder)
    {
        ConfigureDeleteApiResolverFromApim(builder);

        builder.Services.TryAddSingleton(GetDeleteApiResolver);
    }

    private static DeleteApiResolver GetDeleteApiResolver(IServiceProvider provider)
    {
        var deleteFromApim = provider.GetRequiredService<DeleteApiResolverFromApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteApiResolver))
                                       ?.AddTag("api.name", apiName)
                                       ?.AddTag("api_resolver.name", name);

            await deleteFromApim(name, apiName, cancellationToken);
        };
    }

    private static void ConfigureDeleteApiResolverFromApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetDeleteApiResolverFromApim);
    }

    private static DeleteApiResolverFromApim GetDeleteApiResolverFromApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, apiName, cancellationToken) =>
        {
            logger.LogInformation("Removing resolver {ApiResolverName} from API {ApiName}...", name, apiName);

            var resourceUri = ApiResolverUri.From(name, apiName, serviceUri);
            await resourceUri.Delete(pipeline, cancellationToken);
        };
    }
}
