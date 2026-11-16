using BrokeragePortal.Api;
using Xunit;

namespace SettlementProcessing.Tests;

public class SettlementCsvBuilderTests
{
    private static List<SettlementCsvBuilder.Row> MakeRows(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new SettlementCsvBuilder.Row($"doc{i:000}", "PETR4", 100m * (i + 1)))
            .ToList();

    [Fact]
    public void InjectDiscrepancies_WithEnoughRows_AltersFirstThreeAmounts()
    {
        var expected = MakeRows(12);

        var result = SettlementCsvBuilder.InjectDiscrepancies(expected);

        for (var i = 0; i < 3; i++)
            Assert.Equal(expected[i].Amount + 10m, result[i].Amount);
    }

    [Fact]
    public void InjectDiscrepancies_WithEnoughRows_DropsLastTwoOriginalRows()
    {
        var expected = MakeRows(12);

        var result = SettlementCsvBuilder.InjectDiscrepancies(expected);

        var secondToLastOriginal = expected[^2];
        var lastOriginal = expected[^1];
        Assert.DoesNotContain(result, r => r.Document == secondToLastOriginal.Document);
        Assert.DoesNotContain(result, r => r.Document == lastOriginal.Document);
    }

    [Fact]
    public void InjectDiscrepancies_WithEnoughRows_AddsOneFabricatedRow()
    {
        var expected = MakeRows(12);

        var result = SettlementCsvBuilder.InjectDiscrepancies(expected);

        var fabricated = Assert.Single(result, r => r.Document == "99999999999");
        Assert.Equal(1500.00m, fabricated.Amount);
        Assert.Equal(expected[0].Ticker, fabricated.Ticker);
    }

    [Fact]
    public void InjectDiscrepancies_WithEnoughRows_ProducesSixNetDiscrepancies()
    {
        // 3 altered + 2 missing + 1 unexpected = 6 rows that won't match the source anymore.
        var expected = MakeRows(12);

        var result = SettlementCsvBuilder.InjectDiscrepancies(expected);

        Assert.Equal(expected.Count - 2 + 1, result.Count);
    }

    [Fact]
    public void InjectDiscrepancies_WithFewerThanTenRows_ReturnsInputUnchanged()
    {
        var expected = MakeRows(5);

        var result = SettlementCsvBuilder.InjectDiscrepancies(expected);

        Assert.Equal(expected, result);
    }
}
