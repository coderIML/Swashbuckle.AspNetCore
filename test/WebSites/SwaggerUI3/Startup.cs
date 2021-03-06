﻿using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.Cookies;
using SwaggerUI3.ResourceServer.Swagger;
using Swashbuckle.AspNetCore.Swagger;

namespace SwaggerUI3
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Register IdentityServer services to power OAuth2.0 flows
            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryClients(AuthServer.Config.Clients())
                .AddInMemoryApiResources(AuthServer.Config.ApiResources())
                .AddTestUsers(AuthServer.Config.TestUsers());

            // The auth setup is a little nuanced because this app provides the auth-server & the resource-server
            // Use the "Cookies" scheme by default & explicitly require "Bearer" in the resource-server controllers
            // See https://docs.microsoft.com/en-us/aspnet/core/security/authorization/limitingidentitybyscheme?tabs=aspnetcore2x
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie()
                .AddIdentityServerAuthentication(c =>
                {
                    c.Authority = "http://localhost:60891/auth-server/";
                    c.RequireHttpsMetadata = false;
                    c.ApiName = "api";
                });

            // Configure named auth policies that map directly to OAuth2.0 scopes
            services.AddAuthorization(c =>
            {
                c.AddPolicy("readAccess", p => p.RequireClaim("scope", "readAccess"));
                c.AddPolicy("writeAccess", p => p.RequireClaim("scope", "writeAccess"));
            });

            services.AddMvc();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1",
                    new Info
                    {
                        Title = "SwaggerUI V3 Example (with OAuth2)",
                        Version = "v1"
                    }
                );

                // Define the OAuth2.0 scheme that's in use (i.e. Implicit Flow)
                c.AddSecurityDefinition("oauth2", new OAuth2Scheme
                {
                    Type = "oauth2",
                    Flow = "implicit",
                    AuthorizationUrl = "/auth-server/connect/authorize",
                    Scopes = new Dictionary<string, string>
                    {
                        { "readAccess", "Access read operations" },
                        { "writeAccess", "Access write operations" }
                    }
                });
                // Assign scope requirements to operations based on AuthorizeAttribute
                c.OperationFilter<SecurityRequirementsOperationFilter>();
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseDeveloperExceptionPage();

            app.Map("/auth-server", authServer =>
            {
                authServer.UseAuthentication();
                authServer.UseIdentityServer();
                authServer.UseMvc();
            });

            app.Map("/resource-server", resourceServer =>
            {
                resourceServer.UseAuthentication();
                resourceServer.UseMvc();

                resourceServer.UseSwagger();
                resourceServer.UseSwaggerUI3(c =>
                {
                    // Core
                    c.SwaggerEndpoint("/resource-server/swagger/v1/swagger.json", "V1");

                    // Display
                    c.DefaultModelExpandDepth(2);
                    c.DefaultModelRendering(ModelRendering.Model);
                    c.DefaultModelsExpandDepth(-1);
                    c.DisplayOperationId();
                    c.DisplayRequestDuration();
                    c.DocExpansion(DocExpansion.None);
                    c.EnableDeepLinking();
                    c.EnableFilter();
                    c.ShowExtensions();

                    // Network
                    c.OAuth2RedirectUrl("http://localhost:60891/resource-server/swagger/oauth2-redirect.html");
                    c.ValidatorUrl(null);
                });
            });
        }
    }
}
