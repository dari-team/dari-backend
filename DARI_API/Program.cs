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

            // CORS for Vite dev server (port 5173). Lock this down before prod.
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("DariDev", policy =>
                    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
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
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

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