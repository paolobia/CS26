using VbControls.Abstractions;
using Xunit;

namespace VbControls.Abstractions.Tests;

public class ComponentBehaviorTests
{
    private sealed class FakeComponent;

    private sealed class FakeBehavior(FakeComponent component) : ComponentBehavior<FakeComponent>(component);

    [Fact]
    public async Task Default_OnInitAsync_completes_without_override()
    {
        var behavior = new FakeBehavior(new FakeComponent());

        await behavior.OnInitAsync();
    }

    [Fact]
    public async Task Default_OnClickAsync_completes_without_override()
    {
        var behavior = new FakeBehavior(new FakeComponent());

        await behavior.OnClickAsync();
    }
}
