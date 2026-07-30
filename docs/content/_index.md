+++
title = 'Dotnet Messaging'
+++

# Documentation

Utilities for different messaging formats and protocols for .NET applications.
Right now, the library includes a Mailgun email service, but more features and protocols may be added in the future, as well as support for more email providers.

Contributions are welcome!

## Installation

Install the package via the .NET CLI:

```bash
dotnet add package ArturRios.Messaging
```

Or via the NuGet Package Manager:

```powershell
Install-Package ArturRios.Messaging
```

## Requirements

- .NET 10.0 or later
- Environment variables for service configuration (see [Configuration](#configuration))

## Dependencies

| Package | Purpose |
|---|---|
| [ArturRios.Output](https://www.nuget.org/packages/ArturRios.Output) | Structured operation result type (`ProcessOutput`) |

## Features

- **Email** — send transactional emails via [Mailgun](https://www.mailgun.com/) with a clean async interface
- Designed for dependency injection — register services through the standard `IServiceCollection` pattern
- Returns structured `ProcessOutput` results (from [ArturRios.Output](https://www.nuget.org/packages/ArturRios.Output)) rather than throwing exceptions, making error handling predictable

## Configuration

### Mailgun Email Service

Set the following environment variables before calling `SendEmailAsync`:

| Variable | Required | Default | Description |
|---|---|---|---|
| `MAILGUN_API_KEY` | Yes | — | Your Mailgun private API key |
| `MAILGUN_DOMAIN` | Yes | — | Your verified Mailgun sending domain |
| `MAILGUN_API_VERSION` | No | `v3` | Mailgun API version used to build the request URL. When unset or blank, `v3` is used |

Requests are sent to `https://api.mailgun.net/{MAILGUN_API_VERSION}/{MAILGUN_DOMAIN}/messages`.
Environment variables are read on every `SendEmailAsync` call, so changes take effect without recreating the service.

## Usage

### Dependency Injection

Register `MailgunEmailService` with your DI container:

```csharp
using ArturRios.Messaging.Email;

builder.Services.AddHttpClient<IEmailService, MailgunEmailService>();
```

### Sending an Email

```csharp
using ArturRios.Messaging.Email;

public class NotificationService(IEmailService emailService)
{
    public async Task NotifyAsync(string recipient)
    {
        var output = await emailService.SendEmailAsync(
            to: recipient,
            subject: "Welcome!",
            body: "Thanks for signing up."
        );

        if (!output.Success)
        {
            foreach (var error in output.Errors)
                Console.WriteLine(error);
        }
    }
}
```

### Without Dependency Injection

```csharp
using ArturRios.Messaging.Email;
using Microsoft.Extensions.Logging.Abstractions;

var service = new MailgunEmailService(NullLogger<MailgunEmailService>.Instance);
var output = await service.SendEmailAsync("to@example.com", "Hello", "World");
```

## Class Diagram

```mermaid
classDiagram
    class IEmailService {
        <<interface>>
        +SendEmailAsync(to: string, subject: string, body: string) Task~ProcessOutput~
    }

    class MailgunEmailService {
        -ILogger~MailgunEmailService~ _logger
        -HttpClient _httpClient
        -string MailgunApiBaseUrl$
        -string MailgunMessagesEndpoint$
        +string ApiKeyVariable$
        +string DomainVariable$
        +string ApiVersionVariable$
        +string DefaultMailgunApiVersion$
        +MailgunEmailService(logger: ILogger~MailgunEmailService~, httpClient: HttpClient?)
        +SendEmailAsync(to: string, subject: string, body: string) Task~ProcessOutput~
        -GetApiVersion() string$
    }

    class ProcessOutput {
        <<ArturRios.Output>>
        +bool Success
        +IEnumerable~string~ Errors
        +AddError(message: string) void
    }

    IEmailService <|.. MailgunEmailService : implements
    MailgunEmailService ..> ProcessOutput : returns
```

## Versioning

Semantic Versioning (SemVer). Breaking changes result in a new major version. New methods or non-breaking behavior
changes increment the minor version; fixes or tweaks increment the patch.

## Build, test and publish

Use the official [.NET CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/) to build, test and publish the project and Git for source control.
If you want, optional helper toolsets I built to facilitate these tasks are available:

- [Dotnet Tools](https://github.com/artur-rios/dotnet-tools)
- [Python Dotnet Tools](https://github.com/artur-rios/python-dotnet-tools)

## Legal Details

This project is licensed under the [MIT License](https://en.wikipedia.org/wiki/MIT_License). A copy of the license is available at [LICENSE](https://github.com/artur-rios/dotnet-messaging/blob/main/LICENSE) in the repository.
