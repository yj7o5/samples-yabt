using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Raven.Yabt.Database.Common;
using Raven.Yabt.Database.Common.BacklogItem;
using Raven.Yabt.Database.Common.References;
using Raven.Yabt.Database.Models.BacklogItems;
using Raven.Yabt.Database.Models.BacklogItems.Indexes;
using Raven.Yabt.Domain.BacklogItemServices.Commands.DTOs;
using Raven.Yabt.Domain.Common;
using Raven.Yabt.Domain.CustomFieldServices.Query;
using Raven.Yabt.Domain.CustomFieldServices.Query.DTOs;
using Raven.Yabt.Domain.Helpers;
using Raven.Yabt.Domain.UserServices.Query;

namespace Raven.Yabt.Domain.BacklogItemServices.Commands
{
	public interface IBacklogItemDtoToEntityConversion
	{
		Task<TModel> ConvertToEntity<TModel, TDto>(TDto dto, TModel? entity = null)
			where TModel : BacklogItem, new() where TDto : BacklogItemAddUpdRequestBase;
	}
	
	public class BacklogItemDtoToEntityConversion : BaseService<BacklogItem>, IBacklogItemDtoToEntityConversion
	{
		private readonly IUserReferenceResolver _userResolver;
		private readonly ICustomFieldListQueryService _customFieldQueryService;

		public BacklogItemDtoToEntityConversion(IAsyncDocumentSession dbSession, IUserReferenceResolver userResolver, ICustomFieldListQueryService customFieldQueryService) : base(dbSession)
		{
			_userResolver = userResolver;
			_customFieldQueryService = customFieldQueryService;
		}

		public async Task<TModel> ConvertToEntity<TModel, TDto>(TDto dto, TModel? entity = null)
			where TModel : BacklogItem, new()
			where TDto : BacklogItemAddUpdRequestBase
		{
			entity ??= new TModel();

			entity.Title = dto.Title;
			entity.State = dto.State;
			entity.EstimatedSize = dto.EstimatedSize;
			entity.Tags = dto.Tags;
			entity.Assignee = dto.AssigneeId != null ? await _userResolver.GetReferenceById(dto.AssigneeId) : null;
	
			entity.AddHistoryRecord(
				await _userResolver.GetCurrentUserReference(), 
				entity.ModifiedBy.Any() ? "Modified" : "Created"	// TODO: Provide more informative description in case of modifications
			);

			if (dto.ChangedCustomFields != null)
			{
				entity.CustomFields ??= new Dictionary<string, object>();
				await ResolveChangedCustomFields(entity.CustomFields, dto.ChangedCustomFields);
			}

			await ResolveChangedRelatedItems(entity.RelatedItems, dto.ChangedRelatedItems);

			if (dto is BugAddUpdRequest bugDto && entity is BacklogItemBug bugEntity)
			{
				bugEntity.Severity = bugDto.Severity;
				bugEntity.Priority = bugDto.Priority;
				bugEntity.StepsToReproduce = bugDto.StepsToReproduce;
				bugEntity.AcceptanceCriteria = bugDto.AcceptanceCriteria;
			}
			else if (dto is UserStoryAddUpdRequest storyDto && entity is BacklogItemUserStory storyEntity)
			{
				storyEntity.AcceptanceCriteria = storyDto.AcceptanceCriteria;
			}
			else if (dto is TaskAddUpdRequest taskDto && entity is BacklogItemTask taskEntity)
			{
				taskEntity.Description = taskDto.Description;
			}
			else if (dto is FeatureAddUpdRequest featureDto && entity is BacklogItemFeature featureEntity)
			{
				featureEntity.Description = featureDto.Description;
			}

			return entity;
		}

		private async Task ResolveChangedRelatedItems(List<BacklogItemRelatedItem> existingRelatedItems, IList<BacklogRelationshipAction>? actions)
		{
			if (actions == null)
				return;

			// Remove 'old' links
			foreach (var (id, linkType) in from a in actions
				where a.ActionType == ListActionType.Remove
				select (a.BacklogItemId, a.RelationType))
			{
				existingRelatedItems.RemoveAll(existing => existing.RelatedTo.Id == id && existing.LinkType == linkType);
			}
			
			// Add new links
			(string fullId, BacklogRelationshipType linkType)[] array = 
				(from a in actions
					where a.ActionType == ListActionType.Add
					select (GetFullId(a.BacklogItemId), a.RelationType)
				).ToArray();
			if (array.Any())
			{
				// Resolve new references
				var fullIds = array.Select(a => a.fullId).Distinct();
				var references = await (from b in DbSession.Query<BacklogItemIndexedForList, BacklogItems_ForList>()
					where b.Id.In(fullIds)
					select new BacklogItemReference
					{
						Id = b.Id,
						Name = b.Title,
						Type = b.Type
					}).ToListAsync();

				// Add resolved references
				foreach (var (fullId, linkType) in array)
				{
					var relatedTo = references.SingleOrDefault(r => r.Id == fullId);
					if (relatedTo == null)
						continue;
					existingRelatedItems.Add(new BacklogItemRelatedItem { LinkType = linkType, RelatedTo = relatedTo.RemoveEntityPrefixFromId() });
				}
			}
		}
	
		private async Task ResolveChangedCustomFields(IDictionary<string, object> existingFields, IList<BacklogCustomFieldAction>? actions)
		{
			if (actions == null)
				return;

			// Remove 'old' fields
			foreach (var id in from a in actions
				where a.ActionType == ListActionType.Remove || a.Value is null
				select a.CustomFieldId)
			{
				existingFields.Remove(id);
			}
			
			// Add new fields
			var array = 
				(from a in actions
					where a.ActionType == ListActionType.Add	// Keep only the ones that we're adding
					select a
				).ToList();
			if (array.Any())
			{
				var fieldRequest = new CustomFieldListGetRequest
					{
						Ids = array.Select(a => a.CustomFieldId).Distinct(),
						PageSize = Int32.MaxValue
					};
				var verifiedCustomFields = await _customFieldQueryService.GetArray(fieldRequest);

				foreach (var a in array)
				{
					var field = verifiedCustomFields.SingleOrDefault(f => f.Id == a.CustomFieldId);
					if (a.Value is null || field is null)
						continue;

					var obj = field.FieldType switch
					{
						CustomFieldType.Text or CustomFieldType.Url => a.GetValue<string>(),
						CustomFieldType.Numeric => a.GetValue<decimal>(),
						CustomFieldType.Date => a.GetValue<DateTime>(),
						_ => throw new ArgumentOutOfRangeException($"Unsupported field type: {field.FieldType}")
					};
					if (obj is null)
						continue;
					
					if (existingFields.ContainsKey(a.CustomFieldId))
						existingFields[a.CustomFieldId] = obj;
					else
						existingFields.Add(a.CustomFieldId, obj);
				}
			}
		}
	}
}