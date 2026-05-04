FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY data_benchmark_dotnet.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out ./
COPY benchmark_data ./benchmark_data
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
CMD ["dotnet", "data_benchmark_dotnet.dll"]
