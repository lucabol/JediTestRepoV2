using common;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace common.unit.tests;

public sealed class EnumerableExtensionsTests
{
    // Iter (synchronous)

    [Fact]
    public void Iter_ExecutesActionForEachElement()
    {
        var results = new List<int>();
        new[] { 1, 2, 3 }.Iter(results.Add);
        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public void Iter_OnEmptyEnumerable_ExecutesNoActions()
    {
        var executed = false;
        Array.Empty<int>().Iter(_ => executed = true);
        Assert.False(executed);
    }

    // Choose

    [Fact]
    public void Choose_FiltersMappedNoneValues()
    {
        var result = new[] { 1, 2, 3, 4, 5 }
            .Choose(n => n % 2 == 0 ? Option<int>.Some(n * 10) : Option<int>.None)
            .ToList();
        Assert.Equal(new[] { 20, 40 }, result);
    }

    [Fact]
    public void Choose_WhenAllNone_ReturnsEmpty()
    {
        var result = new[] { 1, 3, 5 }
            .Choose(_ => Option<string>.None)
            .ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void Choose_WhenAllSome_ReturnsAllMapped()
    {
        var result = new[] { "a", "bb", "ccc" }
            .Choose(s => Option<int>.Some(s.Length))
            .ToList();
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    // HeadOrNone

    [Fact]
    public void HeadOrNone_NonEmptyEnumerable_ReturnsSomeOfFirstElement()
    {
        var result = new[] { 10, 20, 30 }.HeadOrNone();
        Assert.True(result.IsSome);
        Assert.Equal(10, result.ValueUnsafe());
    }

    [Fact]
    public void HeadOrNone_EmptyEnumerable_ReturnsNone()
    {
        var result = Array.Empty<int>().HeadOrNone();
        Assert.True(result.IsNone);
    }

    // LastOrNone

    [Fact]
    public void LastOrNone_NonEmptyEnumerable_ReturnsSomeOfLastElement()
    {
        var result = new[] { 10, 20, 30 }.LastOrNone();
        Assert.True(result.IsSome);
        Assert.Equal(30, result.ValueUnsafe());
    }

    [Fact]
    public void LastOrNone_EmptyEnumerable_ReturnsNone()
    {
        var result = Array.Empty<int>().LastOrNone();
        Assert.True(result.IsNone);
    }

    // ToFrozenDictionary (tuple overload)

    [Fact]
    public void ToFrozenDictionary_BuildsDictionaryFromTuples()
    {
        var dict = new[] { ("a", 1), ("b", 2), ("c", 3) }
            .ToFrozenDictionary();
        Assert.Equal(1, dict["a"]);
        Assert.Equal(2, dict["b"]);
        Assert.Equal(3, dict["c"]);
    }
}
