# CI/CD Integration Example

Use Faborite in your CI/CD pipelines to keep test data fresh.

## GitHub Actions

### Automated Data Sync

The workflow in `sync-data.yml` shows how to:
1. Sync data on a schedule (daily)
2. Cache the synced data between runs
3. Use the data in tests

### Setup Required

1. **Create Azure Service Principal:**
   ```bash
   az ad sp create-for-rbac --name "faborite-ci" --role "Reader"
   ```

2. **Add GitHub Secrets:**
   - `AZURE_TENANT_ID`
   - `AZURE_CLIENT_ID`
   - `AZURE_CLIENT_SECRET`
   - `FABRIC_WORKSPACE_ID`
   - `FABRIC_LAKEHOUSE_ID`

3. **Grant Fabric Access:**
   - Add the service principal to your Fabric workspace with Viewer role

## Azure DevOps

Similar patterns work with Azure DevOps - see `azure-pipelines.yml`.

## Best Practices

### Cache Synced Data

```yaml
- uses: actions/cache@v4
  with:
    path: ./local_lakehouse
    key: faborite-data-${{ hashFiles('faborite.json') }}
```

### Use Service Principal

Don't use personal credentials in CI:

```json
{
  "auth": {
    "method": "ServicePrincipal",
    "tenantId": "${{ secrets.AZURE_TENANT_ID }}",
    "clientId": "${{ secrets.AZURE_CLIENT_ID }}"
  }
}
```

### Scheduled Sync

Keep data fresh with scheduled runs:

```yaml
on:
  schedule:
    - cron: '0 6 * * *'  # Daily at 6 AM
```
