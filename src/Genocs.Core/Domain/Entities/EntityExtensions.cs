﻿using Genocs.Core.Domain.Entities.Auditing;
using Genocs.Core.Extensions;

namespace Genocs.Core.Domain.Entities;

/// <summary>
/// Some useful extension methods for Entities.
/// </summary>
public static class EntityExtensions
{
    /// <summary>
    /// Check if this Entity is null of marked as deleted.
    /// </summary>
    public static bool IsNullOrDeleted(this ISoftDelete entity)
    {
        return entity == null || entity.IsDeleted;
    }

    /// <summary>
    /// Undeletes this entity by setting <see cref="ISoftDelete.IsDeleted"/> to false and
    /// <see cref="IDeletionAudited"/> properties to null.
    /// </summary>
    public static void UnDelete(this ISoftDelete entity)
    {
        entity.IsDeleted = false;
        if (entity is IDeletionAudited)
        {
            var deletionAuditedEntity = entity.As<IDeletionAudited>();
            deletionAuditedEntity.DeletedAt = null;
            deletionAuditedEntity.DeletedBy = null;
        }
    }
}