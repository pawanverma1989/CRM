namespace IdentityApi.Tests.Helpers;
using IdentityApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public static class DbContextFactory
{
    public static IdentityDbContext Create()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options);
    }
}
