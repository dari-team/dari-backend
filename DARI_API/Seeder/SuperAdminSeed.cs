using DARI_API.Models;
using Microsoft.AspNetCore.Identity;

namespace DARI_API.Seeder
{
    public static class SeedSuperAdmin
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            string email = "superadmin@dari.com";
            string password = "Admin@123";

            // Ensure role exists
            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>
                {
                    Name = "Admin"
                });
            }

            var user = await userManager.FindByEmailAsync(email);

            if (user != null)
                return;

            var admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                Name = "Super Admin",
                EmailConfirmed = true,
                AccountStatus = AccountStatus.Active,
                UserType = UserType.Admin,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(admin, password);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }
    }
}