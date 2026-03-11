using Gates;

namespace GateSimMac.Models;

public enum PortSide
{
    Top = 0,
    Left = 1,
    Right = 2,
    Bottom = 3,
}

public sealed class TerminalLayout
{
    public bool IsInput { get; init; }
    public int PortIndex { get; init; }
    public PortSide Side { get; init; }
    public int SideOrdinal { get; init; }
    public int SideCount { get; init; }
}

public sealed class GatePlacement
{
    public required int Id { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required AbstractGate Gate { get; init; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Angle { get; set; }

    public double Width { get; set; }
    public double Height { get; set; }

    public string CommentText { get; init; }

    public List<TerminalLayout> Terminals { get; } = new();

    private readonly Dictionary<(bool IsInput, int PortIndex), TerminalLayout> _terminalLookup = new();

    public void RebuildTerminalLookup()
    {
        _terminalLookup.Clear();
        foreach (TerminalLayout terminal in Terminals)
        {
            _terminalLookup[(terminal.IsInput, terminal.PortIndex)] = terminal;
        }
    }

    public TerminalLayout GetTerminal(bool isInput, int portIndex)
    {
        if (!_terminalLookup.TryGetValue((isInput, portIndex), out TerminalLayout terminal))
        {
            throw new KeyNotFoundException($"Gate '{Name}' ({Type}) has no {(isInput ? "input" : "output")} port {portIndex}.");
        }

        return terminal;
    }
}

public sealed class WirePlacement
{
    public required GatePlacement FromGate { get; init; }
    public required int FromPort { get; init; }
    public required GatePlacement ToGate { get; init; }
    public required int ToPort { get; init; }
}

public sealed class LoadedCircuit
{
    public required string DisplayName { get; init; }
    public required bool IsNamed { get; init; }
    public required Circuit Circuit { get; init; }
    public required List<GatePlacement> Gates { get; init; }
    public required List<WirePlacement> Wires { get; init; }
}

public sealed class LoadedProject
{
    public required string SourcePath { get; init; }
    public required List<LoadedCircuit> Circuits { get; init; }
    public required int DefaultCircuitIndex { get; init; }
    public required List<NamedIcTemplate> NamedIcTemplates { get; init; }
}

public sealed class NamedIcTemplate
{
    public required string Name { get; init; }
    public required IC Template { get; init; }
    public required List<TerminalLayout> Terminals { get; init; }
}
