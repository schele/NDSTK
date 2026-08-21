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
internal sealed class NdstkContentTypeFactory(
    IContentTypeService contentTypeService,
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

    public async Task<IDataType> EnsureDataTypeAsync(
        Guid key,
        string name,
        string editorAlias,
        string editorUiAlias,
        IDictionary<string, object>? configuration = null)
    {
        IDataType? existing = await dataTypeService.GetAsync(key);
        if (existing is not null)
        {
            return existing;
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
}
