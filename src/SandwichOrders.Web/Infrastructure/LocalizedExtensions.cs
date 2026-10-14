using System.Globalization;
using Microsoft.Extensions.Localization;
using SandwichOrders.Core;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;

namespace SandwichOrders.Web.Infrastructure;

// Small helpers so the views can write @sandwich.Name() and @L.BreadName(bread).
public static class LocalizedExtensions
{
    private static string Culture => CultureInfo.CurrentUICulture.Name;

    public static string Name(this Sandwich sandwich) => sandwich.NameFor(Culture);

    public static string Description(this Sandwich sandwich) => sandwich.DescriptionFor(Culture);

    public static string Name(this Ingredient ingredient) => ingredient.NameFor(Culture);

    public static string BreadName(this IStringLocalizer localizer, Bread bread) =>
        bread == Bread.White ? localizer["White bread"].Value : localizer["Whole-grain bread"].Value;

    public static string StatusName(this IStringLocalizer localizer, OrderStatus status) =>
        localizer[status.ToString()].Value;

    // Turns a business-rule code into a message the user can read.
    public static string Describe(this IStringLocalizer localizer, OrderingError error) => error switch
    {
        OrderingError.DeadlinePassed => localizer["The order deadline has passed."].Value,
        OrderingError.AlreadyOrdered => localizer["You already have an order for today."].Value,
        OrderingError.SandwichUnavailable => localizer["This sandwich is not available."].Value,
        OrderingError.OrderNotFound => localizer["You have no order for today."].Value,
        OrderingError.SandwichNotFound => localizer["This sandwich does not exist."].Value,
        OrderingError.IngredientNotFound => localizer["This ingredient does not exist."].Value,
        _ => localizer["Something went wrong."].Value
    };
}

public static class MenuMessages
{
    public static string Describe(this IStringLocalizer localizer, MenuError error) => error switch
    {
        MenuError.SandwichNotFound => localizer["This sandwich does not exist."].Value,
        MenuError.NumberTaken => localizer["This sandwich number is already used (use a number of 1 or higher)."].Value,
        MenuError.NameRequired => localizer["Both names are required (at most 100 characters) and descriptions can have at most 500 characters."].Value,
        MenuError.InvalidPrice => localizer["Enter a price above 0 with at most two decimals, for example 4.50."].Value,
        MenuError.UnknownIngredient => localizer["One of the selected ingredients does not exist."].Value,
        MenuError.IngredientNameRequired => localizer["Both ingredient names are required (at most 100 characters)."].Value,
        MenuError.IngredientExists => localizer["This ingredient already exists."].Value,
        _ => localizer["Something went wrong."].Value
    };

    public static string Describe(this IStringLocalizer localizer, PhotoError error) => error switch
    {
        PhotoError.TooLarge => localizer["The photo is larger than 2 MB."].Value,
        PhotoError.WrongType => localizer["The photo must be a JPG, PNG or WebP image."].Value,
        _ => localizer["Something went wrong."].Value
    };
}

public static class Money
{
    // Accepts "4.5", "4,50" and "4.50" whatever the language of the page is. Thousands separators are not allowed.
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0;
        return !string.IsNullOrWhiteSpace(text)
               && decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture, out amount);
    }

    // Euro amount with the decimal separator of the current language: 4.20 in English, 4,20 in Dutch.
    public static string Format(decimal amount) => "€" + amount.ToString("0.00", CultureInfo.CurrentCulture);
}
