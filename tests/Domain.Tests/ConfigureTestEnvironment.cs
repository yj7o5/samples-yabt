﻿using System;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Session;
using Raven.TestDriver;
using Raven.Yabt.Database;
using Raven.Yabt.Domain.Common;
using Raven.Yabt.Domain.Infrastructure;

namespace Raven.Yabt.Domain.Tests
{
	/// <summary>
	///		Base test class to configure the environment (IoC and Raven)
	/// </summary>
	public abstract class ConfigureTestEnvironment : RavenTestDriver
	{
		protected IServiceProvider Container { get; }
		protected IAsyncDocumentSession DbSession => Container.GetService<IAsyncDocumentSession>()!;

		/// <summary>
		///		The default c-tor initialising all the IoC interfaces
		/// </summary>
		protected ConfigureTestEnvironment()
		{
			var services = new ServiceCollection();
			// ReSharper disable once VirtualMemberCallInConstructor
			ConfigureIocContainer(services);
			Container = services.BuildServiceProvider();
		}

		protected override void PreInitialize(IDocumentStore store)
		{
			store.PreInitializeDocumentStore();
			store.Conventions.MaxNumberOfRequestsPerSession = 200;

			base.PreInitialize(store);
		}

		/// <summary>
		///		Save changes in the DB
		/// </summary>
		protected async Task SaveChanges()
		{
			await DbSession.SaveChangesAsync();

			// Process all patch requests
			var asyncPatchesHandlers = Container.GetServices<IPatchOperationsExecuteAsync>();
			foreach (var handler in asyncPatchesHandlers)
				await handler.SendAsyncDeferredPatchByQueryOperations(true);
			
			DbSession.Advanced.Clear(); // Clear all cached entities
		}

		/// <summary>
		///		Configure IoC, register all dependencies
		/// </summary>
		protected virtual void ConfigureIocContainer(IServiceCollection services)
		{
			services.RegisterModules(Assembly.GetAssembly(typeof(BaseService<>))!);

			// Register the document store & session
			services.AddScoped(_ =>
				{
					IDocumentStore store = GetDocumentStore();
					// Create all indexes
					IndexCreation.CreateIndexes(typeof(SetupDocumentStore).Assembly, store, null, store.Database);
					return store;
				});
			services.AddScoped(c =>
				{
					var session = c.GetService<IDocumentStore>()!.OpenAsyncSession(new SessionOptions { NoCaching = true });
						session.Advanced.WaitForIndexesAfterSaveChanges();  // Wait on each change to avoid adding WaitForIndexing() in each test
					return session;
				});
		}

		/// <summary>
		///		Returns a string with random content (8 char long)
		/// </summary>
		protected static string GetRandomString()
		{
			const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
			var stringChars = new char[8];
			var random = new Random();

			for (var i = 0; i < stringChars.Length; i++)
			{
				stringChars[i] = chars[random.Next(chars.Length)];
			}

			return new string(stringChars);
		}
	}
}
