# Security

## Reporting

Please report suspected security issues privately through GitHub's security
advisory feature instead of opening a public issue.

## Design boundaries

- The application does not make network requests.
- It does not require administrator elevation.
- It does not bundle or modify the Mad Catz driver.
- It reads the installed vendor assembly from SmartTechnology.
- Local history contains battery readings and timestamps only.

Never include access tokens, credentials, or unredacted personal paths in an
issue or pull request.
