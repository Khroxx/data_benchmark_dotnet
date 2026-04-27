# Data Benchmark Dotnet

.NET backend for the benchmark comparison project. It exposes the same benchmark contract as the other backends and returns timing data for generated payloads.

## Clone

SSH:

```bash
git clone git@github.com:Khroxx/data_benchmark_dotnet.git
```

HTTPS:

```bash
git clone https://github.com/Khroxx/data_benchmark_dotnet.git
```

## Endpoints

- `GET /ping`
- `GET /api/dotnet/benchmark`

Supported query params:

- `type=flat-json | nested-json | csv | blob`
- `size` or `sizeKb`
- `runs`

## Local development

Restore and build:

```bash
dotnet build
```

Run the API:

```bash
dotnet run
```

The service listens on port `8080`.
