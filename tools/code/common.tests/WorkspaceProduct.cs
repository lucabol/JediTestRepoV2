using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

public sealed record WorkspaceProductModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required ProductName Name { get; init; }
    public required string DisplayName { get; init; }
    public required string State { get; init; }
    public Option<string> Description { get; init; }
    public Option<string> Terms { get; init; }

    public static Gen<WorkspaceProductModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from displayName in GenerateDisplayName()
        from state in GenerateState()
        from description in GenerateDescription().OptionOf()
        from terms in GenerateTerms().OptionOf()
        select new WorkspaceProductModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            DisplayName = displayName,
            State = state,
            Description = description,
            Terms = terms
        };

    public static Gen<ProductName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select ProductName.From(name);

    public static Gen<string> GenerateDisplayName() =>
        Generator.AlphaNumericStringBetween(10, 20);

    public static Gen<string> GenerateState() =>
        Gen.OneOfConst("published", "notPublished");

    public static Gen<string> GenerateDescription() =>
        from lorem in Generator.Lorem
        select lorem.Paragraph();

    public static Gen<string> GenerateTerms() =>
        from lorem in Generator.Lorem
        select lorem.Paragraph();

    public static Gen<FrozenSet<WorkspaceProductModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10)
                  .DistinctBy(x => (x.WorkspaceName, x.DisplayName));
}
