using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Ingredient>(entity =>
        {
            entity.Property(i => i.NameEn).HasMaxLength(60).IsRequired();
            entity.Property(i => i.NameNl).HasMaxLength(60).IsRequired();
            entity.HasIndex(i => i.NameEn).IsUnique();
        });

        modelBuilder.Entity<Sandwich>(entity =>
        {
            entity.Property(s => s.NameEn).HasMaxLength(80).IsRequired();
            entity.Property(s => s.NameNl).HasMaxLength(80).IsRequired();
            entity.Property(s => s.DescriptionEn).HasMaxLength(300);
            entity.Property(s => s.DescriptionNl).HasMaxLength(300);
            entity.Property(s => s.ImagePath).HasMaxLength(300);
            entity.Property(s => s.Price).HasPrecision(8, 2);
            entity.HasIndex(s => s.Number).IsUnique();
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
            entity.HasIndex(o => new { o.CustomerId, o.OrderDate });
            entity.HasMany(o => o.Lines).WithOne(l => l.Order).HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.Property(l => l.UnitPrice).HasPrecision(8, 2);
            entity.HasOne(l => l.Sandwich).WithMany().HasForeignKey(l => l.SandwichId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(s => s.Key);
            entity.Property(s => s.Key).HasMaxLength(100);
            entity.Property(s => s.Value).HasMaxLength(200).IsRequired();
        });
    }
}
