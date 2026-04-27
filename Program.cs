using System.Text;
using System.Diagnostics;

LoadEnvFile(".env");

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/ping", () => Results.Text("pong\n", "text/plain"));

app.MapGet("/api/dotnet/benchmark", (
    string? type,
    string? sizeKb,
    string? size,
    string? runs
) =>
{
    var payloadType = (type ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(payloadType))
    {
        return Results.BadRequest(new { error = "missing type query parameter" });
    }

    var parsedSizeKb = ParseSizeKb(sizeKb, size);
    if (parsedSizeKb is null)
    {
        return Results.BadRequest(new { error = "invalid sizeKb query parameter" });
    }

    var (parsedRuns, warnings) = ParseRuns(runs);
    var durations = new List<long>(parsedRuns);
    byte[] payload = [];

    for (var index = 0; index < parsedRuns; index++)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            payload = GeneratePayload(payloadType, parsedSizeKb.Value);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        stopwatch.Stop();
        durations.Add(stopwatch.ElapsedMilliseconds);
    }

    return Results.Json(new
    {
        type = payloadType,
        sizeKb = parsedSizeKb.Value,
        runs = parsedRuns,
        durations,
        average_ms = Average(durations),
        median_ms = Median(durations),
        data_bytes = payload.Length,
        generated = true,
        server_time = DateTimeOffset.UtcNow.ToString("O"),
        warnings = warnings.Count == 0 ? null : warnings
    });
});

app.Run();

static int? ParseSizeKb(string? sizeKb, string? size)
{
    var raw = FirstNonEmpty(sizeKb, size);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    return int.TryParse(raw, out var value) && value > 0 ? value : null;
}

static (int Runs, List<string> Warnings) ParseRuns(string? rawRuns)
{
    if (string.IsNullOrWhiteSpace(rawRuns))
    {
        return (1, []);
    }

    return int.TryParse(rawRuns.Trim(), out var runs) && runs > 0
        ? (runs, [])
        : (1, ["invalid runs value, defaulted to 1"]);
}

static byte[] GeneratePayload(string payloadType, int sizeKb)
{
    var targetBytes = sizeKb * 1024;

    return payloadType switch
    {
        "flat-json" => PadContent(
            "{\"id\":1,\"name\":\"benchmark-entry\",\"status\":\"ok\",\"category\":\"flat\",\"active\":true,\"score\":12345}",
            targetBytes
        ),
        "nested-json" => PadContent(
            "{\"meta\":{\"name\":\"benchmark\",\"version\":1},\"items\":[{\"id\":1,\"tags\":[\"alpha\",\"beta\"],\"payload\":{\"kind\":\"nested\",\"enabled\":true,\"metrics\":{\"count\":3,\"value\":42}}}]}",
            targetBytes
        ),
        "csv" => PadContent(
            "id,name,status,value\n1,benchmark,ok,42\n2,runner,ok,84\n",
            targetBytes
        ),
        "blob" => PadContent("benchmark-payload-blob-", targetBytes),
        _ => throw new ArgumentException("invalid type query parameter")
    };
}

static byte[] PadContent(string baseContent, int targetBytes)
{
    if (targetBytes <= 0)
    {
        return [];
    }

    if (baseContent.Length >= targetBytes)
    {
        return Encoding.UTF8.GetBytes(baseContent[..targetBytes]);
    }

    var builder = new StringBuilder(targetBytes);
    while (builder.Length < targetBytes)
    {
        var remaining = targetBytes - builder.Length;
        builder.Append(remaining >= baseContent.Length ? baseContent : baseContent[..remaining]);
    }

    return Encoding.UTF8.GetBytes(builder.ToString());
}

static double Average(List<long> values)
{
    return values.Count == 0 ? 0 : values.Average();
}

static double Median(List<long> values)
{
    if (values.Count == 0)
    {
        return 0;
    }

    var sorted = values.Order().ToList();
    var middle = sorted.Count / 2;
    return sorted.Count % 2 == 1
        ? sorted[middle]
        : (sorted[middle - 1] + sorted[middle]) / 2.0;
}

static string? FirstNonEmpty(params string?[] values)
{
    return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

static void LoadEnvFile(string path)
{
    if (!File.Exists(path))
    {
        return;
    }

    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || !line.Contains('='))
        {
            continue;
        }

        var parts = line.Split('=', 2);
        var key = parts[0].Trim();
        var value = parts[1].Trim().Trim('"', '\'');

        if (string.IsNullOrWhiteSpace(key) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
        {
            continue;
        }

        Environment.SetEnvironmentVariable(key, value);
    }
}
