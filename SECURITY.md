# Security Policy

## Supported Versions

We release patches for security vulnerabilities. Currently supported versions:

| Version | Supported          |
| ------- | ------------------ |
| 0.1.x   | :white_check_mark: |
| < 0.1   | :x:                |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, please report them via GitHub Security Advisories:

1. Go to https://github.com/mjtpena/faborite/security/advisories/new
2. Click "Report a vulnerability"
3. Fill out the form with details about the vulnerability

Alternatively, you can email security concerns to [michael@datachain.consulting](mailto:michael@datachain.consulting).

### What to Include

Please include as much of the following information as possible:

- Type of issue (e.g. authentication bypass, SQL injection, cross-site scripting, etc.)
- Full paths of source file(s) related to the manifestation of the issue
- The location of the affected source code (tag/branch/commit or direct URL)
- Any special configuration required to reproduce the issue
- Step-by-step instructions to reproduce the issue
- Proof-of-concept or exploit code (if possible)
- Impact of the issue, including how an attacker might exploit it

### Response Timeline

- We will acknowledge receipt of your vulnerability report within 48 hours
- We will provide a more detailed response within 7 days indicating next steps
- We will keep you informed of the progress towards a fix
- We may ask for additional information or guidance

### Disclosure Policy

- We ask that you do not publicly disclose the vulnerability until we've had a chance to address it
- Once a fix is released, we will publicly acknowledge your responsible disclosure (unless you prefer to remain anonymous)
- We aim to patch critical vulnerabilities within 30 days of disclosure

## Security Best Practices

When using Faborite:

### 1. Protect Your Credentials

- **Never commit** `faborite.json` with real workspace/lakehouse IDs to public repositories
- Use environment variables for sensitive configuration:
  ```bash
  export FABORITE_WORKSPACE_ID="your-id"
  export FABORITE_LAKEHOUSE_ID="your-id"
  ```
- When using Service Principal authentication, store credentials securely:
  - Use Azure Key Vault in production
  - Use environment variables, never hardcode secrets
  - Rotate credentials regularly

### 2. Access Control

- Apply principle of least privilege to service principals
- Only grant read access to lakehouses that need to be synced
- Regularly audit access permissions

### 3. Data Protection

- Be mindful of sensitive data when syncing locally
- Use encryption for local data storage if required by your organization
- Follow your organization's data governance policies
- Never commit synced data (`.faborite/` folder) to version control

### 4. Network Security

- When possible, use private endpoints for Fabric/OneLake access
- Consider using VPN or private networks for sensitive data transfers

### 5. Dependencies

- Keep Faborite updated to the latest version
- We regularly update dependencies to address security issues
- Review our release notes for security patches

## Known Security Considerations

### Local Data Storage

Faborite downloads data to your local machine. This data:
- Is stored in plain text (Parquet, CSV, JSON, or DuckDB)
- May contain sensitive information from your lakehouses
- Should be treated with the same security controls as your cloud data

### Authentication Tokens

Faborite uses Azure authentication tokens:
- Tokens are managed by Azure Identity SDK
- Tokens are cached by Azure SDK (typically in `~/.azure/`)
- Token lifetime is controlled by Azure AD

## Security Updates

We will announce security updates through:
- GitHub Security Advisories
- Release notes on GitHub
- Updated documentation

Subscribe to repository notifications to stay informed about security updates.

## Compliance

Faborite uses industry-standard libraries:
- **Azure SDK for .NET**: For secure Azure authentication and data access
- **DuckDB**: For local data processing (no network access)
- **Polly**: For resilient retry policies

We follow .NET security best practices and regularly update dependencies.

## Questions?

For general security questions (not vulnerability reports), please:
- Open a discussion in [GitHub Discussions](https://github.com/mjtpena/faborite/discussions)
- Email [michael@datachain.consulting](mailto:michael@datachain.consulting)

Thank you for helping keep Faborite and its users secure!
