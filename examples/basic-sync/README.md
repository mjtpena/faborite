# Basic Sync Example

This example shows the simplest way to use Faborite - sync all tables with default settings.

## Usage

1. Copy `faborite.json` to your project root
2. Update `workspaceId` and `lakehouseId` with your values
3. Run:

```bash
az login
faborite sync
```

## What This Does

- Syncs **all tables** from your Lakehouse
- Uses **random sampling** with 10,000 rows per table
- Outputs to `./local_lakehouse/` as **Parquet** files
- Includes **schema files** for each table

## Configuration Explained

```json
{
  "workspaceId": "your-workspace-guid",     // Your Fabric Workspace ID
  "lakehouseId": "your-lakehouse-guid",     // Your Lakehouse ID
  "sample": {
    "strategy": "random",                   // Random sampling
    "rows": 10000                           // 10K rows per table
  },
  "format": {
    "format": "parquet"                     // Output as Parquet
  },
  "sync": {
    "localPath": "./local_lakehouse"        // Output directory
  }
}
```

## Output

After running, you'll have:

```
./local_lakehouse/
├── table1/
│   ├── table1.parquet
│   └── _schema.json
├── table2/
│   ├── table2.parquet
│   └── _schema.json
└── ...
```

## Using the Data

```python
import duckdb

# Query your local data
df = duckdb.read_parquet('./local_lakehouse/table1/table1.parquet').df()
print(df.head())
```
