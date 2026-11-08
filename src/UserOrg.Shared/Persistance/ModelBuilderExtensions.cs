using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using UserOrg.Shared.Extensions.Extensions;
using UserOrg.Shared.Persistance.Abstractions.BaseEntities;
using UserOrg.Shared.Persistance.Abstractions.Enums;
using UserOrg.Shared.Persistance.Abstractions.Interfaces;

namespace UserOrg.Shared.Persistance;

/// <summary>
/// Provides extension methods for configuring entity types in an Entity Framework Core model builder, specifically to
/// apply auditing and external entity conventions to entities derived from AuditableExternalEntity<T>.
/// </summary>
/// <remarks>These extensions help standardize the configuration of auditing-related properties, such as creation
/// and modification timestamps, deletion flags, service names, external identifiers, and operation modes. Use these
/// methods to ensure consistent auditing behavior across all applicable entities in the data model.</remarks>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Configures all entities in the model that inherit from AuditableExternalEntity<> with properties required for
    /// auditing and external identification.
    /// </summary>
    /// <remarks>This method sets up properties such as CreatedAt, ModifiedAt, IsDeleted, ServiceName,
    /// ExternalId, and Mode for each auditable entity type. These properties enable tracking of creation and
    /// modification times, soft deletion status, service association, external identifiers, and operational mode. Use
    /// this method to ensure consistent auditing and metadata across all relevant entities.</remarks>
    /// <param name="modelBuilder">The model builder used to configure entity types. This parameter cannot be null.</param>
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Configures all entities in the model that inherit from EnumEntity<T> to ensure they have required Name and
        /// optional Description properties with appropriate constraints.
        /// </summary>
        /// <remarks>This method should be called during the model configuration phase to apply consistent
        /// property settings for enum entities. It enforces maximum length and Unicode support for the Name and
        /// Description fields, helping to standardize how these properties are stored in the database.</remarks>
        public void ApplyEnumEntityConfiguration()
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            var auditableBase = typeof(EnumEntity<>);
            var entityClrTypes = modelBuilder.Model.GetEntityTypes()
                .Where(t => t.ClrType is not null && auditableBase.IsSubclassOfRawGeneric(t.ClrType!))
                .Select(t => t.ClrType!)
                .ToList();

            foreach (var clrType in entityClrTypes)
            {
                var builder = modelBuilder.Entity(clrType);

                builder.Property<string>("Name")
                  .IsRequired()
                  .HasMaxLength(DbConstants.NameMaxLength)
                  .HasColumnType($"nvarchar({DbConstants.NameMaxLength})")
                  .IsUnicode(true);

                builder.Property<string>("Description")
                  .IsRequired(false)
                  .HasMaxLength(DbConstants.DescriptionMaxLength)
                  .HasColumnType($"nvarchar({DbConstants.DescriptionMaxLength})")
                  .IsUnicode(true);
            }
        }

        /// <summary>
        /// Configures the model builder to apply auditing properties to all entities that support auditing.
        /// </summary>
        /// <remarks>This method adds standard auditing properties—such as creation time, modification
        /// time, and deletion state—to all entity types in the model that implement the IAuditableEntity interface or
        /// inherit from AuditableEntity<T>. These properties enable tracking of entity lifecycle events for auditing
        /// purposes. The method should be called during model configuration to ensure that auditing is consistently
        /// applied across relevant entities.</remarks>
        public void ApplyAuditableEntityConfiguration()
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            var auditableBase = typeof(AuditableEntity<>);
            var auditableInterface = typeof(IAuditableEntity);
            var entityClrTypes = modelBuilder.Model.GetEntityTypes()
                .Where(t => t.ClrType is not null
                    && (auditableInterface.IsAssignableFrom(t.ClrType!) || auditableBase.IsSubclassOfRawGeneric(t.ClrType!)))
                .Select(t => t.ClrType!)
                .ToList();

            foreach (var clrType in entityClrTypes)
            {
                var builder = modelBuilder.Entity(clrType);

                builder.Property<DateTime>("CreatedAt")
                   .IsRequired()
                   .HasColumnType($"datetime2({DbConstants.DateTimePrecision})")
                   .HasDefaultValueSql(DbConstants.UtcNowSql)
                   .ValueGeneratedOnAdd();

                builder.Property<DateTime?>("ModifiedAt")
                    .HasColumnType($"datetime2({DbConstants.DateTimePrecision})")
                    .HasDefaultValueSql(DbConstants.UtcNowSql)
                    .HasDefaultValueSql("null")
                    .ValueGeneratedOnUpdate();

                builder.Property<bool>("IsDeleted")
                    .IsRequired()
                    .HasDefaultValue(false);
            }
        }

        /// <summary>
        /// Applies configuration settings for entities derived from AuditableServiceIntegrationEntity<T> to the current
        /// model builder instance.
        /// </summary>
        /// <remarks>This method configures the 'ServiceName' and 'Mode' properties for all entities that
        /// inherit from AuditableServiceIntegrationEntity<T>. The 'ServiceName' property is required, has a maximum
        /// length defined by DbConstants.NameMaxLength, and is stored as a non-Unicode string. The 'Mode' property is
        /// also required, defaults to EOperationMode.Unspecified, and is stored as an integer value. Use this method to
        /// ensure consistent configuration of auditable service integration entities within the model.</remarks>
        public void ApplyAuditableServiceIntegrationEntityConfiguration()
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            var auditableBase = typeof(AuditableServiceIntegrationEntity<>);
            var entityClrTypes = modelBuilder.Model.GetEntityTypes()
                .Where(t => t.ClrType is not null && auditableBase.IsSubclassOfRawGeneric(t.ClrType!))
                .Select(t => t.ClrType!)
                .ToList();

            foreach (var clrType in entityClrTypes)
            {
                var builder = modelBuilder.Entity(clrType);

                builder.Property<string>("ServiceName")
                    .IsRequired()
                    .HasMaxLength(DbConstants.NameMaxLength)
                    .HasDefaultValueSql($"'{ApplicationConstants.DefaultServiceNameInDatabase}'")
                    .HasColumnType($"nvarchar({DbConstants.NameMaxLength})")
                    .IsUnicode(false);

                builder.Property<EOperationMode>("Mode")
                    .IsRequired()
                    .HasDefaultValue(EOperationMode.Unspecified)
                    .HasConversion(
                        v => (int)v,
                        v => (EOperationMode)v);
            }
        }

        /// <summary>
        /// Configures entity properties for all types derived from AuditableExternalEntity<T> to support auditing and
        /// external integration features.
        /// </summary>
        /// <remarks>This method applies standardized property configuration to entities inheriting from
        /// AuditableExternalEntity<T>, including creation and modification timestamps, deletion status, service name,
        /// external identifier, and operation mode. It ensures required constraints, default values, and column types
        /// are set for these properties, facilitating consistent auditing and external data handling across the model.
        /// This method should be called during model configuration to enable these features for relevant
        /// entities.</remarks>
        public void ApplyAuditableExtendedServiceIntegrationEntityConfiguration()
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            var auditableBase = typeof(AuditableExtendedServiceIntegrationEntity<>);
            var entityClrTypes = modelBuilder.Model.GetEntityTypes()
                .Where(t => t.ClrType is not null && auditableBase.IsSubclassOfRawGeneric(t.ClrType!))
                .Select(t => t.ClrType!)
                .ToList();

            foreach (var clrType in entityClrTypes)
            {
                var builder = modelBuilder.Entity(clrType);

                builder.Property<string?>("ExternalId")
                    .IsRequired(false)
                    .HasDefaultValue(null)
                    .HasDefaultValueSql("null")
                    .HasMaxLength(DbConstants.ExternalIdMaxLength)
                    .HasColumnType($"nvarchar({DbConstants.ExternalIdMaxLength})")
                    .IsUnicode(false);
            }
        }

        /// <summary>
        /// Applies client-side UUID v7 value generation to single-<see cref="Guid"/> primary keys, dropping any
        /// store-side default. A <see cref="UuidV7ValueGenerator"/> stamps each key at <c>Add</c> time
        /// (<see cref="ValueGenerated.OnAdd"/>) so entities without an explicit key receive a unique v7 instead of
        /// <c>Guid.Empty</c>. The generator is client-side and permanent, so no store default is needed and
        /// migrations remain zero-diff. Composite keys and integer identity keys are skipped.
        /// </summary>
        public void ApplyCodeGeneratedGuidKeys()
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var clrType = entityType.ClrType;
                if (clrType is null || clrType.IsAbstract)
                    continue;

                var key = entityType.FindPrimaryKey();
                if (key is null || key.Properties.Count != 1)
                    continue;

                var keyProperty = key.Properties[0];
                var providerType = keyProperty.GetValueConverter()?.ProviderClrType
                    ?? keyProperty.GetProviderClrType()
                    ?? keyProperty.ClrType;
                if (Nullable.GetUnderlyingType(providerType) is { } underlying)
                    providerType = underlying;
                if (providerType != typeof(Guid))
                    continue;

                builder(modelBuilder, clrType, keyProperty.Name);
            }

            static void builder(ModelBuilder mb, Type clr, string name)
            {
                var property = mb.Entity(clr).Property(name);
                property.HasDefaultValueSql(null);   // drop legacy NewId() DB default — code generates UUID v7
                property.HasValueGenerator((p, _) => new UuidV7ValueGenerator(p.GetValueConverter()));
                property.Metadata.ValueGenerated = ValueGenerated.OnAdd;
            }
        }

        /// <summary>
        /// Applies query filters to exclude entities marked as deleted, enabling soft delete functionality for entities
        /// implementing the IAuditableEntity interface.
        /// </summary>
        /// <remarks>Call this method during the model configuration phase to ensure that soft delete
        /// query filters are applied across all relevant entity types. Only entities where IsDeleted is false will be
        /// included in query results, allowing deleted entities to be retained in the database but excluded from
        /// standard queries.</remarks>
        public void ApplySoftDeleteQueryFilters()
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {

                if (entityType.ClrType.IsInterface || entityType.ClrType.IsAbstract)
                    continue;

                if (typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
                {
                    var parameter = Expression.Parameter(entityType.ClrType, "e");
                    var property = Expression.Property(parameter, nameof(IAuditableEntity.IsDeleted));
                    var filter = Expression.Lambda(Expression.Not(property), parameter);

                    modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
                }
            }
        }
    }

}