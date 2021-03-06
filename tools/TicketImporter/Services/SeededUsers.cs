﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AutoBogus;

using Bogus;

using Raven.Yabt.Database.Common.References;
using Raven.Yabt.Domain.UserServices.Command;
using Raven.Yabt.Domain.UserServices.Command.DTOs;
using Raven.Yabt.Domain.UserServices.Query;
using Raven.Yabt.Domain.UserServices.Query.DTOs;
using Raven.Yabt.TicketImporter.Configuration;

namespace Raven.Yabt.TicketImporter.Services
{
	internal class SeededUsers: ISeededUsers
	{
		private readonly GeneratedRecordsSettings _importSettings;
		private readonly List<UserReference> _userRefs = new ();
		private readonly IUserCommandService _userCmdService;
		private readonly IUserQueryService _userQueryService;
		private readonly Faker<UserAddUpdRequest> _userFaker;

		public SeededUsers(AppSettings settings, IUserCommandService userCmdService, IUserQueryService userQueryService)
		{
			_importSettings = settings.GeneratedRecords;
			_userCmdService = userCmdService;
			_userQueryService = userQueryService;

			_userFaker = new AutoFaker<UserAddUpdRequest>()
			             .RuleFor(fake => fake.AvatarUrl, _ => null)
			             .RuleFor(fake => fake.FirstName, fake => fake.Name.FirstName())
			             .RuleFor(fake => fake.LastName,  fake => fake.Name.LastName())
			             .RuleFor(fake => fake.Email,	 (_, p) => $"{p.FirstName}.{p.LastName}@yabt.com");
		}

		public async Task<IList<UserReference>> GetGeneratedOrFetchedUsers()
		{
			// Returned cached users if any
			if (_userRefs.Count > 0)
				return _userRefs;
			
			// If don't need to generate, read from the DB
			var userList = await _userQueryService.GetList(new UserListGetRequest { PageSize = _importSettings.NumberOfUsers });
			if (userList.TotalRecords > 0)
			{
				_userRefs!.AddRange(
					userList.Entries.Select(u => new UserReference { Id = u.Id, Name = u.NameWithInitials, FullName = u.FullName })
				);
				return _userRefs!;
			}

			// Generate users
			for (var i=0; i < _importSettings.NumberOfUsers; i++)
			{
				var dto = _userFaker.Generate();
				var resp = await _userCmdService.Create(dto);
				if (!resp.IsSuccess)
					throw new Exception("Failed to create a new user");
				
				_userRefs!.Add(resp.Value);
			}
			return _userRefs!;
		}
	}
}
