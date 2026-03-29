using System.Xml.Linq;
using OpenSmith.Compilation;
using OpenSmith.Engine;

namespace OpenSmith.Cli.Tests;

public class PropertyDeserializerTests
{
    private class TestTemplate : CodeTemplateBase
    {
        public string Name { get; set; } = "";
        public bool Flag { get; set; }
        public List<string> Items { get; set; } = new();
        public int Count { get; set; }
    }

    [Fact]
    public void SetsStringProperty()
    {
        var template = new TestTemplate();
        var props = new Dictionary<string, CspProperty>
        {
            ["Name"] = new CspProperty { Value = "Hello" },
        };

        PropertyDeserializer.SetProperties(template, props, new());

        Assert.Equal("Hello", template.Name);
    }

    [Fact]
    public void SetsBooleanProperty()
    {
        var template = new TestTemplate();
        var props = new Dictionary<string, CspProperty>
        {
            ["Flag"] = new CspProperty { Value = "True" },
        };

        PropertyDeserializer.SetProperties(template, props, new());

        Assert.True(template.Flag);
    }

    [Fact]
    public void SetsBooleanPropertyFalse()
    {
        var template = new TestTemplate();
        template.Flag = true;
        var props = new Dictionary<string, CspProperty>
        {
            ["Flag"] = new CspProperty { Value = "False" },
        };

        PropertyDeserializer.SetProperties(template, props, new());

        Assert.False(template.Flag);
    }

    [Fact]
    public void SetsStringListProperty()
    {
        var template = new TestTemplate();
        var props = new Dictionary<string, CspProperty>
        {
            ["Items"] = new CspProperty { StringList = new List<string> { "a", "b", "c" } },
        };

        PropertyDeserializer.SetProperties(template, props, new());

        Assert.Equal(3, template.Items.Count);
        Assert.Equal("a", template.Items[0]);
    }

    [Fact]
    public void SkipsUnknownProperties()
    {
        var template = new TestTemplate();
        var props = new Dictionary<string, CspProperty>
        {
            ["NonExistent"] = new CspProperty { Value = "x" },
        };

        // Should not throw
        PropertyDeserializer.SetProperties(template, props, new());
    }

    [Fact]
    public void SetsIntProperty()
    {
        var template = new TestTemplate();
        var props = new Dictionary<string, CspProperty>
        {
            ["Count"] = new CspProperty { Value = "42" },
        };

        PropertyDeserializer.SetProperties(template, props, new());

        Assert.Equal(42, template.Count);
    }
}
