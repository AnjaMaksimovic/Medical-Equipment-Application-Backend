﻿using ISAProject.Modules.Stakeholders.Infrastructure;

namespace API.Startup
{
    public static class ModulesConfiguration
    {
        public static IServiceCollection RegisterModules(this IServiceCollection services)
        {
            services.ConfigureStakeholdersModule();
            return services;
        }
    }
}