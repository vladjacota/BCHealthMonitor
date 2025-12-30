# Contributing to BC Health Monitor

Thank you for your interest in contributing to BC Health Monitor! This document provides guidelines and information for contributors.

## Getting Started

### Prerequisites

- Windows 10/11 or Windows Server 2016+
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- Business Central on-premise installation (for testing)

### Development Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/YOUR_ORG/BCHealthMonitor.git
   cd BCHealthMonitor
   ```

2. Copy the example configuration:
   ```bash
   copy appsettings.example.json src\BCHealthMonitor\appsettings.json
   ```

3. Update `appsettings.json` with your BC instance details

4. Build and run:
   ```bash
   dotnet build
   dotnet run --project src/BCHealthMonitor
   ```

## How to Contribute

### Reporting Bugs

Before creating a bug report, please check existing issues to avoid duplicates.

When reporting a bug, include:
- **Environment**: Windows version, .NET version, BC version
- **Configuration**: Relevant `appsettings.json` settings (sanitized)
- **Steps to reproduce**: Clear steps to reproduce the issue
- **Expected behavior**: What you expected to happen
- **Actual behavior**: What actually happened
- **Logs**: Relevant log entries (sanitized of sensitive data)

### Suggesting Features

Feature requests are welcome! Please include:
- **Use case**: Why is this feature needed?
- **Proposed solution**: How should it work?
- **Alternatives**: Have you considered other approaches?

### Pull Requests

1. **Fork** the repository
2. **Create a branch** for your feature: `git checkout -b feature/my-feature`
3. **Make your changes** following the coding standards below
4. **Test** your changes thoroughly
5. **Commit** with a clear message: `git commit -m "Add feature: description"`
6. **Push** to your fork: `git push origin feature/my-feature`
7. **Open a Pull Request** with a clear description

## Coding Standards

### C# Style

- Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful names for variables, methods, and classes
- Add XML documentation comments for public APIs
- Keep methods focused and under 50 lines when possible

### Project Structure

```
src/BCHealthMonitor/
├── Configuration/     # Configuration models
├── Endpoints/         # Minimal API endpoint definitions
├── Models/            # Data transfer objects
├── Services/          # Business logic and integrations
└── Program.cs         # Application entry point
```

### Commit Messages

- Use present tense: "Add feature" not "Added feature"
- Use imperative mood: "Fix bug" not "Fixes bug"
- Keep the first line under 72 characters
- Reference issues when applicable: "Fix session count (#123)"

### Testing

- Test with different BC availability strategies
- Test with various threshold configurations
- Verify health endpoint responses match expected HTTP codes
- Test scheduler control if modifying that functionality

## Areas for Contribution

We especially welcome contributions in these areas:

- **Additional health check strategies**: New ways to detect BC availability
- **Session data sources**: Support for additional session query methods
- **Metrics**: Additional Prometheus metrics
- **Documentation**: Improvements to README, examples, troubleshooting guides
- **Platform support**: Adaptations for containerized or cloud deployments
- **Performance**: Optimizations for high-frequency polling scenarios

## Questions?

If you have questions about contributing, feel free to:
- Open a GitHub Discussion
- Create an issue with the "question" label

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
