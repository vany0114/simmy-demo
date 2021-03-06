﻿using Duber.Domain.ACL.Adapters;
using Duber.Domain.ACL.Contracts;
using Duber.Domain.Invoice.Persistence;
using Duber.Domain.Invoice.Repository;
using Duber.Domain.Invoice.Services;
using Duber.Infrastructure.Chaos;
using Duber.Infrastructure.Resilience.Abstractions;
using Duber.Infrastructure.Resilience.Http;
using Duber.Infrastructure.Resilience.Sql;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Duber.Invoice.API.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static string HttpResiliencePolicy = "HttpResiliencePolicy";

        public static IServiceCollection AddPersistenceAndRepository(this IServiceCollection services, IConfiguration configuration)
        {
            // just to perform the migrations
            services.AddDbContext<InvoiceMigrationContext>(options =>
            {
                options.UseSqlServer(
                    configuration["ConnectionStrings:InvoiceDB"],
                    sqlOptions =>
                    {
                        sqlOptions.MigrationsAssembly(typeof(InvoiceMigrationContext).GetTypeInfo().Assembly.GetName().Name);
                        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
                    });
            });

            // Invoice repository and context configuration
            services.AddTransient<IInvoiceContext, InvoiceContext>(provider =>
            {
                var mediator = provider.GetService<IMediator>();
                var sqlExecutor = provider.GetService<IPolicyAsyncExecutor>();
                var chaosSettingsFactory = provider.GetService<Lazy<Task<GeneralChaosSetting>>>();
                var connectionString = configuration["ConnectionStrings:InvoiceDB"];
                return new InvoiceContext(connectionString, mediator, sqlExecutor, chaosSettingsFactory);
            });

            services.AddTransient<IInvoiceRepository, InvoiceRepository>();
            return services;
        }

        public static IServiceCollection AddResilientStrategies(this IServiceCollection services, IConfiguration configuration)
        {
            // Resilient SQL Executor configuration.
            services.AddSingleton<IPolicyAsyncExecutor>(sp =>
            {
                var sqlPolicyBuilder = new SqlPolicyBuilder();
                return sqlPolicyBuilder
                    .UseAsyncExecutor()
                    .WithDefaultPolicies()
                    .Build();
            });

            // Create (and register with DI) a policy registry containing some policies we want to use.
            var policyRegistry = services.AddPolicyRegistry();
            policyRegistry[HttpResiliencePolicy] = GetResiliencePolicy(configuration);

            // Resilient Http Invoker onfiguration.
            // Register a typed client via HttpClientFactory, set to use the policy we placed in the policy registry.
            services.AddHttpClient<ResilientHttpClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(50);
            }).AddPolicyHandlerFromRegistry(HttpResiliencePolicy);

            return services;
        }

        public static IServiceCollection AddCustomSwagger(this IServiceCollection services)
        {
            // swagger configuration
            services.AddSwaggerGen(options =>
            {
                options.DescribeAllEnumsAsStrings();
                options.SwaggerDoc("v1", new Swashbuckle.AspNetCore.Swagger.Info
                {
                    Title = "Duber.Invoice HTTP API",
                    Version = "v1",
                    Description = "The Duber Invoice Service HTTP API",
                    TermsOfService = "Terms Of Service"
                });

                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetEntryAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                options.IncludeXmlComments(xmlPath);
            });

            return services;
        }

        public static IServiceCollection AddPaymentService(this IServiceCollection services, IConfiguration configuration)
        {
            // payment service configuration
            services.AddTransient<IPaymentService, PaymentService>();
            services.AddTransient<IPaymentServiceAdapter, PaymentServiceAdapter>(provider =>
            {
                var httpInvoker = provider.GetRequiredService<ResilientHttpClient>();
                var paymentServiceBaseUrl = configuration["PaymentServiceBaseUrl"];
                var chaosSettingsFactory = provider.GetService<Lazy<Task<GeneralChaosSetting>>>();
                return new PaymentServiceAdapter(httpInvoker, paymentServiceBaseUrl, chaosSettingsFactory);
            });

            return services;
        }

        private static IAsyncPolicy<HttpResponseMessage> GetResiliencePolicy(IConfiguration configuration)
        {
            var retryCount = 5;
            if (!string.IsNullOrEmpty(configuration["HttpClientRetryCount"]))
            {
                retryCount = int.Parse(configuration["HttpClientRetryCount"]);
            }

            var exceptionsAllowedBeforeBreaking = 4;
            if (!string.IsNullOrEmpty(configuration["HttpClientExceptionsAllowedBeforeBreaking"]))
            {
                exceptionsAllowedBeforeBreaking = int.Parse(configuration["HttpClientExceptionsAllowedBeforeBreaking"]);
            }

            // Define a couple of policies which will form our resilience strategy.
            var policies = HttpPolicyExtensions.HandleTransientHttpError()
                .RetryAsync(retryCount)
                .WrapAsync(HttpPolicyExtensions.HandleTransientHttpError()
                    .CircuitBreakerAsync(exceptionsAllowedBeforeBreaking, TimeSpan.FromSeconds(5)));

            return policies;
        }
    }
}
