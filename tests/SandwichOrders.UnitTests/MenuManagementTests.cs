using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SandwichOrders.Core.Menu;

namespace SandwichOrders.UnitTests;

// The rules for adding and changing sandwiches (menu management for the canteen employee).
public class MenuManagementTests
{
    private readonly IMenuRepository repository = Substitute.For<IMenuRepository>();
    private readonly MenuService service;

    public MenuManagementTests()
    {
        service = new MenuService(repository, NullLogger<MenuService>.Instance);
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Sandwich>
        {
            new() { Id = 1, Number = 1, NameEn = "Ham", NameNl = "Ham", Price = 4m }
        });
        repository.GetIngredientsAsync(Arg.Any<CancellationToken>()).Returns(new List<Ingredient>
        {
            new() { Id = 10, NameEn = "Ham", NameNl = "Ham" },
            new() { Id = 11, NameEn = "Cheese", NameNl = "Kaas" }
        });
    }

    private static SandwichInput Input(
        int? id = null, int number = 2, string nameEn = "Cheese", string nameNl = "Kaas",
        decimal price = 3.5m, int[]? ingredients = null, bool keepImage = true, string? image = null) =>
        new(id, number, nameEn, nameNl, "desc", "beschrijving", price, ingredients ?? new[] { 11 }, true, keepImage, image);

    [Fact]
    public async Task ANewSandwichIsAddedWithTrimmedNamesAndItsIngredients()
    {
        var saved = await service.SaveSandwichAsync(Input(nameEn: "  Cheese  ", ingredients: new[] { 10, 11, 11 }));

        repository.Received(1).Add(Arg.Is<Sandwich>(s => s.NameEn == "Cheese" && s.Number == 2 && s.Price == 3.5m));
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(new[] { 10, 11 }, saved.Sandwich.SandwichIngredients.Select(i => i.IngredientId).Order());
    }

    [Theory]
    [InlineData("", "Kaas")]
    [InlineData("Cheese", "   ")]
    public async Task ABlankNameIsRefused(string nameEn, string nameNl)
    {
        var error = await Assert.ThrowsAsync<MenuException>(() => service.SaveSandwichAsync(Input(nameEn: nameEn, nameNl: nameNl)));

        Assert.Equal(MenuError.NameRequired, error.Error);
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATooLongDescriptionIsRefusedWithItsOwnReason()
    {
        var input = Input() with { DescriptionEn = new string('x', MenuLimits.MaxDescriptionLength + 1) };

        var error = await Assert.ThrowsAsync<MenuException>(() => service.SaveSandwichAsync(input));

        Assert.Equal(MenuError.DescriptionTooLong, error.Error);
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("3.555")]
    public async Task AnInvalidPriceIsRefused(string priceText)
    {
        var price = decimal.Parse(priceText, System.Globalization.CultureInfo.InvariantCulture);

        var error = await Assert.ThrowsAsync<MenuException>(() => service.SaveSandwichAsync(Input(price: price)));

        Assert.Equal(MenuError.InvalidPrice, error.Error);
    }

    [Fact]
    public async Task ANumberUsedByAnotherSandwichIsRefused()
    {
        var error = await Assert.ThrowsAsync<MenuException>(() => service.SaveSandwichAsync(Input(number: 1)));

        Assert.Equal(MenuError.NumberTaken, error.Error);
    }

    [Fact]
    public async Task ASandwichMayKeepItsOwnNumberWhenItIsEdited()
    {
        var existing = new Sandwich { Id = 1, Number = 1, NameEn = "Ham", NameNl = "Ham", Price = 4m };
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(existing);

        await service.SaveSandwichAsync(Input(id: 1, number: 1, nameEn: "Ham deluxe", ingredients: new[] { 10 }));

        Assert.Equal("Ham deluxe", existing.NameEn);
        Assert.Single(existing.SandwichIngredients);
    }

    [Fact]
    public async Task AnUnknownIngredientIsRefused()
    {
        var error = await Assert.ThrowsAsync<MenuException>(() => service.SaveSandwichAsync(Input(ingredients: new[] { 99 })));

        Assert.Equal(MenuError.UnknownIngredient, error.Error);
    }

    [Fact]
    public async Task EditingAnUnknownSandwichIsRefused()
    {
        repository.GetAsync(5, Arg.Any<CancellationToken>()).Returns((Sandwich?)null);

        var error = await Assert.ThrowsAsync<MenuException>(() => service.SaveSandwichAsync(Input(id: 5)));

        Assert.Equal(MenuError.SandwichNotFound, error.Error);
    }

    [Fact]
    public async Task ChangingTheIngredientsRemovesTheUntickedOnesAndKeepsTheRest()
    {
        var existing = new Sandwich
        {
            Id = 1, Number = 1, NameEn = "Ham", NameNl = "Ham", Price = 4m,
            SandwichIngredients = { new SandwichIngredient { SandwichId = 1, IngredientId = 10 } }
        };
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(existing);

        await service.SaveSandwichAsync(Input(id: 1, number: 1, ingredients: new[] { 11 }));

        Assert.Equal(new[] { 11 }, existing.SandwichIngredients.Select(i => i.IngredientId));
    }

    [Fact]
    public async Task ReplacingThePhotoReportsTheOldOneSoItsFileCanBeDeleted()
    {
        var existing = new Sandwich { Id = 1, Number = 1, NameEn = "Ham", NameNl = "Ham", Price = 4m, ImagePath = "/uploads/old.jpg" };
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(existing);

        var saved = await service.SaveSandwichAsync(Input(id: 1, number: 1, keepImage: false, image: "/uploads/new.jpg"));

        Assert.Equal("/uploads/old.jpg", saved.OldImagePath);
        Assert.Equal("/uploads/new.jpg", existing.ImagePath);
    }

    [Fact]
    public async Task KeepingThePhotoLeavesItUntouched()
    {
        var existing = new Sandwich { Id = 1, Number = 1, NameEn = "Ham", NameNl = "Ham", Price = 4m, ImagePath = "/uploads/old.jpg" };
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(existing);

        var saved = await service.SaveSandwichAsync(Input(id: 1, number: 1, keepImage: true));

        Assert.Null(saved.OldImagePath);
        Assert.Equal("/uploads/old.jpg", existing.ImagePath);
    }

    [Fact]
    public async Task ASandwichCanBeTakenOffAndPutBackOnTheMenu()
    {
        var existing = new Sandwich { Id = 1, Number = 1, IsActive = true };
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(existing);

        await service.SetActiveAsync(1, false);
        Assert.False(existing.IsActive);

        await service.SetActiveAsync(1, true);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task AnIngredientIsAdded_AndADuplicateNameIsRefusedWhateverTheCase()
    {
        await service.AddIngredientAsync(" Tuna ", "Tonijn");
        repository.Received(1).Add(Arg.Is<Ingredient>(i => i.NameEn == "Tuna" && i.NameNl == "Tonijn"));

        var error = await Assert.ThrowsAsync<MenuException>(() => service.AddIngredientAsync("cheese", "Kaas"));
        Assert.Equal(MenuError.IngredientExists, error.Error);
    }

    [Fact]
    public async Task AnIngredientWithoutBothNamesIsRefused()
    {
        var error = await Assert.ThrowsAsync<MenuException>(() => service.AddIngredientAsync("Tuna", ""));

        Assert.Equal(MenuError.IngredientNameRequired, error.Error);
    }
}
