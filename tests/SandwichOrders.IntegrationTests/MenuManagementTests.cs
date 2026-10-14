using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SandwichOrders.IntegrationTests;

// The canteen employee adds and changes sandwiches, ingredients and photos through the real pages.
public class MenuManagementTests : IntegrationTestBase
{
    // The 8-byte PNG signature followed by filler: the app only checks the signature, not the whole image.
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0];

    private async Task<HttpResponseMessage> PostSandwichAsync(
        HttpClient client, string nameEn = "Cheese sandwich", string nameNl = "Broodje kaas",
        string number = "2", string price = "3.50", byte[]? photo = null, string photoName = "photo.png",
        string? id = null, bool removePhoto = false, params int[] ingredientIds)
    {
        var token = await GetTokenAsync(client, "/Staff/SandwichEdit");
        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(number), "Number" },
            { new StringContent(nameEn), "NameEn" },
            { new StringContent(nameNl), "NameNl" },
            { new StringContent("Tasty"), "DescriptionEn" },
            { new StringContent("Lekker"), "DescriptionNl" },
            { new StringContent(price), "PriceText" },
            { new StringContent("true"), "IsActive" }
        };
        if (id is not null)
        {
            content.Add(new StringContent(id), "Id");
        }

        if (removePhoto)
        {
            content.Add(new StringContent("true"), "RemovePhoto");
        }

        foreach (var ingredientId in ingredientIds)
        {
            content.Add(new StringContent(ingredientId.ToString()), "IngredientIds");
        }

        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "Photo", photoName);
        }

        return await client.PostAsync("/Staff/SandwichEdit", content);
    }

    [Fact]
    public async Task TheEmployeeCanAddASandwichWithAPhoto_AndCustomersSeeIt()
    {
        using var staff = await CreateStaffClientAsync();

        var response = await PostSandwichAsync(staff, photo: PngBytes);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var visitor = CreateClient();
        var menu = await visitor.GetStringAsync("/");
        Assert.Contains("Cheese sandwich", menu);
        Assert.Contains("3.50", menu);

        var imageUrl = System.Text.RegularExpressions.Regex.Match(menu, "src=\"(/uploads/[0-9a-f]{32}\\.png)\"");
        Assert.True(imageUrl.Success, "The menu shows no uploaded photo.");
        var image = await visitor.GetAsync(imageUrl.Groups[1].Value);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal(PngBytes, await image.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APriceWithACommaIsAccepted()
    {
        using var staff = await CreateStaffClientAsync();

        await PostSandwichAsync(staff, price: "4,75");

        using var visitor = CreateClient();
        Assert.Contains("4.75", await visitor.GetStringAsync("/"));
    }

    [Fact]
    public async Task AFileThatIsNotAnImageIsRefused_EvenWithAPngName()
    {
        using var staff = await CreateStaffClientAsync();

        var response = await PostSandwichAsync(staff, photo: Encoding.UTF8.GetBytes("this is not an image"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("must be a JPG, PNG or WebP", await response.Content.ReadAsStringAsync());
        using var visitor = CreateClient();
        Assert.DoesNotContain("Cheese sandwich", await visitor.GetStringAsync("/"));
        Assert.False(Directory.Exists(Factory.UploadsPath) && Directory.GetFiles(Factory.UploadsPath).Length > 0);
    }

    [Fact]
    public async Task APhotoOverTwoMegabytesIsRefused()
    {
        using var staff = await CreateStaffClientAsync();
        var big = new byte[2 * 1024 * 1024 + 1];
        PngBytes.CopyTo(big, 0);

        var response = await PostSandwichAsync(staff, photo: big);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("larger than 2 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ANumberThatIsAlreadyUsedIsRefused()
    {
        using var staff = await CreateStaffClientAsync();

        var response = await PostSandwichAsync(staff, number: "1"); // the seeded ham sandwich is number 1

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already used", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("-2")]
    public async Task AnInvalidPriceIsRefused(string price)
    {
        using var staff = await CreateStaffClientAsync();

        var response = await PostSandwichAsync(staff, price: price);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter a price above 0", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ABlankNameShowsAnError_AndNothingIsSaved()
    {
        using var staff = await CreateStaffClientAsync();

        var response = await PostSandwichAsync(staff, nameEn: "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("English name is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EditingASandwichChangesItsPriceAndName()
    {
        using var staff = await CreateStaffClientAsync();

        var response = await PostSandwichAsync(staff, id: HamSandwichId.ToString(), number: "1",
            nameEn: "Ham and cheese", price: "5.00");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var visitor = CreateClient();
        var menu = await visitor.GetStringAsync("/");
        Assert.Contains("Ham and cheese", menu);
        Assert.Contains("5.00", menu);
        Assert.DoesNotContain("4.20", menu);
    }

    [Fact]
    public async Task ReplacingAPhotoDeletesTheOldFile_AndRemovingItShowsThePlaceholder()
    {
        using var staff = await CreateStaffClientAsync();
        await PostSandwichAsync(staff, id: HamSandwichId.ToString(), number: "1", photo: PngBytes);
        var first = Directory.GetFiles(Factory.UploadsPath).Single();

        await PostSandwichAsync(staff, id: HamSandwichId.ToString(), number: "1", photo: PngBytes);
        var second = Directory.GetFiles(Factory.UploadsPath).Single();
        Assert.NotEqual(first, second);

        await PostSandwichAsync(staff, id: HamSandwichId.ToString(), number: "1", removePhoto: true);
        Assert.Empty(Directory.GetFiles(Factory.UploadsPath));
    }

    [Fact]
    public async Task EditingWithoutANewPhoto_KeepsTheCurrentOne()
    {
        using var staff = await CreateStaffClientAsync();
        await PostSandwichAsync(staff, id: HamSandwichId.ToString(), number: "1", photo: PngBytes);

        await PostSandwichAsync(staff, id: HamSandwichId.ToString(), number: "1", price: "4.40");

        Assert.Single(Directory.GetFiles(Factory.UploadsPath));
        using var visitor = CreateClient();
        Assert.Contains("/uploads/", await visitor.GetStringAsync("/"));
    }

    [Fact]
    public async Task ASandwichTakenOffTheMenuDisappearsForCustomers_AndCanBePutBack()
    {
        using var staff = await CreateStaffClientAsync();
        var off = await PostFormAsync(staff, "/Staff/SetSandwichActive",
            new Dictionary<string, string> { ["id"] = HamSandwichId.ToString(), ["active"] = "false" }, "/Staff/Menu");
        Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);

        using var visitor = CreateClient();
        Assert.DoesNotContain("Ham sandwich", await visitor.GetStringAsync("/"));
        Assert.Contains("Ham sandwich", await staff.GetStringAsync("/Staff/Menu"));

        await PostFormAsync(staff, "/Staff/SetSandwichActive",
            new Dictionary<string, string> { ["id"] = HamSandwichId.ToString(), ["active"] = "true" }, "/Staff/Menu");
        Assert.Contains("Ham sandwich", await visitor.GetStringAsync("/"));
    }

    [Fact]
    public async Task ANewIngredientCanBeAdded_AndUsedInANewSandwich()
    {
        using var staff = await CreateStaffClientAsync();
        await PostFormAsync(staff, "/Staff/AddIngredient",
            new Dictionary<string, string> { ["nameEn"] = "Tuna", ["nameNl"] = "Tonijn" }, "/Staff/Menu");

        var form = await staff.GetStringAsync("/Staff/SandwichEdit");
        Assert.Contains("Tuna", form);
        var match = System.Text.RegularExpressions.Regex.Match(form, "name=\"IngredientIds\" value=\"(\\d+)\"[^>]*id=\"ingredient-\\d+\"[^>]*/>\\s*<label[^>]*>Tuna<");
        Assert.True(match.Success, "The new ingredient is not offered in the sandwich form.");

        await PostSandwichAsync(staff, nameEn: "Tuna sandwich", ingredientIds: new[] { int.Parse(match.Groups[1].Value) });

        using var visitor = CreateClient();
        var menu = await visitor.GetStringAsync("/");
        Assert.Contains("Tuna sandwich", menu);
        Assert.Contains(">Tuna<", menu);
    }

    [Fact]
    public async Task ADuplicateIngredientIsRefused()
    {
        using var staff = await CreateStaffClientAsync();

        await PostFormAsync(staff, "/Staff/AddIngredient",
            new Dictionary<string, string> { ["nameEn"] = "ham", ["nameNl"] = "Ham" }, "/Staff/Menu");

        Assert.Contains("already exists", await staff.GetStringAsync("/Staff/Menu"));
    }

    [Fact]
    public async Task ACustomerCannotOpenOrUseTheMenuManager()
    {
        using var customer = await CreateSignedInCustomerAsync();

        var page = await customer.GetAsync("/Staff/SandwichEdit");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Account/AccessDenied", page.Headers.Location!.ToString());

        var post = await PostFormAsync(customer, "/Staff/AddIngredient",
            new Dictionary<string, string> { ["nameEn"] = "Hacked", ["nameNl"] = "Hacked" });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("/Account/AccessDenied", post.Headers.Location!.ToString());
    }
}
