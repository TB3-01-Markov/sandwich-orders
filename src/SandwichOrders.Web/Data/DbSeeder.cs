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

    // Only when Seed:Enabled is true. The menu comes from the canteen's paper list; the ingredient stock numbers are invented demo values.
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

    // The canteen's paper list (Broodjeslijst): numbers, names and prices. Where the paper gives a description the
    // ingredients are copied from it; for plain sandwiches (e.g. "hesp") the main filling is taken from the name, and
    // a "smos" gets the same ingredients as the plain sandwich. The canteen employee should check and complete them.
    private static readonly (string En, string Nl)[] Ingredients =
    [
        ("Mayonnaise", "Mayonaise"),
        ("Egg", "Ei"),
        ("Cucumber", "Komkommer"),
        ("Tomato", "Tomaat"),
        ("Carrot", "Wortelen"),
        ("Lettuce", "Sla"),
        ("Ham", "Ham"),
        ("Cheese", "Kaas"),
        ("Goat cheese", "Romige geitenkaas"),
        ("Beetroot", "Rode biet"),
        ("Spring onion", "Lente-ajuin"),
        ("Chicken", "Kip"),
        ("Curry", "Curry"),
        ("Curry dressing", "Currydressing"),
        ("Apple", "Appeltjes"),
        ("Watercress", "Cresson"),
        ("Crab salad", "Krabsalade"),
        ("Préparé", "Préparé"),
        ("Mustard sauce", "Mosterdsaus"),
        ("Pickles", "Augurk"),
        ("Martino sauce", "Martinosaus"),
        ("Onion", "Ui"),
        ("Meat salad", "Vleessla"),
        ("Egg salad", "Eiersla"),
        ("Pesto", "Pesto"),
        ("Italian ham", "Italiaanse ham"),
        ("Mozzarella", "Mozzarella"),
        ("Sun-dried tomatoes", "Gedroogde tomaten"),
        ("Rocket", "Rucola"),
        ("Homemade tartare sauce", "Huisgemaakte tartaarsaus"),
        ("BBQ chicken", "BBQ-kip"),
        ("Brie", "Brie"),
        ("Smoked salmon", "Gerookte zalm"),
        ("Tuna salad", "Tonijnsalade"),
        ("Spicy tuna", "Tonijn pikant"),
        ("Honey", "Honing"),
        ("Nuts", "Noten"),
        ("Homemade curry dressing", "Huisgemaakte currydressing"),
        ("Piri-piri", "Pilipili"),
        ("Pineapple", "Ananas"),
        ("Herb cream cheese", "Romige kruidenkaas"),
        ("Fried chicken", "Gebakken kip")
    ];

    private record SandwichSeed(int Number, string NameEn, string NameNl, decimal Price, string[] IngredientNames);

    private static readonly SandwichSeed[] Sandwiches =
    [
        new(1, "Healthy", "Gezond", 4.00m, ["Mayonnaise", "Egg", "Cucumber", "Tomato", "Carrot", "Lettuce"]),
        new(2, "Ham", "Hesp", 4.00m, ["Ham"]),
        new(3, "Smos ham", "Smos hesp", 5.50m, ["Ham"]),
        new(4, "Cheese", "Kaas", 4.00m, ["Cheese"]),
        new(5, "Smos cheese", "Smos kaas", 5.50m, ["Cheese"]),
        new(6, "Goat cheese", "Chevre", 6.00m, ["Goat cheese", "Beetroot", "Spring onion"]),
        new(7, "Cheese and ham", "Kaas en hesp", 4.50m, ["Cheese", "Ham"]),
        new(8, "Smos cheese and ham", "Smos kaas en hesp", 6.00m, ["Cheese", "Ham"]),
        new(9, "Chicken curry", "Kip curry", 4.50m, ["Chicken", "Curry"]),
        new(10, "Smos chicken curry", "Smos kip curry", 6.00m, ["Chicken", "Curry"]),
        new(11, "Curry chicken", "Curry kip", 6.00m, ["Curry dressing", "Chicken", "Apple", "Watercress"]),
        new(12, "Crab salad", "Krabsalade", 5.00m, ["Crab salad"]),
        new(13, "Smos crab salad", "Smos krabsalade", 6.50m, ["Crab salad"]),
        new(14, "Préparé", "Préparé", 4.50m, ["Préparé"]),
        new(15, "Smos préparé", "Smos préparé", 6.00m, ["Préparé"]),
        new(16, "Club préparé", "Club préparé", 6.00m, ["Préparé", "Mustard sauce", "Pickles", "Tomato", "Egg", "Lettuce"]),
        new(17, "Martino", "Martino", 5.50m, ["Préparé", "Martino sauce", "Onion", "Pickles"]),
        new(18, "Smos martino", "Smos martino", 7.00m, ["Préparé", "Martino sauce", "Onion", "Pickles"]),
        new(19, "Meat salad", "Vleessla", 4.00m, ["Meat salad"]),
        new(20, "Smos meat salad", "Smos vleessla", 5.50m, ["Meat salad"]),
        new(21, "Egg salad", "Eiersla", 4.50m, ["Egg salad"]),
        new(22, "Smos egg salad", "Smos eiersla", 6.00m, ["Egg salad"]),
        new(23, "Italian sandwich", "Italiaans broodje", 7.00m, ["Pesto", "Italian ham", "Mozzarella", "Sun-dried tomatoes", "Rocket"]),
        new(24, "Mystique", "Mystique", 5.10m, ["Préparé", "Homemade tartare sauce", "Watercress"]),
        new(25, "BBQ chicken", "BBQ chicken", 6.50m, ["BBQ chicken"]),
        new(26, "Luigi", "Luigi", 7.50m, ["Brie", "Smoked salmon", "Onion"]),
        new(27, "Tuna salad", "Tonijnsalade", 5.00m, ["Tuna salad"]),
        new(28, "Smos tuna salad", "Smos tonijnsalade", 6.50m, ["Tuna salad"]),
        new(29, "Spicy tuna", "Tonijn pikant", 5.00m, ["Spicy tuna"]),
        new(30, "Smos spicy tuna", "Smos tonijn pikant", 6.50m, ["Spicy tuna"]),
        new(31, "Brie special", "Brie speciaal", 6.50m, ["Brie", "Honey", "Apple", "Nuts", "Lettuce"]),
        new(32, "Tropical", "Tropical", 6.00m, ["Chicken", "Homemade curry dressing", "Piri-piri", "Pineapple", "Lettuce"]),
        new(33, "Tkruimelhofke", "Tkruimelhofke", 7.50m, ["Herb cream cheese", "Smoked salmon", "Watercress"]),
        new(34, "Toscane", "Toscane", 5.50m, ["Pesto", "Mozzarella", "Tomato"]),
        new(35, "Brie", "Brie", 4.50m, ["Brie"]),
        new(36, "Smos brie", "Smos brie", 6.00m, ["Brie"]),
        new(37, "Fried chicken", "Gebakken kip", 4.50m, ["Fried chicken"]),
        new(38, "Smos fried chicken", "Smos gebakken kip", 6.00m, ["Fried chicken"]),
        new(39, "Chicken Hawaii", "Kip hawai", 5.00m, ["Chicken", "Pineapple"])
    ];

    // Baked goods that are bought ready-made, so counted per piece. The stock numbers are INVENTED demo values.
    private record SuppliedSeed(int Number, string NameEn, string NameNl, decimal Price, int Stock);

    private static readonly SuppliedSeed[] SuppliedSandwiches =
    [
        new(40, "Sausage roll", "Worstenbroodje", 3.00m, 10),
        new(41, "Rice tart", "Rijsttaartje", 2.95m, 8)
    ];
}
