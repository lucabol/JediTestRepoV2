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

public delegate ValueTask PutApiResolverPolicies(CancellationToken cancellationToken);
public delegate Option<(ApiResolverPolicyName Name, ApiResolverName ResolverName, ApiName ApiName)> TryParseApiResolverPolicyName(FileInfo file);
public delegate bool IsApiResolverPolicyNameInSourceControl(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName);
public delegate ValueTask PutApiResolverPolicy(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask<Option<ApiResolverPolicyDto>> FindApiResolverPolicyDto(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask PutApiResolverPolicyInApim(ApiResolverPolicyName name, ApiResolverPolicyDto dto, ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask DeleteApiResolverPolicies(CancellationToken cancellationToken);
public delegate ValueTask DeleteApiResolverPolicy(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask DeleteApiResolverPolicyFromApim(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);

internal static class ApiResolverPolicyModule
{
    public static void ConfigurePutApiResolverPolicies(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseApiResolverPolicyName(builder);
        ConfigureIsApiResolverPolicyNameInSourceControl(builder);
        ConfigurePutApiResolverPolicy(builder);

        builder.Services.TryAddSingleton(GetPutApiResolverPolicies);
    }

    private static PutApiResolverPolicies GetPutApiResolverPolicies(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseApiResolverPolicyName>();
        var isNameInSourceControl = provider.GetRequiredService<IsApiResolverPolicyNameInSourceControl>();
        var put = provider.GetRequiredService<PutApiResolverPolicy>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(PutApiResolverPolicies));

            logger.LogInformation("Putting API resolver policies...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(policy => isNameInSourceControl(policy.Name, policy.ResolverName, policy.ApiName))
                    .Distinct()
                    .IterParallel(async policy => await put(policy.Name, policy.ResolverName, policy.ApiName, cancellationToken), cancellationToken);
        };
    }

    private static void ConfigureTryParseApiResolverPolicyName(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetTryParseApiResolverPolicyName);
    }

    private static TryParseApiResolverPolicyName GetTryParseApiResolverPolicyName(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return file => from policyFile in ApiResolverPolicyFile.TryParse(file, serviceDirectory)
                       select (policyFile.Name, policyFile.Parent.Name, policyFile.Parent.Parent.Parent.Name);
    }

    private static void ConfigureIsApiResolverPolicyNameInSourceControl(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetArtifactFiles(builder);
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetIsApiResolverPolicyNameInSourceControl);
    }

    private static IsApiResolverPolicyNameInSourceControl GetIsApiResolverPolicyNameInSourceControl(IServiceProvider provider)
    {
        var getArtifactFiles = provider.GetRequiredService<GetArtifactFiles>();
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();

        return doesPolicyFileExist;

        bool doesPolicyFileExist(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName)
        {
            var artifactFiles = getArtifactFiles();
            var policyFile = ApiResolverPolicyFile.From(name, resolverName, apiName, serviceDirectory);

            return artifactFiles.Contains(policyFile.ToFileInfo());
        }
    }

    private static void ConfigurePutApiResolverPolicy(IHostApplicationBuilder builder)
    {
        ConfigureFindApiResolverPolicyDto(builder);
        ConfigurePutApiResolverPolicyInApim(builder);

        builder.Services.TryAddSingleton(GetPutApiResolverPolicy);
    }

    private static PutApiResolverPolicy GetPutApiResolverPolicy(IServiceProvider provider)
    {
        var findDto = provider.GetRequiredService<FindApiResolverPolicyDto>();
        var putInApim = provider.GetRequiredService<PutApiResolverPolicyInApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, resolverName, apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(PutApiResolverPolicy))
                                       ?.AddTag("api_resolver_policy.name", name)
                                       ?.AddTag("api_resolver.name", resolverName)
                                       ?.AddTag("api.name", apiName);

            var dtoOption = await findDto(name, resolverName, apiName, cancellationToken);
            await dtoOption.IterTask(async dto => await putInApim(name, dto, resolverName, apiName, cancellationToken));
        };
    }

    private static void ConfigureFindApiResolverPolicyDto(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);
        CommonModule.ConfigureTryGetFileContents(builder);

        builder.Services.TryAddSingleton(GetFindApiResolverPolicyDto);
    }

    private static FindApiResolverPolicyDto GetFindApiResolverPolicyDto(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var tryGetFileContents = provider.GetRequiredService<TryGetFileContents>();

        return async (name, resolverName, apiName, cancellationToken) =>
        {
            var contentsOption = await tryGetPolicyContents(name, resolverName, apiName, cancellationToken);

            return from contents in contentsOption
                   select new ApiResolverPolicyDto
                   {
                       Properties = new ApiResolverPolicyDto.ApiResolverPolicyContract
                       {
                           Format = "rawxml",
                           Value = contents.ToString()
                       }
                   };
        };

        async ValueTask<Option<BinaryData>> tryGetPolicyContents(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken)
        {
            var policyFile = ApiResolverPolicyFile.From(name, resolverName, apiName, serviceDirectory);

            return await tryGetFileContents(policyFile.ToFileInfo(), cancellationToken);
        }
    }

    private static void ConfigurePutApiResolverPolicyInApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetPutApiResolverPolicyInApim);
    }

    private static PutApiResolverPolicyInApim GetPutApiResolverPolicyInApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, resolverName, apiName, cancellationToken) =>
        {
            logger.LogInformation("Putting policy {ApiResolverPolicyName} for resolver {ApiResolverName} in API {ApiName}...", name, resolverName, apiName);

            await ApiResolverPolicyUri.From(name, resolverName, apiName, serviceUri)
                                      .PutDto(dto, pipeline, cancellationToken);
        };
    }

    public static void ConfigureDeleteApiResolverPolicies(IHostApplicationBuilder builder)
    {
        CommonModule.ConfigureGetPublisherFiles(builder);
        ConfigureTryParseApiResolverPolicyName(builder);
        ConfigureIsApiResolverPolicyNameInSourceControl(builder);
        ConfigureDeleteApiResolverPolicy(builder);

        builder.Services.TryAddSingleton(GetDeleteApiResolverPolicies);
    }

    private static DeleteApiResolverPolicies GetDeleteApiResolverPolicies(IServiceProvider provider)
    {
        var getPublisherFiles = provider.GetRequiredService<GetPublisherFiles>();
        var tryParseName = provider.GetRequiredService<TryParseApiResolverPolicyName>();
        var isNameInSourceControl = provider.GetRequiredService<IsApiResolverPolicyNameInSourceControl>();
        var delete = provider.GetRequiredService<DeleteApiResolverPolicy>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async cancellationToken =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteApiResolverPolicies));

            logger.LogInformation("Deleting API resolver policies...");

            await getPublisherFiles()
                    .Choose(tryParseName.Invoke)
                    .Where(policy => isNameInSourceControl(policy.Name, policy.ResolverName, policy.ApiName) is false)
                    .Distinct()
                    .IterParallel(async policy => await delete(policy.Name, policy.ResolverName, policy.ApiName, cancellationToken), cancellationToken);
        };
    }

    private static void ConfigureDeleteApiResolverPolicy(IHostApplicationBuilder builder)
    {
        ConfigureDeleteApiResolverPolicyFromApim(builder);

        builder.Services.TryAddSingleton(GetDeleteApiResolverPolicy);
    }

    private static DeleteApiResolverPolicy GetDeleteApiResolverPolicy(IServiceProvider provider)
    {
        var deleteFromApim = provider.GetRequiredService<DeleteApiResolverPolicyFromApim>();
        var activitySource = provider.GetRequiredService<ActivitySource>();

        return async (name, resolverName, apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(DeleteApiResolverPolicy))
                                       ?.AddTag("api_resolver_policy.name", name)
                                       ?.AddTag("api_resolver.name", resolverName)
                                       ?.AddTag("api.name", apiName);

            await deleteFromApim(name, resolverName, apiName, cancellationToken);
        };
    }

    private static void ConfigureDeleteApiResolverPolicyFromApim(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetDeleteApiResolverPolicyFromApim);
    }

    private static DeleteApiResolverPolicyFromApim GetDeleteApiResolverPolicyFromApim(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, resolverName, apiName, cancellationToken) =>
        {
            logger.LogInformation("Deleting policy {ApiResolverPolicyName} from resolver {ApiResolverName} in API {ApiName}...", name, resolverName, apiName);

            await ApiResolverPolicyUri.From(name, resolverName, apiName, serviceUri)
                                      .Delete(pipeline, cancellationToken);
        };
    }
}
