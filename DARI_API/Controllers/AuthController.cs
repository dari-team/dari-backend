using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = model.Email,
            UserName = model.Email,
            Name = model.Name,

            AccountStatus = AccountStatus.Pending,
            UserType = model.UserType,
            CustomerType = model.CustomerType,
            ListerType = model.ListerType,

            AgencyName = model.AgencyName,
            LicenseNumber = model.LicenseNumber,

            IsVerified = false,
            MaxListings = model.UserType == UserType.Lister ? 10 : 0,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        // Assign role based on type
        var role = model.UserType.ToString(); // "Customer" / "Lister" / "Admin"
        await _userManager.AddToRoleAsync(user, role);

        return Ok(new
        {
            message = "User created successfully"
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user == null)
            return Unauthorized("Invalid credentials");

        var valid = await _userManager.CheckPasswordAsync(user, model.Password);

        if (!valid)
            return Unauthorized("Invalid credentials");

        var token = await GenerateToken(user);

        return Ok(new
        {
            token,
            email = user.Email,
            name = user.Name,
            role = (await _userManager.GetRolesAsync(user)).FirstOrDefault()
        });
    }

    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin([FromBody] string idToken)
    {
        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken);

        var user = await _userManager.FindByEmailAsync(payload.Email);

        if (user == null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = payload.Email,
                UserName = payload.Email,
                Name = payload.Name,

                AccountStatus = AccountStatus.Active,
                UserType = UserType.Customer,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            await _userManager.CreateAsync(user);
            await _userManager.AddToRoleAsync(user, "Customer");
        }

        var token = await GenerateToken(user);

        return Ok(new { token });
    }

    [HttpPost("microsoft-login")]
    public async Task<IActionResult> MicrosoftLogin([FromBody] string email)
    {
        // In real case → validate token from Azure

        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                UserName = email,
                Name = email,

                AccountStatus = AccountStatus.Active,
                UserType = UserType.Customer,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            await _userManager.CreateAsync(user);
            await _userManager.AddToRoleAsync(user, "Customer");
        }

        var token = await GenerateToken(user);

        return Ok(new { token });
    }

    [Authorize]
    [HttpPut("update-profile")]
    public async Task<IActionResult> UpdateProfile(ProfileViewModel model)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userId == null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
            return NotFound();

        if (!string.IsNullOrEmpty(model.Name))
            user.Name = model.Name;

        if (!string.IsNullOrEmpty(model.PhoneNumber))
            user.PhoneNumber = model.PhoneNumber;

        if (model.CustomerType.HasValue)
            user.CustomerType = model.CustomerType;

        if (model.ListerType.HasValue)
            user.ListerType = model.ListerType;

        if (!string.IsNullOrEmpty(model.AgencyName))
            user.AgencyName = model.AgencyName;

        if (!string.IsNullOrEmpty(model.LicenseNumber))
            user.LicenseNumber = model.LicenseNumber;

        user.UpdatedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return Ok(new
        {
            message = "Profile updated successfully"
        });
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var user = await _userManager.FindByIdAsync(userId);

        return Ok(new
        {
            user.Id,
            user.Name,
            user.Email,
            user.PhoneNumber,
            user.UserType,
            user.CustomerType,
            user.ListerType,
            user.AgencyName,
            user.LicenseNumber
        });
    }

    private async Task<string> GenerateToken(ApplicationUser user)
    {
        var jwt = _configuration.GetSection("JWT");

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email)
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwt["Secret"]));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwt["ValidIssuer"],
            audience: jwt["ValidAudience"],
            claims: claims,
            expires: DateTime.Now.AddDays(7),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}