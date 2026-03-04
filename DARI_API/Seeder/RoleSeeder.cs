using Microsoft.AspNetCore.Identity;

namespace DARI_API.Seeder
{
    public static class RoleSeeder
    {
        public static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager)
        {
            string[] roles =
            {
                Roles.Admin,
                Roles.Lister,
                Roles.Customer
            };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid>
                    {
                        Name = role
                    });
                }
            }
        }
    }
}