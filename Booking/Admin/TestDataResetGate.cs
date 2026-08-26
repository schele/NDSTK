using Microsoft.Extensions.Configuration;

namespace NDSTK.Booking.Admin;

/// <summary>
/// Decides whether the test data reset exists at all.
/// </summary>
/// <remarks>
/// Two conditions, both required, because one of them can be got wrong by accident. Running in the
/// Development environment is the obvious gate - but the environment comes from
/// ASPNETCORE_ENVIRONMENT, and a deployment that forgets to set it runs as Development on the live
/// site. So a setting has to be turned on as well, and it is only ever turned on in
/// appsettings.Development.json, which is not the file a hosting panel edits.
///
/// Neither gate is authorisation: the endpoints are behind Umbraco's Members section policy too.
/// These two are about the button not existing on a site with real members in it.
/// </remarks>
public sealed class TestDataResetGate(IHostEnvironment environment, IConfiguration configuration)
{
    private const string SettingKey = "NDSTK:AllowTestDataReset";

    public bool IsEnabled =>
        environment.IsDevelopment() && configuration.GetValue<bool>(SettingKey);
}
