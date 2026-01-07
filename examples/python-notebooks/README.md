# Python Notebook Examples

Examples showing how to use synced Faborite data in Python notebooks.

## Prerequisites

1. Sync your data first:
   ```bash
   faborite sync -w <workspace-id> -l <lakehouse-id>
   ```

2. Install Python dependencies:
   ```bash
   pip install duckdb pandas polars matplotlib seaborn
   ```

## Examples

### 1. Basic Data Exploration (`explore_data.py`)

Quick exploration of synced Parquet files.

### 2. Analytics with DuckDB (`analytics_duckdb.py`)

SQL-based analytics using DuckDB's powerful query engine.

### 3. Visualization (`visualization.py`)

Create charts and visualizations from local data.

## Running the Examples

```bash
# Run a specific example
python explore_data.py

# Or in Jupyter
jupyter notebook
# Then open any .ipynb file
```

## Tips

### Use DuckDB for Large Files

DuckDB is much faster than Pandas for large Parquet files:

```python
import duckdb

# Fast aggregation on millions of rows
result = duckdb.sql("""
    SELECT category, SUM(amount) as total
    FROM read_parquet('./local_lakehouse/sales/*.parquet')
    GROUP BY category
""").df()
```

### Lazy Loading with Polars

Polars scan is memory-efficient:

```python
import polars as pl

df = pl.scan_parquet('./local_lakehouse/events/*.parquet')
result = df.filter(pl.col('value') > 100).collect()
```
