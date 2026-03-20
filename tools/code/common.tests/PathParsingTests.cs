using common;
using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace common.tests;

/// <summary>
/// Unit tests for resource path parsing logic used by extractor and publisher.
/// These tests verify that TryParse methods correctly identify directories/files
/// by name and location.
/// </summary>
public sealed class VersionSetPathParsingTests
{
    private static ManagementServiceDirectory CreateServiceDirectory(string path) =>
        ManagementServiceDirectory.From(new DirectoryInfo(path));

    [Fact]
    public void VersionSetsDirectory_TryParse_ReturnsSome_WhenNameMatchesAndParentIsServiceDir()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var versionSetsDir = new DirectoryInfo("/repo/apim/version sets");

        var result = VersionSetsDirectory.TryParse(versionSetsDir, serviceDir);

        result.IsSome.Should().BeTrue();
    }

    [Fact]
    public void VersionSetsDirectory_TryParse_ReturnsNone_WhenNameDoesNotMatch()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var wrongDir = new DirectoryInfo("/repo/apim/wrong-name");

        var result = VersionSetsDirectory.TryParse(wrongDir, serviceDir);

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public void VersionSetsDirectory_TryParse_ReturnsNone_WhenParentIsNotServiceDir()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        // "version sets" is nested one level too deep
        var nestedDir = new DirectoryInfo("/repo/apim/sub/version sets");

        var result = VersionSetsDirectory.TryParse(nestedDir, serviceDir);

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public void VersionSetsDirectory_TryParse_ReturnsNone_WhenDirectoryIsNull()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");

        var result = VersionSetsDirectory.TryParse(null, serviceDir);

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public void VersionSetDirectory_TryParse_ReturnsSome_WhenUnderVersionSetsDirectory()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var specificVersionSetDir = new DirectoryInfo("/repo/apim/version sets/my-version-set");

        var result = VersionSetDirectory.TryParse(specificVersionSetDir, serviceDir);

        result.IsSome.Should().BeTrue();
    }

    [Fact]
    public void VersionSetDirectory_TryParse_ReturnsCorrectName()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var specificVersionSetDir = new DirectoryInfo("/repo/apim/version sets/my-version-set");

        var result = VersionSetDirectory.TryParse(specificVersionSetDir, serviceDir);

        result.IfSome(dir => dir.Name.ToString().Should().Be("my-version-set"));
    }

    [Fact]
    public void VersionSetDirectory_TryParse_ReturnsNone_WhenNotUnderVersionSetsDirectory()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var wrongParent = new DirectoryInfo("/repo/apim/apis/my-version-set");

        var result = VersionSetDirectory.TryParse(wrongParent, serviceDir);

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public void VersionSetInformationFile_TryParse_ReturnsSome_WhenCorrectFileUnderVersionSetDir()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var infoFile = new FileInfo("/repo/apim/version sets/my-version-set/versionSetInformation.json");

        var result = VersionSetInformationFile.TryParse(infoFile, serviceDir);

        result.IsSome.Should().BeTrue();
    }

    [Fact]
    public void VersionSetInformationFile_TryParse_ReturnsNone_WhenWrongFileName()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var wrongFile = new FileInfo("/repo/apim/version sets/my-version-set/wrongName.json");

        var result = VersionSetInformationFile.TryParse(wrongFile, serviceDir);

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public void VersionSetInformationFile_TryParse_ReturnsNone_WhenNotUnderVersionSetsDirectory()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var wrongLocation = new FileInfo("/repo/apim/apis/my-version-set/versionSetInformation.json");

        var result = VersionSetInformationFile.TryParse(wrongLocation, serviceDir);

        result.IsNone.Should().BeTrue();
    }
}

/// <summary>
/// Unit tests for API path parsing, validating directory structure for API artifacts.
/// </summary>
public sealed class ApiPathParsingTests
{
    private static ManagementServiceDirectory CreateServiceDirectory(string path) =>
        ManagementServiceDirectory.From(new DirectoryInfo(path));

    [Fact]
    public void ApisDirectory_TryParse_ReturnsSome_WhenNameMatchesAndParentIsServiceDir()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var apisDir = new DirectoryInfo("/repo/apim/apis");

        var result = ApisDirectory.TryParse(apisDir, serviceDir);

        result.IsSome.Should().BeTrue();
    }

    [Fact]
    public void ApisDirectory_TryParse_ReturnsNone_WhenWrongName()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var wrongDir = new DirectoryInfo("/repo/apim/version sets");

        var result = ApisDirectory.TryParse(wrongDir, serviceDir);

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public void ApiDirectory_TryParse_ReturnsSome_WhenUnderApisDirectory()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var apiDir = new DirectoryInfo("/repo/apim/apis/my-api");

        var result = ApiDirectory.TryParse(apiDir, serviceDir);

        result.IsSome.Should().BeTrue();
    }

    [Fact]
    public void ApiDirectory_TryParse_ReturnsCorrectName()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var apiDir = new DirectoryInfo("/repo/apim/apis/my-api");

        var result = ApiDirectory.TryParse(apiDir, serviceDir);

        result.IfSome(dir => dir.Name.ToString().Should().Be("my-api"));
    }

    [Fact]
    public void ApiInformationFile_TryParse_ReturnsSome_WhenCorrectFileUnderApiDir()
    {
        var serviceDir = CreateServiceDirectory("/repo/apim");
        var infoFile = new FileInfo("/repo/apim/apis/my-api/apiInformation.json");

        var result = ApiInformationFile.TryParse(infoFile, serviceDir);

        result.IsSome.Should().BeTrue();
    }
}

/// <summary>
/// Unit tests for the NonEmptyString base record used by all resource names.
/// </summary>
public sealed class ResourceNameTests
{
    [Fact]
    public void VersionSetName_From_CreatesInstanceWithCorrectValue()
    {
        var name = VersionSetName.From("my-version-set");

        name.ToString().Should().Be("my-version-set");
    }

    [Fact]
    public void VersionSetName_Equality_IsCaseInsensitive()
    {
        var name1 = VersionSetName.From("MyVersionSet");
        var name2 = VersionSetName.From("myVersionSet");

        name1.Should().Be(name2);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void VersionSetName_From_ThrowsOnEmptyOrWhitespace(string value)
    {
        var act = () => VersionSetName.From(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApiName_From_CreatesInstanceWithCorrectValue()
    {
        var name = ApiName.From("my-api");

        name.ToString().Should().Be("my-api");
    }

    [Fact]
    public void ApiName_Equality_IsCaseInsensitive()
    {
        var name1 = ApiName.From("MyApi");
        var name2 = ApiName.From("myapi");

        name1.Should().Be(name2);
    }
}
