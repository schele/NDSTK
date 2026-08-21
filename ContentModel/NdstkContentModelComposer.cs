using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace NDSTK.ContentModel;

/// <summary>
/// Wires up the code-first content model: the schema installer and the first-run content seeder
/// both run from a single notification handler once Umbraco has finished booting.
/// </summary>
public sealed class NdstkContentModelComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<NdstkContentTypeFactory>();
        builder.Services.AddSingleton<NdstkLanguageInstaller>();
        builder.Services.AddSingleton<NdstkContentModelInstaller>();
        builder.Services.AddSingleton<NdstkContentSeeder>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, NdstkContentModelInstallHandler>();
    }
}
