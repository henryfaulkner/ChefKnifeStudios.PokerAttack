using ChefKnifeStudios.PokerAttack.Server.Data.Repos;
using System.ComponentModel.DataAnnotations;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

public abstract class BaseEntity : IAggregateRoot
{
    [Key]
    public virtual int Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? ModifiedOnUtc { get; set; }
}
