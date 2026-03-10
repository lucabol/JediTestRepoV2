using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a diagnostic that lives inside an APIM workspace.
/// Mirrors <see cref="DiagnosticModel"/> but adds the parent <see cref="WorkspaceName"/>
/// so that integration and property-based tests can round-trip workspace-scoped diagnostics.
/// </summary>
public sealed record WorkspaceDiagnosticModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required DiagnosticName Name { get; init; }
    public required LoggerName LoggerName { get; init; }
    public Option<string> AlwaysLog { get; init; }
    public Option<DiagnosticSampling> Sampling { get; init; }

    public static Gen<WorkspaceDiagnosticModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from loggerType in LoggerType.Generate()
        from loggerName in LoggerModel.GenerateName(loggerType)
        from alwaysLog in Gen.Const("allErrors").OptionOf()
        from sampling in DiagnosticSampling.Generate().OptionOf()
        select new WorkspaceDiagnosticModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            LoggerName = loggerName,
            AlwaysLog = alwaysLog,
            Sampling = sampling
        };

    public static Gen<DiagnosticName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select DiagnosticName.From(name);

    /// <summary>
    /// Generates a set of workspace diagnostics that are unique by <see cref="Name"/>
    /// within the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceDiagnosticModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10);
}
