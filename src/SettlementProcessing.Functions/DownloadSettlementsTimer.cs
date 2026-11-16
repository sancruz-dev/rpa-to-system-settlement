using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SettlementProcessing.Functions;

public class DownloadSettlementsTimer(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<DownloadSettlementsTimer> logger)
{
    // Daily at 07:00. While developing, trigger it on demand instead of waiting for the
    // schedule: POST http://localhost:7071/admin/functions/DownloadSettlementsTimer {}
    [Function(nameof(DownloadSettlementsTimer))]
    public async Task Run([TimerTrigger("0 0 7 * * *")] TimerInfo timer)
    {
        var referenceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var portalBaseUrl = configuration["PortalBaseUrl"]
            ?? throw new InvalidOperationException("PortalBaseUrl is not configured.");

        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{portalBaseUrl}/api/settlements/{referenceDate:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        var csvBytes = await response.Content.ReadAsByteArrayAsync();

        var blobName = $"settlements_{referenceDate:yyyyMMdd}.csv";
        var storageConnectionString = configuration["AzureWebJobsStorage"]!;

        var containerName = configuration["SettlementsContainerName"] ?? "settlements-raw";
        var containerClient = new BlobContainerClient(storageConnectionString, containerName);
        await containerClient.CreateIfNotExistsAsync();
        await containerClient.GetBlobClient(blobName).UploadAsync(new BinaryData(csvBytes), overwrite: true);

        logger.LogInformation("Uploaded {BlobName} ({Size} bytes) for {ReferenceDate}", blobName, csvBytes.Length, referenceDate);

        var queueName = configuration["SettlementsQueueName"] ?? "settlements-to-process";
        var queueClient = new QueueClient(storageConnectionString, queueName);
        await queueClient.CreateIfNotExistsAsync();

        var message = JsonSerializer.Serialize(new SettlementMessage(blobName, referenceDate));
        await queueClient.SendMessageAsync(message);

        logger.LogInformation("Queued {BlobName} for processing", blobName);
    }
}
