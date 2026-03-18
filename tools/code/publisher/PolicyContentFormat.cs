using common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System;

namespace publisher;

internal sealed record DefaultPolicyContentFormat(PolicyContentFormat Value);

internal static class PolicyContentFormatModule
{
    public static void ConfigureDefaultPolicyContentFormat(IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(GetDefaultPolicyContentFormat);
    }

    private static DefaultPolicyContentFormat GetDefaultPolicyContentFormat(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        var formatOption = configuration.TryGetValue("POLICY_SPECIFICATION_FORMAT")
                        | configuration.TryGetValue("policySpecificationFormat");

        var format = formatOption.Map(value => value.ToLowerInvariant() switch
        {
            "rawxml" => new PolicyContentFormat.RawXml() as PolicyContentFormat,
            "xml" => new PolicyContentFormat.Xml() as PolicyContentFormat,
            var unsupported => throw new NotSupportedException($"Policy specification format '{unsupported}' is not supported. Valid values are 'rawxml' and 'xml'.")
        }).IfNone(() => new PolicyContentFormat.RawXml());

        return new DefaultPolicyContentFormat(format);
    }
}
