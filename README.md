# Faborite 🎯

[![CI/CD Pipeline](https://github.com/mjtpena/faborite/actions/workflows/ci.yml/badge.svg)](https://github.com/mjtpena/faborite/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/mjtpena/faborite/branch/main/graph/badge.svg)](https://codecov.io/gh/mjtpena/faborite)
[![NuGet](https://img.shields.io/nuget/v/Faborite.svg)](https://www.nuget.org/packages/Faborite/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

**Sync Microsoft Fabric lakehouse data locally for faster development.**

Faborite lets you pull sample data from your Fabric Lakehouses to your local machine, so you can develop and test notebooks/scripts without waiting for cloud compute.

## Why Faborite?

When working with Microsoft Fabric, you often need to:
- 🐢 Wait for cloud compute to spin up just to test a simple query
- 💸 Pay for compute time during development iterations
- 🔄 Context-switch between local and cloud environments

Faborite solves this by bringing a representative sample of your data locally, enabling:
- ⚡ **Instant iteration** - No cold start, no waiting
- 💰 **Cost savings** - Develop locally, deploy to cloud
- 🧪 **Better testing** - Test with real data patterns locally

## Features

- 🎲 **Smart Sampling** - Random, recent, stratified, or custom SQL sampling
- 📦 **Multiple Formats** - Export to Parquet, CSV, JSON, or DuckDB
- ⚡ **Fast** - Parallel downloads, DuckDB-powered sampling
- 🔧 **Configurable** - Sensible defaults, fully customizable per-table
- 🔐 **Secure** - Uses Azure authentication (CLI, Service Principal, Managed Identity)
- 🚀 **Single Executable** - Built with .NET 10 for fast startup and easy deployment
- 🛡️ **Production Ready** - Comprehensive validation, logging, and retry policies

## Installation

### Download Binary

Download the latest release from [GitHub Releases](https://github.com/mjtpena/faborite/releases):

| Platform | Download |
|----------|----------|
| Windows (x64) | [faborite-win-x64.zip](https://github.com/mjtpena/faborite/releases/latest) |
| Linux (x64) | [faborite-linux-x64.tar.gz](https://github.com/mjtpena/faborite/releases/latest) |
| macOS (x64) | [faborite-osx-x64.tar.gz](https://github.com/mjtpena/faborite/releases/latest) |
| macOS (ARM64) | [faborite-osx-arm64.tar.gz](https://github.com/mjtpena/faborite/releases/latest) |

### As .NET Global Tool

```bash
dotnet tool install -g faborite
```

### From Source

```bash
git clone https://github.com/mjtpena/faborite.git
cd faborite
dotnet build
```

## Quick Start (5 minutes)

### 1. Install

```bash
dotnet tool install -g Faborite
```

### 2. Login to Azure

```bash
az login
```

### 3. Get Your IDs from Fabric URL

Open your Lakehouse in Microsoft Fabric. The URL contains your IDs:

```
https://app.fabric.microsoft.com/groups/{WORKSPACE-ID}/lakehouses/{LAKEHOUSE-ID}
```

For example:
```
https://app.fabric.microsoft.com/groups/4bb594bc-a449-4c2a-9415-325e94f04ea4/lakehouses/16f4ae69-a9f6-409a-9c1a-09f7683715ef
                                       └─────────────── workspace ───────────────┘            └─────────────── lakehouse ───────────────┘
```

### 4. List Available Tables

```bash
faborite list-tables -w <workspace-id> -l <lakehouse-id>
```

### 5. Sync Data

```bash
# Sync all tables (10,000 random rows each)
faborite sync -w <workspace-id> -l <lakehouse-id>

# Sync specific table with custom row count
faborite sync -w <workspace-id> -l <lakehouse-id> --table customers --rows 5000
```

### 6. Check Status

```bash
faborite status
```

### 7. Use Your Data

```python
import pandas as pd
df = pd.read_parquet('./local_lakehouse/customers/customers.parquet')
print(df.head())
```

Or with DuckDB:
```python
import duckdb
df = duckdb.read_parquet('./local_lakehouse/customers/customers.parquet').df()
```

## CLI Reference

### `sync`

Sync data from OneLake to local machine.

```bash
faborite sync [options]
```

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--workspace` | `-w` | Workspace ID (GUID) | From config |
| `--lakehouse` | `-l` | Lakehouse ID (GUID) | From config |
| `--config` | `-c` | Config file path | `faborite.json` |
| `--rows` | `-n` | Number of rows to sample | 10000 |
| `--strategy` | `-s` | Sampling strategy | `random` |
| `--format` | `-f` | Output format | `parquet` |
| `--output` | `-o` | Output directory | `./local_lakehouse` |
| `--table` | `-t` | Tables to sync (repeatable) | All tables |
| `--skip` | | Tables to skip (repeatable) | None |
| `--parallel` | `-p` | Max parallel downloads | 4 |
| `--no-schema` | | Skip schema export | false |

**Examples:**

```bash
# Sync specific tables
faborite sync -w <id> -l <id> --table customers --table orders

# Custom sampling
faborite sync -w <id> -l <id> --rows 5000 --strategy recent

# Export as DuckDB database
faborite sync -w <id> -l <id> --format duckdb

# Export as CSV
faborite sync -w <id> -l <id> --format csv
```

### `list-tables` (alias: `ls`)

List available tables in a lakehouse.

```bash
faborite list-tables -w <workspace-id> -l <lakehouse-id>
```

### `init`

Generate a sample configuration file.

```bash
faborite init [options]
```

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--output` | `-o` | Output file path | `faborite.json` |
| `--force` | `-f` | Overwrite existing file | false |

### `status`

Show status of locally synced data.

```bash
faborite status [options]
```

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--path` | `-p` | Local data directory | `./local_lakehouse` |

## Sampling Strategies

| Strategy | Description | Use Case |
|----------|-------------|----------|
| `random` | Random sample using DuckDB's `USING SAMPLE` | General development |
| `recent` | Most recent rows by date column | Time-series data |
| `head` | First N rows | Quick testing |
| `tail` | Last N rows | Recent additions |
| `stratified` | Proportional sample by column | Categorical data |
| `query` | Custom SQL query | Complex filters |
| `full` | All rows (no sampling) | Small lookup tables |

## Configuration

### Config File

Create a `faborite.json` file in your project root:

```json
{
  "workspaceId": "your-workspace-guid",
  "lakehouseId": "your-lakehouse-guid",
  "sample": {
    "rows": 10000,
    "strategy": "random"
  },
  "format": {
    "output": "parquet",
    "compression": "snappy"
  },
  "sync": {
    "localPath": "./local_lakehouse",
    "parallelTables": 4,
    "includeSchema": true
  },
  "auth": {
    "method": "cli"
  },
  "tableOverrides": {
    "large_table": {
      "rows": 1000,
      "strategy": "recent",
      "dateColumn": "created_at"
    },
    "lookup_table": {
      "strategy": "full"
    }
  }
}
```

### Environment Variables

All configuration can be overridden with environment variables:

| Variable | Description |
|----------|-------------|
| `FABORITE_WORKSPACE_ID` | Workspace ID |
| `FABORITE_LAKEHOUSE_ID` | Lakehouse ID |
| `FABORITE_OUTPUT_PATH` | Output directory |
| `FABORITE_SAMPLE_ROWS` | Default sample rows |
| `FABORITE_FORMAT` | Output format |
| `AZURE_TENANT_ID` | Azure tenant for service principal auth |
| `AZURE_CLIENT_ID` | Azure client ID for service principal auth |
| `AZURE_CLIENT_SECRET` | Azure client secret for service principal auth |

## Output Structure

```
./local_lakehouse/
├── customers/
│   ├── customers.parquet
│   └── _schema.json
├── orders/
│   ├── orders.parquet
│   └── _schema.json
├── products/
│   ├── products.parquet
│   └── _schema.json
└── lakehouse.duckdb     # When using --format duckdb
```

## Authentication

Faborite uses Azure Identity for authentication:

| Method | Description | Config |
|--------|-------------|--------|
| **Azure CLI** (default) | Uses `az login` credentials | `"method": "cli"` |
| **Service Principal** | App registration with secret | `"method": "serviceprincipal"` |
| **Managed Identity** | For Azure-hosted environments | `"method": "managedidentity"` |
| **Default** | Azure DefaultAzureCredential chain | `"method": "default"` |

### Service Principal Setup

```bash
# Set environment variables
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_ID="your-client-id"
export AZURE_CLIENT_SECRET="your-client-secret"

# Update config
{
  "auth": {
    "method": "serviceprincipal",
    "tenantId": "your-tenant-id",
    "clientId": "your-client-id"
  }
}
```

## Using with Notebooks

After syncing, load data in your local notebooks:

### Python with DuckDB

```python
import duckdb

# If exported as DuckDB
conn = duckdb.connect('./local_lakehouse/lakehouse.duckdb')
df = conn.execute("SELECT * FROM customers").df()

# If exported as Parquet
df = duckdb.read_parquet('./local_lakehouse/customers/customers.parquet').df()
```

### Python with Pandas

```python
import pandas as pd

df = pd.read_parquet('./local_lakehouse/customers/customers.parquet')
```

### Python with Polars

```python
import polars as pl

df = pl.read_parquet('./local_lakehouse/customers/customers.parquet')
```

### .NET

```csharp
using DuckDB.NET.Data;

using var connection = new DuckDBConnection("Data Source=./local_lakehouse/lakehouse.duckdb");
connection.Open();
// Query your data...
```

## Requirements

- **Runtime**: .NET 10.0 (or use self-contained builds)
- **Azure**: Account with access to Microsoft Fabric
- **Permissions**: Read access to the target Lakehouse via OneLake

### Finding Your IDs

The easiest way is from the **Fabric URL** when viewing your Lakehouse:

```
https://app.fabric.microsoft.com/groups/{WORKSPACE-ID}/lakehouses/{LAKEHOUSE-ID}
```

Alternatively:
1. **Workspace ID**: Go to your Fabric workspace → Settings → Copy the Workspace ID
2. **Lakehouse ID**: Open your Lakehouse → Settings → Copy the Lakehouse ID

## Development

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) (for authentication)

### Building

```bash
# Clone the repository
git clone https://github.com/mjtpena/faborite.git
cd faborite

# Build
dotnet build

# Run tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run the CLI
dotnet run --project src/Faborite.Cli -- sync -w <workspace-id> -l <lakehouse-id>
```

### Publishing

```bash
# Publish self-contained for Windows
dotnet publish src/Faborite.Cli -c Release -r win-x64 --self-contained -o publish/win-x64

# Publish self-contained for Linux
dotnet publish src/Faborite.Cli -c Release -r linux-x64 --self-contained -o publish/linux-x64

# Publish self-contained for macOS
dotnet publish src/Faborite.Cli -c Release -r osx-x64 --self-contained -o publish/osx-x64
dotnet publish src/Faborite.Cli -c Release -r osx-arm64 --self-contained -o publish/osx-arm64
```

### Project Structure

```
faborite/
├── src/
│   ├── Faborite.Core/           # Core library
│   │   ├── Configuration/       # Config loading & validation
│   │   ├── OneLake/             # OneLake ADLS Gen2 client
│   │   ├── Sampling/            # DuckDB-powered sampling
│   │   ├── Export/              # Format exporters
│   │   ├── Logging/             # Logging infrastructure
│   │   ├── Resilience/          # Retry policies (Polly)
│   │   └── FaboriteService.cs   # Main orchestrator
│   └── Faborite.Cli/            # CLI application
│       ├── Commands/            # CLI commands
│       └── Program.cs           # Entry point
├── tests/
│   ├── Faborite.Core.Tests/     # Core library tests
│   └── Faborite.Cli.Tests/      # CLI tests
├── examples/                     # Usage examples
│   ├── basic-sync/              # Quick start example
│   ├── advanced-config/         # Advanced configuration
│   ├── python-notebooks/        # Python analysis scripts
│   └── ci-cd/                   # GitHub Actions workflow
├── .github/
│   └── workflows/
│       └── ci.yml               # CI/CD pipeline
└── Faborite.sln
```

See the [examples/](examples/) folder for detailed usage examples.

## Roadmap

- [ ] Delta Lake time travel support
- [ ] Incremental sync (only changed data)
- [ ] Schema drift detection
- [ ] VS Code extension
- [ ] GitHub Action for CI/CD pipelines
- [ ] Support for Fabric Warehouses

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details on our code of conduct and the process for submitting pull requests.

### Quick Contribution Steps

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Security

Please see our [Security Policy](SECURITY.md) for reporting vulnerabilities.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [DuckDB](https://duckdb.org/) - For blazing fast local analytics
- [Azure SDK for .NET](https://github.com/Azure/azure-sdk-for-net) - For Azure integration
- [Spectre.Console](https://spectreconsole.net/) - For beautiful CLI output
- [Polly](https://github.com/App-vNext/Polly) - For resilience policies

---

Made with ❤️ by [Michael John Peña](https://github.com/mjtpena)
