using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a policy attached to an APIM workspace.
/// Mirrors <see cref="ServicePolicyModel"/> but scoped to a workspace.
/// Each workspace has at most one policy document, always named "policy".
/// </summary>
public sealed record WorkspacePolicyModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public WorkspacePolicyName Name { get; } = WorkspacePolicyName.From("policy");
    public required string Content { get; init; }

    public static Gen<WorkspacePolicyModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from content in GenerateContent()
        select new WorkspacePolicyModel
        {
            WorkspaceName = workspaceName,
            Content = content
        };

    public static Gen<string> GenerateContent() =>
        Gen.OneOfConst(
            """
            <policies>
                <inbound>
                    <mock-response status-code="200" content-type="application/json" />
                </inbound>
                <backend />
                <outbound />
                <on-error />
            </policies>
            """,
            """
            <policies>
                <inbound />
                <backend>
                    <forward-request />
                </backend>
                <outbound />
                <on-error />
            </policies>
            """);

    /// <summary>
    /// Generates a set of workspace policies — at most one per workspace (keyed by
    /// <see cref="WorkspaceName"/>).
    /// </summary>
    public static Gen<FrozenSet<WorkspacePolicyModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => x.WorkspaceName, 0, 10);
}
