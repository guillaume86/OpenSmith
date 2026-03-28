using OpenSmith.Engine;

namespace OpenSmith.Tests.Engine;

public class StringUtilTests
{
    public class ToPascalCaseTests
    {
        [Theory]
        [InlineData("hello_world", "HelloWorld")]
        [InlineData("hello world", "HelloWorld")]
        [InlineData("hello-world", "HelloWorld")]
        [InlineData("helloWorld", "HelloWorld")]
        [InlineData("HelloWorld", "HelloWorld")]
        [InlineData("hello", "Hello")]
        [InlineData("HELLO", "Hello")]
        [InlineData("ID", "Id")]
        [InlineData("myID", "MyId")]
        [InlineData("SampleDb", "SampleDb")]
        [InlineData("sampledb", "Myapp")]
        [InlineData("", "")]
        [InlineData("a", "A")]
        [InlineData("ABC", "Abc")]
        [InlineData("HTMLParser", "HtmlParser")]
        [InlineData("getHTTPResponse", "GetHttpResponse")]
        [InlineData("fn_HrProcedures", "FnHrProcedures")]
        [InlineData("Vw_HrCarHist", "VwHrCarHist")]
        [InlineData("dbo.TableName", "DboTableName")]
        public void ConvertsToPascalCase(string input, string expected)
        {
            Assert.Equal(expected, StringUtil.ToPascalCase(input));
        }

        [Fact]
        public void NullInput_ReturnsNull()
        {
            Assert.Null(StringUtil.ToPascalCase(null));
        }
    }

    public class ToCamelCaseTests
    {
        [Theory]
        [InlineData("HelloWorld", "helloWorld")]
        [InlineData("hello_world", "helloWorld")]
        [InlineData("Hello", "hello")]
        [InlineData("ID", "id")]
        [InlineData("AskId", "askId")]
        [InlineData("", "")]
        [InlineData("a", "a")]
        [InlineData("A", "a")]
        [InlineData("HTMLParser", "htmlParser")]
        public void ConvertsToCamelCase(string input, string expected)
        {
            Assert.Equal(expected, StringUtil.ToCamelCase(input));
        }

        [Fact]
        public void NullInput_ReturnsNull()
        {
            Assert.Null(StringUtil.ToCamelCase(null));
        }
    }

    public class ToPluralTests
    {
        [Theory]
        [InlineData("Category", "Categories")]
        [InlineData("Entity", "Entities")]
        [InlineData("Bus", "Buses")]
        [InlineData("Box", "Boxes")]
        [InlineData("Church", "Churches")]
        [InlineData("Brush", "Brushes")]
        [InlineData("Table", "Tables")]
        [InlineData("Column", "Columns")]
        [InlineData("Child", "Children")]
        [InlineData("Person", "People")]
        [InlineData("Man", "Men")]
        [InlineData("Woman", "Women")]
        [InlineData("Mouse", "Mice")]
        [InlineData("Index", "Indexes")]
        [InlineData("Status", "Statuses")]
        [InlineData("Address", "Addresses")]
        [InlineData("", "")]
        public void PluralizesCorrectly(string input, string expected)
        {
            Assert.Equal(expected, StringUtil.ToPlural(input));
        }

        [Fact]
        public void NullInput_ReturnsNull()
        {
            Assert.Null(StringUtil.ToPlural(null));
        }
    }

    public class ToSingularTests
    {
        [Theory]
        [InlineData("Categories", "Category")]
        [InlineData("Entities", "Entity")]
        [InlineData("Buses", "Bus")]
        [InlineData("Boxes", "Box")]
        [InlineData("Churches", "Church")]
        [InlineData("Tables", "Table")]
        [InlineData("Columns", "Column")]
        [InlineData("Children", "Child")]
        [InlineData("People", "Person")]
        [InlineData("Men", "Man")]
        [InlineData("Women", "Woman")]
        [InlineData("Mice", "Mouse")]
        [InlineData("Indexes", "Index")]
        [InlineData("Statuses", "Status")]
        [InlineData("Addresses", "Address")]
        [InlineData("", "")]
        public void SingularizesCorrectly(string input, string expected)
        {
            Assert.Equal(expected, StringUtil.ToSingular(input));
        }

        [Fact]
        public void NullInput_ReturnsNull()
        {
            Assert.Null(StringUtil.ToSingular(null));
        }
    }
}
