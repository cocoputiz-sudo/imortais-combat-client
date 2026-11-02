using StatisticsAnalysisTool.Enumerations;

namespace StatisticsAnalysisTool.Models;

public class SlotSpell
{
    public SlotType SlotType { get; set; }
    public int Value { get; set; }
    public int ItemIndex { get; set; }
    // Button the player presses for it (Q, W, E, R, D, F, Poção, Comida), from the packet slot order.
    public string Key { get; set; }
}