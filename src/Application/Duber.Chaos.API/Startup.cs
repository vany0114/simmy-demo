﻿using Duber.Chaos.API.Infrastructure.Repository;
using Duber.Infrastructure.Chaos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Duber.Chaos.API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddApplicationInsightsTelemetry(Configuration)
                .AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            services.AddDistributedRedisCache(option =>
            {
                option.Configuration = Configuration.GetValue<string>("ConnectionString");
                option.InstanceName = "master";
            });

            services.AddSingleton<IChaosRepository, ChaosRepository>();
            services.Configure<GeneralChaosSetting>(Configuration.GetSection("GeneralChaosSetting"));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMvc();
        }
    }
}
