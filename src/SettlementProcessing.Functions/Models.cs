namespace SettlementProcessing.Functions;

public record SettlementMessage(string BlobName, DateOnly ReferenceDate);

public sealed record SettlementRow(string Document, string Ticker, decimal Amount);

public sealed record Discrepancy(string Document, string Ticker, decimal? ExpectedAmount, decimal? ReceivedAmount, string DiscrepancyType);
