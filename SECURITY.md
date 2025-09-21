# Security Policy

## Supported Versions

We release patches for security vulnerabilities. Which versions are eligible for receiving such patches depends on the CVSS v3.0 Rating:

| Version | Supported          |
| ------- | ------------------ |
| 5.51.x  | :white_check_mark: |
| < 5.51  | :x:                |

## Reporting a Vulnerability

We take the security of Vidyano.Core seriously. If you have discovered a security vulnerability in our project, please report it to us as described below.

### Reporting Process

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, please report them via email to **security@vidyano.com**.

You should receive a response within 48 hours. If for some reason you do not, please follow up via email to ensure we received your original message.

Please include the following information in your report:

- Type of issue (e.g., buffer overflow, SQL injection, cross-site scripting, etc.)
- Full paths of source file(s) related to the manifestation of the issue
- The location of the affected source code (tag/branch/commit or direct URL)
- Any special configuration required to reproduce the issue
- Step-by-step instructions to reproduce the issue
- Proof-of-concept or exploit code (if possible)
- Impact of the issue, including how an attacker might exploit the issue

This information will help us triage your report more quickly.

### Preferred Languages

We prefer all communications to be in English.

### Disclosure Policy

When we receive a security bug report, we will:

1. Confirm the problem and determine the affected versions
2. Audit code to find any potential similar problems
3. Prepare fixes for all releases still under maintenance
4. Release new security fix versions

## Security Best Practices

When using Vidyano.Core in your applications, we recommend following these security best practices:

### Authentication & Authorization

- Always use HTTPS when connecting to Vidyano services
- Store credentials securely using appropriate credential management systems
- Never hard-code credentials in your source code
- Implement proper session management and timeout policies
- Use strong passwords and consider implementing multi-factor authentication

### Data Protection

- Encrypt sensitive data at rest and in transit
- Validate and sanitize all user inputs
- Implement proper error handling to avoid information leakage
- Follow the principle of least privilege for data access

### API Security

- Use API keys or tokens for authentication
- Implement rate limiting to prevent abuse
- Monitor API usage for suspicious patterns
- Keep the Vidyano.Core library updated to the latest version

### Dependencies

- Regularly update all dependencies to their latest secure versions
- Monitor security advisories for dependencies
- Use tools like `dotnet list package --vulnerable` to check for known vulnerabilities

## Security Updates

Security updates will be released as new patch versions. We recommend all users upgrade to the latest version as soon as possible after a security update is released.

You can monitor security updates through:
- GitHub Security Advisories
- NuGet package updates
- Our mailing list (subscribe at security@vidyano.com)

## Additional Resources

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [.NET Security Best Practices](https://docs.microsoft.com/en-us/dotnet/standard/security/)
- [GitHub Security Features](https://docs.github.com/en/code-security)

## Contact

For any security-related questions that don't require reporting a vulnerability, please contact us at support@vidyano.com.

Thank you for helping to keep Vidyano.Core and its users safe!