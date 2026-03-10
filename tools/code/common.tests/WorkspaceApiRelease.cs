using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for an API release that lives inside an APIM workspace.
/// Mirrors the structure captured by <see cref="WorkspaceApiReleaseDto"/> but wraps
/// the parent context (workspace + API names) so that property-based tests can
/// round-trip workspace-scoped API releases end-to-end.
/// </summary>
public sealed record WorkspaceApiReleaseModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required ApiName ApiName { get; init; }
    public required WorkspaceApiReleaseName Name { get; init; }
    public Option<string> Notes { get; init; }

    public static Gen<WorkspaceApiReleaseModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from apiName in GenerateApiName()
        from name in GenerateName()
        from notes in GenerateNotes().OptionOf()
        select new WorkspaceApiReleaseModel
        {
            WorkspaceName = workspaceName,
            ApiName = apiName,
            Name = name,
            Notes = notes
        };

    public static Gen<ApiName> GenerateApiName() =>
        from name in Generator.AlphaNumericStringBetween(3, 10)
        select ApiName.From(name);

    public static Gen<WorkspaceApiReleaseName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select WorkspaceApiReleaseName.From(name);

    public static Gen<string> GenerateNotes() =>
        from lorem in Generator.Lorem
        select lorem.Paragraph();

    /// <summary>
    /// Generates a set of workspace API releases that are unique by
    /// <see cref="WorkspaceName"/>, <see cref="ApiName"/>, and <see cref="Name"/>.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceApiReleaseModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.ApiName, x.Name), 0, 10);
}
