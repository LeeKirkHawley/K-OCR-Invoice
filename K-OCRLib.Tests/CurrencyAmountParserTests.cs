using K_OCRLib.Services;

namespace K_OCRLib.Tests;

public class CurrencyAmountParserTests
{
    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("USD 1.234,56", 1234.56)]
    [InlineData("€1 234.00", 1234.00)]
    [InlineData("USD 1,50", 1.50)]
    [InlineData("USD 1,234", 1234.00)]
    [InlineData("-$1,234.56", -1234.56)]
    [InlineData("($1,234.56)", -1234.56)]
    [InlineData("USD -1,234.56", -1234.56)]
    public void Parse_RecognizesCommonCurrencyFormats(string raw, decimal expected)
    {
        Assert.Equal(expected, CurrencyAmountParser.Parse(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a value")]
    [InlineData("1234")]
    public void Parse_ReturnsNullForUnrecognizedInput(string? raw)
    {
        Assert.Null(CurrencyAmountParser.Parse(raw));
    }
}
