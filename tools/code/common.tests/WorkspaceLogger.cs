using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a logger that lives inside an APIM workspace.
/// Mirrors <see cref="LoggerModel"/> but adds the parent <see cref="WorkspaceName"/>
/// so that integration and property-based tests can round-trip workspace-scoped loggers.
/// </summary>
public sealed record WorkspaceLoggerModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required LoggerName Name { get; init; }
    public required LoggerType Type { get; init; }
    public Option<string> Description { get; init; }
    public bool IsBuffered { get; init; }

    public static Gen<WorkspaceLoggerModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from type in LoggerType.Generate()
        from name in LoggerModel.GenerateName(type)
        from description in LoggerModel.GenerateDescription().OptionOf()
        from isBuffered in Gen.Bool
        select new WorkspaceLoggerModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            Type = type,
            Description = description,
            IsBuffered = isBuffered
        };

    /// <summary>
    /// Generates a set of workspace loggers that are unique by <see cref="Name"/>
    /// within the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceLoggerModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10);
}
