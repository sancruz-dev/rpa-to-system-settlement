using SettlementProcessing.Functions;
using Xunit;

namespace SettlementProcessing.Tests;

public class CsvParsingTests
{
    [Fact]
    public void ParseCsv_SkipsHeaderAndParsesEachRow()
    {
        const string csv = "Document,Ticker,Amount\r\n" +
                            "00000000014,IVVB11,339922.00\r\n" +
                            "99999999999,PETR4,1500.00\r\n";

        var rows = ProcessSettlementsQueue.ParseCsv(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new SettlementRow("00000000014", "IVVB11", 339922.00m), rows[0]);
        Assert.Equal(new SettlementRow("99999999999", "PETR4", 1500.00m), rows[1]);
    }

    [Fact]
    public void ParseCsv_WithOnlyAHeader_ReturnsNoRows()
    {
        const string csv = "Document,Ticker,Amount\r\n";

        var rows = ProcessSettlementsQueue.ParseCsv(csv);

        Assert.Empty(rows);
    }

    [Fact]
    public void ParseCsv_IgnoresBlankTrailingLines()
    {
        const string csv = "Document,Ticker,Amount\r\n" +
                            "00000000014,IVVB11,339922.00\r\n" +
                            "\r\n";

        var rows = ProcessSettlementsQueue.ParseCsv(csv);

        Assert.Single(rows);
    }
}
