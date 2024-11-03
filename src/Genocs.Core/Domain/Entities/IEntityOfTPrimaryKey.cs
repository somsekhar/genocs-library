﻿namespace Genocs.Core.Domain.Entities;

/// <summary>
/// Defines interface for base entity type. All the domain object must implement this interface.
/// </summary>
/// <typeparam name="TPrimaryKey">Type of the primary key of the entity.</typeparam>
public interface IEntity<TPrimaryKey>
{
    /// <summary>
    /// Unique identifier for this entity.
    /// </summary>
    TPrimaryKey Id { get; set; }
}
