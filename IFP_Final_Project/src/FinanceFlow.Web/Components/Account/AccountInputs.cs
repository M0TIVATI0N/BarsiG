using System.ComponentModel.DataAnnotations;

namespace FinanceFlow.Web.Components.Account;

/// <summary>The model of the sign in form.</summary>
public sealed class LoginInput
{
    /// <summary>The e-mail address or international phone number of the user.</summary>
    [Required(ErrorMessage = "Enter your e-mail address.")]
    public string Identifier { get; set; } = string.Empty;

    /// <summary>The password of the user.</summary>
    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>The model of the registration form.</summary>
public sealed class RegisterInput
{
    /// <summary>The display name of the new user.</summary>
    [Required(ErrorMessage = "Enter your name.")]
    [StringLength(60, ErrorMessage = "The name must not be longer than 60 characters.")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The optional e-mail address of the new user.</summary>
    [EmailAddress(ErrorMessage = "Enter a valid e-mail address.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>The required Kazakhstan mobile number of the new user.</summary>
    [Required(ErrorMessage = "Enter your Kazakhstan mobile number.")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>The password of the new user.</summary>
    [Required(ErrorMessage = "Enter a password.")]
    [StringLength(8, MinimumLength = 8, ErrorMessage = "The password must contain at least 8 characters.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>The repeated password of the new user.</summary>
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
