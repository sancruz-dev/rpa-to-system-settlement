using System.Globalization;
using System.Text;
using BrokeragePortal.Api;

var builder = WebApplication.CreateBuilder(args);

// Load user secrets unconditionally, not just when ASPNETCORE_ENVIRONMENT=Development,
// so the connection string also resolves when the app is run as a plain built executable.
builder.Configuration.AddUserSecrets<Program>(optional: true);

var app = builder.Build();

var connectionString = builder.Configuration.GetConnectionString("SettlementDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Connection string 'SettlementDb' is not configured. Set it via 'dotnet user-secrets' " +
        "(see README) or an environment variable.");

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/settlements/{date}", async (string date) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        return Results.BadRequest("Use the yyyy-MM-dd format, e.g. 2026-11-10.");

    var csv = await SettlementCsvBuilder.GenerateAsync(connectionString, day);
    var fileName = $"settlements_{day:yyyyMMdd}.csv";
    return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
});

app.Run();
