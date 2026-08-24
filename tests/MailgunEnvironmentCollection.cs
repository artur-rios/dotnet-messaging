namespace ArturRios.Messaging.Tests;

/// <summary>
/// Every test that configures Mailgun does so through process-wide environment variables, which two test
/// classes running in parallel would trample on. Sharing one collection makes xUnit run them serially.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class MailgunEnvironmentCollection
{
    /// <summary>The collection name to put on every test class that touches the Mailgun environment variables.</summary>
    public const string Name = "Mailgun environment";
}
