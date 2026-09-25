namespace Imortais.LinuxClient.Models;

public sealed class CombatPlayer
{
    public string Name { get; init; } = string.Empty;
    public long Damage { get; set; }
    public long Healing { get; set; }
    public long DamagePending { get; set; }
    public long HealingPending { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
}
