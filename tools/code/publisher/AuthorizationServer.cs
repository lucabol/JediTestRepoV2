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

public delegate ValueTask PutAuthorizationServers(CancellationToken cancellationToken);
public delegate ValueTask DeleteAuthorizationServers(CancellationToken cancellationToken);
public delegate Option<AuthorizationServerName> TryParseAuthorizationServerName(FileInfo file);
public delegate bool IsAuthorizationServerNameInSourceControl(AuthorizationServerName name);
public delegate ValueTask PutAuthorizationServer(AuthorizationServerName name, CancellationToken cancellationToken);
public delegate ValueTask<Option<AuthorizationServerDto>> FindAuthorizationServerDto(AuthorizationServerName name, CancellationToken cancellationToken);
public delegate ValueTask PutAuthorizationServerInApim(AuthorizationServerName name, AuthorizationServerDto dto, CancellationToken cancellationToken);
public delegate ValueTask DeleteAuthorizationServer(AuthorizationServerName name, CancellationToken cancellationToken);
public delegate ValueTask DeleteAuthorizationServerFromApim(AuthorizationServerName name, CancellationToken cancellationToken);

internal static class AuthorizationServerModule
{
    public static void ConfigurePutAuthorizationServers(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseAuthorizationServerName(builder);
        ConfigureIsAuthorizationServerNameInSourceControl(builder);
        ConfigurePutAuthorizationServer(builder);

        builder.Services.TryAddSingleton(GetPutAuthorizationServers);
    }

    private static PutAuthorizationServers GetPutAuthorizationServers(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseAuthorizationServerName>();
        var isNameInSourceControl = provider.GetRequiredService<IsAuthorizationServerNameInSourceControl>();
        var put = provider.GetRequiredService<PutAuthorizationServer>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(PutAuthorizationServers));

            logger.LogInformation("Putting authorization servers...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(isNameInSourceControl.Invoke)
                    .Distinct()
                    .IterParallel(put.Invoke, cancellationToken);
        };
    }

    private static void ConfigureTryParseAuthorizationServerName(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetTryParseAuthorizationServerName);
    }

    private static TryParseAuthorizationServerName GetTryParseAuthorizationServerName(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return file => from informationFile in AuthorizationServerInformationFile.TryParse(file, serviceDirectory)
                       select informationFile.Parent.Name;
    }

    private static void ConfigureIsAuthorizationServerNameInSourceControl(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetArtifactFiles(builder);
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetIsAuthorizationServerNameInSourceControl);
    }

    private static IsAuthorizationServerNameInSourceControl GetIsAuthorizationServerNameInSourceControl(IServiceProvider provider)
    {
        var getArtifactFiles = provider.GetRequiredService<GetArtifactFiles>();
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return doesInformationFileExist;

        bool doesInformationFileExist(AuthorizationServerName name)
        {
            var artifactFiles = getArtifactFiles();
            var informationFile = AuthorizationServerInformationFile.From(name, serviceDirectory);

            return artifactFiles.Contains(informationFile.ToFileInfo());
        }
    }

    private static void ConfigurePutAuthorizationServer(IHostApplicationBuilder builder)
    {
        ConfigureFindAuthorizationServerDto(builder);
        ConfigurePutAuthorizationServerInApim(builder);

        builder.Services.TryAddSingleton(GetPutAuthorizationServer);
    }

    private static PutAuthorizationServer GetPutAuthorizationServer(IServiceProvider provider)
    {
        var findDto = provider.GetRequiredService<FindAuthorizationServerDto>();
        var putInApim = provider.GetRequiredService<PutAuthorizationServerInApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(PutAuthorizationServer))
                                       ?.AddTag("authorizationServer.name", name);

            var dtoOption = await findDto(name, cancellationToken);
            await dtoOption.IterTask(async dto => await putInApim(name, dto, cancellationToken));
        };
    }

    private static void ConfigureFindAuthorizationServerDto(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);
        CommonModule.ConfigureTryGetFileContents(builder);
        OverrideDtoModule.ConfigureOverrideDtoFactory(builder);

        builder.Services.TryAddSingleton(GetFindAuthorizationServerDto);
    }

    private static FindAuthorizationServerDto GetFindAuthorizationServerDto(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var tryGetFileContents = provider.GetRequiredService<TryGetFileContents>();
        var overrideFactory = provider.GetRequiredService<OverrideDtoFactory>();

        var overrideDto = overrideFactory.Create<AuthorizationServerName, AuthorizationServerDto>();

        return async (name, cancellationToken) =>
        {
            var informationFile = AuthorizationServerInformationFile.From(name, serviceDirectory);
            var contentsOption = await tryGetFileContents(informationFile.ToFileInfo(), cancellationToken);

            return from contents in contentsOption
                   let dto = contents.ToObjectFromJson<AuthorizationServerDto>()
                   select overrideDto(name, dto);
        };
    }

    private static void ConfigurePutAuthorizationServerInApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetPutAuthorizationServerInApim);
    }

    private static PutAuthorizationServerInApim GetPutAuthorizationServerInApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, cancellationToken) =>
        {
            logger.LogInformation("Putting authorization server {AuthorizationServerName}...", name);

            var resourceUri = AuthorizationServerUri.From(name, serviceUri);
            await resourceUri.PutDto(dto, pipeline, cancellationToken);
        };
    }

    public static void ConfigureDeleteAuthorizationServers(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseAuthorizationServerName(builder);
        ConfigureIsAuthorizationServerNameInSourceControl(builder);
        ConfigureDeleteAuthorizationServer(builder);

        builder.Services.TryAddSingleton(GetDeleteAuthorizationServers);
    }

    private static DeleteAuthorizationServers GetDeleteAuthorizationServers(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseAuthorizationServerName>();
        var isNameInSourceControl = provider.GetRequiredService<IsAuthorizationServerNameInSourceControl>();
        var delete = provider.GetRequiredService<DeleteAuthorizationServer>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteAuthorizationServers));

            logger.LogInformation("Deleting authorization servers...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(name => isNameInSourceControl(name) is false)
                    .Distinct()
                    .IterParallel(delete.Invoke, cancellationToken);
        };
    }

    private static void ConfigureDeleteAuthorizationServer(IHostApplicationBuilder builder)
    {
        ConfigureDeleteAuthorizationServerFromApim(builder);

        builder.Services.TryAddSingleton(GetDeleteAuthorizationServer);
    }

    private static DeleteAuthorizationServer GetDeleteAuthorizationServer(IServiceProvider provider)
    {
        var deleteFromApim = provider.GetRequiredService<DeleteAuthorizationServerFromApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteAuthorizationServer))
                                       ?.AddTag("authorizationServer.name", name);

            await deleteFromApim(name, cancellationToken);
        };
    }

    private static void ConfigureDeleteAuthorizationServerFromApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetDeleteAuthorizationServerFromApim);
    }

    private static DeleteAuthorizationServerFromApim GetDeleteAuthorizationServerFromApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, cancellationToken) =>
        {
            logger.LogInformation("Deleting authorization server {AuthorizationServerName}...", name);

            var resourceUri = AuthorizationServerUri.From(name, serviceUri);
            await resourceUri.Delete(pipeline, cancellationToken);
        };
    }
}
