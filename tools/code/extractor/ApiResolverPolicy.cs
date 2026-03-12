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

public delegate ValueTask ExtractApiResolverPolicies(ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);
public delegate IAsyncEnumerable<(ApiResolverPolicyName Name, ApiResolverPolicyDto Dto)> ListApiResolverPolicies(ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask WriteApiResolverPolicyArtifacts(ApiResolverPolicyName name, ApiResolverPolicyDto dto, ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);
public delegate ValueTask WriteApiResolverPolicyFile(ApiResolverPolicyName name, ApiResolverPolicyDto dto, ApiResolverName resolverName, ApiName apiName, CancellationToken cancellationToken);

internal static class ApiResolverPolicyModule
{
    public static void ConfigureExtractApiResolverPolicies(IHostApplicationBuilder builder)
    {
        ConfigureListApiResolverPolicies(builder);
        ConfigureWriteApiResolverPolicyArtifacts(builder);

        builder.Services.TryAddSingleton(GetExtractApiResolverPolicies);
    }

    private static ExtractApiResolverPolicies GetExtractApiResolverPolicies(IServiceProvider provider)
    {
        var list = provider.GetRequiredService<ListApiResolverPolicies>();
        var writeArtifacts = provider.GetRequiredService<WriteApiResolverPolicyArtifacts>();
        var activitySource = provider.GetRequiredService<ActivitySource>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (resolverName, apiName, cancellationToken) =>
        {
            using var _ = activitySource.StartActivity(nameof(ExtractApiResolverPolicies));

            logger.LogInformation("Extracting policies for resolver {ApiResolverName} in GraphQL API {ApiName}...", resolverName, apiName);

            await list(resolverName, apiName, cancellationToken)
                    .IterParallel(async policy => await writeArtifacts(policy.Name, policy.Dto, resolverName, apiName, cancellationToken),
                                  cancellationToken);
        };
    }

    private static void ConfigureListApiResolverPolicies(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceUri(builder);
        AzureModule.ConfigureHttpPipeline(builder);

        builder.Services.TryAddSingleton(GetListApiResolverPolicies);
    }

    private static ListApiResolverPolicies GetListApiResolverPolicies(IServiceProvider provider)
    {
        var serviceUri = provider.GetRequiredService<ManagementServiceUri>();
        var pipeline = provider.GetRequiredService<HttpPipeline>();

        return (resolverName, apiName, cancellationToken) =>
            ApiResolverPoliciesUri.From(resolverName, apiName, serviceUri)
                                  .List(pipeline, cancellationToken);
    }

    private static void ConfigureWriteApiResolverPolicyArtifacts(IHostApplicationBuilder builder)
    {
        ConfigureWriteApiResolverPolicyFile(builder);

        builder.Services.TryAddSingleton(GetWriteApiResolverPolicyArtifacts);
    }

    private static WriteApiResolverPolicyArtifacts GetWriteApiResolverPolicyArtifacts(IServiceProvider provider)
    {
        var writePolicyFile = provider.GetRequiredService<WriteApiResolverPolicyFile>();

        return async (name, dto, resolverName, apiName, cancellationToken) =>
            await writePolicyFile(name, dto, resolverName, apiName, cancellationToken);
    }

    private static void ConfigureWriteApiResolverPolicyFile(IHostApplicationBuilder builder)
    {
        AzureModule.ConfigureManagementServiceDirectory(builder);

        builder.Services.TryAddSingleton(GetWriteApiResolverPolicyFile);
    }

    private static WriteApiResolverPolicyFile GetWriteApiResolverPolicyFile(IServiceProvider provider)
    {
        var serviceDirectory = provider.GetRequiredService<ManagementServiceDirectory>();
        var logger = provider.GetRequiredService<ILogger>();

        return async (name, dto, resolverName, apiName, cancellationToken) =>
        {
            var policyFile = ApiResolverPolicyFile.From(name, resolverName, apiName, serviceDirectory);

            logger.LogInformation("Writing API resolver policy file {ApiResolverPolicyFile}...", policyFile);
            var policy = dto.Properties.Value ?? string.Empty;
            await policyFile.WritePolicy(policy, cancellationToken);
        };
    }
}
