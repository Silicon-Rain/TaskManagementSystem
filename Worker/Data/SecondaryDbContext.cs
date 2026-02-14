using Microsoft.EntityFrameworkCore;
using Worker.Models;

namespace Worker.Data
{
    public class SecondaryDbContext : DbContext
    {
        public DbSet<InboxMessage> InboxMessages { get; set; }
        public SecondaryDbContext(DbContextOptions<SecondaryDbContext> options) : base(options) { }
    }
}
