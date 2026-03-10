using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a version set that lives inside an APIM workspace.
/// Mirrors <see cref="VersionSetModel"/> but adds the parent <see cref="WorkspaceName"/>
/// so that integration and property-based tests can round-trip workspace-scoped version sets.
/// </summary>
public sealed record WorkspaceVersionSetModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required VersionSetName Name { get; init; }
    public required string DisplayName { get; init; }
    public required VersioningScheme Scheme { get; init; }
    public Option<string> Description { get; init; }

    public static Gen<WorkspaceVersionSetModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from displayName in GenerateDisplayName()
        from scheme in VersioningScheme.Generate()
        from description in GenerateDescription().OptionOf()
        select new WorkspaceVersionSetModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            DisplayName = displayName,
            Scheme = scheme,
            Description = description
        };

    public static Gen<VersionSetName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select VersionSetName.From(name);

    public static Gen<string> GenerateDisplayName() =>
        Generator.AlphaNumericStringBetween(10, 20);

    public static Gen<string> GenerateDescription() =>
        from lorem in Generator.Lorem
        select lorem.Paragraph();

    /// <summary>
    /// Generates a set of workspace version sets that are unique by <see cref="Name"/> and
    /// <see cref="DisplayName"/> within the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceVersionSetModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10)
                  .DistinctBy(x => (x.WorkspaceName, x.DisplayName));
}
