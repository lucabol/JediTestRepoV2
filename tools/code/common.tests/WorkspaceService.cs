using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// A composite test model combining a workspace with its sub-resource sets.
/// Enables property-based integration tests for workspace-scoped resource operations
/// (extract, publish, validate) analogous to <see cref="ServiceModel"/> for the
/// top-level service.
/// </summary>
public sealed record WorkspaceServiceModel
{
    public required WorkspaceModel Workspace { get; init; }
    public required FrozenSet<TagModel> Tags { get; init; }
    public required FrozenSet<NamedValueModel> NamedValues { get; init; }
    public required FrozenSet<BackendModel> Backends { get; init; }
    public required FrozenSet<LoggerModel> Loggers { get; init; }
    public required FrozenSet<PolicyFragmentModel> PolicyFragments { get; init; }
    public required FrozenSet<ServicePolicyModel> Policies { get; init; }
    public required FrozenSet<VersionSetModel> VersionSets { get; init; }

    public static Gen<WorkspaceServiceModel> Generate() =>
        from workspace in WorkspaceModel.Generate()
        from tags in TagModel.GenerateSet()
        from namedValues in NamedValueModel.GenerateSet()
        from backends in BackendModel.GenerateSet()
        from loggers in LoggerModel.GenerateSet()
        from policyFragments in PolicyFragmentModel.GenerateSet()
        from policies in ServicePolicyModel.GenerateSet()
        from versionSets in VersionSetModel.GenerateSet()
        select new WorkspaceServiceModel
        {
            Workspace = workspace,
            Tags = tags,
            NamedValues = namedValues,
            Backends = backends,
            Loggers = loggers,
            PolicyFragments = policyFragments,
            Policies = policies,
            VersionSets = versionSets
        };

    public static Gen<FrozenSet<WorkspaceServiceModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => x.Workspace.Name, 0, 5);
}
