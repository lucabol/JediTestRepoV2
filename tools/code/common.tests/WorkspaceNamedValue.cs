using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a named value that lives inside an APIM workspace.
/// Mirrors <see cref="NamedValueModel"/> but adds the parent <see cref="WorkspaceName"/>
/// so that integration and property-based tests can round-trip workspace-scoped named values.
/// </summary>
public sealed record WorkspaceNamedValueModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required NamedValueName Name { get; init; }
    public required NamedValueType Type { get; init; }
    public required ImmutableArray<string> Tags { get; init; }

    public static Gen<WorkspaceNamedValueModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from type in NamedValueType.Generate()
        from tags in GenerateTags()
        select new WorkspaceNamedValueModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            Type = type,
            Tags = tags
        };

    public static Gen<NamedValueName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select NamedValueName.From(name);

    public static Gen<ImmutableArray<string>> GenerateTags() =>
        Generator.AlphaNumericStringBetween(10, 20)
                 .ImmutableArrayOf(0, 32);

    /// <summary>
    /// Generates a set of workspace named values that are unique by <see cref="Name"/>
    /// within the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceNamedValueModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 20);
}
