# Faborite 🎯

[![CI/CD Pipeline](https://github.com/mjtpena/faborite/actions/workflows/ci.yml/badge.svg)](https://github.com/mjtpena/faborite/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/mjtpena/faborite/branch/main/graph/badge.svg)](https://codecov.io/gh/mjtpena/faborite)
[![NuGet](https://img.shields.io/nuget/v/Faborite.svg)](https://www.nuget.org/packages/Faborite/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

**Enterprise-grade data lakehouse sync with ML/AI capabilities**

Faborite is a comprehensive data platform that combines lakehouse synchronization, intelligent data transformations, machine learning, and AI-powered analytics - all in a single, production-ready package.

## 🎯 Core Capabilities

### 📊 Data Integration & Sync
- **30+ Connectors**: SQL databases, NoSQL stores, cloud storage, streaming platforms, time series databases
- **Smart Sampling**: Random, stratified, time-based, and custom SQL sampling strategies
- **Multi-Format Support**: Parquet, Delta Lake, Iceberg, CSV, JSON, Avro
- **Real-time Streaming**: Kafka, RabbitMQ, Redis Streams, Event Hubs

### 🤖 Machine Learning Suite
- **AutoML**: Automated model selection for classification, regression, and multiclass problems
- **7 ML Algorithms**: Classification, regression, anomaly detection, forecasting, clustering, recommendations
- **MLflow-style Tracking**: Experiment management with metrics, parameters, and artifacts
- **Feature Engineering**: 8 transformation techniques including normalization, encoding, and binning

### 🧠 AI-Powered Features
- **Intelligent Schema Inference**: Auto-detect 9 data types with confidence scoring
- **Query Optimization**: Detect and fix 8 SQL anti-patterns automatically
- **PII Detection**: Identify and mask sensitive data (Email, SSN, Credit Card, etc.)
- **Smart Mapping**: Automatic schema mapping with Levenshtein distance matching

### 🔄 Data Transformation
- **22 Statistical Functions**: Mean, median, percentiles, variance, skewness, kurtosis, etc.
- **12 Window Functions**: ROW_NUMBER, RANK, LAG/LEAD, cumulative sums, moving averages
- **Pivot/Unpivot**: Cross-tabulation, transpose, multi-value pivots
- **SQL Engine**: DuckDB-powered analytics with lakehouse integration

## 🚀 Quick Start

### Installation

```bash
# As .NET Global Tool
dotnet tool install -g Faborite

# Or download binary from releases
# https://github.com/mjtpena/faborite/releases
```

### Basic Usage

```bash
# Sync data from Microsoft Fabric
faborite sync --workspace "MyWorkspace" --lakehouse "MyLakehouse"

# With custom sampling
faborite sync --workspace "MyWorkspace" --lakehouse "MyLakehouse" --sample-size 10000

# Export to specific format
faborite export --format parquet --output "./data"
```

### ML/AI Examples

```bash
# Train classification model with AutoML
faborite ml train --data "./data.csv" --target "label" --type classification

# Detect PII in dataset
faborite ai detect-pii --data "./sensitive-data.csv" --mask partial

# Optimize SQL queries
faborite ai optimize-query --file "./query.sql"

# Infer schema from data
faborite ai infer-schema --data "./unknown-data.csv"
```

## 📦 Supported Connectors

### Databases
- **Relational**: SQL Server, PostgreSQL, MySQL, Oracle, SQLite
- **NoSQL**: MongoDB, Cassandra, Elasticsearch, Redis
- **Graph**: Neo4j
- **Data Warehouses**: Snowflake, Azure Synapse, Google BigQuery

### Cloud Storage
- AWS S3, Google Cloud Storage, Azure Blob Storage
- MinIO, Cloudflare R2, Backblaze B2, Wasabi

### Streaming
- Apache Kafka, RabbitMQ, Redis Streams
- Azure Event Hubs, AWS Kinesis

### Time Series
- InfluxDB, TimescaleDB, Prometheus

## 🛠️ Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Faborite Platform                        │
├─────────────────┬───────────────────────┬──────────────────────┤
│   Data Layer    │   Processing Layer    │    ML/AI Layer       │
├─────────────────┼───────────────────────┼──────────────────────┤
│ • 30+ Connectors│ • Transformations     │ • AutoML             │
│ • Multi-format  │ • Window Functions    │ • Classification     │
│ • Streaming     │ • Aggregations        │ • Regression         │
│ • Batch         │ • Pivots              │ • Anomaly Detection  │
│                 │ • SQL Engine          │ • Forecasting        │
│                 │                       │ • Clustering         │
│                 │                       │ • PII Detection      │
│                 │                       │ • Schema Inference   │
│                 │                       │ • Query Optimization │
└─────────────────┴───────────────────────┴──────────────────────┘
```

## 📊 Statistics

- **28,500+ Lines of Code**
- **30+ Production-Ready Connectors**
- **12 ML/AI Engines**
- **100+ Statistical Functions**
- **Full Async/Await Support**
- **Comprehensive Logging**
- **Production-Grade Error Handling**

## 🎓 Examples

### Data Transformation
```csharp
// Window functions
var engine = new WindowFunctionEngine(logger);
var result = await engine.ApplyWindowFunctionAsync(
    data, "ROW_NUMBER", "date", "category");

// Custom aggregations
var aggEngine = new AggregationEngine(logger);
var stats = await aggEngine.CalculateStatisticsAsync(data, "value");
```

### Machine Learning
```csharp
// AutoML classification
var automl = new AutoMLEngine(logger);
var result = await automl.AutoTrainClassificationAsync(
    trainingData, "label", maxExperimentTimeInSeconds: 60);

// Time series forecasting
var forecaster = new ForecastingEngine(logger);
var forecast = await forecaster.ForecastAsync(
    data, "value", horizon: 30, windowSize: 10);
```

### AI Features
```csharp
// Schema inference
var inferrer = new SchemaInferenceEngine(logger);
var schema = await inferrer.InferSchemaAsync(data, sampleSize: 1000);

// Query optimization
var optimizer = new QueryOptimizationEngine(logger);
var analysis = await optimizer.AnalyzeQueryAsync(sqlQuery);
```

## 📚 Documentation

- [Installation Guide](docs/installation.md)
- [Connector Setup](docs/connectors/)
- [ML/AI Usage](docs/ml-ai/)
- [API Reference](docs/api/)
- [Architecture Decisions](docs/architecture/)
- [Issue Tracking](TRACKING.md)
- [Feature Plans](FEATURE_GAPS_300.md)

## 🤝 Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Setup
```bash
# Clone repository
git clone https://github.com/mjtpena/faborite.git

# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test
```

## 📝 License

MIT License - see [LICENSE](LICENSE) for details.

## 🙏 Acknowledgments

Built with:
- [.NET 10](https://dotnet.microsoft.com/)
- [DuckDB](https://duckdb.org/)
- [ML.NET](https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet)
- [Apache Arrow](https://arrow.apache.org/)
- [Delta Lake](https://delta.io/)

## 📞 Support

- GitHub Issues: [Report a bug](https://github.com/mjtpena/faborite/issues)
- Discussions: [Ask questions](https://github.com/mjtpena/faborite/discussions)
- Twitter: [@mjtpena](https://twitter.com/mjtpena)

## 🎯 Roadmap

See [TRACKING.md](TRACKING.md) for detailed feature roadmap and progress.

### Upcoming Features
- Neural network integration (ONNX Runtime)
- Azure ML & AWS SageMaker integration
- Advanced graph analytics
- Real-time data streaming UI
- Kubernetes operators

---

**Made with ❤️ by [Michael John Peña](https://github.com/mjtpena)**
