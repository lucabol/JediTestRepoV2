using common;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace common.unit.tests;

public sealed class DictionaryExtensionsTests
{
    [Fact]
    public void Find_ExistingKey_ReturnsSome()
    {
        var dict = new Dictionary<string, int> { ["alpha"] = 1, ["beta"] = 2 };
        var result = dict.Find("alpha");
        Assert.True(result.IsSome);
        Assert.Equal(1, result.ValueUnsafe());
    }

    [Fact]
    public void Find_MissingKey_ReturnsNone()
    {
        var dict = new Dictionary<string, int> { ["alpha"] = 1 };
        var result = dict.Find("missing");
        Assert.True(result.IsNone);
    }

    [Fact]
    public void Find_EmptyDictionary_ReturnsNone()
    {
        var dict = new Dictionary<string, int>();
        var result = dict.Find("key");
        Assert.True(result.IsNone);
    }
}
