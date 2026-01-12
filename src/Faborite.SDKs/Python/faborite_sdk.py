"""
Faborite Python SDK
Issue #126

Installation:
    pip install faborite-sdk

Usage:
    from faborite import FaboriteClient
    
    client = FaboriteClient(api_key="your-api-key")
    client.trigger_sync(workspace_id="...", lakehouse_id="...")
"""

class FaboriteClient:
    def __init__(self, api_key: str, base_url: str = "https://api.faborite.com"):
        self.api_key = api_key
        self.base_url = base_url
        self.headers = {"Authorization": f"Bearer {api_key}"}
    
    def trigger_sync(self, workspace_id: str, lakehouse_id: str, **kwargs) -> dict:
        """Trigger a sync operation"""
        import requests
        response = requests.post(
            f"{self.base_url}/api/sync",
            headers=self.headers,
            json={"workspaceId": workspace_id, "lakehouseId": lakehouse_id, **kwargs}
        )
        return response.json()
    
    def list_tables(self, workspace_id: str, lakehouse_id: str) -> list:
        """List all tables in a lakehouse"""
        import requests
        response = requests.get(
            f"{self.base_url}/api/tables",
            headers=self.headers,
            params={"workspaceId": workspace_id, "lakehouseId": lakehouse_id}
        )
        return response.json()
    
    def get_sync_status(self, sync_id: str) -> dict:
        """Get status of a sync operation"""
        import requests
        response = requests.get(
            f"{self.base_url}/api/sync/{sync_id}",
            headers=self.headers
        )
        return response.json()
    
    def profile_data(self, table_name: str) -> dict:
        """Generate data profile for a table"""
        import requests
        response = requests.post(
            f"{self.base_url}/api/profile",
            headers=self.headers,
            json={"tableName": table_name}
        )
        return response.json()

# Example usage
if __name__ == "__main__":
    client = FaboriteClient(api_key="test-key")
    
    # Trigger sync
    result = client.trigger_sync(
        workspace_id="workspace-123",
        lakehouse_id="lakehouse-456"
    )
    print(f"Sync started: {result}")
    
    # List tables
    tables = client.list_tables(
        workspace_id="workspace-123",
        lakehouse_id="lakehouse-456"
    )
    print(f"Tables: {tables}")
