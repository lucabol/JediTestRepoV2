using CsCheck;
using LanguageExt;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

public sealed record ApiReleaseModel
{
    public required ApiReleaseName Name { get; init; }
    public required ApiName ApiName { get; init; }
    public Option<string> Notes { get; init; }

    public static Gen<ApiReleaseModel> Generate() =>
        from type in ApiType.Generate()
        from apiName in ApiModel.GenerateName(type)
        from name in GenerateName()
        from notes in GenerateNotes().OptionOf()
        select new ApiReleaseModel
        {
            Name = name,
            ApiName = apiName,
            Notes = notes
        };

    public static Gen<ApiReleaseName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(10, 20)
        select ApiReleaseName.From(name);

    public static Gen<string> GenerateNotes() =>
        from lorem in Generator.Lorem
        select lorem.Sentence();

    public static Gen<FrozenSet<ApiReleaseModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => (x.ApiName, x.Name), 0, 10);
}
