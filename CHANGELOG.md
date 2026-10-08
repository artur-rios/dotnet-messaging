# Changelog

All notable changes to `ArturRios.Messaging` are recorded in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `MAILGUN_FROM` (`MailgunEmailService.FromVariable`) sets the sender: an address, or a display name and an address
  (`Example <no-reply@example.com>`). A value that is not exactly one address makes `SendEmailAsync` return a failed
  `ProcessOutput` naming the variable, and nothing is sent.
- `IEmailService.SendEmailAsync(to, subject, body, CancellationToken)`, matching the cancellation convention of the
  other `ArturRios` packages. It has a default implementation — check the token, then call the three-argument
  overload — so existing implementations keep compiling. `MailgunEmailService` implements it and passes the token to
  the HTTP request; an already-canceled token throws `OperationCanceledException` before anything is sent.
- XML documentation of the exceptions `SendEmailAsync` lets through: `HttpRequestException` when Mailgun cannot be
  reached, `TaskCanceledException` on an `HttpClient` timeout, `OperationCanceledException` on cancellation.

### Changed

- Without `MAILGUN_FROM`, mail is sent from `postmaster@{MAILGUN_DOMAIN}` with no display name. It was sent as
  `Mailgun Sandbox <postmaster@{MAILGUN_DOMAIN}>`, so production mail showed recipients the name "Mailgun Sandbox".
  Set `MAILGUN_FROM` to choose the sender.

### Security

- `MailgunEmailService` no longer writes the recipient address to the log. Log lines carry a pseudonymous reference
  instead (the first twelve hex characters of the SHA-256 of the trimmed, lower-cased address), under the structured
  property `RecipientRef`, which replaces `Recipient`.

## [1.3.0] - 2026-08-24

### Changed

- `MailgunEmailService.SendEmailAsync` returns a failed `ProcessOutput` naming the missing variable, and sends
  nothing, when `MAILGUN_API_KEY` or `MAILGUN_DOMAIN` is unset or blank — instead of issuing an unauthenticated
  request against an empty domain.
- Sends log the recipient at Information and the failure detail at Error, instead of a leftover
  "Testing Mailgun email service..." line and the whole response body at Information.
- When no `HttpClient` is supplied, `MailgunEmailService` uses one client shared by the process instead of creating
  a new one per instance.
- The `MailgunEmailService` constructor throws `ArgumentNullException` for a null logger.
- `ArturRios.Output` updated from 3.1.0 to 3.2.0.

### Fixed

- The Mailgun credential is attached to each request instead of the client's `DefaultRequestHeaders`, which raced
  with concurrent sends and left the key on every later request a shared (`AddHttpClient`) client made.
- Requests and responses are disposed after each send.

## [1.2.0] - 2026-08-19

### Changed

- `ArturRios.Output` updated from 2.0.1 to 3.1.0.

## [1.1.0] - 2026-07-30

### Added

- The Mailgun API version is read from the `MAILGUN_API_VERSION` environment variable, falling back to `v3` when it
  is unset or blank.
- Public constants `ApiKeyVariable`, `DomainVariable`, `ApiVersionVariable` and `DefaultMailgunApiVersion` on
  `MailgunEmailService`.
- XML documentation for every public member.

## [1.0.0] - 2026-06-21

### Added

- `IEmailService`, and `MailgunEmailService` to send plain text e-mails through the Mailgun HTTP API, configured
  through the `MAILGUN_API_KEY` and `MAILGUN_DOMAIN` environment variables and returning a `ProcessOutput` from
  `ArturRios.Output`.

[Unreleased]: https://github.com/artur-rios/dotnet-messaging/compare/1.3.0...HEAD
[1.3.0]: https://github.com/artur-rios/dotnet-messaging/compare/v1.2.0...1.3.0
[1.2.0]: https://github.com/artur-rios/dotnet-messaging/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/artur-rios/dotnet-messaging/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/artur-rios/dotnet-messaging/releases/tag/v1.0.0
