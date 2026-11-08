namespace Heappe.Features.Credentials;

public sealed record HeappeCredential()
{
    public string SecretApiKey
    {
        get;
        init
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("API key cannot be null or empty.", nameof(SecretApiKey));
            }

            field = value.Trim();
        }
    }
}