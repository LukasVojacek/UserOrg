using System.Net.Mail;

using UserOrganizationManagement.Domain.Exceptions;

namespace UserOrganizationManagement.Domain.ValueObjects;

public readonly record struct EmailAddress()
{
    public MailAddress? Value { get; }

    private EmailAddress(string emailAddress) : this()
    {
        if (!MailAddress.TryCreate(emailAddress, out MailAddress? email))
        {
            throw new InvalidEmailAddressException(emailAddress);
        }

        Value = email;
    }

    public static EmailAddress Create(string emailAddress) => new(emailAddress);
}