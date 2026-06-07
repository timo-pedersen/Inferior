namespace Inferior.Core.DataBus;

/// <summary>
/// A command sent from the main thread (UI) to a component or sensor on the sim thread.
/// Topic follows the same convention as instrument topics: "ComponentName.Property.Verb"
/// e.g. "Reactor.Throttle.Set", "Reactor.Output.Query"
/// Value is optional — queries and state-change commands that need no parameter use 0.
/// </summary>
public readonly record struct ComponentCommand(string Topic, double Value = 0.0);
