using System.Text.RegularExpressions;
using Project.Domain.Exceptions;

namespace Project.Domain.ValueObjects;

public sealed partial record Email
{
    public string Value { get; }

    private Email(string value) => Value = value;

    public static Email Create(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email cannot be empty.");

        if (!EmailRegex().IsMatch(email))
            throw new DomainException($"Invalid email format: {email}");

        return new Email(email.ToLowerInvariant());
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    public override string ToString() => Value;
}
