namespace Inferior.Core.DataBus;

public enum SystemMessagePriority
{
    Info,             // 1 — routine status, navigation, economy info
    NB,               // 2 — noteworthy, mild alert
    Warning,          // 3 — something needs attention
    ImportantWarning, // 4 — immediate attention required
    Critical          // 5 — urgent, potentially life-threatening to ship
}

public readonly record struct SystemMessage(
    string Text,
    SystemMessagePriority Priority = SystemMessagePriority.Info);
