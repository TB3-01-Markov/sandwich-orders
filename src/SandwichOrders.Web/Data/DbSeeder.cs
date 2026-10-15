using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SandwichOrders.Core;
using SandwichOrders.Core.Menu;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.Data;

public static class DbSeeder
{
    // Always runs: registration needs the Customer role to exist.
    public static async Task EnsureRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in new[] { Roles.Customer, Roles.Staff })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    // Only when Seed:Enabled is true. All data below is INVENTED sample data, not the real canteen menu.
    public static async Task SeedDemoDataAsync(IServiceProvider services, IConfiguration configuration)
    {
        var db = services.GetRequiredService<AppDbContext>();

        if (!await db.Ingredients.AnyAsync())
        {
            var ingredients = Ingredients.ToDictionary(
                pair => pair.En,
                pair => new Ingredient { NameEn = pair.En, NameNl = pair.Nl });
            db.Ingredients.AddRange(ingredients.Values);

            // A few counted ingredients, so the stock feature is visible in the demo. The rest is not counted.
            ingredients["Cheese"].SetStock(20);
            ingredients["Ham"].SetStock(12);
            ingredients["Smoked salmon"].SetStock(3);

            db.Sandwiches.AddRange(Sandwiches.Select(s => new Sandwich
            {
                Number = s.Number,
                NameEn = s.NameEn,
                NameNl = s.NameNl,
                Price = s.Price,
                SandwichIngredients = s.IngredientNames
                    .Select(name => new SandwichIngredient { Ingredient = ingredients[name] })
                    .ToList()
            }));

            // Delivered ready-made by a supplier: counted per piece instead of per ingredient.
            foreach (var seed in SuppliedSandwiches)
            {
                var supplied = new Sandwich
                {
                    Number = seed.Number,
                    NameEn = seed.NameEn,
                    NameNl = seed.NameNl,
                    Price = seed.Price,
                    Kind = SandwichKind.Supplied
                };
                supplied.SetStock(seed.Stock);
                db.Sandwiches.Add(supplied);
            }

            await db.SaveChangesAsync();
        }

        await SeedStaffUserAsync(services, configuration);
    }

    private static async Task SeedStaffUserAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["Seed:StaffEmail"];
        var password = configuration["Seed:StaffPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var staff = new ApplicationUser { UserName = email, Email = email, DisplayName = "Canteen staff" };
        var created = await users.CreateAsync(staff, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the demo staff user: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(staff, Roles.Staff);
    }

    private static readonly (string En, string Nl)[] Ingredients =
    [
        ("Ham", "Ham"), ("Cheese", "Kaas"), ("Chicken", "Kip"), ("Tuna", "Tonijn"), ("Egg", "Ei"),
        ("Lettuce", "Sla"), ("Tomato", "Tomaat"), ("Cucumber", "Komkommer"), ("Carrot", "Wortel"),
        ("Onion", "Ui"), ("Mayonnaise", "Mayonaise"), ("Curry sauce", "Currysaus"), ("Brie", "Brie"),
        ("Honey", "Honing"), ("Salami", "Salami"), ("Pickles", "Augurk"), ("Smoked salmon", "Gerookte zalm"),
        ("Cream cheese", "Roomkaas"), ("Turkey", "Kalkoen"), ("Hummus", "Hummus"), ("Mozzarella", "Mozzarella"),
        ("Pesto", "Pesto"), ("Bacon", "Spek"), ("Mustard", "Mosterd")
    ];

    private record SandwichSeed(int Number, string NameEn, string NameNl, decimal Price, string[] IngredientNames);

    private static readonly SandwichSeed[] Sandwiches =
    [
        new(1, "Ham & cheese", "Ham en kaas", 4.20m, ["Ham", "Cheese", "Lettuce", "Tomato"]),
        new(2, "Chicken curry", "Kip curry", 4.60m, ["Chicken", "Curry sauce", "Lettuce", "Carrot"]),
        new(3, "Tuna crunch", "Tonijnsla", 4.50m, ["Tuna", "Mayonnaise", "Onion", "Lettuce", "Cucumber"]),
        new(4, "Brie & honey", "Brie en honing", 4.80m, ["Brie", "Honey", "Lettuce"]),
        new(5, "Egg mayo", "Eiersla", 3.90m, ["Egg", "Mayonnaise", "Lettuce", "Tomato"]),
        new(6, "Salami special", "Salami speciaal", 4.30m, ["Salami", "Cheese", "Pickles", "Mustard"]),
        new(7, "Smoked salmon", "Gerookte zalm", 5.80m, ["Smoked salmon", "Cream cheese", "Cucumber"]),
        new(8, "Turkey club", "Kalkoen club", 5.20m, ["Turkey", "Bacon", "Lettuce", "Tomato", "Mayonnaise"]),
        new(9, "Veggie hummus", "Veggie hummus", 4.10m, ["Hummus", "Carrot", "Cucumber", "Tomato", "Lettuce"]),
        new(10, "Mozzarella pesto", "Mozzarella pesto", 4.70m, ["Mozzarella", "Pesto", "Tomato"]),
        new(11, "Chicken pesto", "Kip pesto", 5.00m, ["Chicken", "Pesto", "Mozzarella", "Lettuce"]),
        new(12, "Cheese & pickles", "Kaas en augurk", 3.60m, ["Cheese", "Pickles", "Lettuce"]),
        new(13, "Bacon & egg", "Spek en ei", 4.90m, ["Bacon", "Egg", "Tomato"]),
        new(14, "Ham & egg", "Ham en ei", 4.40m, ["Ham", "Egg", "Mayonnaise", "Lettuce"]),
        new(15, "Classic cheese", "Klassieke kaas", 3.50m, ["Cheese", "Lettuce", "Tomato", "Cucumber"])
    ];

    private record SuppliedSeed(int Number, string NameEn, string NameNl, decimal Price, int Stock);

    private static readonly SuppliedSeed[] SuppliedSandwiches =
    [
        new(16, "Chicken wrap (supplier)", "Kipwrap (leverancier)", 5.20m, 6),
        new(17, "Vegan bagel (supplier)", "Vegan bagel (leverancier)", 4.90m, 4)
    ];
}
