using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a tag that lives inside an APIM workspace.
/// Mirrors <see cref="TagModel"/> but adds the parent <see cref="WorkspaceName"/>
/// so that integration and property-based tests can round-trip workspace-scoped tags.
/// </summary>
public sealed record WorkspaceTagModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required TagName Name { get; init; }
    public required string DisplayName { get; init; }

    public static Gen<WorkspaceTagModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from displayName in GenerateDisplayName()
        select new WorkspaceTagModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            DisplayName = displayName
        };

    public static Gen<TagName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select TagName.From(name);

    public static Gen<string> GenerateDisplayName() =>
        Generator.AlphaNumericStringBetween(10, 20);

    /// <summary>
    /// Generates a set of workspace tags that are unique by <see cref="Name"/> and
    /// <see cref="DisplayName"/> within the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceTagModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10)
                  .DistinctBy(x => (x.WorkspaceName, x.DisplayName));
}
