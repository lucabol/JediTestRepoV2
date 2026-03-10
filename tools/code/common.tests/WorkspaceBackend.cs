using CsCheck;
using LanguageExt;
using System;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a backend that lives inside an APIM workspace.
/// Mirrors <see cref="BackendModel"/> but adds the parent <see cref="WorkspaceName"/>
/// so that integration and property-based tests can round-trip workspace-scoped backends.
/// </summary>
public sealed record WorkspaceBackendModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required BackendName Name { get; init; }
    public string Protocol { get; } = "http";
    public required Uri Url { get; init; }
    public Option<string> Description { get; init; }

    public static Gen<WorkspaceBackendModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from url in Generator.AbsoluteUri
        from description in GenerateDescription().OptionOf()
        select new WorkspaceBackendModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            Url = url,
            Description = description
        };

    public static Gen<BackendName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select BackendName.From(name);

    public static Gen<string> GenerateDescription() =>
        from lorem in Generator.Lorem
        select lorem.Paragraph();

    /// <summary>
    /// Generates a set of workspace backends that are unique by <see cref="Name"/>
    /// within the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceBackendModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10);
}
