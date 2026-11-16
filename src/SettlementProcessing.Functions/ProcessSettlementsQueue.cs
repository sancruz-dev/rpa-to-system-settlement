using System.Data;
using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;
using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SettlementProcessing.Functions;

public class ProcessSettlementsQueue(IConfiguration configuration, ILogger<ProcessSettlementsQueue> logger)
{
    [Function(nameof(ProcessSettlementsQueue))]
    public async Task Run([QueueTrigger("%SettlementsQueueName%")] string messageText)
    {
        var message = JsonSerializer.Deserialize<SettlementMessage>(messageText)
            ?? throw new InvalidOperationException("Could not parse the queue message.");

        logger.LogInformation("Processing {BlobName} for {ReferenceDate}", message.BlobName, message.ReferenceDate);

        var storageConnectionString = configuration["AzureWebJobsStorage"]!;
        var containerName = configuration["SettlementsContainerName"] ?? "settlements-raw";
        var blobClient = new BlobContainerClient(storageConnectionString, containerName).GetBlobClient(message.BlobName);

        var download = await blobClient.DownloadContentAsync();
        var rows = ParseCsv(download.Value.Content.ToString());

        var connectionString = configuration["SettlementDb"]
            ?? throw new InvalidOperationException("SettlementDb is not configured.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var (fileId, alreadyExisted) = await RegisterFileAsync(connection, message.BlobName, message.ReferenceDate);
        if (alreadyExisted)
        {
            logger.LogInformation(
                "{BlobName} was already registered as file {FileId} — skipping, nothing to do (idempotent retry).",
                message.BlobName, fileId);
            return;
        }

        foreach (var row in rows)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.LiquidacoesImportadas (ArquivoId, Documento, Ticker, Valor) VALUES (@FileId, @Document, @Ticker, @Amount);",
                new { FileId = fileId, row.Document, row.Ticker, row.Amount });
        }

        var discrepancies = await ReconcileAsync(connection, fileId, message.ReferenceDate);

        await connection.ExecuteAsync(
            "UPDATE dbo.ArquivosLiquidacao SET Status = 'Processado' WHERE ArquivoId = @FileId;",
            new { FileId = fileId });

        logger.LogInformation(
            "Imported {RowCount} rows and found {DiscrepancyCount} discrepancies for {BlobName}",
            rows.Count, discrepancies.Count, message.BlobName);
    }

    private static List<SettlementRow> ParseCsv(string content)
    {
        var rows = new List<SettlementRow>();
        using var reader = new StringReader(content);
        reader.ReadLine(); // header

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            rows.Add(new SettlementRow(parts[0], parts[1], decimal.Parse(parts[2], CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static async Task<(int FileId, bool AlreadyExisted)> RegisterFileAsync(
        SqlConnection connection, string fileName, DateOnly referenceDate)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@NomeArquivo", fileName);
        parameters.Add("@DataReferencia", referenceDate.ToDateTime(TimeOnly.MinValue));
        parameters.Add("@ArquivoId", dbType: DbType.Int32, direction: ParameterDirection.Output);
        parameters.Add("@JaExistia", dbType: DbType.Boolean, direction: ParameterDirection.Output);

        await connection.ExecuteAsync(
            "dbo.uspRegistrarArquivo", parameters, commandType: CommandType.StoredProcedure);

        return (parameters.Get<int>("@ArquivoId"), parameters.Get<bool>("@JaExistia"));
    }

    // Mirrors the reconciliation logic used while building this project (expected = Operacoes
    // of the day, received = the imported file's rows), persisted so it can be monitored later.
    private static async Task<List<Discrepancy>> ReconcileAsync(SqlConnection connection, int fileId, DateOnly referenceDate)
    {
        var start = referenceDate.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var discrepancies = (await connection.QueryAsync<Discrepancy>(
            """
            WITH Expected AS (
                SELECT c.Documento AS Document, a.Ticker AS Ticker, SUM(o.Quantidade * o.Preco) AS Amount
                FROM dbo.Operacoes AS o
                JOIN dbo.Clientes  AS c ON c.ClienteId = o.ClienteId
                JOIN dbo.Ativos    AS a ON a.AtivoId = o.AtivoId
                WHERE o.DataOperacao >= @Start AND o.DataOperacao < @End
                GROUP BY c.Documento, a.Ticker
            ), Received AS (
                SELECT Documento AS Document, Ticker AS Ticker, SUM(Valor) AS Amount
                FROM dbo.LiquidacoesImportadas
                WHERE ArquivoId = @FileId
                GROUP BY Documento, Ticker
            )
            SELECT COALESCE(e.Document, r.Document) AS Document,
                   COALESCE(e.Ticker, r.Ticker)     AS Ticker,
                   e.Amount                          AS ExpectedAmount,
                   r.Amount                          AS ReceivedAmount,
                   CASE
                       WHEN r.Document IS NULL THEN 'Missing'
                       WHEN e.Document IS NULL THEN 'Unexpected'
                       ELSE 'AmountMismatch'
                   END                                AS DiscrepancyType
            FROM Expected AS e
            FULL OUTER JOIN Received AS r
                   ON r.Document = e.Document
                  AND r.Ticker    = e.Ticker
            WHERE e.Document IS NULL
               OR r.Document IS NULL
               OR e.Amount <> r.Amount;
            """,
            new { Start = start, End = end, FileId = fileId })).ToList();

        foreach (var d in discrepancies)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.SettlementDiscrepancies
                    (ArquivoId, Document, Ticker, ExpectedAmount, ReceivedAmount, DiscrepancyType)
                VALUES (@FileId, @Document, @Ticker, @ExpectedAmount, @ReceivedAmount, @DiscrepancyType);
                """,
                new
                {
                    FileId = fileId,
                    d.Document,
                    d.Ticker,
                    d.ExpectedAmount,
                    d.ReceivedAmount,
                    d.DiscrepancyType
                });
        }

        return discrepancies;
    }
}
