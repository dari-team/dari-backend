using DARI_API.Models;
using DARI_API.Seeder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.HttpOverrides;
using DARI_API.AiSearch;
using DARI_API.ServicesLayer;
using DARI_API.IServicesLayer;
using DARI_API.Services;

namespace DARI_API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<IServiceLayer, ServiceLayer>();

            builder.Services.AddHttpClient("Groq");

            builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection("Cloudinary"));
            builder.Services.AddSingleton<ICloudinaryService, CloudinaryService>();

            // Allowed origins: dev + Vercel production. Extra origins can be
            // appended via the "Cors:AllowedOrigins" config array (e.g. Azure
            // App Service → Configuration) without redeploying.
            var defaultOrigins = new[]
            {
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "https://dari-frontend-lnzm.vercel.app",
                "https://dari-frontend-lnzm-git-main-georges-projects-2b86278b.vercel.app",
                "https://dari-frontend-lnzm-4ph7osbl6-georges-projects-2b86278b.vercel.app",
            };
            var configuredOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();
            var allowedOrigins = defaultOrigins.Concat(configuredOrigins).Distinct().ToArray();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("DariDev", policy =>
                    policy.SetIsOriginAllowed(origin =>
                          {
                              if (allowedOrigins.Contains(origin)) return true;
                              // Accept any *.vercel.app deployment (preview + prod aliases).
                              if (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                                  && uri.Scheme == "https"
                                  && (uri.Host == "vercel.app" || uri.Host.EndsWith(".vercel.app")))
                                  return true;
                              return false;
                          })
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials());
            });

            builder.Services
                .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            builder.Services.AddHttpClient("CVService", client =>
            {
                client.BaseAddress = new Uri("http://localhost:8000");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            

            var jwt = builder.Configuration.GetSection("JWT");

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.SaveToken = true;
                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = jwt["ValidIssuer"],
                    ValidAudience = jwt["ValidAudience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwt["Secret"]))
                };
            });

            // ── AI Search pipeline ────────────────────────────────────────
            builder.Services.AddMemoryCache();
            builder.Services.AddHttpClient(); // default factory used by GeminiExtractionService
            builder.Services.AddSingleton<UserAiSearchQuota>();
            builder.Services.AddSingleton<IAiSearchLogger, AiSearchLogger>();
            builder.Services.AddSingleton<IAiExtractionService, GeminiExtractionService>();
            builder.Services.AddSingleton<IStreetTransliterationService, GeminiStreetTransliterationService>();
            builder.Services.AddSingleton<IListingTranslationService, GeminiListingTranslationService>();
            builder.Services.AddScoped<SearchExecutor>();
            builder.Services.AddScoped<AiSearchPipeline>();

            // Per-IP token bucket for /api/AiSearch/search — paired with the
            // per-user weekly quota inside the controller. Catches scripted abuse
            // before the AI call is made.
            builder.Services.AddRateLimiter(options =>
            {
                options.AddPolicy("ai-search", httpContext =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                        factory: _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 10,
                            QueueLimit = 0,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            TokensPerPeriod = 10,
                            AutoReplenishment = true,
                        }));

                // Per-IP cap on listing view recording so a script can't hammer
                // the DB even though most rows would be discarded by 24h dedup.
                options.AddPolicy("listing-views", httpContext =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                        factory: _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 60,
                            QueueLimit = 0,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            TokensPerPeriod = 60,
                            AutoReplenishment = true,
                        }));
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });

            // Behind Azure App Service (and any reverse proxy) the real client IP
            // arrives in X-Forwarded-For. Without this, RemoteIpAddress is the
            // proxy's IP and every visitor would collapse to one VisitorHash.
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler =
                        System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                });

            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "DARI API", Version = "v1" });

                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Paste your JWT token here (without 'Bearer ' prefix)."
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id   = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                await RoleSeeder.SeedRolesAsync(roleManager);
                await SeedSuperAdmin.SeedAsync(services);
            }

            // Swagger enabled in all environments while the project is in
            // early access. Gate behind IsDevelopment() once the API is public.
            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseForwardedHeaders();
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCors("DariDev");
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();

            app.MapControllers();

            await app.RunAsync();
        }
    }
}