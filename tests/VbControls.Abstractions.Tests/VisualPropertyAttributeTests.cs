using System.Reflection;
using VbControls.Abstractions;
using Xunit;

namespace VbControls.Abstractions.Tests;

public class VisualPropertyAttributeTests
{
    private sealed class FakeVisualComponent
    {
        [VisualProperty("Layout")]
        public double Width { get; set; }

        [VisualProperty("Aspetto")]
        public string? Text { get; set; }

        public string? Untagged { get; set; }
    }

    [Fact]
    public void Attribute_is_readable_via_reflection_with_correct_category()
    {
        var property = typeof(FakeVisualComponent).GetProperty(nameof(FakeVisualComponent.Width));

        var attribute = property!.GetCustomAttribute<VisualPropertyAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("Layout", attribute!.Category);
    }

    [Fact]
    public void Different_properties_can_have_different_categories()
    {
        var property = typeof(FakeVisualComponent).GetProperty(nameof(FakeVisualComponent.Text));

        var attribute = property!.GetCustomAttribute<VisualPropertyAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("Aspetto", attribute!.Category);
    }

    [Fact]
    public void Property_without_attribute_returns_null()
    {
        var property = typeof(FakeVisualComponent).GetProperty(nameof(FakeVisualComponent.Untagged));

        var attribute = property!.GetCustomAttribute<VisualPropertyAttribute>();

        Assert.Null(attribute);
    }

    [Fact]
    public void PropertyGrid_scan_finds_only_the_tagged_properties()
    {
        var taggedProperties = typeof(FakeVisualComponent)
            .GetProperties()
            .Where(p => p.GetCustomAttribute<VisualPropertyAttribute>() is not null)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { nameof(FakeVisualComponent.Text), nameof(FakeVisualComponent.Width) }, taggedProperties);
    }
}
