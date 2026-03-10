using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for an API that lives inside an APIM workspace.
/// Mirrors the essential fields of <see cref="WorkspaceApiDto"/> and adds the
/// parent <see cref="WorkspaceName"/> so that property-based tests can
/// round-trip workspace-scoped APIs end-to-end.
/// </summary>
public sealed record WorkspaceApiModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required ApiName Name { get; init; }
    public required string Path { get; init; }
    public required string DisplayName { get; init; }
    public Option<string> Description { get; init; }

    public static Gen<WorkspaceApiModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from path in GeneratePath()
        from displayName in GenerateDisplayName()
        from description in GenerateDescription().OptionOf()
        select new WorkspaceApiModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            Path = path,
            DisplayName = displayName,
            Description = description
        };

    public static Gen<ApiName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(3, 10)
        select ApiName.From(name);

    public static Gen<string> GeneratePath() =>
        Generator.AlphaNumericStringWithLength(10);

    public static Gen<string> GenerateDisplayName() =>
        Generator.AlphaNumericStringBetween(10, 20);

    public static Gen<string> GenerateDescription() =>
        from lorem in Generator.Lorem
        select lorem.Paragraph();

    /// <summary>
    /// Generates a set of workspace APIs that are unique by <see cref="WorkspaceName"/>
    /// and <see cref="Name"/>, and distinct by <see cref="DisplayName"/> within
    /// the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceApiModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10)
                  .DistinctBy(x => (x.WorkspaceName, x.DisplayName));
}
