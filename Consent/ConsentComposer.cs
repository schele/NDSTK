using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace NDSTK.Consent;

public sealed class ConsentComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();

        builder.Services
            .AddOptions<ConsentOptions>()
            .BindConfiguration(ConsentOptions.SectionName);

        builder.Services.AddScoped<IConsentState, ConsentState>();
    }
}
