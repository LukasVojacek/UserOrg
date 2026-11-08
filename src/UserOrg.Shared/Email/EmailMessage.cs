namespace UserOrg.Shared.Email;

/// <summary>
/// The content of an email in template terms rather than rendered HTML — the sender resolves
/// <see cref="TemplateName"/> and <see cref="TemplateArguments"/> against the mail template store.
/// Addresses are plain strings so that this project keeps no references of its own.
/// </summary>
/// <param name="TemplateName">
/// Name of the template to render, as the mail template store knows it (for example
/// <c>RequestState</c>). An unknown name fails at the transport, not here.
/// </param>
/// <param name="Receivers">Primary recipient addresses.</param>
/// <param name="TemplateArguments">
/// Arguments filled into the template's placeholders by position — the first element fills the
/// template's first slot, the second its second, and so on. The template declares how many slots
/// it has, so callers must pass exactly that many in that order; nothing here validates the count
/// or the order.
/// </param>
/// <param name="Cc">Carbon-copy addresses; <see langword="null"/> means none.</param>
/// <param name="Priority">
/// Passed through to the sending transport unchanged and never interpreted here; <c>1</c> is what
/// every current caller sends.
/// </param>
public sealed record EmailMessage(
    string TemplateName,
    IReadOnlyList<string> Receivers,
    IReadOnlyList<string> TemplateArguments,
    IReadOnlyList<string>? Cc = null,
    int Priority = 1);