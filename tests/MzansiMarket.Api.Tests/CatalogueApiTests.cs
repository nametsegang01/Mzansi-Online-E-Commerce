using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class CatalogueApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Products_ArePagedSortedAndLimitedToActiveCatalogueContent()
    {
        var seed = await EnsureCatalogueSeededAsync();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/products?page=1&pageSize=2&sort=price-desc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("totalCount").GetInt32());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(seed.BasketId, items[0].GetProperty("id").GetGuid());
        Assert.Equal(seed.MugId, items[1].GetProperty("id").GetGuid());
        Assert.DoesNotContain(items, item => item.GetProperty("id").GetGuid() == seed.DraftId);
    }

    [Fact]
    public async Task Products_SupportSearchCategoryPriceStoreAndAvailabilityFilters()
    {
        await EnsureCatalogueSeededAsync();
        using var client = factory.CreateApiClient();

        var inStock = await client.GetAsync(
            "/api/products?search=ubuntu&category=home-living&store=ubuntu-weaves&inStock=true");
        Assert.Equal(HttpStatusCode.OK, inStock.StatusCode);
        using var inStockBody = JsonDocument.Parse(await inStock.Content.ReadAsStringAsync());
        Assert.Equal(1, inStockBody.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Handwoven Storage Basket",
            inStockBody.RootElement.GetProperty("items")[0].GetProperty("name").GetString());

        var outOfStock = await client.GetAsync("/api/products?maximumPrice=200&inStock=false");
        Assert.Equal(HttpStatusCode.OK, outOfStock.StatusCode);
        using var outOfStockBody = JsonDocument.Parse(await outOfStock.Content.ReadAsStringAsync());
        Assert.Equal(1, outOfStockBody.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Forest Ceramic Mug",
            outOfStockBody.RootElement.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ProductDetailsAndCategories_HideDraftOrInactiveContent()
    {
        var seed = await EnsureCatalogueSeededAsync();
        using var client = factory.CreateApiClient();

        var detail = await client.GetAsync($"/api/products/{seed.BasketId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailBody = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(8, detailBody.RootElement.GetProperty("availableQuantity").GetInt32());
        Assert.Equal("Ubuntu Weaves", detailBody.RootElement.GetProperty("store").GetProperty("name").GetString());
        Assert.Equal("A handwoven storage basket",
            detailBody.RootElement.GetProperty("images")[0].GetProperty("altText").GetString());

        var bySlug = await client.GetAsync("/api/stores/ubuntu-weaves/products/handwoven-storage-basket");
        Assert.Equal(HttpStatusCode.OK, bySlug.StatusCode);

        var draft = await client.GetAsync($"/api/products/{seed.DraftId}");
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);

        var categories = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        using var categoriesBody = JsonDocument.Parse(await categories.Content.ReadAsStringAsync());
        var home = Assert.Single(categoriesBody.RootElement.EnumerateArray(),
            item => item.GetProperty("slug").GetString() == "home-living");
        Assert.Equal(2, home.GetProperty("activeProductCount").GetInt32());
        Assert.DoesNotContain(categoriesBody.RootElement.EnumerateArray(),
            item => item.GetProperty("slug").GetString() == "hidden-category");
    }

    [Fact]
    public async Task InvalidProductFilters_ReturnSpecificValidationProblems()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync(
            "/api/products?page=0&pageSize=101&minimumPrice=500&maximumPrice=100&sort=unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Page", out _));
        Assert.True(errors.TryGetProperty("PageSize", out _));
        Assert.True(errors.TryGetProperty("PriceRange", out _));
        Assert.True(errors.TryGetProperty("Sort", out _));
    }

    private async Task<CatalogueSeed> EnsureCatalogueSeededAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var existing = await dbContext.Products.IgnoreQueryFilters()
            .Where(product => product.Sku == "TEST-BASKET")
            .Select(product => new CatalogueSeed(
                product.Id,
                dbContext.Products.IgnoreQueryFilters().Where(item => item.Sku == "TEST-MUG").Select(item => item.Id).Single(),
                dbContext.Products.IgnoreQueryFilters().Where(item => item.Sku == "TEST-DRAFT").Select(item => item.Id).Single()))
            .SingleOrDefaultAsync();
        if (existing is not null)
        {
            return existing;
        }

        var activeStore = new Store
        {
            SellerId = Guid.NewGuid(),
            Name = "Ubuntu Weaves",
            Slug = "ubuntu-weaves",
            Status = StoreStatus.Active
        };
        var suspendedStore = new Store
        {
            SellerId = Guid.NewGuid(),
            Name = "Suspended Seller",
            Slug = "suspended-seller",
            Status = StoreStatus.Suspended
        };
        var homeCategory = new Category { Name = "Home & living", Slug = "home-living", IsActive = true };
        var hiddenCategory = new Category { Name = "Hidden", Slug = "hidden-category", IsActive = false };
        var basket = CreateProduct(activeStore, "TEST-BASKET", "Handwoven Storage Basket",
            "handwoven-storage-basket", 420m, ProductStatus.Active, 10, 2);
        basket.Description = "Handmade storage from an independent South African seller.";
        basket.Categories.Add(new ProductCategory { Product = basket, Category = homeCategory });
        basket.Images.Add(new ProductImage
        {
            Product = basket,
            StorageKey = "tests/catalogue/basket",
            PublicUrl = "https://images.example.test/basket.jpg",
            AltText = "A handwoven storage basket",
            SortOrder = 0,
            IsPrimary = true
        });

        var mug = CreateProduct(activeStore, "TEST-MUG", "Forest Ceramic Mug",
            "forest-ceramic-mug", 150m, ProductStatus.Active, 1, 1);
        mug.Categories.Add(new ProductCategory { Product = mug, Category = homeCategory });

        var draft = CreateProduct(activeStore, "TEST-DRAFT", "Draft Product",
            "draft-product", 100m, ProductStatus.Draft, 5, 0);
        draft.Categories.Add(new ProductCategory { Product = draft, Category = homeCategory });

        var inactiveCategoryProduct = CreateProduct(activeStore, "TEST-HIDDEN-CATEGORY", "Hidden Category Product",
            "hidden-category-product", 90m, ProductStatus.Active, 5, 0);
        inactiveCategoryProduct.Categories.Add(new ProductCategory
        {
            Product = inactiveCategoryProduct,
            Category = hiddenCategory
        });

        var suspendedProduct = CreateProduct(suspendedStore, "TEST-SUSPENDED", "Suspended Product",
            "suspended-product", 80m, ProductStatus.Active, 5, 0);
        suspendedProduct.Categories.Add(new ProductCategory { Product = suspendedProduct, Category = homeCategory });

        dbContext.AddRange(activeStore, suspendedStore, homeCategory, hiddenCategory,
            basket, mug, draft, inactiveCategoryProduct, suspendedProduct);
        await dbContext.SaveChangesAsync();

        return new CatalogueSeed(basket.Id, mug.Id, draft.Id);
    }

    private static Product CreateProduct(
        Store store,
        string sku,
        string name,
        string slug,
        decimal price,
        ProductStatus status,
        int onHand,
        int reserved)
    {
        var product = new Product
        {
            Store = store,
            Sku = sku,
            Name = name,
            Slug = slug,
            Price = price,
            Currency = "ZAR",
            Status = status
        };
        product.Inventory = new InventoryItem
        {
            Product = product,
            OnHandQuantity = onHand,
            ReservedQuantity = reserved,
            ReorderLevel = 2
        };
        return product;
    }

    private sealed record CatalogueSeed(Guid BasketId, Guid MugId, Guid DraftId);
}
