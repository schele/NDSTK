using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Security;

namespace NDSTK.Booking.Security;

/// <summary>
/// How long a verification link stays valid, and under which data protection purpose.
/// </summary>
/// <remarks>
/// A subclass rather than a call to <c>Configure&lt;DataProtectionTokenProviderOptions&gt;</c>,
/// because that type is a single unnamed options instance shared by every Identity token provider
/// in the application - members and backoffice users alike, since both are registered with
/// <c>AddDefaultTokenProviders()</c>. Shortening it there would also cut the backoffice user invite
/// and password reset links from a day to a quarter of an hour, and a new editor would find the
/// link in their invitation mail dead before they had finished reading it.
///
/// Giving the member verification token its own options type keeps the change to the one link the
/// club actually sends. <see cref="MemberVerificationTokenProvider"/> binds to it, and nothing else
/// does.
/// </remarks>
public sealed class MemberVerificationTokenOptions : DataProtectionTokenProviderOptions
{
    /// <summary>
    /// Short on purpose. The link activates an account, so a copy of it sitting in a mailbox
    /// backup, a forwarded message or a shared inbox is worth having for as little time as
    /// possible. Nothing is lost by expiring quickly: submitting the registration form again
    /// resends a fresh link to an account that has not been verified yet, so the recovery path is
    /// the same one somebody takes when the first mail never arrived.
    /// </summary>
    public static readonly TimeSpan Lifespan = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Identifies the provider in <see cref="IdentityOptions"/>.<c>Tokens.ProviderMap</c>, and -
    /// through <see cref="DataProtectionTokenProviderOptions.Name"/> - is also the data protection
    /// purpose string. Prefixed so it cannot collide with a provider Umbraco or a package adds.
    /// </summary>
    public const string ProviderName = "NdstkMemberVerification";

    public MemberVerificationTokenOptions()
    {
        Name = ProviderName;
        TokenLifespan = Lifespan;
    }
}

/// <summary>
/// Issues and validates the token in the member verification link.
/// </summary>
/// <remarks>
/// Identical to Identity's own <see cref="DataProtectorTokenProvider{TUser}"/> apart from which
/// options it reads. <c>IOptions&lt;T&gt;</c> is covariant, so the derived options above satisfy
/// the base constructor without any adapter.
/// </remarks>
public sealed class MemberVerificationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<MemberVerificationTokenOptions> options,
    ILogger<DataProtectorTokenProvider<MemberIdentityUser>> logger)
    : DataProtectorTokenProvider<MemberIdentityUser>(dataProtectionProvider, options, logger);
