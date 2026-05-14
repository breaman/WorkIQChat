using Microsoft.AspNetCore.Identity;

using WorkIQChat.Data.Interfaces;

namespace WorkIQChat.Data.Models;

public class Role : IdentityRole<int>, IEntityBase
{
}