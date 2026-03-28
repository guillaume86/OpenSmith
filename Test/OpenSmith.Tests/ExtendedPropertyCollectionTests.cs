using SchemaExplorer;

namespace OpenSmith.Tests;

public class ExtendedPropertyCollectionTests
{
    [Fact]
    public void Contains_ReturnsTrue_WhenPropertyExists()
    {
        var collection = new ExtendedPropertyCollection();
        collection.Add(new ExtendedProperty { Name = "CS_IsIdentity", Value = true });

        Assert.True(collection.Contains("CS_IsIdentity"));
    }

    [Fact]
    public void Contains_ReturnsFalse_WhenPropertyMissing()
    {
        var collection = new ExtendedPropertyCollection();

        Assert.False(collection.Contains("CS_IsIdentity"));
    }

    [Fact]
    public void Indexer_ReturnsProperty_ByName()
    {
        var collection = new ExtendedPropertyCollection();
        collection.Add(new ExtendedProperty { Name = "CS_IsComputed", Value = "true" });

        Assert.Equal("true", collection["CS_IsComputed"].Value);
    }
}
