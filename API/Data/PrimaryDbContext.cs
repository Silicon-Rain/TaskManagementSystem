using Microsoft.EntityFrameworkCore;
using API.Data.Entities;

public class PrimaryDbContext : DbContext
{
    public DbSet<TaskItem> Tasks { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    public PrimaryDbContext(DbContextOptions<PrimaryDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>()
            .Property(e => e.Type)
            .HasConversion<string>();

        modelBuilder.Entity<TaskItem>()
            .Property(e => e.Status)
            .HasConversion<string>();

        base.OnModelCreating(modelBuilder);
    }
}