using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;

namespace SandwichOrders.Web.Models;

public class MenuViewModel
{
    public IReadOnlyList<Sandwich> Sandwiches { get; init; } = Array.Empty<Sandwich>();
    public OrderingWindow Window { get; init; } = new(OrderingRules.DefaultDeadline, true);
    public Order? TodaysOrder { get; init; }
    public bool IsSignedIn { get; init; }
    public string GuestName { get; init; } = "";

    // US-09: the ingredient filter. Sandwiches holds only the sandwiches that match; TotalCount is the whole menu.
    public IReadOnlyList<Ingredient> Ingredients { get; init; } = Array.Empty<Ingredient>();
    public IReadOnlyCollection<int> Include { get; init; } = Array.Empty<int>();
    public IReadOnlyCollection<int> Exclude { get; init; } = Array.Empty<int>();
    public int TotalCount { get; init; }
    public bool IsFiltered => Include.Count > 0 || Exclude.Count > 0;
}

public class TodayOrderViewModel
{
    public Order? Order { get; init; }
    public OrderingWindow Window { get; init; } = new(OrderingRules.DefaultDeadline, true);
}

public class StaffMenuViewModel
{
    public IReadOnlyList<Sandwich> Sandwiches { get; init; } = Array.Empty<Sandwich>();
    public IReadOnlyList<Ingredient> Ingredients { get; init; } = Array.Empty<Ingredient>();
}

public class DeadlineViewModel
{
    [Required(ErrorMessage = "Please enter a valid time.")]
    public TimeOnly? Deadline { get; set; }
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "The name is too long.")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

public class SandwichEditViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "The number is required.")]
    [Range(1, 9999, ErrorMessage = "The number must be between 1 and 9999.")]
    public int? Number { get; set; }

    [Required(ErrorMessage = "The English name is required.")]
    [StringLength(100, ErrorMessage = "The name is too long.")]
    public string? NameEn { get; set; }

    [Required(ErrorMessage = "The Dutch name is required.")]
    [StringLength(100, ErrorMessage = "The name is too long.")]
    public string? NameNl { get; set; }

    [StringLength(500, ErrorMessage = "The description is too long.")]
    public string? DescriptionEn { get; set; }

    [StringLength(500, ErrorMessage = "The description is too long.")]
    public string? DescriptionNl { get; set; }

    // Text, not decimal: the model binder would read "4.20" as 420 in a Dutch page. Money.TryParse handles both.
    [Required(ErrorMessage = "The price is required.")]
    public string? PriceText { get; set; }

    public List<int> IngredientIds { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public string? ExistingImagePath { get; set; }
    public bool RemovePhoto { get; set; }
    public IFormFile? Photo { get; set; }

    [BindNever, ValidateNever]
    public IReadOnlyList<Ingredient> AllIngredients { get; set; } = Array.Empty<Ingredient>();
}
