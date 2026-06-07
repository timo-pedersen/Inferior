namespace Inferior.Core;

/// <summary>
/// Determines the order in which a PowerPriorityManager distributes bus power
/// when total demand exceeds supply. Lower value = served first.
/// </summary>
public enum PowerPriority
{
    Critical = 0,  // life support — never starved
    High     = 1,  // navigation, engines — starved last
    Normal   = 2,  // weapons, shields
    Low      = 3,  // luxury, non-essential — starved first
}
