﻿//using SourceCrafter.Mapping.Attributes;
//using SourceCrafter.Mapping.Constants;

using SourceCrafter.Bindings.Attributes;
using SourceCrafter.Bindings.UnitTests;

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace SourceCrafter.UnitTests;

public partial class User : IUserPerson2, IContactableUser //: IUser
{
    internal string FullName
    {
        get => $"{LastName?.Trim()}, {FirstName?.Trim()}";
        set => 
            (FirstName, LastName) = value?.Split(", ") switch
            {
                [{ } lastName, { } firstName] => (firstName.Trim(), lastName.Trim()),
                [{ } firstName] => (firstName.Trim(), null!),
                _ => (null!, null!)
            };
    }
    public string FirstName { get; set; } = null!;
    public string? LastName { get; set; }
    public int Age { get; set; }
    public string? Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
    [Bind(nameof(UserDto.TotalAmount))]
    public double? Balance { get; set; }
    [Max(2)]
    public IEnumerable<User> Asignees { get; set; } = [];
    public Role MainRole { get; set; }
    public User? Supervisor { get; init; }
    public (string, object)[] ExtendedProperties { get; init; } = [];
    public string[] Phrases { get; set; } = [];
    public Status Status { get; }
    public IEmail? MainEmail { get; set; }
    public IPhone? MainPhone { get; set; }
    //IEmail? IContactableUser.MainEmail { get => MainEmail; set => MainEmail = (Email?)value; }
    //IPhone? IContactableUser.MainPhone { get => MainPhone; set => MainPhone = (Phone?)value; }

    public bool IsAvailable { get; set; }
    public Guid GlobalId { get; set; }

    public List<IContact> Contacts { get; } = [];
    public Person? Person { get; set; }
    //string IUser.FullName { get => FullName; set => FullName = value; }
}
public partial class User
{
    public int Count { get; set; }

}

public record struct Email(ContactType ContactType, string Value) : IEmail;
public record struct Phone(ContactType ContactType, int CountryCode, string Value) : IPhone;


public interface IPhone : IContact
{
    int CountryCode { get; set; }
    new ContactType ContactType => ContactType.Phone;
    static T Create<T>(int code, string value) where T : IPhone, new() => new() { Value = value, CountryCode = code };
}
public interface IEmail : IContact
{
    new ContactType ContactType => ContactType.Email;
    static T Create<T>(string value) where T : IEmail, new() => new() { Value = value };
}
public interface IContact
{
    string Value { get; set; }
    ContactType ContactType { get; }
}
public interface IUserPerson2
{
    Person? Person { get; set; }
}
public class Person : IPerson
{
}
public interface IPerson
{
}

public interface IContactableUser
{
    IEmail? MainEmail { get; set; }
    IPhone? MainPhone { get; set; }
    List<IContact> Contacts { get; }
}

[JsonConverter(typeof(JsonNumberEnumConverter<byte>))]
public enum ContactType
{
    Email,
    Phone
}