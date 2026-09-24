# Contributing to DotNetDevMCP

Thank you for your interest in contributing to DotNetDevMCP! This document provides guidelines for contributing to the project.

## Start here

You don't need to write code to help. In order of how much they help right now:

1. **Run it on your solution and tell us what happened.** Especially large or multi-targeted solutions, and test suites on
   Microsoft.Testing.Platform, NUnit, MSTest or TUnit. A [bug report](https://github.com/csa7mdm/DotNetDevMCP/issues/new?template=bug_report.yml)
   or a note in [Discussions](https://github.com/csa7mdm/DotNetDevMCP/discussions) with numbers is gold.
2. **Pick a [good first issue](https://github.com/csa7mdm/DotNetDevMCP/labels/good%20first%20issue)** and say in a comment
   that you're on it. The maintainer will answer questions and review quickly.
3. **Improve the [wiki](https://github.com/csa7mdm/DotNetDevMCP/wiki).** Setup for another MCP client, a clearer tutorial
   step, a troubleshooting entry for something that tripped you up.

Where things live: Roslyn tools in `src/DotNetDevMCP.CodeIntelligence`, test running and affected-test selection in
`src/DotNetDevMCP.Testing`, the CLI and tool registration in `src/DotNetDevMCP.Server/Program.cs`. The
[Architecture](https://github.com/csa7mdm/DotNetDevMCP/wiki/Architecture) wiki page has the picture.

Three rules that have caused real bugs here:

- Tool parameters are arrays (`string[]`), never `IEnumerable<T>`: the MCP C# SDK tries to resolve `IEnumerable<T>` from DI.
- Child processes (`dotnet`, `git`) redirect and close stdin. In stdio mode the server's stdin is the MCP connection.
- CI runs on Linux and Windows. Build paths with `Path.Combine`, and don't assume `C:\`.

Changing how tests are selected or run? Re-run the Polly benchmark in [benchmarks/polly](benchmarks/polly/README.md)
and put the before/after numbers in the pull request.

## Development Philosophy

- **Test-Driven Development (TDD)**: Write tests before implementation
- **Documentation First**: Update documentation with every change
- **Concurrent by Default**: Prefer async/await and parallel operations
- **Simplicity Over Complexity**: Clear, maintainable code

## Getting Started

### Prerequisites

- .NET 10.0 SDK or later
- Git
- Your favorite C# IDE (Rider, Visual Studio, VS Code)

### Setup

```bash
# Clone the repository
git clone https://github.com/csa7mdm/DotNetDevMCP.git
cd DotNetDevMCP

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

## Development Workflow

### 1. Create a Branch

```bash
git checkout -b feature/your-feature-name
```

### 2. Write Tests First (TDD)

All new features and bug fixes should have tests:

```csharp
// tests/DotNetDevMCP.Feature.Tests/FeatureTests.cs
public class FeatureTests
{
    [Fact]
    public async Task MethodName_Scenario_ExpectedBehavior()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

### 3. Implement Feature

- Follow the existing code style (.editorconfig)
- Add XML documentation comments for public APIs
- Use async/await for I/O operations
- Support cancellation tokens

### 4. Update Documentation

**Required documentation updates:**

1. **Code Comments**: XML docs for public APIs
2. **AI Context**: Update `docs/ai-context/project-context.json`
3. **Architecture**: Update relevant docs in `docs/architecture/`
4. **ADRs**: Create ADR for significant architectural decisions
5. **README**: Update if adding major features

### 5. Test Your Changes

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/DotNetDevMCP.Feature.Tests/

# Check code coverage (if configured)
dotnet test /p:CollectCoverage=true
```

### 6. Commit Your Changes

Follow conventional commits format:

```bash
git add .
git commit -m "feat: add concurrent test execution"
git commit -m "fix: resolve null reference in SolutionLoader"
git commit -m "docs: update architecture overview"
git commit -m "test: add tests for Git integration"
```

**Commit types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `test`: Test additions or modifications
- `refactor`: Code refactoring
- `perf`: Performance improvements
- `chore`: Build process or auxiliary tool changes

### 7. Push and Create Pull Request

```bash
git push origin feature/your-feature-name
```

Create a pull request on GitHub with:
- Clear description of changes
- Link to related issues
- Test results
- Documentation updates

## Code Style Guidelines

### Naming Conventions

```csharp
// Namespaces: DotNetDevMCP.[Layer].[Feature]
namespace DotNetDevMCP.SourceControl.Git;

// Interfaces: I[Name]
public interface ISourceControlService { }

// Classes: PascalCase
public class GitService { }

// Methods: PascalCase with descriptive names
public async Task<RepositoryStatus> GetStatusAsync() { }

// Parameters: camelCase
public void ProcessFile(string filePath, bool includeHidden) { }

// Private fields: _camelCase
private readonly IGitService _gitService;
```

### Code Organization

```csharp
// 1. Using statements (sorted)
using System;
using System.Threading;
using DotNetDevMCP.Core.Interfaces;

// 2. Namespace
namespace DotNetDevMCP.Feature;

// 3. Class with XML docs
/// <summary>
/// Brief description of what this class does
/// </summary>
public class FeatureService : IFeatureService
{
    // 4. Fields
    private readonly IDependency _dependency;

    // 5. Constructor
    public FeatureService(IDependency dependency)
    {
        _dependency = dependency;
    }

    // 6. Public methods
    public async Task<Result> DoSomethingAsync(CancellationToken cancellationToken = default)
    {
        // Implementation
    }

    // 7. Private methods
    private void HelperMethod() { }
}
```

### Async/Await Guidelines

```csharp
// ✅ DO: Use async/await for I/O operations
public async Task<Solution> LoadSolutionAsync(string path, CancellationToken cancellationToken)
{
    var solution = await File.ReadAllTextAsync(path, cancellationToken);
    return ParseSolution(solution);
}

// ✅ DO: Support cancellation
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    // ... work ...
}

// ✅ DO: Use ConfigureAwait(false) in libraries
await operation.ConfigureAwait(false);

// ❌ DON'T: Use async void (except event handlers)
public async void BadMethod() { } // NO!
```

### Error Handling

```csharp
// ✅ DO: Use specific exceptions
throw new ArgumentNullException(nameof(parameter));
throw new InvalidOperationException("Detailed message");

// ✅ DO: Provide context in exceptions
catch (Exception ex)
{
    throw new DotNetDevMCPException(
        $"Failed to load solution from {path}",
        ex);
}

// ✅ DO: Return result types for expected failures
public record Result<T>(bool IsSuccess, T? Value, string? Error);
```

## Testing Guidelines

### Test Structure

```csharp
public class FeatureServiceTests
{
    // Use descriptive test names
    [Fact]
    public async Task LoadSolution_ValidPath_ReturnsSolution()
    {
        // Arrange
        var service = new FeatureService();
        var path = "/valid/path.sln";

        // Act
        var result = await service.LoadSolutionAsync(path);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedValue, result.Property);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task LoadSolution_InvalidPath_ThrowsException(string path)
    {
        // Arrange
        var service = new FeatureService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.LoadSolutionAsync(path));
    }
}
```

### Test Coverage

- Aim for **80%+ code coverage**
- Focus on business logic and complex scenarios
- Test edge cases and error conditions
- Don't test framework code or trivial getters/setters

## Documentation Guidelines

### XML Documentation

```csharp
/// <summary>
/// Loads a solution from the specified path and returns a Roslyn Solution object.
/// </summary>
/// <param name="solutionPath">The absolute path to the .sln file</param>
/// <param name="cancellationToken">Optional cancellation token</param>
/// <returns>A Solution object if successful, null if not found</returns>
/// <exception cref="ArgumentNullException">Thrown when solutionPath is null</exception>
/// <exception cref="FileNotFoundException">Thrown when solution file doesn't exist</exception>
public async Task<Solution?> LoadSolutionAsync(
    string solutionPath,
    CancellationToken cancellationToken = default)
```

### Architecture Decision Records (ADRs)

For significant architectural decisions, create an ADR:

```markdown
# ADR-XXX: Title

**Status**: Proposed | Accepted | Deprecated | Superseded
**Date**: YYYY-MM-DD

## Context
What is the issue we're seeing?

## Decision
What is the change we're proposing?

## Consequences
What becomes easier or more difficult?
```

### AI-Friendly Documentation

Update `docs/ai-context/project-context.json` when:
- Adding new features or layers
- Making architectural changes
- Changing design decisions
- Updating dependencies

## Pull Request Process

1. **Self-Review**: Review your own code first
2. **Tests Pass**: Ensure all tests pass
3. **Documentation Updated**: All required docs updated
4. **Clean Commit History**: Squash commits if needed
5. **Descriptive PR**: Clear title and description
6. **Link Issues**: Reference related issues

### PR Checklist

- [ ] Tests written and passing
- [ ] Documentation updated
- [ ] Code follows style guidelines
- [ ] No warnings introduced
- [ ] AI context updated (if architectural change)
- [ ] Commit messages follow conventional commits

## Questions?

Ask in [Discussions](https://github.com/csa7mdm/DotNetDevMCP/discussions); no question is too small. Check existing issues and pull requests first.

## Code of Conduct

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).

Thank you for contributing to DotNetDevMCP!
