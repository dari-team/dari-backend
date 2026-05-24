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
using DARI_API.IServicesLayer;


[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IServiceLayer _serviceLayer;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,IServiceLayer serviceLayer)
    {
        _userManager = userManager;
        _configuration = configuration;
        _serviceLayer = serviceLayer;
    }

    // Roles a visitor may self-assign. Admin is intentionally excluded so it can
    // never be obtained through a public, unauthenticated endpoint.
    private static bool IsPubliclyRegisterable(UserType type)
        => type == UserType.Customer || type == UserType.Lister;

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!IsPubliclyRegisterable(model.UserType))
            return BadRequest("Invalid account type.");

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = model.Email,
            UserName = model.Email,
            Name = model.Name,
            PhoneNumber = model.PhoneNumber,

            AccountStatus = AccountStatus.Pending,
            UserType = model.UserType,
            CustomerType = model.CustomerType,
            ListerType = model.ListerType,

            AgencyName = model.AgencyName,
            LicenseNumber = model.LicenseNumber,

            IsVerified = false,
            EmailConfirmed = false, 

            MaxListings = model.UserType == UserType.Lister ? 10 : 0,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        
        var role = model.UserType.ToString();
        await _userManager.AddToRoleAsync(user, role);


        var code = new Random().Next(100000, 999999).ToString();

        user.EmailVerificationCode = code;
        user.EmailVerificationExpiry = DateTime.UtcNow.AddMinutes(10);

        await _userManager.UpdateAsync(user);

        
        await _serviceLayer.SendEmailAsync(
            user.Email,
            "Verification Code",
            $"Your verification code is: {code}"
        );

        return Ok("User created. Verification code sent.");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user == null)
            return Unauthorized("Invalid credentials");

        if (!user.IsVerified)
            return Unauthorized("Please verify your email first");

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
        if (string.IsNullOrWhiteSpace(idToken))
            return BadRequest("idToken is required");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await ValidateGoogleToken(idToken);
        }
        catch (InvalidJwtException)
        {
            return Unauthorized("Invalid Google token");
        }

        var user = await _userManager.FindByEmailAsync(payload.Email);

        // New user — do NOT auto-create. Ask the frontend to collect
        // account type + lister/agent fields via a profile-completion step.
        if (user == null)
        {
            return Ok(new
            {
                needsProfile = true,
                email = payload.Email,
                name = payload.Name
            });
        }

        // User registered via email but never verified — Google has proven the
        // email is theirs, so promote them to verified.
        if (!user.IsVerified)
        {
            user.IsVerified = true;
            user.AccountStatus = AccountStatus.Active;
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        var token = await GenerateToken(user);

        return Ok(new
        {
            token,
            email = user.Email,
            name = user.Name,
            role = (await _userManager.GetRolesAsync(user)).FirstOrDefault()
        });
    }

    [HttpPost("google-complete")]
    public async Task<IActionResult> GoogleComplete(GoogleCompleteViewModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await ValidateGoogleToken(model.IdToken);
        }
        catch (InvalidJwtException)
        {
            return Unauthorized("Invalid Google token");
        }

        // Race-safety: someone could have already completed registration for
        // this email between /google-login and /google-complete. Just log them in.
        var existing = await _userManager.FindByEmailAsync(payload.Email);
        if (existing != null)
        {
            var tok = await GenerateToken(existing);
            return Ok(new
            {
                token = tok,
                email = existing.Email,
                name = existing.Name,
                role = (await _userManager.GetRolesAsync(existing)).FirstOrDefault()
            });
        }

        if (!IsPubliclyRegisterable(model.UserType))
            return BadRequest("Invalid account type.");

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = payload.Email,
            UserName = payload.Email,
            Name = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name,
            PhoneNumber = model.PhoneNumber,

            AccountStatus = AccountStatus.Active,
            UserType = model.UserType,
            CustomerType = model.CustomerType,
            ListerType = model.ListerType,
            AgencyName = model.AgencyName,
            LicenseNumber = model.LicenseNumber,

            MaxListings = model.UserType == UserType.Lister ? 10 : 0,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            EmailConfirmed = true,
            IsVerified = true
        };

        // No password — Google is the credential. CreateAsync without a password
        // leaves PasswordHash null; the user can later set one via forgot-password.
        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
            return BadRequest(created.Errors);

        await _userManager.AddToRoleAsync(user, model.UserType.ToString());

        var token = await GenerateToken(user);
        return Ok(new
        {
            token,
            email = user.Email,
            name = user.Name,
            role = model.UserType.ToString()
        });
    }

    private async Task<GoogleJsonWebSignature.Payload> ValidateGoogleToken(string idToken)
    {
        var clientId = _configuration["GoogleAuth:ClientId"];
        var settings = new GoogleJsonWebSignature.ValidationSettings();
        if (!string.IsNullOrWhiteSpace(clientId))
            settings.Audience = new[] { clientId };
        return await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
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

        // Allow setting and explicit clearing (empty string -> null).
        if (model.ProfilePictureUrl != null)
            user.ProfilePictureUrl = model.ProfilePictureUrl.Length == 0 ? null : model.ProfilePictureUrl;

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
            user.LicenseNumber,
            user.ProfilePictureUrl
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

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user == null)
            return BadRequest("User not found");

       
        if (user.EmailVerificationCode != model.Code)
            return BadRequest("Invalid code");

        
        if (user.EmailVerificationExpiry < DateTime.UtcNow)
            return BadRequest("Code expired");

        
        user.IsVerified = true;
        user.AccountStatus = AccountStatus.Active;

        
        user.EmailVerificationCode = null;
        user.EmailVerificationExpiry = null;

        await _userManager.UpdateAsync(user);

        return Ok("Email verified successfully");
    }
    [HttpPost("resend-code")]
    public async Task<IActionResult> ResendCode(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
            return BadRequest("User not found");

        var code = new Random().Next(100000, 999999).ToString();

        user.EmailVerificationCode = code;
        user.EmailVerificationExpiry = DateTime.UtcNow.AddMinutes(10);

        await _userManager.UpdateAsync(user);

        await _serviceLayer.SendEmailAsync(
            user.Email,
            "Verification Code",
            $"Your new code is: {code}"
        );

        return Ok("Code resent");
    }
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgetPasswordViewModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);

        
        if (user == null)
            return Ok("If the email exists, a reset code has been sent.");

        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

        user.PasswordResetCode = code;
        user.PasswordResetExpiry = DateTime.UtcNow.AddMinutes(10);

        await _userManager.UpdateAsync(user);

        await _serviceLayer.SendEmailAsync(
            user.Email,
            "Password Reset Code",
            $"Your password reset code is: {code}"
        );

        return Ok("If the email exists, a reset code has been sent.");
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user == null)
            return BadRequest("Invalid request");

        
        if (user.PasswordResetCode != model.Code)
            return BadRequest("Invalid code");

        
        if (user.PasswordResetExpiry < DateTime.UtcNow)
            return BadRequest("Code expired");

        
        var removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
            return BadRequest(removeResult.Errors);

        
        var addResult = await _userManager.AddPasswordAsync(user, model.NewPassword);
        if (!addResult.Succeeded)
            return BadRequest(addResult.Errors);

        
        user.PasswordResetCode = null;
        user.PasswordResetExpiry = null;
        user.ResetCodeAttempts = 0;

        await _userManager.UpdateAsync(user);

        return Ok("Password reset successful");
    }

    [HttpPost("resend-reset-code")]
    public async Task<IActionResult> ResendResetCode(ForgetPasswordViewModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user == null)
            return Ok("If the email exists, a reset code has been sent.");

   
        if (user.PasswordResetExpiry.HasValue &&
            user.PasswordResetExpiry > DateTime.UtcNow.AddMinutes(8))
        {
            return BadRequest("Please wait before requesting a new code.");
        }

        if (user.ResetCodeAttempts >= 5)
            return BadRequest("Too many attempts. Try again later.");

        user.ResetCodeAttempts++;

        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

        user.PasswordResetCode = code;
        user.PasswordResetExpiry = DateTime.UtcNow.AddMinutes(10);

        await _userManager.UpdateAsync(user);

        await _serviceLayer.SendEmailAsync(
            user.Email,
            "Password Reset Code",
            $"Your new password reset code is: {code}"
        );

        return Ok("If the email exists, a reset code has been sent.");
    }
}