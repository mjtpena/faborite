# Advanced Configuration Example

This example shows how to configure different sampling strategies for different tables.

## Use Case

You have:
- **Large event tables** - Need recent data only
- **Lookup/dimension tables** - Need all data
- **Wide fact tables** - Need stratified sampling
- **Staging tables** - Should be skipped

## Usage

```bash
az login
faborite sync --config faborite.json
```

## Key Features

### Per-Table Configuration

Override sampling strategy for specific tables:

```json
"tables": {
  "events": {
    "sample": {
      "strategy": "recent",
      "rows": 5000,
      "dateColumn": "event_timestamp"
    }
  }
}
```

### Skip Tables

Exclude tables by pattern:

```json
"sync": {
  "skipTables": ["_staging", "_temp", "_backup"]
}
```

### Different Output Formats

Export as DuckDB for SQL querying:

```json
"format": {
  "format": "duckdb"
}
```

## Sampling Strategies Explained

| Strategy | When to Use | Config |
|----------|-------------|--------|
| `random` | General purpose | Default |
| `recent` | Time-series data | Needs `dateColumn` |
| `head` | Quick testing | First N rows |
| `tail` | Latest additions | Last N rows |
| `stratified` | Categorical data | Needs `stratifyColumn` |
| `full` | Small lookup tables | All rows |
| `query` | Custom filters | Needs `whereClause` |
