using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

public sealed record WorkspaceGroupModel
{
    public required WorkspaceName WorkspaceName { get; init; }
    public required GroupName Name { get; init; }
    public required string DisplayName { get; init; }
    public Option<string> Description { get; init; }

    public static Gen<WorkspaceGroupModel> Generate() =>
        from workspaceName in WorkspaceModel.GenerateName()
        from name in GenerateName()
        from displayName in GenerateDisplayName()
        from description in GenerateDescription().OptionOf()
        select new WorkspaceGroupModel
        {
            WorkspaceName = workspaceName,
            Name = name,
            DisplayName = displayName,
            Description = description
        };

    public static Gen<GroupName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select GroupName.From(name);

    public static Gen<string> GenerateDisplayName() =>
        Generator.AlphaNumericStringBetween(10, 20);

    public static Gen<string> GenerateDescription() =>
        from lorem in Generator.Lorem
        select lorem.Paragraph();

    public static Gen<FrozenSet<WorkspaceGroupModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.WorkspaceName, x.Name), 0, 10)
                  .DistinctBy(x => (x.WorkspaceName, x.DisplayName));
}
