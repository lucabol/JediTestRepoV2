using CsCheck;
using System.Collections.Frozen;

namespace common.tests;

public sealed record ApiOperationModel
{
    public required ApiOperationName Name { get; init; }
    public required FrozenSet<ApiOperationPolicyModel> Policies { get; init; }

    public static Gen<ApiOperationModel> Generate() =>
        from name in GenerateName()
        from policies in ApiOperationPolicyModel.GenerateSet()
        select new ApiOperationModel
        {
            Name = name,
            Policies = policies
        };

    public static Gen<ApiOperationName> GenerateName() =>
        from name in Generator.AlphaNumericStringBetween(5, 20)
        select ApiOperationName.From(name);

    public static Gen<FrozenSet<ApiOperationModel>> GenerateSet() =>
        Generate().FrozenSetOf(x => x.Name, 0, 10);
}
