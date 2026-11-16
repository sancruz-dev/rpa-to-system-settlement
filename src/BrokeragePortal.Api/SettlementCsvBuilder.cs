using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SettlementProcessing.Tests")]

namespace BrokeragePortal.Api;

public static class SettlementCsvBuilder
{
    internal sealed record Row(string Document, string Ticker, decimal Amount);

    public static async Task<string> GenerateAsync(string connectionString, DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        await using var conn = new SqlConnection(connectionString);
        var expected = (await conn.QueryAsync<Row>(
            """
            SELECT c.Documento AS Document, a.Ticker AS Ticker, SUM(o.Quantidade * o.Preco) AS Amount
            FROM dbo.Operacoes AS o
            JOIN dbo.Clientes  AS c ON c.ClienteId = o.ClienteId
            JOIN dbo.Ativos    AS a ON a.AtivoId = o.AtivoId
            WHERE o.DataOperacao >= @Start AND o.DataOperacao < @End
            GROUP BY c.Documento, a.Ticker
            ORDER BY c.Documento, a.Ticker;
            """,
            new { Start = start, End = end })).ToList();

        var rows = InjectDiscrepancies(expected);

        var sb = new StringBuilder();
        sb.Append("Document,Ticker,Amount\r\n");
        foreach (var r in rows)
        {
            sb.Append(r.Document).Append(',')
              .Append(r.Ticker).Append(',')
              .Append(r.Amount.ToString("F2", CultureInfo.InvariantCulture))
              .Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Injects a fixed, deterministic pattern of 6 discrepancies: 3 rows with an altered
    /// amount, 2 missing rows and 1 extra row. Same input day always produces the same
    /// CSV, since it only depends on the query's stable ordering.
    /// </summary>
    internal static List<Row> InjectDiscrepancies(List<Row> expected)
    {
        if (expected.Count < 10)
            return expected;

        var rows = new List<Row>(expected);

        for (var i = 0; i < 3; i++)
            rows[i] = rows[i] with { Amount = rows[i].Amount + 10m };

        rows.RemoveRange(rows.Count - 2, 2);

        rows.Add(new Row("99999999999", expected[0].Ticker, 1500.00m));

        return rows;
    }
}
