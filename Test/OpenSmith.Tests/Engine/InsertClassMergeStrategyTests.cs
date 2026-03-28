using OpenSmith.Engine;

namespace OpenSmith.Tests.Engine;

public class InsertClassMergeStrategyTests
{
    [Fact]
    public void Merge_WhenSectionNotFound_InsertsInParent()
    {
        var strategy = new InsertClassMergeStrategy(SupportedLanguage.CSharp, "Metadata")
        {
            OnlyInsertMatchingClass = true,
            NotFoundAction = InsertClassMergeStrategy.NotFoundActionEnum.InsertInParent,
            NotFoundParent = "MyEntity",
            MergeImports = true,
        };

        var existing = """
            namespace Test
            {
                public partial class MyEntity
                {
                    // User code here
                }
            }
            """;

        var newContent = """
            namespace Test
            {
                public partial class MyEntity
                {
                    #region Metadata
                    internal class Metadata
                    {
                        public string Name { get; set; }
                    }
                    #endregion
                }
            }
            """;

        var result = strategy.Merge(existing, newContent);

        // Should contain user code and the new Metadata section
        Assert.Contains("User code here", result);
        Assert.Contains("Metadata", result);
        Assert.Contains("public string Name { get; set; }", result);
    }

    [Fact]
    public void Merge_WhenSectionExists_ReplacesIt()
    {
        var strategy = new InsertClassMergeStrategy(SupportedLanguage.CSharp, "Metadata")
        {
            OnlyInsertMatchingClass = true,
            NotFoundAction = InsertClassMergeStrategy.NotFoundActionEnum.InsertInParent,
            NotFoundParent = "MyEntity",
        };

        var existing = """
            namespace Test
            {
                public partial class MyEntity
                {
                    // User code
                    #region Metadata
                    internal class Metadata
                    {
                        [Required]
                        public string OldProp { get; set; }
                    }
                    #endregion
                }
            }
            """;

        var newContent = """
            namespace Test
            {
                public partial class MyEntity
                {
                    #region Metadata
                    internal class Metadata
                    {
                        public string NewProp { get; set; }
                    }
                    #endregion
                }
            }
            """;

        var result = strategy.Merge(existing, newContent);

        Assert.Contains("User code", result);
        Assert.Contains("NewProp", result);
        Assert.DoesNotContain("OldProp", result);
    }

    [Fact]
    public void Merge_PreservesUserCodeOutsideSection()
    {
        var strategy = new InsertClassMergeStrategy(SupportedLanguage.CSharp, "Metadata")
        {
            OnlyInsertMatchingClass = true,
            NotFoundAction = InsertClassMergeStrategy.NotFoundActionEnum.InsertInParent,
            NotFoundParent = "MyEntity",
        };

        var existing = """
            using System;
            using CustomLib;

            namespace Test
            {
                public partial class MyEntity
                {
                    public void MyCustomMethod()
                    {
                        // User's custom code
                    }

                    #region Metadata
                    internal class Metadata
                    {
                        public int Id { get; set; }
                    }
                    #endregion
                }
            }
            """;

        var newContent = """
            using System;

            namespace Test
            {
                public partial class MyEntity
                {
                    #region Metadata
                    internal class Metadata
                    {
                        public int Id { get; set; }
                        public string Name { get; set; }
                    }
                    #endregion
                }
            }
            """;

        var result = strategy.Merge(existing, newContent);

        Assert.Contains("MyCustomMethod", result);
        Assert.Contains("User's custom code", result);
        Assert.Contains("public string Name { get; set; }", result);
    }

    [Fact]
    public void Merge_MergeImports_AddsNewUsings()
    {
        var strategy = new InsertClassMergeStrategy(SupportedLanguage.CSharp, "Metadata")
        {
            MergeImports = true,
            NotFoundAction = InsertClassMergeStrategy.NotFoundActionEnum.InsertInParent,
            NotFoundParent = "MyEntity",
        };

        var existing = """
            using System;

            namespace Test
            {
                public partial class MyEntity
                {
                }
            }
            """;

        var newContent = """
            using System;
            using System.Linq;

            namespace Test
            {
                public partial class MyEntity
                {
                    #region Metadata
                    internal class Metadata
                    {
                    }
                    #endregion
                }
            }
            """;

        var result = strategy.Merge(existing, newContent);

        Assert.Contains("using System.Linq;", result);
    }
}
