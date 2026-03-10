using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

/// <summary>
/// CsCheck generator model for a subscription that lives inside an APIM workspace.
/// Mirrors <see cref="SubscriptionModel"/> but adds the parent <see cref="WorkspaceName"/>
/// so that integration and property-based tests can round-trip workspace-scoped subscriptions.
/// </summary>
public sealed record WorkspaceSubscriptionModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required SubscriptionName Name { get; init; }
    public required string DisplayName { get; init; }
    public Option<string> Scope { get; init; }

    public static Gen<WorkspaceSubscriptionModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from displayName in GenerateDisplayName()
        from scope in GenerateScope().OptionOf()
        select new WorkspaceSubscriptionModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            DisplayName = displayName,
            Scope = scope
        };

    public static Gen<SubscriptionName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select SubscriptionName.From(name);

    public static Gen<string> GenerateDisplayName() =>
        Generator.AlphaNumericStringBetween(10, 20);

    public static Gen<string> GenerateScope() =>
        from productName in ProductModel.GenerateName()
        select $"/products/{productName}";

    /// <summary>
    /// Generates a set of workspace subscriptions that are unique by <see cref="Name"/>
    /// within the same workspace.
    /// </summary>
    public static Gen<FrozenSet<WorkspaceSubscriptionModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10)
                  .DistinctBy(x => (x.WorkspaceName, x.DisplayName));
}
