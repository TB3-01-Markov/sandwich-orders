using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SandwichOrders.Core;
using SandwichOrders.Core.Announcements;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Sandwich> Sandwiches => Set<Sandwich>();
    public DbSet<SandwichIngredient> SandwichIngredients => Set<SandwichIngredient>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<Announcement> Announcements => Set<Announcement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // The column sizes come from MenuLimits, the same numbers the forms and MenuService check, so a text the
        // form accepts always fits. (SQLite ignores sizes; SQL Server would refuse a longer text with an error.)
        modelBuilder.Entity<Ingredient>(entity =>
        {
            entity.Property(i => i.NameEn).HasMaxLength(MenuLimits.MaxNameLength).IsRequired();
            entity.Property(i => i.NameNl).HasMaxLength(MenuLimits.MaxNameLength).IsRequired();
            entity.HasIndex(i => i.NameEn).IsUnique();

            // Two orders that take the last portion at the same moment: the second save fails instead of
            // silently making the stock negative (see EfOrderRepository.SaveChangesAsync).
            entity.Property(i => i.StockVersion).IsConcurrencyToken();
        });

        modelBuilder.Entity<Sandwich>(entity =>
        {
            entity.Property(s => s.NameEn).HasMaxLength(MenuLimits.MaxNameLength).IsRequired();
            entity.Property(s => s.NameNl).HasMaxLength(MenuLimits.MaxNameLength).IsRequired();
            entity.Property(s => s.DescriptionEn).HasMaxLength(MenuLimits.MaxDescriptionLength);
            entity.Property(s => s.DescriptionNl).HasMaxLength(MenuLimits.MaxDescriptionLength);
            entity.Property(s => s.ImagePath).HasMaxLength(300);
            entity.Property(s => s.Price).HasPrecision(8, 2);
            entity.HasIndex(s => s.Number).IsUnique();
            entity.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(s => s.StockVersion).IsConcurrencyToken();
        });

        // Many-to-many through an explicit join entity (same idea as RecipeIngredient in RecipeMatcher).
        modelBuilder.Entity<SandwichIngredient>(entity =>
        {
            entity.HasKey(si => new { si.SandwichId, si.IngredientId });
            entity.HasOne(si => si.Sandwich).WithMany(s => s.SandwichIngredients).HasForeignKey(si => si.SandwichId);
            entity.HasOne(si => si.Ingredient).WithMany(i => i.SandwichIngredients).HasForeignKey(si => si.IngredientId);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.CustomerId).HasMaxLength(450).IsRequired();
            entity.Property(o => o.CustomerName).HasMaxLength(100).IsRequired();
            entity.Property(o => o.CustomerPhone).HasMaxLength(30);

            // One active order per customer per day, guaranteed by the database. OrderService checks this too, but two
            // requests at the same moment (a double click) could both pass that check; the second save then fails here.
            // Cancelled orders are left out, so after a cancellation the customer can order again.
            entity.HasIndex(o => new { o.CustomerId, o.OrderDate })
                .IsUnique()
                .HasFilter($"{nameof(Order.Status)} <> {(int)OrderStatus.Cancelled}");
            entity.HasMany(o => o.Lines).WithOne(l => l.Order).HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.Property(l => l.UnitPrice).HasPrecision(8, 2);
            entity.HasOne(l => l.Sandwich).WithMany().HasForeignKey(l => l.SandwichId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.Property(a => a.MessageEn).HasMaxLength(Announcement.MaxMessageLength).IsRequired();
            entity.Property(a => a.MessageNl).HasMaxLength(Announcement.MaxMessageLength).IsRequired();
            entity.Property(a => a.PeriodMessageEn).HasMaxLength(Announcement.MaxMessageLength);
            entity.Property(a => a.PeriodMessageNl).HasMaxLength(Announcement.MaxMessageLength);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(s => s.Key);
            entity.Property(s => s.Key).HasMaxLength(100);
            entity.Property(s => s.Value).HasMaxLength(200).IsRequired();
        });
    }
}
