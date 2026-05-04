using System.Diagnostics;

LoadEnvFile(".env");

var builder = WebApplication.CreateBuilder(args);
var allowedOrigin = FirstNonEmpty(Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGIN"), "*")!;
var allowedMethods = FirstNonEmpty(Environment.GetEnvironmentVariable("CORS_ALLOWED_METHODS"), "GET,OPTIONS")!;
var allowedHeaders = FirstNonEmpty(Environment.GetEnvironmentVariable("CORS_ALLOWED_HEADERS"), "Content-Type,Authorization")!;

builder.Services.AddCors(options =>
{
    options.AddPolicy("BenchmarkCors", policy =>
    {
        var methods = allowedMethods.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var headers = allowedHeaders.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (allowedOrigin == "*")
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(allowedOrigin);
        }

        if (methods.Length == 0)
        {
            policy.AllowAnyMethod();
        }
        else
        {
            policy.WithMethods(methods);
        }

        if (headers.Length == 0)
        {
            policy.AllowAnyHeader();
        }
        else
        {
            policy.WithHeaders(headers);
        }
    });
});

var app = builder.Build();
app.UseCors("BenchmarkCors");

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
    var fileName = payloadType switch
    {
        "flat-json" => "flat.json",
        "nested-json" => "nested.json",
        "csv" => "table.csv",
        "blob" => "blob.txt",
        _ => throw new ArgumentException("invalid type query parameter")
    };

    return RepeatBytes(ReadBenchmarkData(fileName), targetBytes);
}

static byte[] ReadBenchmarkData(string fileName)
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "benchmark_data", fileName),
        Path.Combine(AppContext.BaseDirectory, "benchmark_data", fileName)
    };

    foreach (var path in candidates)
    {
        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }
    }

    throw new ArgumentException($"benchmark data file not found: {fileName}");
}

static byte[] RepeatBytes(byte[] baseContent, int targetBytes)
{
    if (targetBytes <= 0)
    {
        return [];
    }

    var payload = new byte[targetBytes];
    if (baseContent.Length == 0)
    {
        return payload;
    }

    var offset = 0;
    while (offset < targetBytes)
    {
        var count = Math.Min(baseContent.Length, targetBytes - offset);
        baseContent.AsSpan(0, count).CopyTo(payload.AsSpan(offset, count));
        offset += count;
    }

    return payload;
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
