# Contributing to Vidyano.Core

We love your input! We want to make contributing to Vidyano.Core as easy and transparent as possible, whether it's:

- Reporting a bug
- Discussing the current state of the code
- Submitting a fix
- Proposing new features
- Becoming a maintainer

## We Develop with Github
We use GitHub to host code, to track issues and feature requests, as well as accept pull requests.

## We Use [Github Flow](https://guides.github.com/introduction/flow/index.html)
Pull requests are the best way to propose changes to the codebase. We actively welcome your pull requests:

1. Fork the repo and create your branch from `main`.
2. If you've added code that should be tested, add tests.
3. If you've changed APIs, update the documentation.
4. Ensure the test suite passes.
5. Make sure your code follows the existing style.
6. Issue that pull request!

## Any contributions you make will be under the MIT Software License
In short, when you submit code changes, your submissions are understood to be under the same [MIT License](http://choosealicense.com/licenses/mit/) that covers the project. Feel free to contact the maintainers if that's a concern.

## Report bugs using Github's [issues](https://github.com/2sky/Vidyano.Core/issues)
We use GitHub issues to track public bugs. Report a bug by [opening a new issue](https://github.com/2sky/Vidyano.Core/issues/new); it's that easy!

## Write bug reports with detail, background, and sample code

**Great Bug Reports** tend to have:

- A quick summary and/or background
- Steps to reproduce
  - Be specific!
  - Give sample code if you can
- What you expected would happen
- What actually happens
- Notes (possibly including why you think this might be happening, or stuff you tried that didn't work)

## Development Process

### Prerequisites
- .NET SDK 8.0 or later
- Visual Studio 2022, Visual Studio Code, or JetBrains Rider
- Git

### Setting up your development environment

1. Clone the repository:
```bash
git clone https://github.com/2sky/Vidyano.Core.git
cd Vidyano.Core
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Build the solution:
```bash
dotnet build
```

4. Run tests:
```bash
dotnet test
```

5. Run the demo application:
```bash
cd Demo
dotnet run
```

### Code Style

- Follow C# coding conventions
- Use 4 spaces for indentation (not tabs)
- Keep lines under 120 characters when possible
- Use meaningful variable and method names
- Add XML documentation comments to public APIs
- Follow the existing code style in the project

### Testing

- Write unit tests for new functionality
- Ensure all tests pass before submitting a PR
- Aim for good code coverage, especially for critical paths
- Test against both .NET Standard 2.0 and .NET 8.0 targets

### Commit Messages

- Use clear and meaningful commit messages
- Start with a verb in the imperative mood (e.g., "Add", "Fix", "Update")
- Keep the first line under 50 characters
- Add a blank line and then a more detailed description if needed

Example:
```
Add support for custom authentication headers

- Implement IAuthenticationProvider interface
- Add unit tests for custom auth scenarios
- Update documentation with usage examples
```

### Pull Request Process

1. Update the README.md with details of changes if applicable
2. Update the CLAUDE.md file if you've made architectural changes
3. Increase the version numbers in any examples files and the README.md to the new version that this Pull Request would represent
4. You may merge the Pull Request once you have the sign-off of at least one other developer, or if you do not have permission to do that, you may request the reviewer to merge it for you

## Community

- Be respectful and inclusive
- Welcome newcomers and help them get started
- Provide constructive feedback
- Focus on what is best for the community
- Show empathy towards other community members

## Questions?

Feel free to open an issue with your question or contact us at support@vidyano.com.

## License
By contributing, you agree that your contributions will be licensed under its MIT License.

## References
This document was adapted from the open-source contribution guidelines for [Facebook's Draft](https://github.com/facebook/draft-js/blob/master/CONTRIBUTING.md)