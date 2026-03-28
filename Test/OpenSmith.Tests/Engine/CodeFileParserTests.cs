using OpenSmith.Engine;

namespace OpenSmith.Tests.Engine;

public class CodeFileParserTests
{
    private static string CreateTempFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ParsesExistingFile()
    {
        var content = """
            namespace Test
            {
                public partial class MyEntity
                {
                    #region Metadata
                    internal class Metadata
                    {
                        [Required]
                        public string Name { get; set; }

                        public int Id { get; set; }
                    }
                    #endregion
                }
            }
            """;
        var path = CreateTempFile(content);
        try
        {
            var parser = new CodeFileParser(path);
            Assert.NotNull(parser.CompilationUnit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AttributeSectionVisitor_FindsAttributesOnProperties()
    {
        var content = """
            namespace Test
            {
                public partial class MyEntity
                {
                    internal class Metadata
                    {
                        [Required]
                        [StringLength(100)]
                        public string Name { get; set; }

                        public int Id { get; set; }

                        [DataType(DataType.EmailAddress)]
                        public string Email { get; set; }
                    }
                }
            }
            """;
        var path = CreateTempFile(content);
        try
        {
            var parser = new CodeFileParser(path);
            var visitor = new AttributeSectionVisitor();
            parser.CompilationUnit.AcceptVisitor(visitor, "Metadata");

            Assert.True(visitor.PropertyMap.ContainsKey("Name"));
            Assert.Equal(2, visitor.PropertyMap["Name"].Attributes.Count);
            Assert.Contains("[Required]", visitor.PropertyMap["Name"].Attributes[0].Text);
            Assert.Contains("[StringLength(100)]", visitor.PropertyMap["Name"].Attributes[1].Text);

            Assert.False(visitor.PropertyMap.ContainsKey("Id")); // No attributes

            Assert.True(visitor.PropertyMap.ContainsKey("Email"));
            Assert.Single(visitor.PropertyMap["Email"].Attributes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetSection_ExtractsTextBetweenLocations()
    {
        var content = """
            line one
            line two
            line three
            """;
        var path = CreateTempFile(content);
        try
        {
            var parser = new CodeFileParser(path);
            var section = parser.GetSection(
                new SourceLocation(1, 1),
                new SourceLocation(1, 9));
            Assert.Equal("line one", section);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
