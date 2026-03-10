using CsCheck;
using LanguageExt;
using Nito.Comparers;
using System.Collections.Frozen;

namespace common.tests;

public sealed record ApiReleaseModel
{
    public required ApiReleaseName Name { get; init; }
    public required ApiName ApiName { get; init; }
    public Option<string> Notes { get; init; }

    public static Gen<ApiReleaseModel> Generate() =>
        from name in GenerateName()
        from apiName in GenerateApiName()
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

    public static Gen<ApiName> GenerateApiName() =>
        from name in Generator.AlphaNumericStringBetween(3, 10)
        select ApiName.From(name);

    public static Gen<string> GenerateNotes() =>
        from lorem in Generator.Lorem
        select lorem.Sentence();

    public static Gen<FrozenSet<ApiReleaseModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => x.Name, 0, 10);
}
