using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

using WorkIQChat.Data.Models;

namespace WorkIQChat.Server.Components.Account.Shared;

public partial class ExternalLoginPicker : ComponentBase
{
    [Inject] private SignInManager<User> SignInManager { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private AuthenticationScheme[] _externalLogins = [];

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _externalLogins = (await SignInManager.GetExternalAuthenticationSchemesAsync()).ToArray();
    }
}