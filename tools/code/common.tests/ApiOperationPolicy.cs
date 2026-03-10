using CsCheck;
using System.Collections.Frozen;
using System.Linq;

namespace common.tests;

public sealed record ApiOperationPolicyModel
{
    public ApiOperationPolicyName Name { get; } = ApiOperationPolicyName.From("policy");
    public required string Content { get; init; }

    public static Gen<ApiOperationPolicyModel> Generate() =>
        from content in GenerateContent()
        select new ApiOperationPolicyModel
        {
            Content = content
        };

    public static Gen<string> GenerateContent() =>
        Gen.OneOfConst("""
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
                           <inbound>
                               <set-header name="X-Custom-Header" exists-action="override">
                                   <value>operation-level-override</value>
                               </set-header>
                           </inbound>
                           <backend>
                               <forward-request />
                           </backend>
                           <outbound />
                           <on-error />
                       </policies>
                       """,
                       """
                       <policies>
                           <inbound>
                               <rate-limit calls="5" renewal-period="60" />
                           </inbound>
                           <backend>
                               <forward-request />
                           </backend>
                           <outbound />
                           <on-error />
                       </policies>
                       """);

    public static Gen<FrozenSet<ApiOperationPolicyModel>> GenerateSet() =>
        from model in Generate()
        select new[] { model }.ToFrozenSet(x => x.Name);
}
