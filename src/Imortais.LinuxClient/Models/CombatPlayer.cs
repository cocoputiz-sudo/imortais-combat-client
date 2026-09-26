namespace Imortais.LinuxClient.Models;

public sealed class CombatPlayer
{
    public string Name = string.Empty;
    public long Damage;
    public long Healing;
    public long DamagePending;
    public long HealingPending;
    public int Kills;
    public int Deaths;
}
