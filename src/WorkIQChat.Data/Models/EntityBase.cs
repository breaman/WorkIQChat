using WorkIQChat.Data.Interfaces;

namespace WorkIQChat.Data.Models;

public abstract class EntityBase : IEntityBase
{
    public int Id { get; set; }
}