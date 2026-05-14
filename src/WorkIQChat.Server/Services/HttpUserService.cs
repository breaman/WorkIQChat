using System.Security.Claims;

using WorkIQChat.Data.Interfaces;

namespace WorkIQChat.Server.Services;

public class HttpUserService : IUserService
{
    private IHttpContextAccessor HttpContextAccessor { get; }
    public HttpUserService(IHttpContextAccessor httpContextAccessor)
    {
        HttpContextAccessor = httpContextAccessor;
    }
    public int UserId
    {
        get
        {
            return Convert.ToInt32(HttpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
    }
}