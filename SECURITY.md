# Security Policy

## Supported Versions

Currently, only the latest version of DotNetDevMCP is supported with security updates.

| Version | Supported          |
| ------- | ------------------ |
| 0.1.x   | :white_check_mark: |
| < 0.1   | :x:                |

## Reporting a Vulnerability

We take the security of DotNetDevMCP seriously. If you discover a security vulnerability, please follow these steps:

### 1. **Do Not** Open a Public Issue

Please do not report security vulnerabilities through public GitHub issues.

### 2. Report Privately

Instead, please report security vulnerabilities by emailing:

**Email**: [your.email@example.com](mailto:your.email@example.com)

Include the following information:
- Description of the vulnerability
- Steps to reproduce the issue
- Potential impact
- Suggested fix (if any)

### 3. Response Timeline

- **Initial Response**: Within 48 hours, we will acknowledge receipt of your report
- **Status Update**: Within 7 days, we will provide an initial assessment
- **Resolution**: We aim to release a fix within 30 days for critical vulnerabilities

### 4. Coordinated Disclosure

We practice coordinated disclosure:
- We will work with you to understand and validate the vulnerability
- Once a fix is ready, we will coordinate a release timeline
- We will publicly credit you for the discovery (unless you prefer to remain anonymous)

## Security Best Practices

When using DotNetDevMCP:

1. **Keep Dependencies Updated**
   - Regularly update to the latest version
   - Monitor security advisories for .NET and dependencies

2. **Validate Input**
   - Always validate and sanitize user input
   - Be cautious with paths and file operations

3. **Least Privilege**
   - Run DotNetDevMCP with minimal required permissions
   - Avoid running as administrator/root unless necessary

4. **Secure Configuration**
   - Use secure defaults
   - Review configuration for security implications
   - Keep sensitive data (API keys, tokens) out of source control

5. **Network Security**
   - Use HTTPS for all network communications
   - Validate SSL/TLS certificates
   - Use secure authentication mechanisms

## Known Security Considerations

### File System Access
DotNetDevMCP requires file system access to:
- Read source code files
- Execute build and test commands
- Write temporary files

**Mitigation**: Run in sandboxed environments when processing untrusted code.

### Code Execution
DotNetDevMCP executes:
- `dotnet build` commands
- `dotnet test` commands
- MSBuild scripts

**Mitigation**: Validate all inputs and use isolated build environments.

### Dependencies
DotNetDevMCP depends on:
- .NET Runtime
- Roslyn compiler
- Third-party NuGet packages

**Mitigation**: Regularly update dependencies and monitor for vulnerabilities.

## Security Updates

Security updates will be released as:
- Patch versions for non-breaking security fixes (0.1.x)
- Minor versions for breaking security fixes (0.x.0)

All security updates will be:
- Documented in the CHANGELOG
- Announced in GitHub Releases
- Tagged with `security` label

## Security Hall of Fame

We recognize and thank security researchers who responsibly disclose vulnerabilities:

*(No vulnerabilities reported yet)*

---

**Last Updated**: December 31, 2025
