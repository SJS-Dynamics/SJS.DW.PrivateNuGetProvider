# Contributing to SJS.DW.PrivateNugetProvider

Thank you for your interest in contributing to this project! We welcome contributions from the community while maintaining high standards for code quality and security.

## How to Contribute

### 🔍 What You Can Contribute
- **Source Code**: Improvements to existing features, bug fixes, new features
- **Documentation**: README updates, code comments, usage examples
- **Unit Tests**: Test cases to improve code coverage and reliability
- **Bug Reports**: Issues with detailed reproduction steps
- **Feature Requests**: Ideas for new functionality

### 🚫 What You Cannot Modify
For security and project integrity, the following files are restricted to maintainers only:
- CI/CD pipelines and GitHub Actions (`.github/workflows/`, `azure-pipelines.yml`)
- Repository configuration (`.gitignore`, `CODEOWNERS`, `LICENSE`)
- Build configuration (`*.sln`, `*.csproj`, `nuget.config`)
- Security policies (`SECURITY.md`)

## Getting Started

### 1. Fork and Clone
```bash
# Fork the repository on GitHub, then clone your fork
git clone https://github.com/YOUR-USERNAME/SJS.DW.PrivateNugetProvider.git
cd SJS.DW.PrivateNugetProvider
```

### 2. Create a Feature Branch
```bash
git checkout -b feature/your-feature-name
# or
git checkout -b bugfix/issue-description
```

### 3. Make Your Changes
- Focus on the `Middleware/`, `Models/`, and `Providers/` directories for code changes
- Add unit tests for new functionality
- Update documentation as needed
- Follow existing code style and conventions

### 4. Test Your Changes
```bash
dotnet build
dotnet test  # If tests exist
```

### 5. Submit a Pull Request
- Push your branch to your fork
- Create a pull request against the main repository
- Fill out the pull request template completely
- Ensure all checks pass

## Code Review Process

### 📋 Review Requirements
All pull requests must be approved by project maintainers (@jweeksSJS or @SJS-Dynamics/Corporate Team) before merging.

### ⏱️ Review Timeline
- Initial review: Within 5 business days
- Follow-up reviews: Within 2 business days
- Security-related changes: May require additional review time

### ✅ Review Criteria
Your contribution will be evaluated on:
- **Code Quality**: Clean, readable, well-documented code
- **Functionality**: Does it work as intended and solve the stated problem?
- **Security**: No introduction of vulnerabilities or security risks
- **Testing**: Adequate test coverage for new functionality
- **Documentation**: Clear documentation for new features
- **Compatibility**: Maintains backward compatibility where possible

## Development Guidelines

### 🎯 Code Style
- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments for public members
- Keep methods focused and single-purpose

### 🧪 Testing
- Add unit tests for new functionality
- Ensure existing tests continue to pass
- Test edge cases and error conditions
- Mock external dependencies appropriately

### 📝 Documentation
- Update README.md if adding new features
- Add inline code comments for complex logic
- Include usage examples for new functionality

### 🔒 Security Considerations
- Never include credentials, API keys, or sensitive data
- Validate all inputs appropriately
- Follow secure coding practices
- Report security vulnerabilities privately (see SECURITY.md)

## Issue Reporting

### 🐛 Bug Reports
Include:
- Clear description of the issue
- Steps to reproduce
- Expected vs actual behavior
- Environment details (.NET version, OS, etc.)
- Sample code if applicable

### 💡 Feature Requests
Include:
- Clear description of the proposed feature
- Use case and justification
- Proposed implementation approach (optional)
- Willingness to contribute the implementation

## Community Guidelines

### 🤝 Be Respectful
- Use welcoming and inclusive language
- Respect differing viewpoints and experiences
- Accept constructive criticism gracefully
- Focus on what's best for the community

### 💬 Communication
- Use GitHub issues for bug reports and feature requests
- Use pull request comments for code-related discussions
- Be clear and concise in communications
- Ask questions if you're unsure about anything

## Getting Help

- 📖 Check existing documentation and issues first
- 💬 Create an issue for questions or clarification
- 🔍 Review closed issues for similar problems
- 📧 Contact maintainers for security-related concerns (see SECURITY.md)

## Recognition

Contributors will be acknowledged in release notes and may be invited to join the project team for sustained, high-quality contributions.

Thank you for helping make this project better! 🎉
