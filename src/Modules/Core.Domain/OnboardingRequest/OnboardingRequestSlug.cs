using System.Text.Json.Serialization;

namespace Core.Domain.OnboardingRequest;

/// <summary>
/// GUID-free public identifier an onboarding request is addressed by (ADR-0024). Wraps the
/// <see cref="New"/> generator rather than coupling to the shared <c>Request</c> aggregate.
/// Format <c>req-{e3}-{shortname}-{r6}</c>: <c>e3</c> is the first three <c>[a-z0-9]</c> of the requester's
/// email local part (lower-cased), <c>shortname</c> the project short name (lower-cased), and <c>r6</c> six
/// random base36 characters disambiguating repeat requests for the same requester+project. The random
/// suffix means the caller must regenerate-and-retry on the (rare) unique-index collision.
/// </summary>
[JsonConverter(typeof(ValueObjectJsonConverter<OnboardingRequestSlug, string>))]
public readonly record struct OnboardingRequestSlug(string Value) : IValueObject<OnboardingRequestSlug, string>
{
    public const int MaxLength = 120;

    private const string Base36 = "abcdefghijklmnopqrstuvwxyz0123456789";

    static OnboardingRequestSlug IValueObject<OnboardingRequestSlug, string>.From(string value) => new(value);

    public static OnboardingRequestSlug Create(string value) =>
        TryCreate(value, out var result, out var error)
            ? result
            : throw new ArgumentException(error, nameof(value));

    /// <summary>Validates and constructs an <see cref="OnboardingRequestSlug"/> without throwing.</summary>
    public static bool TryCreate(string? value, out OnboardingRequestSlug result, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            error = "OnboardingRequestSlug cannot be empty";
            return false;
        }

        if (value.Length > MaxLength)
        {
            result = default;
            error = $"OnboardingRequestSlug must not exceed {MaxLength} characters";
            return false;
        }

        result = new OnboardingRequestSlug(value.Trim());
        error = null;
        return true;
    }

    public static OnboardingRequestSlug From(string value) => Create(value);

    /// <summary>
    /// Generates a slug for a requester+project (mirrors the v2 <c>RequestSlug</c> algorithm, ADR-0024).
    /// </summary>
    public static OnboardingRequestSlug New(Email email, ProjectShortName projectShortName)
    {
        var local = email.Value.Split('@', 2)[0].ToLowerInvariant();
        var e3 = new string(local.Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')).Take(3).ToArray());
        var shortName = projectShortName.Value.ToLowerInvariant();
        return new OnboardingRequestSlug($"req-{e3}-{shortName}-{Random6()}");
    }

    private static string Random6()
    {
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Base36[Random.Shared.Next(Base36.Length)];
        return new string(chars);
    }

    public override string ToString() => Value;
}