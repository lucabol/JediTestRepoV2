using Azure.Core.Pipeline;
using Flurl;
using LanguageExt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace common;

public sealed record ApiResolverPolicyName : ResourceName
{
    private ApiResolverPolicyName(string value) : base(value) { }

    public static ApiResolverPolicyName From(string value) => new(value);
}

public sealed record ApiResolverPoliciesUri : ResourceUri
{
    public required ApiResolverUri Parent { get; init; }

    private static string PathSegment { get; } = "policies";

    protected override Uri Value => Parent.ToUri().AppendPathSegment(PathSegment).ToUri();

    public static ApiResolverPoliciesUri From(ApiResolverName resolverName, ApiName apiName, ManagementServiceUri serviceUri) =>
        new() { Parent = ApiResolverUri.From(resolverName, apiName, serviceUri) };
}

public sealed record ApiResolverPolicyUri : ResourceUri
{
    public required ApiResolverPoliciesUri Parent { get; init; }
    public required ApiResolverPolicyName Name { get; init; }

    protected override Uri Value => Parent.ToUri().AppendPathSegment(Name.ToString()).ToUri();

    public static ApiResolverPolicyUri From(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName, ManagementServiceUri serviceUri) =>
        new()
        {
            Parent = ApiResolverPoliciesUri.From(resolverName, apiName, serviceUri),
            Name = name
        };
}

public sealed record ApiResolverPolicyFile : ResourceFile
{
    public required ApiResolverDirectory Parent { get; init; }
    public required ApiResolverPolicyName Name { get; init; }

    protected override FileInfo Value =>
        Parent.ToDirectoryInfo().GetChildFile($"{Name}.xml");

    public static ApiResolverPolicyFile From(ApiResolverPolicyName name, ApiResolverName resolverName, ApiName apiName, ManagementServiceDirectory serviceDirectory) =>
        new()
        {
            Parent = ApiResolverDirectory.From(resolverName, apiName, serviceDirectory),
            Name = name
        };

    public static Option<ApiResolverPolicyFile> TryParse(FileInfo? file, ManagementServiceDirectory serviceDirectory) =>
        from name in TryParseApiResolverPolicyName(file)
        from parent in ApiResolverDirectory.TryParse(file?.Directory, serviceDirectory)
        select new ApiResolverPolicyFile
        {
            Name = name,
            Parent = parent
        };

    internal static Option<ApiResolverPolicyName> TryParseApiResolverPolicyName(FileInfo? file) =>
        file?.Name.EndsWith(".xml", StringComparison.Ordinal) switch
        {
            true => ApiResolverPolicyName.From(Path.GetFileNameWithoutExtension(file.Name)),
            _ => Option<ApiResolverPolicyName>.None
        };
}

public sealed record ApiResolverPolicyDto
{
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public required ApiResolverPolicyContract Properties { get; init; }

    public sealed record ApiResolverPolicyContract
    {
        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Description { get; init; }

        [JsonPropertyName("format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Format { get; init; }

        [JsonPropertyName("value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Value { get; init; }
    }
}

public static class ApiResolverPolicyModule
{
    public static IAsyncEnumerable<ApiResolverPolicyName> ListNames(this ApiResolverPoliciesUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        pipeline.ListJsonObjects(uri.ToUri(), cancellationToken)
                .Select(jsonObject => jsonObject.GetStringProperty("name"))
                .Select(ApiResolverPolicyName.From);

    public static IAsyncEnumerable<(ApiResolverPolicyName Name, ApiResolverPolicyDto Dto)> List(this ApiResolverPoliciesUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        uri.ListNames(pipeline, cancellationToken)
           .SelectAwait(async name =>
           {
               var policyUri = new ApiResolverPolicyUri { Parent = uri, Name = name };
               var dto = await policyUri.GetDto(pipeline, cancellationToken);
               return (name, dto);
           });

    public static async ValueTask<ApiResolverPolicyDto> GetDto(this ApiResolverPolicyUri uri, HttpPipeline pipeline, CancellationToken cancellationToken)
    {
        var contentUri = uri.ToUri().AppendQueryParam("format", "rawxml").ToUri();
        var content = await pipeline.GetContent(contentUri, cancellationToken);
        return content.ToObjectFromJson<ApiResolverPolicyDto>();
    }

    public static async ValueTask Delete(this ApiResolverPolicyUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        await pipeline.DeleteResource(uri.ToUri(), waitForCompletion: true, cancellationToken);

    public static async ValueTask PutDto(this ApiResolverPolicyUri uri, ApiResolverPolicyDto dto, HttpPipeline pipeline, CancellationToken cancellationToken)
    {
        var content = BinaryData.FromObjectAsJson(dto);
        await pipeline.PutContent(uri.ToUri(), content, cancellationToken);
    }

    public static async ValueTask WritePolicy(this ApiResolverPolicyFile file, string policy, CancellationToken cancellationToken)
    {
        var content = BinaryData.FromString(policy);
        await file.ToFileInfo().OverwriteWithBinaryData(content, cancellationToken);
    }

    public static async ValueTask<string> ReadPolicy(this ApiResolverPolicyFile file, CancellationToken cancellationToken)
    {
        var content = await file.ToFileInfo().ReadAsBinaryData(cancellationToken);
        return content.ToString();
    }
}
