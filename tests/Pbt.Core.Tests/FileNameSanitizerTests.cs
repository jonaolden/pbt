using Pbt.Core.Infrastructure;

namespace Pbt.Core.Tests;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("SalesModel", "SalesModel")]
    [InlineData("Sales Model", "Sales_Model")]
    [InlineData("My/Model", "My_Model")]
    public void Sanitize_ShouldReplaceInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData(null, "Model")]
    [InlineData("", "Model")]
    [InlineData("   ", "Model")]
    public void Sanitize_NullOrEmpty_ShouldReturnDefault(string? input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input!));
    }

    [Fact]
    public void SanitizeToLower_ShouldReturnLowercase()
    {
        Assert.Equal("sales_model", FileNameSanitizer.SanitizeToLower("Sales Model"));
    }

    [Fact]
    public void Sanitize_AlreadyValid_ShouldReturnUnchanged()
    {
        Assert.Equal("valid_name-123", FileNameSanitizer.Sanitize("valid_name-123"));
    }
}
