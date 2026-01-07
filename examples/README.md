# Faborite Examples

This folder contains example configurations and usage patterns for Faborite.

## Examples

| Example | Description |
|---------|-------------|
| [basic-sync](./basic-sync/) | Simple sync with default settings |
| [advanced-config](./advanced-config/) | Per-table configuration with different strategies |
| [python-notebooks](./python-notebooks/) | Python notebook examples using synced data |
| [ci-cd](./ci-cd/) | GitHub Actions workflow for automated sync |

## Quick Start

### 1. Copy an example config

```bash
cp examples/basic-sync/faborite.json ./faborite.json
```

### 2. Update with your IDs

Edit `faborite.json` and replace:
- `your-workspace-guid` with your Fabric Workspace ID
- `your-lakehouse-guid` with your Lakehouse ID

### 3. Login and sync

```bash
az login
faborite sync
```

## Finding Your Workspace and Lakehouse IDs

### From Fabric URL

When you open a Lakehouse in Fabric, the URL contains both IDs:

```
https://app.fabric.microsoft.com/groups/{workspace-id}/lakehouses/{lakehouse-id}
```

### From Fabric UI

1. **Workspace ID**: Go to Workspace Settings → Overview → Workspace ID
2. **Lakehouse ID**: Open Lakehouse → Settings → Lakehouse ID

## Need Help?

- 📖 [Full Documentation](../README.md)
- 🐛 [Report Issues](https://github.com/mjtpena/faborite/issues)
- 💬 [Discussions](https://github.com/mjtpena/faborite/discussions)
