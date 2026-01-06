# Contributing to Faborite

First off, thank you for considering contributing to Faborite! 🎉 It's people like you that make Faborite such a great tool.

## Code of Conduct

This project and everyone participating in it is governed by the [Faborite Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior to [michael@datachain.consulting](mailto:michael@datachain.consulting).

## How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check the [existing issues](https://github.com/mjtpena/faborite/issues) to avoid duplicates. When you create a bug report, please include as many details as possible:

**Bug Report Template:**
- **Title**: A clear and descriptive title
- **Description**: A clear and concise description of the bug
- **Steps to Reproduce**: Step-by-step instructions to reproduce the behavior
- **Expected Behavior**: What you expected to happen
- **Actual Behavior**: What actually happened
- **Environment**:
  - Faborite version
  - Operating System
  - .NET version
  - Fabric workspace type

### Suggesting Enhancements

Enhancement suggestions are tracked as [GitHub issues](https://github.com/mjtpena/faborite/issues). When creating an enhancement suggestion, please include:

- **Use case**: Why is this enhancement useful?
- **Proposed solution**: A clear description of what you want to happen
- **Alternatives considered**: Any alternative solutions you've considered

### Pull Requests

1. **Fork the repository** and create your branch from `main`
2. **Make your changes** following our coding standards
3. **Write or update tests** for your changes
4. **Ensure all tests pass** by running `dotnet test`
5. **Update documentation** if needed
6. **Submit a pull request**

## Development Setup

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)
- [Git](https://git-scm.com/)

### Getting Started

```bash
# Clone your fork
git clone https://github.com/YOUR_USERNAME/faborite.git
cd faborite

# Add upstream remote
git remote add upstream https://github.com/mjtpena/faborite.git

# Install dependencies and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Project Structure

```
faborite/
├── src/
│   ├── Faborite.Core/           # Core library (main logic)
│   └── Faborite.Cli/            # CLI application
├── tests/
│   ├── Faborite.Core.Tests/     # Unit tests for core
│   └── Faborite.Cli.Tests/      # Unit tests for CLI
└── docs/                         # Documentation
```

## Coding Standards

### C# Style Guide

- Follow [Microsoft's C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable and method names
- Keep methods small and focused (single responsibility)
- Use async/await for I/O operations
- Add XML documentation comments for public APIs

### Code Examples

```csharp
// ✅ Good
public async Task<TableSyncResult> SyncTableAsync(
    string tableName,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNullOrEmpty(tableName);
    // Implementation...
}

// ❌ Bad
public async Task<TableSyncResult> Sync(string t)
{
    // No validation, poor naming
}
```

### Commit Messages

Follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

**Types:**
- `feat`: A new feature
- `fix`: A bug fix
- `docs`: Documentation only changes
- `style`: Changes that don't affect code meaning (formatting)
- `refactor`: Code change that neither fixes a bug nor adds a feature
- `perf`: Performance improvements
- `test`: Adding or updating tests
- `chore`: Changes to build process or auxiliary tools

**Examples:**
```
feat(sampling): add stratified sampling strategy
fix(onelake): handle connection timeout properly
docs(readme): update installation instructions
test(exporter): add tests for CSV export
```

### Testing

- Write unit tests for all new functionality
- Maintain test coverage above 80%
- Use descriptive test names that explain the scenario

```csharp
// ✅ Good test name
[Fact]
public async Task SyncTableAsync_WithInvalidTableName_ThrowsArgumentException()

// ❌ Bad test name
[Fact]
public async Task Test1()
```

### Pull Request Checklist

Before submitting a PR, ensure:

- [ ] Code compiles without errors
- [ ] All tests pass (`dotnet test`)
- [ ] New code has appropriate test coverage
- [ ] Documentation is updated if needed
- [ ] Commit messages follow conventions
- [ ] PR description explains the changes

## First Time Contributors

Looking for something to work on? Check out issues labeled:
- [`good first issue`](https://github.com/mjtpena/faborite/labels/good%20first%20issue) - Great for newcomers
- [`help wanted`](https://github.com/mjtpena/faborite/labels/help%20wanted) - Issues where we need help

## Getting Help

- **GitHub Discussions**: Ask questions and share ideas
- **GitHub Issues**: Report bugs or request features
- **Email**: [michael@datachain.consulting](mailto:michael@datachain.consulting)

## Recognition

Contributors will be recognized in our [README](README.md) and release notes. We appreciate every contribution, no matter how small!

## License

By contributing to Faborite, you agree that your contributions will be licensed under its MIT license.

---

Thank you for contributing! 🙏
