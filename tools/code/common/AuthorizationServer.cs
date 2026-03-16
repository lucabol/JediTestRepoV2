using Azure.Core.Pipeline;
using Flurl;
using LanguageExt;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace common;

public sealed record AuthorizationServerName : ResourceName, IResourceName<AuthorizationServerName>
{
    private AuthorizationServerName(string value) : base(value) { }

    public static AuthorizationServerName From(string value) => new(value);
}

public sealed record AuthorizationServersUri : ResourceUri
{
    public required ManagementServiceUri ServiceUri { get; init; }

    private static string PathSegment { get; } = "authorizationServers";

    protected override Uri Value =>
        ServiceUri.ToUri().AppendPathSegment(PathSegment).ToUri();

    public static AuthorizationServersUri From(ManagementServiceUri serviceUri) =>
        new() { ServiceUri = serviceUri };
}

public sealed record AuthorizationServerUri : ResourceUri
{
    public required AuthorizationServersUri Parent { get; init; }

    public required AuthorizationServerName Name { get; init; }

    protected override Uri Value =>
        Parent.ToUri().AppendPathSegment(Name.ToString()).ToUri();

    public static AuthorizationServerUri From(AuthorizationServerName name, ManagementServiceUri serviceUri) =>
        new()
        {
            Parent = AuthorizationServersUri.From(serviceUri),
            Name = name
        };
}

public sealed record AuthorizationServersDirectory : ResourceDirectory
{
    public required ManagementServiceDirectory ServiceDirectory { get; init; }

    private static string Name { get; } = "authorization servers";

    protected override DirectoryInfo Value =>
        ServiceDirectory.ToDirectoryInfo().GetChildDirectory(Name);

    public static AuthorizationServersDirectory From(ManagementServiceDirectory serviceDirectory) =>
        new() { ServiceDirectory = serviceDirectory };

    public static Option<AuthorizationServersDirectory> TryParse(DirectoryInfo? directory, ManagementServiceDirectory serviceDirectory) =>
        directory?.Name == Name &&
        directory?.Parent?.FullName == serviceDirectory.ToDirectoryInfo().FullName
            ? new AuthorizationServersDirectory { ServiceDirectory = serviceDirectory }
            : Option<AuthorizationServersDirectory>.None;
}

public sealed record AuthorizationServerDirectory : ResourceDirectory
{
    public required AuthorizationServersDirectory Parent { get; init; }

    public required AuthorizationServerName Name { get; init; }

    protected override DirectoryInfo Value =>
        Parent.ToDirectoryInfo().GetChildDirectory(Name.Value);

    public static AuthorizationServerDirectory From(AuthorizationServerName name, ManagementServiceDirectory serviceDirectory) =>
        new()
        {
            Parent = AuthorizationServersDirectory.From(serviceDirectory),
            Name = name
        };

    public static Option<AuthorizationServerDirectory> TryParse(DirectoryInfo? directory, ManagementServiceDirectory serviceDirectory) =>
        from parent in AuthorizationServersDirectory.TryParse(directory?.Parent, serviceDirectory)
        let name = AuthorizationServerName.From(directory!.Name)
        select new AuthorizationServerDirectory
        {
            Parent = parent,
            Name = name
        };
}

public sealed record AuthorizationServerInformationFile : ResourceFile
{
    public required AuthorizationServerDirectory Parent { get; init; }

    public static string Name { get; } = "authorizationServerInformation.json";

    protected override FileInfo Value =>
        Parent.ToDirectoryInfo().GetChildFile(Name);

    public static AuthorizationServerInformationFile From(AuthorizationServerName name, ManagementServiceDirectory serviceDirectory) =>
        new()
        {
            Parent = AuthorizationServerDirectory.From(name, serviceDirectory)
        };

    public static Option<AuthorizationServerInformationFile> TryParse(FileInfo? file, ManagementServiceDirectory serviceDirectory) =>
        file is not null &&
        file.Name == Name
            ? from parent in AuthorizationServerDirectory.TryParse(file.Directory, serviceDirectory)
              select new AuthorizationServerInformationFile
              {
                  Parent = parent
              }
            : Option<AuthorizationServerInformationFile>.None;
}

public sealed record AuthorizationServerDto
{
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public required AuthorizationServerContract Properties { get; init; }

    public record AuthorizationServerContract
    {
        [JsonPropertyName("authorizationEndpoint")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? AuthorizationEndpoint { get; init; }

        [JsonPropertyName("authorizationMethods")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public ImmutableArray<string>? AuthorizationMethods { get; init; }

        [JsonPropertyName("bearerTokenSendingMethods")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public ImmutableArray<string>? BearerTokenSendingMethods { get; init; }

        [JsonPropertyName("clientAuthenticationMethod")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public ImmutableArray<string>? ClientAuthenticationMethod { get; init; }

        [JsonPropertyName("clientId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? ClientId { get; init; }

        [JsonPropertyName("clientRegistrationEndpoint")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? ClientRegistrationEndpoint { get; init; }

        [JsonPropertyName("clientSecret")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? ClientSecret { get; init; }

        [JsonPropertyName("defaultScope")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? DefaultScope { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Description { get; init; }

        [JsonPropertyName("displayName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? DisplayName { get; init; }

        [JsonPropertyName("grantTypes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public ImmutableArray<string>? GrantTypes { get; init; }

        [JsonPropertyName("resourceOwnerPassword")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? ResourceOwnerPassword { get; init; }

        [JsonPropertyName("resourceOwnerUsername")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? ResourceOwnerUsername { get; init; }

        [JsonPropertyName("supportState")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool? SupportState { get; init; }

        [JsonPropertyName("tokenBodyParameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public ImmutableArray<TokenBodyParameterContract>? TokenBodyParameters { get; init; }

        [JsonPropertyName("tokenEndpoint")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? TokenEndpoint { get; init; }
    }

    public record TokenBodyParameterContract
    {
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Name { get; init; }

        [JsonPropertyName("value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Value { get; init; }
    }
}

public static class AuthorizationServerModule
{
    public static async ValueTask DeleteAll(this AuthorizationServersUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        await uri.ListNames(pipeline, cancellationToken)
                 .IterParallel(async name =>
                 {
                     var authorizationServerUri = new AuthorizationServerUri { Parent = uri, Name = name };
                     await authorizationServerUri.Delete(pipeline, cancellationToken);
                 }, cancellationToken);

    public static IAsyncEnumerable<AuthorizationServerName> ListNames(this AuthorizationServersUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        pipeline.ListJsonObjects(uri.ToUri(), cancellationToken)
                .Select(jsonObject => jsonObject.GetStringProperty("name"))
                .Select(AuthorizationServerName.From);

    public static IAsyncEnumerable<(AuthorizationServerName Name, AuthorizationServerDto Dto)> List(this AuthorizationServersUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        uri.ListNames(pipeline, cancellationToken)
           .SelectAwait(async name =>
           {
               var authorizationServerUri = new AuthorizationServerUri { Parent = uri, Name = name };
               var dto = await authorizationServerUri.GetDto(pipeline, cancellationToken);
               return (name, dto);
           });

    public static async ValueTask<Option<AuthorizationServerDto>> TryGetDto(this AuthorizationServerUri uri, HttpPipeline pipeline, CancellationToken cancellationToken)
    {
        var contentOption = await pipeline.GetContentOption(uri.ToUri(), cancellationToken);
        return contentOption.Map(content => content.ToObjectFromJson<AuthorizationServerDto>());
    }

    public static async ValueTask<AuthorizationServerDto> GetDto(this AuthorizationServerUri uri, HttpPipeline pipeline, CancellationToken cancellationToken)
    {
        var content = await pipeline.GetContent(uri.ToUri(), cancellationToken);
        return content.ToObjectFromJson<AuthorizationServerDto>();
    }

    public static async ValueTask Delete(this AuthorizationServerUri uri, HttpPipeline pipeline, CancellationToken cancellationToken) =>
        await pipeline.DeleteResource(uri.ToUri(), waitForCompletion: true, cancellationToken);

    public static async ValueTask PutDto(this AuthorizationServerUri uri, AuthorizationServerDto dto, HttpPipeline pipeline, CancellationToken cancellationToken)
    {
        var content = BinaryData.FromObjectAsJson(dto);
        await pipeline.PutContent(uri.ToUri(), content, cancellationToken);
    }

    public static IEnumerable<AuthorizationServerDirectory> ListDirectories(ManagementServiceDirectory serviceDirectory)
    {
        var authorizationServersDirectory = AuthorizationServersDirectory.From(serviceDirectory);

        return from authorizationServersDirectoryInfo in authorizationServersDirectory.ToDirectoryInfo().ListDirectories("*")
               let name = AuthorizationServerName.From(authorizationServersDirectoryInfo.Name)
               select new AuthorizationServerDirectory
               {
                   Parent = authorizationServersDirectory,
                   Name = name
               };
    }

    public static IEnumerable<AuthorizationServerInformationFile> ListInformationFiles(ManagementServiceDirectory serviceDirectory) =>
        from authorizationServerDirectory in ListDirectories(serviceDirectory)
        let informationFile = new AuthorizationServerInformationFile { Parent = authorizationServerDirectory }
        where informationFile.ToFileInfo().Exists()
        select informationFile;

    public static async ValueTask WriteDto(this AuthorizationServerInformationFile file, AuthorizationServerDto dto, CancellationToken cancellationToken)
    {
        var content = BinaryData.FromObjectAsJson(dto, JsonObjectExtensions.SerializerOptions);
        await file.ToFileInfo().OverwriteWithBinaryData(content, cancellationToken);
    }

    public static async ValueTask<AuthorizationServerDto> ReadDto(this AuthorizationServerInformationFile file, CancellationToken cancellationToken)
    {
        var content = await file.ToFileInfo().ReadAsBinaryData(cancellationToken);
        return content.ToObjectFromJson<AuthorizationServerDto>();
    }
}
