using System.Reflection;
using Xunit;

namespace Inferior.Game.Test;

public class ShipRecordContainmentTests
{
    [Fact]
    public void ShipRecord_ShouldNotLeakOutsidePermittedTypes()
    {
        var permitted = new[] { "ShipBuilder", "ShipPersistenceService", "ShipExtensions" };

        // Walk up the declaring-type chain so compiler-generated nested types
        // (e.g. async state machines) inherit the permission of their outer class.
        bool IsPermitted(Type t)
        {
            for (var cur = t; cur != null; cur = cur.DeclaringType)
                if (permitted.Any(p => cur.Name.Contains(p)))
                    return true;
            return false;
        }

        var violations = typeof(InferiorGame).Assembly
            .GetTypes()
            .Where(t => !IsPermitted(t))
            .Where(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static)
                         .Any(m => m.ToString()!.Contains("ShipRecord")))
            .Select(t => t.FullName!)
            .ToList();

        Assert.Empty(violations);
    }
}
