using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a policy fragment that lives inside an APIM workspace.
/// Mirrors <see cref="PolicyFragmentModel"/> but adds the parent <see cref="WorkspaceName"/>
/// so that integration and property-based tests can round-trip workspace-scoped policy fragments.
/// </summary>
public sealed record WorkspacePolicyFragmentModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required PolicyFragmentName Name { get; init; }
    public Option<string> Description { get; init; }
    public required string Content { get; init; }

    public static Gen<WorkspacePolicyFragmentModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from description in GenerateDescription().OptionOf()
        from content in GenerateContent()
        select new WorkspacePolicyFragmentModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            Description = description,
            Content = content
        };

    public static Gen<PolicyFragmentName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select PolicyFragmentName.From(name);

    public static Gen<string> GenerateDescription() =>
        from lorem in Generator.Lorem
        select lorem.Paragraph();

    public static Gen<string> GenerateContent() =>
        Gen.Const("""
            <fragment>
                <mock-response status-code="200" content-type="application/json" />
            </fragment>
            """);

    /// <summary>
    /// Generates a set of workspace policy fragments that are unique by <see cref="Name"/>
    /// within the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspacePolicyFragmentModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10);
}
