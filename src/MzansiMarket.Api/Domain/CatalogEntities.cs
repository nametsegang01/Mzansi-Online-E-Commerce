namespace MzansiMarket.Api.Domain;

public sealed class Store : Entity
{
    public Guid SellerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SupportEmail { get; set; }
    public StoreStatus Status { get; set; } = StoreStatus.Draft;
    public SellerProfile Seller { get; set; } = null!;
    public ICollection<Product> Products { get; set; } = [];
}

public sealed class Category : Entity
{
    public Guid? ParentCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Category? ParentCategory { get; set; }
    public ICollection<Category> Children { get; set; } = [];
    public ICollection<ProductCategory> Products { get; set; } = [];
}

public sealed class Product : Entity
{
    public Guid StoreId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "ZAR";
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public bool IsDeleted { get; set; }
    public Store Store { get; set; } = null!;
    public InventoryItem Inventory { get; set; } = null!;
    public ICollection<ProductCategory> Categories { get; set; } = [];
    public ICollection<ProductImage> Images { get; set; } = [];
}

public sealed class ProductCategory
{
    public Guid ProductId { get; set; }
    public Guid CategoryId { get; set; }
    public Product Product { get; set; } = null!;
    public Category Category { get; set; } = null!;
}

public sealed class ProductImage : Entity
{
    public Guid ProductId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class InventoryItem
{
    public Guid ProductId { get; set; }
    public int OnHandQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Product Product { get; set; } = null!;
    public ICollection<InventoryTransaction> Transactions { get; set; } = [];
    public ICollection<StockReservation> Reservations { get; set; } = [];
}

public sealed class InventoryTransaction : Entity
{
    public Guid ProductId { get; set; }
    public InventoryTransactionType Type { get; set; }
    public int QuantityDelta { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;
}
