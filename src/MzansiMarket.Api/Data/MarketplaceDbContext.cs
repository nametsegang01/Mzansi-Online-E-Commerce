using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Data;

public sealed class MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<SellerProfile> SellerProfiles => Set<SellerProfile>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderAddress> OrderAddresses => Set<OrderAddress>();
    public DbSet<SellerOrder> SellerOrders => Set<SellerOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<PaymentProviderEvent> PaymentProviderEvents => Set<PaymentProviderEvent>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<RefundRecord> RefundRecords => Set<RefundRecord>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionProduct> PromotionProducts => Set<PromotionProduct>();
    public DbSet<SellerPayout> SellerPayouts => Set<SellerPayout>();
    public DbSet<SellerPayoutItem> SellerPayoutItems => Set<SellerPayoutItem>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("marketplace");

        ConfigureIdentity(modelBuilder);
        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys", "identity");
        ConfigureProfiles(modelBuilder);
        ConfigureCatalog(modelBuilder);
        ConfigureCommerce(modelBuilder);
        ConfigureOperations(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampChanges()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is Entity entity)
            {
                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAt = now;
                }

                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    entity.UpdatedAt = now;
                }
            }

            if (entry.Entity is ApplicationUser user
                && entry.State is EntityState.Added or EntityState.Modified)
            {
                if (entry.State == EntityState.Added) user.CreatedAt = now;
                user.UpdatedAt = now;
            }

            if (entry.Entity is CustomerProfile customer
                && entry.State is EntityState.Added or EntityState.Modified)
            {
                if (entry.State == EntityState.Added) customer.CreatedAt = now;
                customer.UpdatedAt = now;
            }

            if (entry.Entity is SellerProfile seller
                && entry.State is EntityState.Added or EntityState.Modified)
            {
                if (entry.State == EntityState.Added) seller.CreatedAt = now;
                seller.UpdatedAt = now;
            }

            if (entry.Entity is InventoryItem inventory
                && entry.State is EntityState.Added or EntityState.Modified)
            {
                inventory.UpdatedAt = now;
                if (entry.State == EntityState.Modified) inventory.Version++;
            }
        }
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users", "identity");
            entity.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.ToTable("Roles", "identity");
            entity.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasData(RoleSeeds.All);
        });
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", "identity");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
    }

    private static void ConfigureProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.ToTable("CustomerProfiles");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.MobileNumber).HasMaxLength(32);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(x => x.User).WithOne(x => x.CustomerProfile)
                .HasForeignKey<CustomerProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SellerProfile>(entity =>
        {
            entity.ToTable("SellerProfiles", table =>
                table.HasCheckConstraint("CK_SellerProfiles_CommissionRate", "\"CommissionRate\" >= 0 AND \"CommissionRate\" <= 1"));
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.TradingName).HasMaxLength(180).IsRequired();
            entity.Property(x => x.RegistrationNumber).HasMaxLength(80);
            entity.Property(x => x.TaxNumber).HasMaxLength(80);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.CommissionRate).HasPrecision(5, 4).HasDefaultValue(0.10m);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(x => x.User).WithOne(x => x.SellerProfile)
                .HasForeignKey<SellerProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Address>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("Addresses");
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.RecipientName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Line1).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Line2).HasMaxLength(200);
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Province).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PostalCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
            entity.HasIndex(x => new { x.UserId, x.IsDefault });
            entity.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDefault\"");
            entity.HasOne(x => x.User).WithMany(x => x.Addresses)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Store>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("Stores");
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.SupportEmail).HasMaxLength(256);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.SellerId).IsUnique();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasOne(x => x.Seller).WithOne(x => x.Store)
                .HasForeignKey<Store>(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("Categories");
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(140).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasOne(x => x.ParentCategory).WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("Products", table =>
                table.HasCheckConstraint("CK_Products_Price", "\"Price\" >= 0"));
            entity.Property(x => x.Sku).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.Sku).IsUnique();
            entity.HasIndex(x => new { x.StoreId, x.Slug }).IsUnique();
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasOne(x => x.Store).WithMany(x => x.Products)
                .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductCategory>(entity =>
        {
            entity.ToTable("ProductCategories");
            entity.HasKey(x => new { x.ProductId, x.CategoryId });
            entity.HasOne(x => x.Product).WithMany(x => x.Categories)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Category).WithMany(x => x.Products)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("ProductImages", table =>
                table.HasCheckConstraint("CK_ProductImages_SortOrder", "\"SortOrder\" >= 0"));
            entity.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(x => x.PublicUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.AltText).HasMaxLength(300).IsRequired();
            entity.HasIndex(x => x.StorageKey).IsUnique();
            entity.HasIndex(x => new { x.ProductId, x.SortOrder }).IsUnique();
            entity.HasOne(x => x.Product).WithMany(x => x.Images)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("InventoryItems", table =>
                table.HasCheckConstraint("CK_InventoryItems_Quantities",
                    "\"OnHandQuantity\" >= 0 AND \"ReservedQuantity\" >= 0 AND \"ReservedQuantity\" <= \"OnHandQuantity\" AND \"ReorderLevel\" >= 0"));
            entity.HasKey(x => x.ProductId);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(x => x.Product).WithOne(x => x.Inventory)
                .HasForeignKey<InventoryItem>(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryTransaction>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("InventoryTransactions", table =>
                table.HasCheckConstraint("CK_InventoryTransactions_Delta", "\"QuantityDelta\" <> 0"));
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ReferenceType).HasMaxLength(80);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.ProductId, x.CreatedAt });
            entity.HasOne(x => x.InventoryItem).WithMany(x => x.Transactions)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCommerce(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cart>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("Carts");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.CustomerId, x.Status });
            entity.HasIndex(x => x.CustomerId).IsUnique().HasFilter("\"Status\" = 'Active'");
            entity.HasOne(x => x.Customer).WithMany(x => x.Carts)
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("CartItems", table =>
                table.HasCheckConstraint("CK_CartItems_Quantity", "\"Quantity\" > 0"));
            entity.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique();
            entity.HasOne(x => x.Cart).WithMany(x => x.Items)
                .HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("Orders", table =>
                table.HasCheckConstraint("CK_Orders_Totals",
                    "\"Subtotal\" >= 0 AND \"DiscountTotal\" >= 0 AND \"DeliveryTotal\" >= 0 AND \"GrandTotal\" >= 0"));
            entity.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
            entity.Property(x => x.CheckoutKey).HasMaxLength(100);
            entity.Property(x => x.PromotionCode).HasMaxLength(80);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            ConfigureMoney(entity.Property(x => x.Subtotal));
            ConfigureMoney(entity.Property(x => x.DiscountTotal));
            ConfigureMoney(entity.Property(x => x.DeliveryTotal));
            ConfigureMoney(entity.Property(x => x.GrandTotal));
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.HasIndex(x => x.OrderNumber).IsUnique();
            entity.HasIndex(x => new { x.CustomerId, x.CheckoutKey }).IsUnique()
                .HasFilter("\"CheckoutKey\" IS NOT NULL");
            entity.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            entity.HasOne(x => x.Customer).WithMany(x => x.Orders)
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderAddress>(entity =>
        {
            entity.ToTable("OrderAddresses");
            entity.HasKey(x => x.OrderId);
            entity.Property(x => x.RecipientName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Line1).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Line2).HasMaxLength(200);
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Province).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PostalCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
            entity.HasOne(x => x.Order).WithOne(x => x.ShippingAddress)
                .HasForeignKey<OrderAddress>(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SellerOrder>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("SellerOrders", table =>
                table.HasCheckConstraint("CK_SellerOrders_Totals",
                    "\"Subtotal\" >= 0 AND \"DiscountTotal\" >= 0 AND \"DeliveryTotal\" >= 0 AND \"CommissionAmount\" >= 0 AND \"SellerNetAmount\" >= 0"));
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            ConfigureMoney(entity.Property(x => x.Subtotal));
            ConfigureMoney(entity.Property(x => x.DiscountTotal));
            ConfigureMoney(entity.Property(x => x.DeliveryTotal));
            ConfigureMoney(entity.Property(x => x.CommissionAmount));
            ConfigureMoney(entity.Property(x => x.SellerNetAmount));
            entity.HasIndex(x => new { x.OrderId, x.StoreId }).IsUnique();
            entity.HasOne(x => x.Order).WithMany(x => x.SellerOrders)
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Seller).WithMany()
                .HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Store).WithMany()
                .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("OrderItems", table =>
            {
                table.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_OrderItems_Totals",
                    "\"UnitPrice\" >= 0 AND \"DiscountAmount\" >= 0 AND \"LineTotal\" >= 0");
            });
            entity.Property(x => x.SkuSnapshot).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsRequired();
            ConfigureMoney(entity.Property(x => x.UnitPrice));
            ConfigureMoney(entity.Property(x => x.DiscountAmount));
            ConfigureMoney(entity.Property(x => x.LineTotal));
            entity.HasOne(x => x.SellerOrder).WithMany(x => x.Items)
                .HasForeignKey(x => x.SellerOrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("StockReservations", table =>
                table.HasCheckConstraint("CK_StockReservations_Quantity", "\"Quantity\" > 0"));
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.OrderItemId).IsUnique();
            entity.HasIndex(x => new { x.ProductId, x.Status, x.ExpiresAt });
            entity.HasOne(x => x.OrderItem).WithOne(x => x.Reservation)
                .HasForeignKey<StockReservation>(x => x.OrderItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.InventoryItem).WithMany(x => x.Reservations)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentRecord>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("PaymentRecords", table =>
                table.HasCheckConstraint("CK_PaymentRecords_Amount", "\"Amount\" >= 0"));
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PaymentKey).HasMaxLength(100);
            entity.Property(x => x.ProviderReference).HasMaxLength(160);
            entity.Property(x => x.PaymentMethodType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            ConfigureMoney(entity.Property(x => x.Amount));
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.FailureReason).HasMaxLength(500);
            entity.HasIndex(x => new { x.Provider, x.ProviderReference })
                .IsUnique().HasFilter("\"ProviderReference\" IS NOT NULL");
            entity.HasIndex(x => new { x.OrderId, x.PaymentKey })
                .IsUnique().HasFilter("\"PaymentKey\" IS NOT NULL");
            entity.HasOne(x => x.Order).WithMany(x => x.Payments)
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentProviderEvent>(entity =>
        {
            entity.ToTable("PaymentProviderEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EventId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.ReceivedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
            entity.HasOne(x => x.PaymentRecord).WithMany(x => x.ProviderEvents)
                .HasForeignKey(x => x.PaymentRecordId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Shipment>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("Shipments");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Carrier).HasMaxLength(100);
            entity.Property(x => x.TrackingNumber).HasMaxLength(160);
            entity.HasIndex(x => new { x.Carrier, x.TrackingNumber })
                .IsUnique().HasFilter("\"TrackingNumber\" IS NOT NULL");
            entity.HasOne(x => x.SellerOrder).WithMany(x => x.Shipments)
                .HasForeignKey(x => x.SellerOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReturnRequest>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("ReturnRequests", table =>
            {
                table.HasCheckConstraint("CK_ReturnRequests_Quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_ReturnRequests_RefundAmount", "\"RefundAmount\" >= 0");
            });
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            ConfigureMoney(entity.Property(x => x.RefundAmount));
            entity.HasIndex(x => new { x.OrderItemId, x.CustomerId, x.CreatedAt });
            entity.HasOne(x => x.OrderItem).WithMany(x => x.ReturnRequests)
                .HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Customer).WithMany()
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefundRecord>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("RefundRecords", table =>
                table.HasCheckConstraint("CK_RefundRecords_Amount", "\"Amount\" > 0"));
            entity.Property(x => x.ProviderReference).HasMaxLength(160);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            ConfigureMoney(entity.Property(x => x.Amount));
            entity.HasOne(x => x.ReturnRequest).WithMany(x => x.Refunds)
                .HasForeignKey(x => x.ReturnRequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PaymentRecord).WithMany(x => x.Refunds)
                .HasForeignKey(x => x.PaymentRecordId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("Promotions", table =>
            {
                table.HasCheckConstraint("CK_Promotions_Value", "\"Value\" > 0");
                table.HasCheckConstraint("CK_Promotions_Dates", "\"EndsAt\" > \"StartsAt\"");
            });
            entity.Property(x => x.Code).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            ConfigureMoney(entity.Property(x => x.Value));
            ConfigureMoney(entity.Property(x => x.MinimumOrderAmount));
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasOne(x => x.Seller).WithMany()
                .HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PromotionProduct>(entity =>
        {
            entity.ToTable("PromotionProducts");
            entity.HasKey(x => new { x.PromotionId, x.ProductId });
            entity.HasOne(x => x.Promotion).WithMany(x => x.Products)
                .HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureOperations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SellerPayout>(entity =>
        {
            ConfigureEntity(entity);
            entity.ToTable("SellerPayouts", table =>
            {
                table.HasCheckConstraint("CK_SellerPayouts_Period", "\"PeriodEnd\" >= \"PeriodStart\"");
                table.HasCheckConstraint("CK_SellerPayouts_Amounts",
                    "\"GrossSales\" >= 0 AND \"PlatformFees\" >= 0 AND \"Refunds\" >= 0 AND \"NetAmount\" >= 0");
            });
            ConfigureMoney(entity.Property(x => x.GrossSales));
            ConfigureMoney(entity.Property(x => x.PlatformFees));
            ConfigureMoney(entity.Property(x => x.Refunds));
            ConfigureMoney(entity.Property(x => x.NetAmount));
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ExternalReference).HasMaxLength(160);
            entity.HasIndex(x => new { x.SellerId, x.PeriodStart, x.PeriodEnd }).IsUnique();
            entity.HasIndex(x => x.ExternalReference).IsUnique()
                .HasFilter("\"ExternalReference\" IS NOT NULL");
            entity.HasOne(x => x.Seller).WithMany(x => x.Payouts)
                .HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SellerPayoutItem>(entity =>
        {
            entity.ToTable("SellerPayoutItems", table =>
                table.HasCheckConstraint("CK_SellerPayoutItems_Amount", "\"Amount\" >= 0"));
            entity.HasKey(x => new { x.SellerPayoutId, x.SellerOrderId });
            ConfigureMoney(entity.Property(x => x.Amount));
            entity.HasOne(x => x.SellerPayout).WithMany(x => x.Items)
                .HasForeignKey(x => x.SellerPayoutId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.SellerOrder).WithMany(x => x.PayoutItems)
                .HasForeignKey(x => x.SellerOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("AuditEntries", "audit");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn();
            entity.Property(x => x.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ChangesJson).HasColumnType("jsonb");
            entity.Property(x => x.CorrelationId).HasMaxLength(120);
            entity.Property(x => x.OccurredAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => x.OccurredAt);
            entity.HasIndex(x => new { x.EntityType, x.EntityId });
            entity.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : Entity
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }

    private static void ConfigureMoney(PropertyBuilder<decimal> property) =>
        property.HasPrecision(18, 2);

    private static void ConfigureMoney(PropertyBuilder<decimal?> property) =>
        property.HasPrecision(18, 2);
}

internal static class RoleSeeds
{
    public static readonly IdentityRole<Guid>[] All =
    [
        Create("11111111-1111-1111-1111-111111111111", AppRoles.Customer),
        Create("22222222-2222-2222-2222-222222222222", AppRoles.Seller),
        Create("33333333-3333-3333-3333-333333333333", AppRoles.ProductAdministrator),
        Create("44444444-4444-4444-4444-444444444444", AppRoles.FulfilmentEmployee),
        Create("55555555-5555-5555-5555-555555555555", AppRoles.BusinessManager),
        Create("66666666-6666-6666-6666-666666666666", AppRoles.SystemAdministrator)
    ];

    private static IdentityRole<Guid> Create(string id, string name) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = $"role-{name.ToLowerInvariant()}-v1"
    };
}
