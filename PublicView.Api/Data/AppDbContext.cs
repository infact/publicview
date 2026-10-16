using Microsoft.EntityFrameworkCore;
using PublicView.Api.Models;

namespace PublicView.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<Posting> Postings => Set<Posting>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => u.Email);
        });

        modelBuilder.Entity<ExternalLogin>(b =>
        {
            b.HasIndex(l => new { l.Provider, l.ProviderKey }).IsUnique();
            b.HasOne(l => l.User)
                .WithMany(u => u.Logins)
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Posting>(b =>
        {
            b.Property(p => p.Price).HasColumnType("numeric(10,2)");
            b.HasIndex(p => p.Status);
            b.HasIndex(p => p.CreatedAt);
            b.HasOne(p => p.Category)
                .WithMany(c => c.Postings)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.User)
                .WithMany(u => u.Postings)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Payment>(b =>
        {
            b.HasOne(p => p.Posting)
                .WithMany(p => p.Payments)
                .HasForeignKey(p => p.PostingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.HasIndex(c => c.Slug).IsUnique();
        });
    }
}
