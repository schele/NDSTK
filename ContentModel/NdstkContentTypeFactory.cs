using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace NDSTK.ContentModel;

/// <summary>
/// Thin wrapper over the Umbraco services that turns the declarative descriptions in
/// <see cref="NdstkContentModelInstaller"/> into persisted schema. Every Ensure* method is
/// create-if-missing: an entity that already exists is returned untouched, so changes made in
/// the backoffice survive an app restart.
/// </summary>
/// <remarks>
/// The create-if-missing rule has one consequence worth spelling out: <see cref="EnsureContentTypeAsync"/>
/// runs its <c>configure</c> callback only for a brand new type, so it cannot roll a new field
/// out to a site that is already installed. <see cref="EnsureGroupAsync"/> and
/// <see cref="EnsureMemberPropertiesAsync"/> exist for exactly that, and add only what is
/// missing.
/// </remarks>
internal sealed class NdstkContentTypeFactory(
    IContentTypeService contentTypeService,
    IMemberTypeService memberTypeService,
    IDataTypeService dataTypeService,
    ITemplateService templateService,
    PropertyEditorCollection propertyEditors,
    IConfigurationEditorJsonSerializer configurationSerializer,
    IShortStringHelper shortStringHelper)
{
    private const int RootParentId = -1;
    private static readonly Guid UserKey = Constants.Security.SuperUserKey;

    private readonly Dictionary<Guid, IDataType> _dataTypes = [];

    public async Task<ITemplate> EnsureTemplateAsync(Guid key, string name, string alias, string content)
    {
        ITemplate? existing = await templateService.GetAsync(key);
        if (existing is not null)
        {
            return existing;
        }

        var template = new Template(shortStringHelper, name, alias)
        {
            Key = key,
            Content = content,
        };

        var attempt = await templateService.CreateAsync(template, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException($"Could not create template '{alias}': {attempt.Status}.");
        }

        return attempt.Result!;
    }

    /// <param name="storageType">
    /// Which column the values land in. Worth stating for anything that is not text: the editor's
    /// own default is not applied by this constructor, so a date editor created without it stores
    /// Ntext, and a property bound to it then cannot hold a date at all. Left null for the editors
    /// where Ntext is right anyway.
    /// </param>
    public async Task<IDataType> EnsureDataTypeAsync(
        Guid key,
        string name,
        string editorAlias,
        string editorUiAlias,
        IDictionary<string, object>? configuration = null,
        ValueStorageType? storageType = null)
    {
        IDataType? existing = await dataTypeService.GetAsync(key);
        if (existing is not null)
        {
            // Corrected rather than accepted. These data types are declared in code, so code is
            // the source of truth for how they store their values - and one created with the wrong
            // storage type is unusable until it is put right, which no amount of restarting would
            // otherwise achieve.
            if (storageType is not { } wanted || existing.DatabaseType == wanted)
            {
                return existing;
            }

            existing.DatabaseType = wanted;

            var correction = await dataTypeService.UpdateAsync(existing, UserKey);
            if (correction.Success is false)
            {
                throw new InvalidOperationException(
                    $"Could not correct the storage type of '{name}': {correction.Status}.");
            }

            return correction.Result;
        }

        if (propertyEditors.TryGet(editorAlias, out IDataEditor? editor) is false)
        {
            throw new InvalidOperationException($"No property editor is registered for alias '{editorAlias}'.");
        }

        var dataType = new DataType(editor, configurationSerializer, RootParentId)
        {
            Key = key,
            Name = name,
            EditorUiAlias = editorUiAlias,
        };

        if (storageType is { } declared)
        {
            dataType.DatabaseType = declared;
        }

        dataType.SetConfigurationData(configuration ?? new Dictionary<string, object>());

        var attempt = await dataTypeService.CreateAsync(dataType, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException($"Could not create data type '{name}': {attempt.Status}.");
        }

        return attempt.Result;
    }

    /// <summary>
    /// Loads the data types that <see cref="Property"/> will bind to. Doing it up front keeps the
    /// schema declarations synchronous and therefore readable.
    /// </summary>
    public async Task PreloadDataTypesAsync(params Guid[] keys)
    {
        foreach (Guid key in keys.Distinct().Where(key => _dataTypes.ContainsKey(key) is false))
        {
            _dataTypes[key] = await dataTypeService.GetAsync(key)
                              ?? throw new InvalidOperationException($"Data type {key} was not found.");
        }
    }

    /// <summary>Builds a property type bound to one of the preloaded data types.</summary>
    public IPropertyType Property(
        Guid dataTypeKey,
        string alias,
        string name,
        string? description = null,
        int sortOrder = 0)
    {
        if (_dataTypes.TryGetValue(dataTypeKey, out IDataType? dataType) is false)
        {
            throw new InvalidOperationException($"Data type {dataTypeKey} was not preloaded.");
        }

        return new PropertyType(shortStringHelper, dataType, alias)
        {
            Name = name,
            Description = description,
            SortOrder = sortOrder,
            Variations = ContentVariation.Nothing,
        };
    }

    /// <summary>
    /// Creates a document type or element type when it is missing. <paramref name="configure"/>
    /// only runs for a brand new type, so existing schema is never rewritten.
    /// </summary>
    public async Task<IContentType> EnsureContentTypeAsync(
        Guid key,
        string alias,
        string name,
        string icon,
        Action<IContentType> configure)
    {
        IContentType? existing = contentTypeService.Get(key);
        if (existing is not null)
        {
            return existing;
        }

        var contentType = new ContentType(shortStringHelper, RootParentId)
        {
            Key = key,
            Alias = alias,
            Name = name,
            Icon = icon,
            Variations = ContentVariation.Nothing,
        };

        configure(contentType);

        var attempt = await contentTypeService.CreateAsync(contentType, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException($"Could not create content type '{alias}': {attempt.Result}.");
        }

        return contentTypeService.Get(key)
               ?? throw new InvalidOperationException($"Content type '{alias}' was created but could not be read back.");
    }

    /// <summary>
    /// Applies the allowed-children list in a second pass, once every document type exists.
    /// </summary>
    public async Task SetAllowedChildrenAsync(Guid key, params (Guid Key, string Alias)[] children)
    {
        IContentType contentType = contentTypeService.Get(key)
                                   ?? throw new InvalidOperationException($"Content type {key} was not found.");

        ContentTypeSort[] desired = children
            .Select((child, index) => new ContentTypeSort(child.Key, index, child.Alias))
            .ToArray();

        HashSet<Guid> current = contentType.AllowedContentTypes?.Select(x => x.Key).ToHashSet() ?? [];
        if (current.SetEquals(desired.Select(x => x.Key)))
        {
            return;
        }

        contentType.AllowedContentTypes = desired;

        var attempt = await contentTypeService.UpdateAsync(contentType, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException($"Could not set allowed children on '{contentType.Alias}': {attempt.Result}.");
        }
    }

    public static void AddGroup(
        IContentType contentType,
        Guid key,
        string alias,
        string caption,
        int sortOrder,
        params IPropertyType[] properties)
        => contentType.PropertyGroups.Add(new PropertyGroup(true)
        {
            Key = key,
            Alias = alias,
            Name = caption,
            Type = PropertyGroupType.Tab,
            SortOrder = sortOrder,
            PropertyTypes = new PropertyTypeCollection(true, properties),
        });

    public static void UseTemplate(IContentType contentType, ITemplate template)
    {
        contentType.AllowedTemplates = [template];
        contentType.SetDefaultTemplate(template);
    }

    // ------------------------------------------------------------------ upgrades

    /// <summary>
    /// Adds properties to a document type that already exists, creating the group if it is not
    /// there. Properties are matched by alias, so a re-run changes nothing and an editor's own
    /// additions to the group survive.
    /// </summary>
    /// <param name="groupKey">
    /// Applied only when this call creates the group. Umbraco would otherwise assign a random key,
    /// which would make a uSync export differ between environments for no reason.
    /// </param>
    /// <returns>True when something was added, so the caller can log only real changes.</returns>
    public async Task<bool> EnsureGroupAsync(
        Guid contentTypeKey,
        Guid groupKey,
        string groupAlias,
        string groupCaption,
        params IPropertyType[] properties)
    {
        IContentType contentType = contentTypeService.Get(contentTypeKey)
                                   ?? throw new InvalidOperationException($"Content type {contentTypeKey} was not found.");

        var groupExisted = contentType.PropertyGroups.Any(group => group.Alias == groupAlias);
        var changed = false;

        foreach (IPropertyType property in properties)
        {
            if (contentType.PropertyTypeExists(property.Alias))
            {
                // Already there, but its label and help text may be out of date - changing the
                // declaration above would otherwise leave the backoffice showing the old wording
                // for ever, because adding is the only thing this used to do. Those two are
                // code-owned in a code-first model, so they are kept in step.
                changed |= SyncLabels(contentType, property);
                continue;
            }

            contentType.AddPropertyType(property, groupAlias, groupCaption);
            changed = true;
        }

        if (changed is false)
        {
            return false;
        }

        if (groupExisted is false)
        {
            PropertyGroup? created = contentType.PropertyGroups.FirstOrDefault(group => group.Alias == groupAlias);
            if (created is not null)
            {
                created.Key = groupKey;
            }
        }

        var attempt = await contentTypeService.UpdateAsync(contentType, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException(
                $"Could not add group '{groupAlias}' to '{contentType.Alias}': {attempt.Result}.");
        }

        return true;
    }

    /// <summary>
    /// Brings an existing property's label and description into line with the declaration.
    /// </summary>
    /// <remarks>
    /// Deliberately limited to those two. They are help text an editor reads rather than data they
    /// own, so code is the source of truth and overwriting is safe. The data type, sort order and
    /// alias are left alone: changing those on a live site moves or reinterprets stored values,
    /// which is a migration rather than a label fix.
    /// </remarks>
    private static bool SyncLabels(IContentTypeBase contentType, IPropertyType declared)
    {
        IPropertyType? existing = contentType.PropertyTypes
            .FirstOrDefault(property => property.Alias == declared.Alias);

        if (existing is null)
        {
            return false;
        }

        var changed = false;

        if (existing.Name != declared.Name)
        {
            existing.Name = declared.Name;
            changed = true;
        }

        if (existing.Description != declared.Description)
        {
            existing.Description = declared.Description;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Adds properties to the member type. The two visibility flags matter: the membership expiry
    /// and the first-class discount are administrative facts, so a member is allowed to see them
    /// but never to edit them - a member who could edit their own expiry date would have a free
    /// membership.
    /// </summary>
    public async Task<bool> EnsureMemberPropertiesAsync(
        string memberTypeAlias,
        string groupAlias,
        string groupCaption,
        params (IPropertyType Property, bool MemberCanView, bool MemberCanEdit)[] properties)
    {
        IMemberType memberType = memberTypeService.Get(memberTypeAlias)
                                 ?? throw new InvalidOperationException($"Member type '{memberTypeAlias}' was not found.");

        var changed = false;

        foreach ((IPropertyType property, bool canView, bool canEdit) in properties)
        {
            if (memberType.PropertyTypeExists(property.Alias))
            {
                // Same reasoning as EnsureGroupAsync: the label and help text follow the code.
                changed |= SyncLabels(memberType, property);
                continue;
            }

            memberType.AddPropertyType(property, groupAlias, groupCaption);
            memberType.SetMemberCanViewProperty(property.Alias, canView);
            memberType.SetMemberCanEditProperty(property.Alias, canEdit);
            changed = true;
        }

        if (changed is false)
        {
            return false;
        }

        var attempt = await memberTypeService.UpdateAsync(memberType, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException(
                $"Could not add properties to member type '{memberTypeAlias}': {attempt.Result}.");
        }

        return true;
    }

    /// <summary>
    /// Points an existing property at a different data type. Returns false when it already does.
    /// </summary>
    /// <remarks>
    /// The third kind of upgrade, alongside <see cref="EnsureGroupAsync"/> and
    /// <see cref="EnsureMemberPropertiesAsync"/>. Those add fields that are missing;
    /// <see cref="EnsureContentTypeAsync"/> never revisits a type that exists. Neither can change
    /// the editor behind a field that is already there, which is what reaches a live site when the
    /// choice of data type turns out to be wrong.
    ///
    /// Safe only while the two data types agree on <c>ValueStorageType</c> - the stored values are
    /// not touched, so swapping a date editor for another date editor keeps every existing value
    /// readable, and swapping one for a text editor would not. That is asserted rather than assumed.
    /// </remarks>
    public async Task<bool> RepointPropertyAsync(Guid contentTypeKey, string propertyAlias, Guid dataTypeKey)
    {
        IContentType? contentType = await contentTypeService.GetAsync(contentTypeKey);
        if (contentType is null)
        {
            return false;
        }

        IPropertyType? property = contentType.PropertyTypes
            .FirstOrDefault(candidate => candidate.Alias == propertyAlias);

        if (property is null || property.DataTypeKey == dataTypeKey)
        {
            return false;
        }

        IDataType dataType = await dataTypeService.GetAsync(dataTypeKey)
                             ?? throw new InvalidOperationException($"Data type {dataTypeKey} was not found.");

        if (property.ValueStorageType != dataType.DatabaseType)
        {
            throw new InvalidOperationException(
                $"Refusing to repoint '{propertyAlias}' at {dataType.Name}: it stores "
                + $"{dataType.DatabaseType} where the property holds {property.ValueStorageType}, "
                + "so existing values would become unreadable.");
        }

        property.DataTypeId = dataType.Id;
        property.DataTypeKey = dataType.Key;
        property.PropertyEditorAlias = dataType.EditorAlias;

        var attempt = await contentTypeService.UpdateAsync(contentType, UserKey);
        if (attempt.Success is false)
        {
            throw new InvalidOperationException(
                $"Could not repoint '{propertyAlias}' on '{contentType.Alias}': {attempt.Result}.");
        }

        return true;
    }
}
