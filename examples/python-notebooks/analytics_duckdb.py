"""
Faborite Example: Analytics with DuckDB
=======================================
This example shows powerful SQL analytics using DuckDB.

Prerequisites:
    pip install duckdb pandas

Usage:
    python analytics_duckdb.py
"""

import duckdb

# Configuration
LOCAL_DATA_PATH = "./local_lakehouse"


def setup_duckdb():
    """Create a DuckDB connection with all local tables."""
    conn = duckdb.connect(":memory:")
    
    # Register all parquet files as views
    from pathlib import Path
    data_path = Path(LOCAL_DATA_PATH)
    
    if not data_path.exists():
        print(f"❌ No data found at {LOCAL_DATA_PATH}")
        return None
    
    for table_dir in data_path.iterdir():
        if table_dir.is_dir():
            table_name = table_dir.name
            parquet_pattern = f"{table_dir}/*.parquet"
            
            conn.execute(f"""
                CREATE VIEW {table_name} AS 
                SELECT * FROM read_parquet('{parquet_pattern}')
            """)
            print(f"✓ Registered view: {table_name}")
    
    return conn


def demo_queries(conn):
    """Demonstrate various DuckDB queries."""
    
    # List all views
    print("\n📊 Available tables:")
    tables = conn.execute("SHOW TABLES").fetchall()
    for (table,) in tables:
        count = conn.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]
        print(f"   • {table}: {count:,} rows")
    
    if not tables:
        return
    
    first_table = tables[0][0]
    
    # Schema inspection
    print(f"\n📋 Schema of '{first_table}':")
    schema = conn.execute(f"DESCRIBE {first_table}").df()
    print(schema.to_string(index=False))
    
    # Sample data
    print(f"\n🔍 Sample from '{first_table}':")
    sample = conn.execute(f"SELECT * FROM {first_table} LIMIT 5").df()
    print(sample)
    
    # Aggregation example
    print(f"\n📈 Column statistics:")
    stats = conn.execute(f"""
        SELECT 
            COUNT(*) as total_rows,
            COUNT(DISTINCT *) as distinct_rows
        FROM {first_table}
    """).df()
    print(stats)


def main():
    print("🦆 DuckDB Analytics Example")
    print("=" * 50)
    
    conn = setup_duckdb()
    if conn:
        demo_queries(conn)
        
        # Interactive mode hint
        print("\n💡 Tip: Use conn.execute('YOUR SQL').df() for custom queries!")
        return conn
    
    return None


if __name__ == "__main__":
    conn = main()
