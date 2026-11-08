using Aspire.Hosting.Publishing;

namespace UserOrg.AppHost;

/// <summary>
/// Provides a worktree-safe default value for AppHost parameters, allowing CLI and config overrides to win.
/// </summary>
/// <remarks>
/// A parameter left without one sits in ValueMissing until the dashboard's unresolved-parameter dialog is
/// answered, and every resource wired to it waits that long - the gateway among them. Declaring the default
/// only as the dialog's prefill is not enough: that is a prefill, not a value.
/// </remarks>
internal sealed class RuntimeDefault(string value) : ParameterDefault
{
    public override string GetDefaultValue() => value;

    public override void WriteToManifest(ManifestPublishingContext context) =>
        context.Writer.WriteString("value", value);
}