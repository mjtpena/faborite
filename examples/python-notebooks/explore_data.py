"""
Faborite Example: Basic Data Exploration
=========================================
This example shows how to explore synced Parquet data.

Prerequisites:
    pip install duckdb pandas

Usage:
    python explore_data.py
"""

import duckdb
import pandas as pd
from pathlib import Path

# Configuration
LOCAL_DATA_PATH = "./local_lakehouse"


def list_tables():
    """List all synced tables."""
    data_path = Path(LOCAL_DATA_PATH)
    
    if not data_path.exists():
        print(f"❌ No data found at {LOCAL_DATA_PATH}")
        print("   Run 'faborite sync' first!")
        return []
    
    tables = [d.name for d in data_path.iterdir() if d.is_dir()]
    print(f"📊 Found {len(tables)} tables:")
    for table in sorted(tables):
        print(f"   • {table}")
    
    return tables


def explore_table(table_name: str):
    """Explore a single table."""
    parquet_path = f"{LOCAL_DATA_PATH}/{table_name}/{table_name}.parquet"
    
    print(f"\n📋 Table: {table_name}")
    print("=" * 50)
    
    # Read with DuckDB (faster for large files)
    df = duckdb.read_parquet(parquet_path).df()
    
    print(f"Rows: {len(df):,}")
    print(f"Columns: {len(df.columns)}")
    print(f"\nColumn types:")
    for col, dtype in df.dtypes.items():
        print(f"   {col}: {dtype}")
    
    print(f"\nSample data:")
    print(df.head())
    
    return df


def run_sql_query(sql: str):
    """Run a SQL query across local data."""
    print(f"\n🔍 Running query:")
    print(f"   {sql[:100]}...")
    
    result = duckdb.sql(sql).df()
    print(f"\nResults ({len(result)} rows):")
    print(result)
    
    return result


if __name__ == "__main__":
    # List available tables
    tables = list_tables()
    
    if tables:
        # Explore first table
        first_table = tables[0]
        df = explore_table(first_table)
        
        # Example SQL query
        print("\n" + "=" * 50)
        print("Example: Count rows per table")
        
        sql = f"""
        SELECT 
            '{first_table}' as table_name,
            COUNT(*) as row_count
        FROM read_parquet('{LOCAL_DATA_PATH}/{first_table}/*.parquet')
        """
        run_sql_query(sql)
