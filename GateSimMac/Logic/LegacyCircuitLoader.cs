using System.Globalization;
using System.Xml.Linq;
using Gates;
using Gates.IOGates;
using GateSimMac.Models;

namespace GateSimMac.Logic;

public static class LegacyCircuitLoader
{
    private sealed class NamedIcDefinition
    {
        public required IC Template { get; init; }
        public required List<TerminalLayout> Terminals { get; init; }
    }

    private sealed class BuiltCircuit
    {
        public required Circuit Circuit { get; init; }
        public required Dictionary<int, GatePlacement> PlacementsById { get; init; }
        public required Dictionary<AbstractGate, GatePlacement> PlacementsByGate { get; init; }
        public required List<WirePlacement> Wires { get; init; }
    }

    private sealed class IcTerminalSeed
    {
        public required AbstractGate Gate { get; init; }
        public required bool IsInput { get; init; }
        public required PortSide Side { get; init; }
        public required double Offset { get; init; }
    }

    public static LoadedProject LoadProject(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Circuit file not found.", path);
        }

        XElement root = XElement.Load(path);
        if (!string.Equals(root.Name.LocalName, "CircuitGroup", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unsupported file: root element must be CircuitGroup.");
        }

        XAttribute? version = root.Attribute("Version");
        if (version != null && version.Value != "1.2")
        {
            throw new InvalidDataException($"Unsupported circuit version '{version.Value}'.");
        }

        var namedIcs = new Dictionary<string, NamedIcDefinition>(StringComparer.Ordinal);
        var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);

        var circuits = new List<LoadedCircuit>();
        int unnamedCount = 0;
        int? defaultCircuitIndex = null;

        foreach (XElement circuitElement in root.Elements("Circuit"))
        {
            string? declaredName = (string?)circuitElement.Attribute("Name");
            if (!string.IsNullOrEmpty(declaredName) && namedIcs.ContainsKey(declaredName))
            {
                renameMap[declaredName] = GenerateAvailableName(declaredName, name => namedIcs.ContainsKey(name));
            }

            BuiltCircuit built = BuildCircuit(circuitElement, namedIcs, renameMap);

            if (!string.IsNullOrEmpty(declaredName))
            {
                string finalName = renameMap.TryGetValue(declaredName, out string? renamed) ? renamed : declaredName;
                NamedIcDefinition def = BuildNamedIc(finalName, built);
                namedIcs[finalName] = def;

                circuits.Add(ToLoadedCircuit(finalName, isNamed: true, built));
            }
            else
            {
                unnamedCount++;
                string displayName = unnamedCount == 1 ? "(Main)" : $"(Main {unnamedCount})";
                circuits.Add(ToLoadedCircuit(displayName, isNamed: false, built));
                defaultCircuitIndex ??= circuits.Count - 1;
            }
        }

        if (circuits.Count == 0)
        {
            throw new InvalidDataException("No circuits were found in this file.");
        }

        if (!defaultCircuitIndex.HasValue)
        {
            defaultCircuitIndex = circuits.Count - 1;
        }

        return new LoadedProject
        {
            SourcePath = path,
            Circuits = circuits,
            DefaultCircuitIndex = defaultCircuitIndex.Value,
        };
    }

    private static LoadedCircuit ToLoadedCircuit(string displayName, bool isNamed, BuiltCircuit built)
    {
        List<GatePlacement> gates = built.PlacementsById.Values.OrderBy(g => g.Id).ToList();
        foreach (GatePlacement gate in gates)
        {
            gate.RebuildTerminalLookup();
        }

        return new LoadedCircuit
        {
            DisplayName = displayName,
            IsNamed = isNamed,
            Circuit = built.Circuit,
            Gates = gates,
            Wires = built.Wires,
        };
    }

    private static BuiltCircuit BuildCircuit(
        XElement circuitElement,
        Dictionary<string, NamedIcDefinition> namedIcs,
        Dictionary<string, string> renameMap)
    {
        XElement gatesElement = circuitElement.Element("Gates")
            ?? throw new InvalidDataException("Circuit is missing the Gates element.");
        XElement wiresElement = circuitElement.Element("Wires")
            ?? throw new InvalidDataException("Circuit is missing the Wires element.");

        var circuit = new Circuit();
        var gateById = new Dictionary<int, AbstractGate>();
        var placementsById = new Dictionary<int, GatePlacement>();
        var placementsByGate = new Dictionary<AbstractGate, GatePlacement>();
        var wires = new List<WirePlacement>();

        foreach (XElement gateElement in gatesElement.Elements("Gate"))
        {
            int id = ParseIntAttribute(gateElement, "ID");
            string type = ParseStringAttribute(gateElement, "Type");

            AbstractGate gate = CreateGate(type, gateElement, namedIcs, renameMap);
            circuit.Add(gate);
            gateById[id] = gate;

            GatePlacement placement = CreatePlacement(gateElement, id, type, gate, namedIcs);
            placementsById[id] = placement;
            placementsByGate[gate] = placement;
        }

        foreach (XElement wireElement in wiresElement.Elements("Wire"))
        {
            XElement from = wireElement.Element("From")
                ?? throw new InvalidDataException("Wire is missing the From element.");
            XElement to = wireElement.Element("To")
                ?? throw new InvalidDataException("Wire is missing the To element.");

            int fromId = ParseIntAttribute(from, "ID");
            int fromPort = ParseIntAttribute(from, "Port");
            int toId = ParseIntAttribute(to, "ID");
            int toPort = ParseIntAttribute(to, "Port");

            if (!gateById.TryGetValue(fromId, out AbstractGate? fromGate) ||
                !gateById.TryGetValue(toId, out AbstractGate? toGate))
            {
                throw new InvalidDataException("Wire references an unknown gate ID.");
            }

            circuit[new Terminal(toPort, toGate)] = new Terminal(fromPort, fromGate);

            wires.Add(new WirePlacement
            {
                FromGate = placementsById[fromId],
                FromPort = fromPort,
                ToGate = placementsById[toId],
                ToPort = toPort,
            });
        }

        return new BuiltCircuit
        {
            Circuit = circuit,
            PlacementsById = placementsById,
            PlacementsByGate = placementsByGate,
            Wires = wires,
        };
    }

    private static NamedIcDefinition BuildNamedIc(string name, BuiltCircuit built)
    {
        List<UserInput> userInputs = new();
        List<UserOutput> userOutputs = new();

        foreach (AbstractGate gate in built.Circuit)
        {
            if (gate is UserInput ui)
            {
                userInputs.Add(ui);
            }

            if (gate is UserOutput uo)
            {
                userOutputs.Add(uo);
            }
        }

        List<IcTerminalSeed> seeds = ComputeIcTerminalSeeds(built.PlacementsByGate);

        seeds.Sort((a, b) =>
        {
            int sideCmp = a.Side.CompareTo(b.Side);
            return sideCmp != 0 ? sideCmp : a.Offset.CompareTo(b.Offset);
        });

        var terminals = new List<(bool IsInput, int PortIndex, PortSide Side)>();
        foreach (IcTerminalSeed seed in seeds)
        {
            if (seed.IsInput)
            {
                terminals.Add((true, userInputs.IndexOf((UserInput)seed.Gate), seed.Side));
            }
            else
            {
                terminals.Add((false, userOutputs.IndexOf((UserOutput)seed.Gate), seed.Side));
            }
        }

        List<TerminalLayout> terminalLayouts = BuildTerminalLayouts(terminals);

        return new NamedIcDefinition
        {
            Template = new IC(built.Circuit, userInputs.ToArray(), userOutputs.ToArray(), name),
            Terminals = terminalLayouts,
        };
    }

    private static List<IcTerminalSeed> ComputeIcTerminalSeeds(Dictionary<AbstractGate, GatePlacement> placementsByGate)
    {
        if (placementsByGate.Count == 0)
        {
            return new List<IcTerminalSeed>();
        }

        double minX = placementsByGate.Values.Min(p => p.X);
        double maxX = placementsByGate.Values.Max(p => p.X);
        double minY = placementsByGate.Values.Min(p => p.Y);
        double maxY = placementsByGate.Values.Max(p => p.Y);

        double avgX = (minX + maxX) / 2.0;
        double avgY = (minY + maxY) / 2.0;

        var seeds = new List<IcTerminalSeed>();

        foreach ((AbstractGate gate, GatePlacement placement) in placementsByGate)
        {
            if (gate is not UserInput && gate is not UserOutput)
            {
                continue;
            }

            bool isInput = gate is UserInput;
            double dx = placement.X - avgX;
            double dy = placement.Y - avgY;

            PortSide side;
            double offset;

            if (Math.Abs(dx) > Math.Abs(dy))
            {
                if (dx < 0)
                {
                    side = PortSide.Left;
                    offset = -placement.Y;
                }
                else
                {
                    side = PortSide.Right;
                    offset = placement.Y;
                }
            }
            else
            {
                if (dy < 0)
                {
                    side = PortSide.Top;
                    offset = placement.X;
                }
                else
                {
                    side = PortSide.Bottom;
                    offset = placement.X;
                }
            }

            seeds.Add(new IcTerminalSeed
            {
                Gate = gate,
                IsInput = isInput,
                Side = side,
                Offset = offset,
            });
        }

        return seeds;
    }

    private static GatePlacement CreatePlacement(
        XElement gateElement,
        int id,
        string type,
        AbstractGate gate,
        Dictionary<string, NamedIcDefinition> namedIcs)
    {
        XElement point = gateElement.Element("Point")
            ?? throw new InvalidDataException($"Gate {id} is missing Point coordinates.");

        string name = (string?)gateElement.Attribute("Name") ?? gate.Name;
        string? comment = gate is Comment cmt ? cmt.Value : null;

        List<TerminalLayout> terminals = GetGateTerminalLayouts(type, gate, namedIcs);
        (double width, double height) = EstimateGateSize(type, name, comment, terminals);

        var placement = new GatePlacement
        {
            Id = id,
            Type = type,
            Name = name,
            Gate = gate,
            X = ParseDoubleAttribute(point, "X"),
            Y = ParseDoubleAttribute(point, "Y"),
            Angle = ParseDoubleAttribute(point, "Angle"),
            Width = width,
            Height = height,
            CommentText = comment,
        };

        placement.Terminals.AddRange(terminals);
        placement.RebuildTerminalLookup();

        return placement;
    }

    private static List<TerminalLayout> GetGateTerminalLayouts(
        string type,
        AbstractGate gate,
        Dictionary<string, NamedIcDefinition> namedIcs)
    {
        List<(bool IsInput, int PortIndex, PortSide Side)> descriptors = type switch
        {
            "And" or "Not" or "Or" or "Nand" or "Nor" or "Xor" or "Xnor" or "Buffer" =>
                BuildShapeGateDescriptors(gate),
            "UserInput" => new List<(bool, int, PortSide)> { (false, 0, PortSide.Right) },
            "UserOutput" => new List<(bool, int, PortSide)> { (true, 0, PortSide.Left) },
            "NumericInput" or "NumericOutput" => BuildNumericDescriptors(gate),
            "Clock" => new List<(bool, int, PortSide)> { (false, 0, PortSide.Top) },
            "Comment" => new List<(bool, int, PortSide)>(),
            "IC" => BuildIcDescriptors((IC)gate, namedIcs),
            _ => throw new InvalidDataException($"Unsupported gate type '{type}'."),
        };

        return BuildTerminalLayouts(descriptors);
    }

    private static List<(bool IsInput, int PortIndex, PortSide Side)> BuildShapeGateDescriptors(AbstractGate gate)
    {
        var descriptors = new List<(bool, int, PortSide)>();

        for (int i = 0; i < gate.NumberOfInputs; i++)
        {
            descriptors.Add((true, i, PortSide.Left));
        }

        descriptors.Add((false, 0, PortSide.Right));
        return descriptors;
    }

    private static List<(bool IsInput, int PortIndex, PortSide Side)> BuildNumericDescriptors(AbstractGate gate)
    {
        var descriptors = new List<(bool, int, PortSide)>();

        for (int i = 0; i < gate.NumberOfInputs; i++)
        {
            descriptors.Add((true, gate.NumberOfInputs - i - 1, PortSide.Top));
        }

        for (int i = 0; i < gate.Output.Length; i++)
        {
            descriptors.Add((false, gate.Output.Length - i - 1, PortSide.Bottom));
        }

        return descriptors;
    }

    private static List<(bool IsInput, int PortIndex, PortSide Side)> BuildIcDescriptors(
        IC gate,
        Dictionary<string, NamedIcDefinition> namedIcs)
    {
        if (!namedIcs.TryGetValue(gate.Name, out NamedIcDefinition? def))
        {
            throw new InvalidDataException($"IC '{gate.Name}' is referenced but not defined.");
        }

        return def.Terminals
            .Select(t => (t.IsInput, t.PortIndex, t.Side))
            .ToList();
    }

    private static List<TerminalLayout> BuildTerminalLayouts(
        IReadOnlyList<(bool IsInput, int PortIndex, PortSide Side)> descriptors)
    {
        var sideCounts = new Dictionary<PortSide, int>
        {
            [PortSide.Top] = 0,
            [PortSide.Left] = 0,
            [PortSide.Right] = 0,
            [PortSide.Bottom] = 0,
        };

        foreach ((_, _, PortSide side) in descriptors)
        {
            sideCounts[side]++;
        }

        var sideOrdinals = new Dictionary<PortSide, int>
        {
            [PortSide.Top] = 0,
            [PortSide.Left] = 0,
            [PortSide.Right] = 0,
            [PortSide.Bottom] = 0,
        };

        var result = new List<TerminalLayout>(descriptors.Count);
        foreach ((bool isInput, int portIndex, PortSide side) in descriptors)
        {
            sideOrdinals[side]++;
            result.Add(new TerminalLayout
            {
                IsInput = isInput,
                PortIndex = portIndex,
                Side = side,
                SideOrdinal = sideOrdinals[side],
                SideCount = sideCounts[side],
            });
        }

        return result;
    }

    private static (double Width, double Height) EstimateGateSize(
        string type,
        string name,
        string? comment,
        IReadOnlyList<TerminalLayout> terminals)
    {
        double width = type == "Buffer" ? 32 : 64;
        double height = type == "Buffer" ? 32 : 64;

        int top = terminals.Count(t => t.Side == PortSide.Top);
        int left = terminals.Count(t => t.Side == PortSide.Left);
        int right = terminals.Count(t => t.Side == PortSide.Right);
        int bottom = terminals.Count(t => t.Side == PortSide.Bottom);

        width = Math.Max(width, Math.Max(top, bottom) * 20.0);
        height = Math.Max(height, Math.Max(left, right) * 20.0);

        if (type == "Comment")
        {
            int length = (comment ?? string.Empty).Length;
            width = Math.Max(width, 50 + length * 8);
        }

        if (type == "IC")
        {
            width = Math.Max(width, 50 + name.Length * 8);
        }

        return (width, height);
    }

    private static AbstractGate CreateGate(
        string type,
        XElement gateElement,
        Dictionary<string, NamedIcDefinition> namedIcs,
        Dictionary<string, string> renameMap)
    {
        int numInputs = (int?)gateElement.Attribute("NumInputs") ?? 2;

        return type switch
        {
            "And" => new Gates.BasicGates.And(numInputs),
            "Not" => new Gates.BasicGates.Not(),
            "Or" => new Gates.BasicGates.Or(numInputs),
            "Nand" => new Gates.BasicGates.Nand(numInputs),
            "Nor" => new Gates.BasicGates.Nor(numInputs),
            "Xor" => new Gates.BasicGates.Xor(),
            "Xnor" => new Gates.BasicGates.Xnor(),
            "Buffer" => new Gates.BasicGates.Buffer(),
            "UserInput" => CreateUserInput(gateElement),
            "UserOutput" => CreateUserOutput(gateElement),
            "NumericInput" => CreateNumericInput(gateElement),
            "NumericOutput" => CreateNumericOutput(gateElement),
            "Clock" => new Clock(ParseIntAttribute(gateElement, "Milliseconds")),
            "IC" => CreateIcGate(gateElement, namedIcs, renameMap),
            "Comment" => CreateComment(gateElement),
            _ => throw new InvalidDataException($"Unknown gate type '{type}'."),
        };
    }

    private static AbstractGate CreateIcGate(
        XElement gateElement,
        Dictionary<string, NamedIcDefinition> namedIcs,
        Dictionary<string, string> renameMap)
    {
        string circuitName = ParseStringAttribute(gateElement, "Name");
        if (renameMap.TryGetValue(circuitName, out string? renamed))
        {
            circuitName = renamed;
        }

        if (!namedIcs.TryGetValue(circuitName, out NamedIcDefinition? named))
        {
            throw new InvalidDataException($"IC '{circuitName}' is referenced before it is defined.");
        }

        return named.Template.Clone();
    }

    private static AbstractGate CreateUserInput(XElement gateElement)
    {
        var gate = new UserInput();
        gate.SetName(ParseStringAttribute(gateElement, "Name"));
        return gate;
    }

    private static AbstractGate CreateUserOutput(XElement gateElement)
    {
        var gate = new UserOutput();
        gate.SetName(ParseStringAttribute(gateElement, "Name"));
        return gate;
    }

    private static AbstractGate CreateNumericInput(XElement gateElement)
    {
        int bits = ParseIntAttribute(gateElement, "Bits");
        var gate = new NumericInput(bits)
        {
            SelectedRepresentation = ParseRepresentation(gateElement),
        };

        gate.Value = ParseStringAttribute(gateElement, "Value");
        return gate;
    }

    private static AbstractGate CreateNumericOutput(XElement gateElement)
    {
        int bits = ParseIntAttribute(gateElement, "Bits");
        return new NumericOutput(bits)
        {
            SelectedRepresentation = ParseRepresentation(gateElement),
        };
    }

    private static AbstractGate CreateComment(XElement gateElement)
    {
        XElement? comment = gateElement.Element("Comment");
        return new Comment { Value = comment?.Value ?? string.Empty };
    }

    private static AbstractNumeric.Representation ParseRepresentation(XElement gateElement)
    {
        int selRep = ParseIntAttribute(gateElement, "SelRep");
        return (AbstractNumeric.Representation)selRep;
    }

    private static string GenerateAvailableName(string baseName, Func<string, bool> isInUse)
    {
        int seq = 1;
        while (isInUse($"{baseName}-{seq}"))
        {
            seq++;
        }

        return $"{baseName}-{seq}";
    }

    private static int ParseIntAttribute(XElement element, string attributeName)
    {
        XAttribute? attr = element.Attribute(attributeName);
        if (attr == null)
        {
            throw new InvalidDataException($"Missing '{attributeName}' attribute.");
        }

        return int.Parse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static double ParseDoubleAttribute(XElement element, string attributeName)
    {
        XAttribute? attr = element.Attribute(attributeName);
        if (attr == null)
        {
            throw new InvalidDataException($"Missing '{attributeName}' attribute.");
        }

        return double.Parse(attr.Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    private static string ParseStringAttribute(XElement element, string attributeName)
    {
        XAttribute? attr = element.Attribute(attributeName);
        if (attr == null)
        {
            throw new InvalidDataException($"Missing '{attributeName}' attribute.");
        }

        return attr.Value;
    }
}
