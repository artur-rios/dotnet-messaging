namespace ArturRios.Messaging.Email;

/// <summary>
/// One plain text e-mail, with the sender and reply-to it should carry.
/// </summary>
/// <param name="To">Recipient e-mail address.</param>
/// <param name="Subject">Subject line of the e-mail.</param>
/// <param name="Body">Plain text body of the e-mail.</param>
public sealed record EmailMessage(string To, string Subject, string Body)
{
    /// <summary>
    /// The sender of this message: an address, or a display name and an address
    /// (<c>Example &lt;no-reply@example.com&gt;</c>). When <see langword="null"/>, the service's own default sender is
    /// used.
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// The address replies to this message should go to. When <see langword="null"/>, the message has no reply-to.
    /// </summary>
    public string? ReplyTo { get; init; }
}
