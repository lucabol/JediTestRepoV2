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

public sealed record ApiResolverName : ResourceName, IResourceName<ApiResolverName>
{
    private ApiResolverName(string value) : base(value) { }

    public static ApiResolverName From(string value) => new(value);
}

public sealed record ApiResolversUri : ResourceUri
{
    public required ApiUri Parent { get; init; }

    private static string PathSegment { get; } = "resolvers";

    protected override Uri Value =>
        Parent.ToUri().AppendPathSegment(PathSegment).ToUri();

    public static ApiResolversUri From(ApiName apiName, ManagementServiceUri serviceUri) =>
        new() { Parent = ApiUri.From(apiName, serviceUri) };
}

public sealed record ApiResolverUri : ResourceUri
{
    public required ApiResolversUri Parent { get; init; }

    public required ApiResolverName Name { get; init; }

    protected override Uri Value =>
        Parent.ToUri().AppendPathSegment(Name.ToString()).ToUri();

    public static ApiResolverUri From(ApiResolverName name, ApiName apiName, ManagementServiceUri serviceUri) =>
        new()
        {
            Parent = ApiResolversUri.From(apiName, serviceUri),
            Name = name
        };
}

public sealed record ApiResolversDirectory : ResourceDirectory
{
    public required ApiDirectory Parent { get; init; }

    private static string Name { get; } = "resolvers";

    protected override DirectoryInfo Value =>
        Parent.ToDirectoryInfo().GetChildDirectory(Name);

    public static ApiResolversDirectory From(ApiName apiName, ManagementServiceDirectory serviceDirectory) =>
        new() { Parent = ApiDirectory.From(apiName, serviceDirectory) };

    public static Option<ApiResolversDirectory> TryParse(DirectoryInfo? directory, ManagementServiceDirectory serviceDirectory) =>
        directory?.Name == Name
            ? from parent in ApiDirectory.TryParse(directory.Parent, serviceDirectory)
              select new ApiResolversDirectory { Parent = parent }
            : Option<ApiResolversDirectory>.None;
}

public sealed record ApiResolverDirectory : ResourceDirectory
{
    public required ApiResolversDirectory Parent { get; init; }

    public required ApiResolverName Name { get; init; }

    protected override DirectoryInfo Value =>
        Parent.ToDirectoryInfo().GetChildDirectory(Name.Value);

    public static ApiResolverDirectory From(ApiResolverName name, ApiName apiName, ManagementServiceDirectory serviceDirectory) =>
        new()
        {
            Parent = ApiResolversDirectory.From(apiName, serviceDirectory),
            Name = name
        };

    public static Option<ApiResolverDirectory> TryParse(DirectoryInfo? directory, ManagementServiceDirectory serviceDirectory) =>
        from parent in ApiResolversDirectory.TryParse(directory?.Parent, serviceDirectory)
        let name = ApiResolverName.From(directory!.Name)
        select new ApiResolverDirectory
        {
            Parent = parent,
            Name = name
        };
}

public sealed record ApiResolverInformationFile : ResourceFile
{
    public required ApiResolverDirectory Parent { get; init; }

    private static string Name { get; } = "resolverInformation.json";

    protected override FileInfo Value =>
        Parent.ToDirectoryInfo().GetChildFile(Name);

    public static ApiResolverInformationFile From(ApiResolverName name, ApiName apiName, ManagementServiceDirectory serviceDirectory) =>
        new()
        {
            Parent = ApiResolverDirectory.From(name, apiName, serviceDirectory)
        };

    public static Option<ApiResolverInformationFile> TryParse(FileInfo? file, ManagementServiceDirectory serviceDirectory) =>
        file?.Name == Name
            ? from parent in ApiResolverDirectory.TryParse(file.Directory, serviceDirectory)
              select new ApiResolverInformationFile
              {
                  Parent = parent
              }
            : Option<ApiResolverInformationFile>.None;
}

public sealed record ApiResolverDto
{
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public required ResolverContract Properties { get; init; }

    public sealed record ResolverContract
    {
        [JsonPropertyName("displayName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? DisplayName { get; init; }

        [JsonPropertyName("path")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Path { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Description { get; init; }

        [JsonPropertyName("requestMethod")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? RequestMethod { get; init; }
    }
}

public static class ApiResolverModule
{
    public static IAsyncEnumerable<ApiResolverName> ListNames(this ApiResolversUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        pipeline.ListJsonObjects(uri.ToUri(), cancellationToken)
                .Select(jsonObject => jsonObject.GetStringProperty("name"))
                .Select(ApiResolverName.From);

    public static IAsyncEnumerable<(ApiResolverName Name, ApiResolverDto Dto)> List(this ApiResolversUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        uri.ListNames(pipeline, cancellationToken)
           .SelectAwait(async name =>
           {
               var resourceUri = new ApiResolverUri { Parent = uri, Name = name };
               var dto = await resourceUri.GetDto(pipeline, cancellationToken);
               return (name, dto);
           });

    public static async ValueTask<ApiResolverDto> GetDto(this ApiResolverUri uri, HttpPipeline pipeline, CancellationToken cancellationToken)
    {
        var content = await pipeline.GetContent(uri.ToUri(), cancellationToken);
        return content.ToObjectFromJson<ApiResolverDto>();
    }

    public static IEnumerable<ApiResolverDirectory> ListDirectories(ManagementServiceDirectory serviceDirectory) =>
        from apiDirectory in ApiModule.ListDirectories(serviceDirectory)
        let resolversDirectory = new ApiResolversDirectory { Parent = apiDirectory }
        where resolversDirectory.ToDirectoryInfo().Exists()
        from resolverDirectoryInfo in resolversDirectory.ToDirectoryInfo().ListDirectories("*")
        let name = ApiResolverName.From(resolverDirectoryInfo.Name)
        select new ApiResolverDirectory
        {
            Parent = resolversDirectory,
            Name = name
        };

    public static IEnumerable<ApiResolverInformationFile> ListInformationFiles(ManagementServiceDirectory serviceDirectory) =>
        from resolverDirectory in ListDirectories(serviceDirectory)
        let informationFile = new ApiResolverInformationFile { Parent = resolverDirectory }
        where informationFile.ToFileInfo().Exists()
        select informationFile;

    public static async ValueTask WriteDto(this ApiResolverInformationFile file, ApiResolverDto dto, CancellationToken cancellationToken)
    {
        var content = BinaryData.FromObjectAsJson(dto, JsonObjectExtensions.SerializerOptions);
        await file.ToFileInfo().OverwriteWithBinaryData(content, cancellationToken);
    }

    public static async ValueTask<ApiResolverDto> ReadDto(this ApiResolverInformationFile file, CancellationToken cancellationToken)
    {
        var content = await file.ToFileInfo().ReadAsBinaryData(cancellationToken);
        return content.ToObjectFromJson<ApiResolverDto>();
    }

    public static async ValueTask PutDto(this ApiResolverUri uri, ApiResolverDto dto, HttpPipeline pipeline, CancellationToken cancellationToken)
    {
        var content = BinaryData.FromObjectAsJson(dto);
        await pipeline.PutContent(uri.ToUri(), content, cancellationToken);
    }

    public static async ValueTask Delete(this ApiResolverUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        await pipeline.DeleteResource(uri.ToUri(), waitForCompletion: true, cancellationToken);
}
