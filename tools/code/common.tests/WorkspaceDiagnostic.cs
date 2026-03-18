using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

public sealed record WorkspaceDiagnosticModel
{
    public required DiagnosticName Name { get; init; }
    public required WorkspaceName WorkspaceName { get; init; }
    public required LoggerName LoggerName { get; init; }
    public Option<string> AlwaysLog { get; init; }
    public Option<DiagnosticSampling> Sampling { get; init; }

    public static Gen<WorkspaceDiagnosticModel> Generate() =>
        from name in DiagnosticModel.GenerateName()
        from workspaceName in WorkspaceModel.GenerateName()
        from loggerType in LoggerType.Generate()
        from loggerName in LoggerModel.GenerateName(loggerType)
        from alwaysLog in Gen.Const("allErrors").OptionOf()
        from sampling in DiagnosticSampling.Generate().OptionOf()
        select new WorkspaceDiagnosticModel
        {
            Name = name,
            WorkspaceName = workspaceName,
            LoggerName = loggerName,
            AlwaysLog = alwaysLog,
            Sampling = sampling
        };

    public static Gen<FrozenSet<WorkspaceDiagnosticModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 20);
}
