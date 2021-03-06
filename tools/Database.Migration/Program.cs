﻿using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Raven.Migrations;
using Raven.Yabt.Database.Migration.Configuration;

namespace Raven.Yabt.Database.Migration
{
	class Program
	{
		public static async Task<int> Main(string[] args)
		{
			using (var host = CreateHostBuilder(args).Build())
			{
				await host.StartAsync();
				var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

				// insert other console app code here

				lifetime.StopApplication();
				await host.WaitForShutdownAsync();
			}
			return 0;
		}

		private static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureAppConfiguration((builderContext, config) =>
				{
					// The 'CreateDefaultBuilder()' resolves 'DOTNET_' environments, then the WebAPI uses the 'ASPNETCORE_' ones, so add it manually
					var env = builderContext.HostingEnvironment;
						env.EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
					config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);
				})
				.UseConsoleLifetime()
				.ConfigureServices(ConfigureServices);

		private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
		{
			// Register appsettings.json
			services.AddAndConfigureAppSettings(context);
			// Register the Migration service
			services.AddHostedService<MigrationService>();
			// Register the document store
			services.AddAndConfigureDatabase();
			// Add the MigrationRunner into the dependency injection container.
			services.AddRavenDbMigrations();
		}
	}
}
