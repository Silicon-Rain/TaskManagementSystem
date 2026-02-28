using Microsoft.EntityFrameworkCore;
using Shared.Messages;

namespace Worker.Data
{
    public class SecondaryDbContext : DbContext
    {
        public DbSet<InboxMessage> InboxMessages { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public SecondaryDbContext(DbContextOptions<SecondaryDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OutboxMessage>()
                .Property(e => e.Type)
                .HasConversion<string>();

            modelBuilder.Entity<InboxMessage>()
                .Property(e => e.Type)
                .HasConversion<string>();

            modelBuilder.Entity<InboxMessage>()
                .HasIndex(i => new { i.AggregateId, i.Type })
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}
