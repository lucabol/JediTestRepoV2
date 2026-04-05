using common;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace common.unit.tests;

public sealed class OptionExtensionsTests
{
    // IterTask (no cancellation token)

    [Fact]
    public async Task IterTask_WhenSome_ExecutesAction()
    {
        var executed = false;
        await Option<int>.Some(42).IterTask(_ => { executed = true; return ValueTask.CompletedTask; });
        Assert.True(executed);
    }

    [Fact]
    public async Task IterTask_WhenSome_PassesValue()
    {
        var captured = 0;
        await Option<int>.Some(7).IterTask(v => { captured = v; return ValueTask.CompletedTask; });
        Assert.Equal(7, captured);
    }

    [Fact]
    public async Task IterTask_WhenNone_DoesNotExecuteAction()
    {
        var executed = false;
        await Option<int>.None.IterTask(_ => { executed = true; return ValueTask.CompletedTask; });
        Assert.False(executed);
    }

    // MapTask

    [Fact]
    public async Task MapTask_WhenSome_MapsValue()
    {
        var result = await Option<int>.Some(5).MapTask(v => ValueTask.FromResult(v * 2));
        Assert.True(result.IsSome);
        Assert.Equal(10, result.ValueUnsafe());
    }

    [Fact]
    public async Task MapTask_WhenNone_ReturnsNone()
    {
        var result = await Option<int>.None.MapTask(v => ValueTask.FromResult(v * 2));
        Assert.True(result.IsNone);
    }

    // BindTask (no cancellation token)

    [Fact]
    public async Task BindTask_WhenSome_AndBindReturnsSome_ReturnsSome()
    {
        var result = await Option<int>.Some(3).BindTask(v => ValueTask.FromResult(Option<string>.Some(v.ToString())));
        Assert.True(result.IsSome);
        Assert.Equal("3", result.ValueUnsafe());
    }

    [Fact]
    public async Task BindTask_WhenSome_AndBindReturnsNone_ReturnsNone()
    {
        var result = await Option<int>.Some(3).BindTask(_ => ValueTask.FromResult(Option<string>.None));
        Assert.True(result.IsNone);
    }

    [Fact]
    public async Task BindTask_WhenNone_ReturnsNone()
    {
        var result = await Option<int>.None.BindTask(v => ValueTask.FromResult(Option<string>.Some(v.ToString())));
        Assert.True(result.IsNone);
    }

    // Or

    [Fact]
    public async Task Or_WhenSome_ReturnsOriginalValue()
    {
        var result = await Option<int>.Some(99).Or(() => ValueTask.FromResult(Option<int>.Some(0)));
        Assert.True(result.IsSome);
        Assert.Equal(99, result.ValueUnsafe());
    }

    [Fact]
    public async Task Or_WhenNone_ReturnsAlternative()
    {
        var result = await Option<int>.None.Or(() => ValueTask.FromResult(Option<int>.Some(42)));
        Assert.True(result.IsSome);
        Assert.Equal(42, result.ValueUnsafe());
    }

    [Fact]
    public async Task Or_WhenNone_AndAlternativeIsNone_ReturnsNone()
    {
        var result = await Option<int>.None.Or(() => ValueTask.FromResult(Option<int>.None));
        Assert.True(result.IsNone);
    }

    // Or (ValueTask<Option<T>> overload)

    [Fact]
    public async Task OrValueTask_WhenSome_ReturnsOriginalValue()
    {
        var result = await ValueTask.FromResult(Option<int>.Some(5))
                                    .Or(() => ValueTask.FromResult(Option<int>.Some(0)));
        Assert.True(result.IsSome);
        Assert.Equal(5, result.ValueUnsafe());
    }

    [Fact]
    public async Task OrValueTask_WhenNone_ReturnsAlternative()
    {
        var result = await ValueTask.FromResult(Option<int>.None)
                                    .Or(() => ValueTask.FromResult(Option<int>.Some(7)));
        Assert.True(result.IsSome);
        Assert.Equal(7, result.ValueUnsafe());
    }
}
