# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.x.x   | :white_check_mark: |

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please report it by:

1. **DO NOT** create a public GitHub issue
2. Send an email to [your-security-email@domain.com] with:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

We will acknowledge your email within 48 hours and provide a detailed response within 5 business days.

## Security Best Practices for Contributors

1. **Dependency Management**
   - Keep dependencies up to date
   - Use `dotnet list package --vulnerable` to check for vulnerabilities
   - Review new dependencies for security issues

2. **Code Security**
   - Never commit secrets, API keys, or passwords
   - Use secure coding practices
   - Validate all inputs
   - Follow principle of least privilege

3. **Authentication & Authorization**
   - Properly validate NuGet feed credentials
   - Secure storage of authentication tokens
   - Implement proper error handling that doesn't leak sensitive information

## Automated Security Measures

This repository implements the following automated security measures:

1. **Branch Protection**: Main branch is protected and requires pull request reviews
2. **CODEOWNERS**: Critical files require approval from repository owners
3. **Dependency Scanning**: Regular checks for vulnerable dependencies (configure in CI/CD)
4. **Code Analysis**: Static analysis tools to identify security issues
