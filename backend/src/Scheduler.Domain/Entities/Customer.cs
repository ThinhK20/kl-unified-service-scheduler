using Scheduler.Domain.Common;

namespace Scheduler.Domain.Entities;

/// <summary>A person who books services for their vehicles.</summary>
public sealed class Customer : Entity
{
    private Customer()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public static Customer Create(string name, string email, string phone) => new()
    {
        Name = Guard.NotBlank(name, nameof(Name)),
        Email = Guard.NotBlank(email, nameof(Email)),
        Phone = Guard.NotBlank(phone, nameof(Phone)),
    };
}
