using Microsoft.EntityFrameworkCore;

namespace PaymentService.Models;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<OperationEvent> Events => Set<OperationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Operation>(entity =>
        {
            entity.HasKey(e => e.OperationId);
        });

        modelBuilder.Entity<OperationEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.HasOne<Operation>()
                  .WithMany(o => o.Events)
                  .HasForeignKey(e => e.OperationId);
        });
    }
}