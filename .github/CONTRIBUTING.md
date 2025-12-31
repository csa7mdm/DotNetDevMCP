# Contributing to DotNetDevMCP

Thank you for your interest in contributing to DotNetDevMCP! This document provides guidelines and instructions for contributing.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [How to Contribute](#how-to-contribute)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Pull Request Process](#pull-request-process)
- [Commit Message Guidelines](#commit-message-guidelines)
- [Community](#community)

## Code of Conduct

This project adheres to a Code of Conduct that all contributors are expected to follow. Please read [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) before contributing.

## Getting Started

### Prerequisites

- **.NET 9.0 SDK** or later
- **Git** for version control
- **IDE**: Visual Studio 2022, VS Code, or Rider
- **Basic knowledge** of C#, async/await, and .NET development

### First-Time Contributors

Looking for a good first issue? Check out issues labeled:
- `good-first-issue` - Perfect for newcomers
- `help-wanted` - We'd love your help on these
- `documentation` - Improve our docs

## Development Setup

### 1. Fork and Clone

```bash
# Fork the repository on GitHub, then clone your fork
git clone https://github.com/csa7mdm/DotNetDevMCP.git
cd DotNetDevMCP
```

### 2. Create a Branch

```bash
# Create a feature branch
git checkout -b feature/your-feature-name

# Or for bug fixes
git checkout -b fix/bug-description
```

### 3. Build the Solution

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

### 4. Run Samples

```bash
# Run the orchestration demo
dotnet run --project samples/OrchestrationDemo

# Run the testing service demo
dotnet run --project samples/TestingServiceDemo
```

## How to Contribute

### Reporting Bugs

1. **Search existing issues** to avoid duplicates
2. **Use the bug report template** when creating a new issue
3. **Provide detailed information**:
   - Clear description of the bug
   - Steps to reproduce
   - Expected vs actual behavior
   - Environment details (.NET version, OS, etc.)
   - Stack traces and logs

### Suggesting Features

1. **Check existing feature requests** to avoid duplicates
2. **Use the feature request template**
3. **Explain the use case** and benefits
4. **Provide code examples** of how it would be used
5. **Consider implementation** challenges and alternatives

### Contributing Code

1. **Pick an issue** or create one describing your changes
2. **Discuss your approach** before major changes
3. **Write clean, tested code** following our standards
4. **Submit a pull request** with a clear description
5. **Respond to feedback** from reviewers

## Coding Standards

### C# Conventions

- Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use **C# 13 features** where appropriate (record types, pattern matching, etc.)
- Prefer **readonly** and **const** where possible
- Use **nullable reference types** (`#nullable enable`)

### Code Style

```csharp
// ✅ GOOD: Clear, descriptive names with XML documentation
/// <summary>
/// Executes operations concurrently with resource management
/// </summary>
public async Task<OperationResult> ExecuteAsync(
    IEnumerable<Operation> operations,
    CancellationToken cancellationToken = default)
{
    // Implementation
}

// ❌ BAD: Poor naming, no documentation
public async Task<object> DoIt(List<object> stuff)
{
    // Implementation
}
```

### File Organization

- One class per file
- File name matches the primary type name
- Organize using folders that match namespaces
- Keep related types together

### Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Namespace | PascalCase | `DotNetDevMCP.Testing` |
| Class | PascalCase | `TestingService` |
| Interface | IPascalCase | `ITestExecutor` |
| Method | PascalCase | `ExecuteAsync` |
| Property | PascalCase | `MaxDegreeOfParallelism` |
| Field (private) | _camelCase | `_semaphore` |
| Parameter | camelCase | `cancellationToken` |
| Local Variable | camelCase | `testResult` |

### Documentation

- **All public APIs** must have XML documentation
- Include `<summary>`, `<param>`, and `<returns>` tags
- Provide **code examples** for complex APIs
- Document **exceptions** with `<exception>` tags

```csharp
/// <summary>
/// Executes a collection of test cases with the specified strategy
/// </summary>
/// <param name="testCases">Test cases to execute</param>
/// <param name="options">Execution options including strategy and parallelism</param>
/// <param name="progress">Optional progress reporter</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>A summary of the test run results</returns>
/// <exception cref="ArgumentNullException">Thrown when testCases is null</exception>
public async Task<TestRunSummary> RunTestsAsync(
    IEnumerable<TestCase> testCases,
    TestExecutionOptions? options = null,
    IProgress<TestProgress>? progress = null,
    CancellationToken cancellationToken = default)
```

## Testing Guidelines

### Test Structure

- Use **xUnit** for all tests
- Follow **AAA pattern** (Arrange, Act, Assert)
- Use **descriptive test names** that explain the scenario

```csharp
[Fact]
public async Task ExecuteAsync_WithCancellation_StopsGracefully()
{
    // Arrange
    using var cts = new CancellationTokenSource();
    var executor = new ConcurrentExecutor();
    var operations = CreateLongRunningOperations();

    // Act
    cts.CancelAfter(TimeSpan.FromMilliseconds(100));
    var result = await executor.ExecuteAsync(operations, cts.Token);

    // Assert
    result.Status.Should().Be(OperationStatus.Cancelled);
}
```

### Test Coverage

- **Unit tests** for all business logic
- **Integration tests** for external dependencies
- **Performance tests** for critical paths
- Target **80%+ code coverage** for new code

### Test Organization

```
tests/
├── DotNetDevMCP.Core.Tests/
│   ├── ConcurrentExecutorTests.cs
│   ├── ResourceManagerTests.cs
│   └── WorkflowEngineTests.cs
├── DotNetDevMCP.Testing.Tests/
│   └── TestingServiceTests.cs
└── DotNetDevMCP.Integration.Tests/
    └── EndToEndTests.cs
```

## Pull Request Process

### Before Submitting

1. ✅ **Build succeeds** locally
2. ✅ **All tests pass**
3. ✅ **Code follows standards**
4. ✅ **Documentation updated**
5. ✅ **No new warnings**

### PR Guidelines

1. **Use the PR template** - Fill out all sections
2. **Link related issues** - Use "Fixes #123" syntax
3. **Keep changes focused** - One feature/fix per PR
4. **Write clear commits** - Follow commit message guidelines
5. **Update CHANGELOG.md** - Document user-facing changes

### Review Process

1. **Automated checks** run on all PRs (build, test, CodeQL)
2. **Maintainer review** - At least one approval required
3. **Address feedback** - Make requested changes
4. **Squash and merge** - Maintain clean history

### After Merge

- Your changes will be included in the next release
- You'll be credited in the release notes
- Thank you for contributing! 🎉

## Commit Message Guidelines

### Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Types

- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes (formatting, etc.)
- `refactor`: Code refactoring
- `perf`: Performance improvements
- `test`: Test additions or fixes
- `chore`: Build process, tooling, dependencies

### Examples

```
feat(testing): Add support for parallel test execution

Implemented four execution strategies:
- Sequential for debugging
- FullParallel for maximum throughput
- AssemblyLevelParallel for resource management
- SmartParallel for optimized scheduling

Closes #42
```

```
fix(build): Correct diagnostic parsing regex

The previous regex didn't handle file paths with spaces.
Updated to properly capture file paths in parentheses.

Fixes #89
```

## Community

### Communication Channels

- **GitHub Discussions** - Questions, ideas, and general discussion
- **GitHub Issues** - Bug reports and feature requests
- **Pull Requests** - Code contributions

### Getting Help

- Check the [README.md](../README.md) for documentation
- Browse [sample applications](../samples/)
- Search existing issues and discussions
- Ask in GitHub Discussions

### Recognition

Contributors are recognized in:
- Release notes
- README.md contributors section
- GitHub insights and graphs

## License

By contributing to DotNetDevMCP, you agree that your contributions will be licensed under the same license as the project.

---

**Thank you for contributing to DotNetDevMCP!** Your efforts help make .NET development with AI assistants better for everyone.
