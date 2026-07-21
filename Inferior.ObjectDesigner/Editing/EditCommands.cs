using Inferior.Core.Math;

namespace Inferior.ObjectDesigner.Editing;

public interface IEditCommand
{
    string Description { get; }
    void Execute(ObjectDesignerSession session);
    void Undo(ObjectDesignerSession session);
}

public sealed record MoveVertexCommand(
    string VertexId,
    DVec3 Before,
    DVec3 After,
    string Description = "Move vertex") : IEditCommand
{
    public void Execute(ObjectDesignerSession session) => session.SetVertexPosition(VertexId, After);
    public void Undo(ObjectDesignerSession session) => session.SetVertexPosition(VertexId, Before);
}

public sealed class EditHistory
{
    private readonly List<IEditCommand> _commands = [];
    private int _nextIndex;
    private int _cleanIndex;

    public bool CanUndo => _nextIndex > 0;
    public bool CanRedo => _nextIndex < _commands.Count;
    public bool IsDirty => _nextIndex != _cleanIndex;

    public void Execute(IEditCommand command, ObjectDesignerSession session)
    {
        if (_nextIndex < _commands.Count)
            _commands.RemoveRange(_nextIndex, _commands.Count - _nextIndex);
        command.Execute(session);
        _commands.Add(command);
        _nextIndex++;
    }

    public void Undo(ObjectDesignerSession session)
    {
        if (!CanUndo)
            return;
        _nextIndex--;
        _commands[_nextIndex].Undo(session);
    }

    public void Redo(ObjectDesignerSession session)
    {
        if (!CanRedo)
            return;
        _commands[_nextIndex].Execute(session);
        _nextIndex++;
    }

    public void MarkClean() => _cleanIndex = _nextIndex;
    public void ResetClean()
    {
        _commands.Clear();
        _nextIndex = 0;
        _cleanIndex = 0;
    }
}
