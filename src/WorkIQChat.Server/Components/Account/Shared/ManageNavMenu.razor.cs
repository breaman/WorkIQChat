using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

using WorkIQChat.Data.Models;

namespace WorkIQChat.Server.Components.Account.Shared;

public partial class ManageNavMenu : ComponentBase
{
    [Inject] private SignInManager<User> SignInManager { get; set; } = default!;

    private bool _hasExternalLogins;

    protected override async Task OnInitializedAsync()
    {
        _hasExternalLogins = (await SignInManager.GetExternalAuthenticationSchemesAsync()).Any();
    }
}