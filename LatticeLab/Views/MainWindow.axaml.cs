using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LatticeLab.Logic;
using LatticeLab.Models;
using Gates;
using Gates.IOGates;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace LatticeLab.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;

    private LoadedProject _project;
    private LoadedCircuit _activeCircuit;
    private string _loadedPath;

    private readonly Dictionary<GatePlacement, Border> _gateRoots = new();
    private readonly Dictionary<GatePlacement, TextBlock> _gateStates = new();
    private readonly Dictionary<WirePlacement, WireVisual> _wireVisuals = new();

    private readonly Dictionary<UserInput, Border> _userInputIndicators = new();
    private readonly Dictionary<UserInput, Button> _userInputButtons = new();
    private readonly Dictionary<UserOutput, Ellipse> _userOutputIndicators = new();
    private readonly Dictionary<NumericInput, TextBox> _numericEditors = new();
    private readonly Dictionary<NumericInput, Button> _numericRepButtons = new();
    private readonly Dictionary<Clock, TextBox> _clockEditors = new();
    private readonly Dictionary<string, NamedIcTemplate> _namedIcTemplates = new(StringComparer.Ordinal);

    private readonly Dictionary<(GatePlacement Gate, bool IsInput, int Port), TerminalVisual> _terminalVisuals = new();

    private GatePlacement _selectedGate;

    private GatePlacement _draggingGate;
    private bool _isDragging;
    private Point _dragOffset;

    private bool _isConnecting;
    private GatePlacement _connectFromGate;
    private int _connectFromPort;
    private ShapePath _connectionPreview;
    private Point _connectionPreviewPoint;
    private (GatePlacement Gate, int Port)? _connectHoverTarget;

    private string _pendingAddGateType;

    private double _offsetX = 80;
    private double _offsetY = 80;
    private double _zoom = 1.0;
    private bool _isUpdatingZoomSlider;
    private Point _lastCanvasPointer;
    private bool _hasLastCanvasPointer;

    private string _paletteDragType;
    private Button _paletteDragButton;
    private bool _palettePointerDown;
    private bool _paletteDragInProgress;
    private bool _suppressNextPaletteClick;
    private Point _paletteDragStart;

    private bool _isSwitchingCircuit;
    private bool _showTrueFalse = true;
    private bool _endUserMode;
    private bool _useCurvedWires = true;
    private bool _snapToGrid;
    private bool _showGrid = true;
    private bool _showOutputSnapshot = true;
    private bool _showSidebar;
    private bool _isSyncingMenuState;
    private double _gestureZoomSensitivity = 0.030;
    private double _wheelZoomSensitivity = 0.160;
    private bool _invertWheelZoom;
    private double _zoomStep = 0.12;
    private double _panStep = 120;
    private double _zoomMinFactor = 0.90;
    private double _zoomMaxFactor = 1.12;

    private double _wireFlowOffset;

    private GateClipboardData _gateClipboard;

    private static bool _clockPrecessionInitialized;

    private const string PaletteIdleText = "Palette: click to place, or drag onto canvas.";
    private const string PaletteGateTypeDataFormat = "GateType";
    private const string IcPalettePrefix = "IC:";
    private const string KeybindLegendText =
        "Ctrl/Cmd + C = Copy selected gate\n" +
        "Ctrl/Cmd + V = Paste gate\n" +
        "Ctrl/Cmd + I = Inline selected component\n" +
        "Ctrl/Cmd + Shift + I = Inline all components\n" +
        "Ctrl/Cmd + +/- = Zoom in/out\n" +
        "Ctrl/Cmd + 0 = Fit view\n" +
        "Ctrl/Cmd + 1 = Actual size\n" +
        "Ctrl/Cmd + Wheel = Zoom to cursor\n" +
        "Arrow keys = Pan";

    private static readonly (string Type, string Label)[] BasicPaletteItems =
    {
        ("Not", "NOT"),
        ("And", "AND"),
        ("Or", "OR"),
        ("Buffer", "BUFFER"),
    };

    private static readonly (string Type, string Label)[] CompoundPaletteItems =
    {
        ("Nand", "NAND"),
        ("Nor", "NOR"),
        ("Xor", "XOR"),
        ("Xnor", "XNOR"),
    };

    private static readonly (string Type, string Label)[] IoPaletteItems =
    {
        ("UserInput", "User Input"),
        ("UserOutput", "User Output"),
        ("NumericInput", "Numeric Input"),
        ("NumericOutput", "Numeric Output"),
        ("Clock", "Clock"),
        ("Comment", "Comment"),
    };

    private sealed class WireVisual
    {
        public required ShapePath Outer { get; init; }
        public required ShapePath Inner { get; init; }
    }

    private sealed class TerminalVisual
    {
        public required Canvas StemRoot { get; init; }
        public required Canvas Root { get; init; }
        public required Ellipse Dot { get; init; }
        public required Polygon Arrow { get; init; }
    }

    private sealed class IcTerminalSeed
    {
        public required AbstractGate Gate { get; init; }
        public required bool IsInput { get; init; }
        public required PortSide Side { get; init; }
        public required double Offset { get; init; }
    }

    private sealed class GateClipboardData
    {
        public required string Type { get; init; }
        public required AbstractGate Gate { get; init; }
        public required List<TerminalLayout> Terminals { get; init; }
        public required string CommentText { get; init; }
        public required double Width { get; init; }
        public required double Height { get; init; }
        public required double Angle { get; init; }
    }

    private static readonly Dictionary<string, string> GatePath = new(StringComparer.Ordinal)
    {
        ["And"] = "M 17,17 v 30 h 15 a 2,2 1 0 0 0,-30 h -15",
        ["Not"] = "M 15,17 L 15,47 L 45,32 Z M 46,33.5 a 3,3 1 1 1 0.1,0.1",
        ["Or"] = "M 15,17 h 10 c 10,0 20,5 25,15 c -5,10 -15,15 -25,15 h -10 c 5,-10 5,-20 0,-30",
        ["Nor"] = "M 15,17 h 5 c 10,0 20,5 25,15 c -5,10 -15,15 -25,15 h -5 c 5,-10 5,-20 0,-30 M 46,33.5 a 3,3 1 1 1 0.1,0.1",
        ["Nand"] = "M 15,17 v 30 h 15 a 2,2 1 0 0 0,-30 h -15 M 46,33.5 a 3,3 1 1 1 0.1,0.1",
        ["Xor"] = "M 13,47 c 5,-10 5,-20 0,-30 M 13,17 c 5,10 5,20 0,30 M 18,17 h 7 c 10,0 20,5 25,15 c -5,10 -15,15 -25,15 h -7 c 5,-10 5,-20 0,-30",
        ["Xnor"] = "M 13,47 c 5,-10 5,-20 0,-30 M 13,17 c 5,10 5,20 0,30 M 18,17 h 2 c 10,0 20,5 25,15 c -5,10 -15,15 -25,15 h -2 c 5,-10 5,-20 0,-30 M 46,33.5 a 3,3 1 1 1 0.1,0.1",
        ["Buffer"] = "M 12,12 v 8 l 8,-4 l -8,-4",
    };

    public MainWindow() : this(Array.Empty<string>())
    {
    }

    public MainWindow(string[] args)
    {
        InitializeComponent();

        if (!_clockPrecessionInitialized)
        {
            Clock.CalculatePrecession();
            _clockPrecessionInitialized = true;
        }

        _project = null;
        _activeCircuit = null;
        _loadedPath = string.Empty;

        InfoText.Text =
            "Classic-style compatibility editor: drag gates, wire terminals, Ctrl/Cmd+C/V to copy/paste, Ctrl/Cmd+I to inline selected component.";

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(70),
        };
        _refreshTimer.Tick += (_, _) => RefreshLiveState();
        _refreshTimer.Start();

        PropagationThread.SLEEP_TIME = (int)SpeedSlider.Value;
        _useCurvedWires = CurvyWiresToggle.IsChecked ?? true;
        _snapToGrid = SnapGridToggle.IsChecked ?? false;
        _lastCanvasPointer = new Point(CircuitCanvas.Width / 2.0, CircuitCanvas.Height / 2.0);
        _showOutputSnapshot = ShowOutputToggle.IsChecked ?? true;
        _showGrid = ShowGridToggle.IsChecked ?? true;
        _gestureZoomSensitivity = GestureZoomSensitivitySlider.Value;
        _wheelZoomSensitivity = WheelZoomSensitivitySlider.Value;
        _invertWheelZoom = InvertWheelZoomToggle.IsChecked ?? false;
        _zoomStep = ZoomStepSlider.Value;
        _panStep = PanStepSlider.Value;
        _zoomMinFactor = ZoomMinFactorSlider.Value;
        _zoomMaxFactor = ZoomMaxFactorSlider.Value;
        _showSidebar = ShowSidebarMenuItem.IsChecked;
        KeybindsText.Text = KeybindLegendText;
        ApplyOutputSectionVisibility();
        ApplySidebarVisibility();
        SyncMenuStateFromControls();
        KeyDown += MainWindow_KeyDown;

        InitializePalette();
        ResetPaletteStatus();

        Gestures.AddPointerTouchPadGestureMagnifyHandler(CircuitCanvas, CircuitCanvas_PointerTouchPadGestureMagnify);
        DragDrop.SetAllowDrop(CircuitCanvas, true);
        CircuitCanvas.AddHandler(DragDrop.DragOverEvent, CircuitCanvas_DragOver);
        CircuitCanvas.AddHandler(DragDrop.DropEvent, CircuitCanvas_Drop);

        if (args.Length > 0 && File.Exists(args[0]))
        {
            LoadProject(args[0]);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        StopAllCircuits();
        base.OnClosed(e);
    }

    private void StopAllCircuits()
    {
        if (_project == null)
        {
            return;
        }

        foreach (LoadedCircuit circuit in _project.Circuits)
        {
            circuit.Circuit.Stop();
        }
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        TopLevel top = TopLevel.GetTopLevel(this);
        if (top == null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Gate Circuit File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Lattice Lab Circuit")
                {
                    Patterns = new[] { "*.gcg", "*.gcf", "*.ic" },
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*" },
                },
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        string localPath = files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            StatusText.Text = "Unable to open non-local files in compatibility mode.";
            return;
        }

        LoadProject(localPath);
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_loadedPath))
        {
            LoadProject(_loadedPath);
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        _activeCircuit.Circuit.Stop();

        foreach (AbstractGate gate in _activeCircuit.Circuit.ToList())
        {
            _activeCircuit.Circuit.Remove(gate);
        }

        _activeCircuit.Gates.Clear();
        _activeCircuit.Wires.Clear();
        _selectedGate = null;

        RenderCircuit(_activeCircuit);
        RenderInputPanel(_activeCircuit);
        _activeCircuit.Circuit.Start();

        StatusText.Text = "New circuit";
        InfoLineText.Text = "Created empty circuit.";
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCircuit == null || _selectedGate == null)
        {
            return;
        }

        DeleteGate(_selectedGate);
    }

    private void CreateComponentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        try
        {
            string name = GenerateAvailableNamedIcName("Untitled");
            NamedIcTemplate created = BuildNamedIcTemplateFromActiveCircuit(name);
            _namedIcTemplates[created.Name] = created;
            RebuildCustomPalette();

            _pendingAddGateType = $"{IcPalettePrefix}{created.Name}";
            PaletteStatusText.Text = $"Palette: placing {created.Name}. Click on canvas to add.";
            StatusText.Text = "Custom component created";
            InfoLineText.Text = $"Created custom component '{created.Name}'.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Create component failed";
            InfoText.Text = ex.Message;
        }
    }

    private void InitializePalette()
    {
        PopulatePalettePanel(BasicPalettePanel, BasicPaletteItems);
        PopulatePalettePanel(CompoundPalettePanel, CompoundPaletteItems);
        PopulatePalettePanel(IoPalettePanel, IoPaletteItems);
        RebuildCustomPalette();
    }

    private void PopulatePalettePanel(Panel panel, IEnumerable<(string Type, string Label)> entries)
    {
        panel.Children.Clear();
        foreach ((string type, string label) in entries)
        {
            panel.Children.Add(CreatePaletteButton(type, label));
        }
    }

    private Button CreatePaletteButton(string type, string label)
    {
        Button button = new()
        {
            Tag = type,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 4),
        };

        button.Content = BuildPaletteButtonContent(type, label);
        button.Click += PaletteGate_Click;
        button.PointerPressed += PaletteButton_PointerPressed;
        button.PointerMoved += PaletteButton_PointerMoved;
        button.PointerReleased += PaletteButton_PointerReleased;

        return button;
    }

    private static Grid BuildPaletteButtonContent(string type, string label)
    {
        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("44,*"),
        };

        Border glyphFrame = new()
        {
            Width = 40,
            Height = 28,
            BorderBrush = new SolidColorBrush(Color.Parse("#FFD0D0D0")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Background = Brushes.White,
            Child = BuildPaletteGlyph(type, label),
        };

        TextBlock text = new()
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
        };

        Grid.SetColumn(glyphFrame, 0);
        Grid.SetColumn(text, 1);
        content.Children.Add(glyphFrame);
        content.Children.Add(text);

        return content;
    }

    private static Viewbox BuildPaletteGlyph(string type, string label)
    {
        string normalizedType = NormalizePaletteType(type);

        Canvas canvas = new()
        {
            Width = 64,
            Height = 64,
            Background = Brushes.Transparent,
        };

        switch (normalizedType)
        {
            case "And":
            case "Not":
            case "Or":
            case "Nand":
            case "Nor":
            case "Xor":
            case "Xnor":
            case "Buffer":
            {
                string pathData = normalizedType == "Buffer"
                    ? "M 18,16 L 18,48 L 48,32 Z"
                    : GatePath[normalizedType];
                Avalonia.Controls.Shapes.Path path = new()
                {
                    Data = Geometry.Parse(pathData),
                    Stroke = Brushes.Black,
                    Fill = Brushes.White,
                    StrokeThickness = 2,
                };
                canvas.Children.Add(path);
                break;
            }
            case "UserInput":
            {
                Border outer = new()
                {
                    Width = 34,
                    Height = 34,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(2),
                    Background = Brushes.White,
                };
                Border inner = new()
                {
                    Width = 22,
                    Height = 22,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(2),
                    Background = Brushes.Beige,
                };
                Canvas.SetLeft(outer, 15);
                Canvas.SetTop(outer, 15);
                Canvas.SetLeft(inner, 21);
                Canvas.SetTop(inner, 21);
                canvas.Children.Add(outer);
                canvas.Children.Add(inner);
                break;
            }
            case "UserOutput":
            {
                Ellipse outer = new()
                {
                    Width = 34,
                    Height = 34,
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                    Fill = Brushes.White,
                };
                Ellipse inner = new()
                {
                    Width = 22,
                    Height = 22,
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                    Fill = Brushes.Beige,
                };
                Canvas.SetLeft(outer, 15);
                Canvas.SetTop(outer, 15);
                Canvas.SetLeft(inner, 21);
                Canvas.SetTop(inner, 21);
                canvas.Children.Add(outer);
                canvas.Children.Add(inner);
                break;
            }
            case "NumericInput":
            case "NumericOutput":
            {
                Border body = new()
                {
                    Width = 42,
                    Height = 30,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(2),
                    Background = Brushes.White,
                };
                TextBlock txt = new()
                {
                    Text = normalizedType == "NumericInput" ? "IN" : "OUT",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Width = 42,
                    TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(body, 11);
                Canvas.SetTop(body, 17);
                Canvas.SetLeft(txt, 11);
                Canvas.SetTop(txt, 27);
                canvas.Children.Add(body);
                canvas.Children.Add(txt);
                break;
            }
            case "Clock":
            {
                Border body = new()
                {
                    Width = 42,
                    Height = 30,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(2),
                    Background = Brushes.White,
                };
                Avalonia.Controls.Shapes.Path wave = new()
                {
                    Data = Geometry.Parse("M 16,30 h 6 v 6 h -6 v 6 h 6 v 6 h -6 v 6 h 6"),
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                };
                Canvas.SetLeft(body, 11);
                Canvas.SetTop(body, 17);
                canvas.Children.Add(body);
                canvas.Children.Add(wave);
                break;
            }
            case "Comment":
            {
                Avalonia.Controls.Shapes.Path bubble = new()
                {
                    Data = Geometry.Parse(BuildCommentBubblePath(42, 28)),
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                    Fill = Brushes.White,
                };
                Canvas.SetLeft(bubble, 11);
                Canvas.SetTop(bubble, 18);
                canvas.Children.Add(bubble);
                break;
            }
            case "IC":
            {
                Border body = new()
                {
                    Width = 42,
                    Height = 30,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(2),
                    Background = Brushes.White,
                };
                TextBlock txt = new()
                {
                    Text = label.Length <= 4 ? label : label.Substring(0, 4).ToUpperInvariant(),
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    Width = 42,
                    TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(body, 11);
                Canvas.SetTop(body, 17);
                Canvas.SetLeft(txt, 11);
                Canvas.SetTop(txt, 27);
                canvas.Children.Add(body);
                canvas.Children.Add(txt);
                break;
            }
        }

        return new Viewbox
        {
            Width = 38,
            Height = 26,
            Stretch = Stretch.Uniform,
            Child = canvas,
        };
    }

    private static string NormalizePaletteType(string type)
    {
        return type.StartsWith(IcPalettePrefix, StringComparison.Ordinal)
            ? "IC"
            : type;
    }

    private void RebuildCustomPalette()
    {
        CustomPalettePanel.Children.Clear();

        foreach (NamedIcTemplate template in _namedIcTemplates.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            string type = $"{IcPalettePrefix}{template.Name}";
            CustomPalettePanel.Children.Add(CreatePaletteButton(type, template.Name));
        }

        if (CustomPalettePanel.Children.Count == 0)
        {
            CustomPalettePanel.Children.Add(new TextBlock
            {
                Text = "No custom components yet.",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void SyncNamedIcTemplatesFromProject()
    {
        _namedIcTemplates.Clear();

        if (_project == null)
        {
            RebuildCustomPalette();
            return;
        }

        foreach (NamedIcTemplate template in _project.NamedIcTemplates)
        {
            _namedIcTemplates[template.Name] = new NamedIcTemplate
            {
                Name = template.Name,
                Template = template.Template,
                Terminals = CloneTerminalLayouts(template.Terminals),
            };
        }

        RebuildCustomPalette();
    }

    private static List<TerminalLayout> CloneTerminalLayouts(IReadOnlyList<TerminalLayout> terminals)
    {
        return terminals
            .Select(t => new TerminalLayout
            {
                IsInput = t.IsInput,
                PortIndex = t.PortIndex,
                Side = t.Side,
                SideOrdinal = t.SideOrdinal,
                SideCount = t.SideCount,
            })
            .ToList();
    }

    private static string GetPaletteDisplayName(string type)
    {
        if (type.StartsWith(IcPalettePrefix, StringComparison.Ordinal))
        {
            return type.Substring(IcPalettePrefix.Length);
        }

        return type;
    }

    private bool TryResolveIcPaletteType(string type, out NamedIcTemplate template)
    {
        template = null;
        if (!type.StartsWith(IcPalettePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string name = type.Substring(IcPalettePrefix.Length);
        return _namedIcTemplates.TryGetValue(name, out template);
    }

    private string GenerateAvailableNamedIcName(string baseName)
    {
        string candidate = baseName;
        int seq = 1;
        while (_namedIcTemplates.ContainsKey(candidate))
        {
            candidate = $"{baseName}-{seq}";
            seq++;
        }

        return candidate;
    }

    private NamedIcTemplate BuildNamedIcTemplateFromActiveCircuit(string name)
    {
        if (_activeCircuit == null)
        {
            throw new InvalidOperationException("No active circuit to convert.");
        }

        Circuit source = _activeCircuit.Circuit;
        if (source.Count == 0)
        {
            throw new InvalidOperationException("Current circuit is empty.");
        }

        Circuit cloned = source.Clone();

        var sourceInputs = new List<UserInput>();
        var sourceOutputs = new List<UserOutput>();
        var clonedInputs = new List<UserInput>();
        var clonedOutputs = new List<UserOutput>();

        for (int i = 0; i < source.Count; i++)
        {
            AbstractGate gate = source[i];
            if (gate is UserInput input)
            {
                sourceInputs.Add(input);
                clonedInputs.Add((UserInput)cloned[i]);
            }
            else if (gate is UserOutput output)
            {
                sourceOutputs.Add(output);
                clonedOutputs.Add((UserOutput)cloned[i]);
            }
        }

        if (sourceInputs.Count == 0 && sourceOutputs.Count == 0)
        {
            throw new InvalidOperationException("Custom components require at least one User Input or User Output.");
        }

        Dictionary<AbstractGate, GatePlacement> placementsByGate = _activeCircuit.Gates.ToDictionary(g => g.Gate);
        List<IcTerminalSeed> seeds = ComputeIcTerminalSeeds(placementsByGate);
        seeds.Sort((a, b) =>
        {
            int sideCmp = a.Side.CompareTo(b.Side);
            return sideCmp != 0 ? sideCmp : a.Offset.CompareTo(b.Offset);
        });

        var descriptors = new List<(bool IsInput, int PortIndex, PortSide Side)>();
        foreach (IcTerminalSeed seed in seeds)
        {
            if (seed.IsInput)
            {
                int index = sourceInputs.IndexOf((UserInput)seed.Gate);
                if (index >= 0)
                {
                    descriptors.Add((true, index, seed.Side));
                }
            }
            else
            {
                int index = sourceOutputs.IndexOf((UserOutput)seed.Gate);
                if (index >= 0)
                {
                    descriptors.Add((false, index, seed.Side));
                }
            }
        }

        List<TerminalLayout> terminalLayouts = BuildTerminalLayouts(descriptors);

        return new NamedIcTemplate
        {
            Name = name,
            Template = new IC(cloned, clonedInputs.ToArray(), clonedOutputs.ToArray(), name),
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

    private void ResetPaletteStatus()
    {
        PaletteStatusText.Text = PaletteIdleText;
    }

    private void PaletteButton_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string type)
        {
            return;
        }

        if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _paletteDragButton = button;
        _paletteDragType = type;
        _paletteDragStart = e.GetPosition(button);
        _palettePointerDown = true;
        _suppressNextPaletteClick = false;
    }

    private async void PaletteButton_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_paletteDragInProgress ||
            !_palettePointerDown ||
            sender is not Button button ||
            button != _paletteDragButton ||
            string.IsNullOrWhiteSpace(_paletteDragType))
        {
            return;
        }

        if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
        {
            ResetPaletteDragState();
            return;
        }

        Point now = e.GetPosition(button);
        double dx = now.X - _paletteDragStart.X;
        double dy = now.Y - _paletteDragStart.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 6)
        {
            return;
        }

        _paletteDragInProgress = true;
        _suppressNextPaletteClick = true;

        try
        {
            DataObject data = new();
            data.Set(PaletteGateTypeDataFormat, _paletteDragType);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        }
        finally
        {
            _paletteDragInProgress = false;
            ResetPaletteDragState();
        }

        e.Handled = true;
    }

    private void PaletteButton_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        ResetPaletteDragState();
    }

    private void ResetPaletteDragState()
    {
        _palettePointerDown = false;
        _paletteDragType = null;
        _paletteDragButton = null;
    }

    private void PaletteGate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string type)
        {
            return;
        }

        if (_suppressNextPaletteClick)
        {
            _suppressNextPaletteClick = false;
            return;
        }

        _pendingAddGateType = type;
        PaletteStatusText.Text = $"Palette: placing {GetPaletteDisplayName(type)}. Click on canvas to add.";
        StatusText.Text = "Placement mode";
    }

    private void CircuitPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_project == null || _isSwitchingCircuit)
        {
            return;
        }

        int index = CircuitPicker.SelectedIndex;
        if (index < 0 || index >= _project.Circuits.Count)
        {
            return;
        }

        ActivateCircuit(index);
    }

    private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingZoomSlider)
        {
            return;
        }

        SetZoom(e.NewValue, null, syncZoomSlider: false);
    }

    private void SpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        PropagationThread.SLEEP_TIME = (int)e.NewValue;
    }

    private void ActualSizeButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 1.0;
    }

    private void FitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        Size viewport = CanvasScroll.Viewport;
        if (viewport.Width <= 1 || viewport.Height <= 1)
        {
            return;
        }

        double zx = viewport.Width / (CircuitCanvas.Width + 40);
        double zy = viewport.Height / (CircuitCanvas.Height + 40);
        double fit = Math.Clamp(Math.Min(zx, zy), ZoomSlider.Minimum, ZoomSlider.Maximum);

        ZoomSlider.Value = fit;
    }

    private void ShowTfToggle_Changed(object sender, RoutedEventArgs e)
    {
        _showTrueFalse = ShowTfToggle.IsChecked ?? true;
        SyncMenuStateFromControls();
        RefreshLiveState();
    }

    private void EndUserToggle_Changed(object sender, RoutedEventArgs e)
    {
        _endUserMode = EndUserToggle.IsChecked ?? false;
        SyncMenuStateFromControls();
        RefreshLiveState();
    }

    private void CurvyWiresToggle_Changed(object sender, RoutedEventArgs e)
    {
        _useCurvedWires = CurvyWiresToggle.IsChecked ?? true;
        SyncMenuStateFromControls();
        UpdateAllWireGeometry();
        if (_isConnecting && _connectionPreview != null)
        {
            UpdateConnectionPreview(_connectionPreviewPoint);
        }
    }

    private void SnapGridToggle_Changed(object sender, RoutedEventArgs e)
    {
        _snapToGrid = SnapGridToggle.IsChecked ?? false;
        SyncMenuStateFromControls();
    }

    private void ShowOutputToggle_Changed(object sender, RoutedEventArgs e)
    {
        _showOutputSnapshot = ShowOutputToggle.IsChecked ?? true;
        ApplyOutputSectionVisibility();
        SyncMenuStateFromControls();
    }

    private void ShowGridToggle_Changed(object sender, RoutedEventArgs e)
    {
        _showGrid = ShowGridToggle.IsChecked ?? true;
        SyncMenuStateFromControls();
        if (_activeCircuit != null)
        {
            RenderCircuit(_activeCircuit);
        }
    }

    private void GestureZoomSensitivitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _gestureZoomSensitivity = e.NewValue;
    }

    private void WheelZoomSensitivitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _wheelZoomSensitivity = e.NewValue;
    }

    private void InvertWheelZoomToggle_Changed(object sender, RoutedEventArgs e)
    {
        _invertWheelZoom = InvertWheelZoomToggle.IsChecked ?? false;
        SyncMenuStateFromControls();
    }

    private void ZoomStepSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoomStep = e.NewValue;
    }

    private void PanStepSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _panStep = e.NewValue;
    }

    private void ZoomMinFactorSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoomMinFactor = e.NewValue;
        if (_zoomMinFactor > _zoomMaxFactor)
        {
            _zoomMaxFactor = _zoomMinFactor;
            ZoomMaxFactorSlider.Value = _zoomMaxFactor;
        }
    }

    private void ZoomMaxFactorSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoomMaxFactor = e.NewValue;
        if (_zoomMaxFactor < _zoomMinFactor)
        {
            _zoomMinFactor = _zoomMaxFactor;
            ZoomMinFactorSlider.Value = _zoomMinFactor;
        }
    }

    private void MenuNew_Click(object sender, RoutedEventArgs e)
    {
        NewButton_Click(sender, e);
    }

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        OpenButton_Click(sender, e);
    }

    private void MenuReload_Click(object sender, RoutedEventArgs e)
    {
        ReloadButton_Click(sender, e);
    }

    private void MenuCreateComponent_Click(object sender, RoutedEventArgs e)
    {
        CreateComponentButton_Click(sender, e);
    }

    private void MenuDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        DeleteButton_Click(sender, e);
    }

    private void MenuInlineSelected_Click(object sender, RoutedEventArgs e)
    {
        InlineSelectedComponent();
    }

    private void MenuInlineAll_Click(object sender, RoutedEventArgs e)
    {
        InlineAllComponents();
    }

    private void MenuCopyGate_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedGate();
    }

    private void MenuPasteGate_Click(object sender, RoutedEventArgs e)
    {
        PasteCopiedGate();
    }

    private void MenuActualSize_Click(object sender, RoutedEventArgs e)
    {
        ActualSizeButton_Click(sender, e);
    }

    private void MenuFitCircuit_Click(object sender, RoutedEventArgs e)
    {
        FitButton_Click(sender, e);
    }

    private void ShowSidebarMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSyncingMenuState)
        {
            return;
        }

        _showSidebar = ShowSidebarMenuItem.IsChecked;
        ApplySidebarVisibility();
    }

    private void ShowOutputMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSyncingMenuState)
        {
            return;
        }

        ShowOutputToggle.IsChecked = ShowOutputMenuItem.IsChecked;
    }

    private void ShowGridMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSyncingMenuState)
        {
            return;
        }

        ShowGridToggle.IsChecked = ShowGridMenuItem.IsChecked;
    }

    private void ShowTrueFalseMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSyncingMenuState)
        {
            return;
        }

        ShowTfToggle.IsChecked = ShowTrueFalseMenuItem.IsChecked;
    }

    private void CurvyWiresMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSyncingMenuState)
        {
            return;
        }

        CurvyWiresToggle.IsChecked = CurvyWiresMenuItem.IsChecked;
    }

    private void SnapToGridMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSyncingMenuState)
        {
            return;
        }

        SnapGridToggle.IsChecked = SnapToGridMenuItem.IsChecked;
    }

    private void EndUserModeMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSyncingMenuState)
        {
            return;
        }

        EndUserToggle.IsChecked = EndUserModeMenuItem.IsChecked;
    }

    private void InvertWheelZoomMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSyncingMenuState)
        {
            return;
        }

        InvertWheelZoomToggle.IsChecked = InvertWheelZoomMenuItem.IsChecked;
    }

    private void MenuZoomStepIncrease_Click(object sender, RoutedEventArgs e)
    {
        ZoomStepSlider.Value = Math.Min(ZoomStepSlider.Maximum, ZoomStepSlider.Value + 0.02);
    }

    private void MenuZoomStepDecrease_Click(object sender, RoutedEventArgs e)
    {
        ZoomStepSlider.Value = Math.Max(ZoomStepSlider.Minimum, ZoomStepSlider.Value - 0.02);
    }

    private void MenuPanStepIncrease_Click(object sender, RoutedEventArgs e)
    {
        PanStepSlider.Value = Math.Min(PanStepSlider.Maximum, PanStepSlider.Value + 20);
    }

    private void MenuPanStepDecrease_Click(object sender, RoutedEventArgs e)
    {
        PanStepSlider.Value = Math.Max(PanStepSlider.Minimum, PanStepSlider.Value - 20);
    }

    private void ApplyOutputSectionVisibility()
    {
        bool visible = _showOutputSnapshot;
        OutputTopSeparator.IsVisible = visible;
        OutputBottomSeparator.IsVisible = visible;
        OutputSection.IsVisible = visible;
    }

    private void ApplySidebarVisibility()
    {
        if (MainContentGrid.ColumnDefinitions.Count > 2)
        {
            MainContentGrid.ColumnDefinitions[2].Width = _showSidebar
                ? new GridLength(310)
                : new GridLength(0);
        }

        SidebarPane.IsVisible = _showSidebar;
    }

    private void SyncMenuStateFromControls()
    {
        _isSyncingMenuState = true;
        try
        {
            ShowSidebarMenuItem.IsChecked = _showSidebar;
            ShowOutputMenuItem.IsChecked = _showOutputSnapshot;
            ShowGridMenuItem.IsChecked = _showGrid;
            ShowTrueFalseMenuItem.IsChecked = _showTrueFalse;
            CurvyWiresMenuItem.IsChecked = _useCurvedWires;
            SnapToGridMenuItem.IsChecked = _snapToGrid;
            EndUserModeMenuItem.IsChecked = _endUserMode;
            InvertWheelZoomMenuItem.IsChecked = _invertWheelZoom;
        }
        finally
        {
            _isSyncingMenuState = false;
        }
    }

    private static bool HasCommandModifier(KeyModifiers modifiers)
    {
        return modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (_activeCircuit == null || e.Source is TextBox)
        {
            return;
        }

        bool command = HasCommandModifier(e.KeyModifiers);

        if (command && e.Key == Key.C)
        {
            CopySelectedGate();
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.V)
        {
            PasteCopiedGate();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _selectedGate != null)
        {
            DeleteGate(_selectedGate);
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.I && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            InlineAllComponents();
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.I)
        {
            InlineSelectedComponent();
            e.Handled = true;
            return;
        }

        if (command && (e.Key == Key.Add || e.Key == Key.OemPlus))
        {
            ZoomByFactor(1.0 + _zoomStep, GetZoomAnchorFallback());
            e.Handled = true;
            return;
        }

        if (command && (e.Key == Key.Subtract || e.Key == Key.OemMinus))
        {
            ZoomByFactor(Math.Max(0.01, 1.0 - _zoomStep), GetZoomAnchorFallback());
            e.Handled = true;
            return;
        }

        if (command && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            FitButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (command && (e.Key == Key.D1 || e.Key == Key.NumPad1))
        {
            ActualSizeButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        double pan = _panStep * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2.0 : 1.0);
        switch (e.Key)
        {
            case Key.Left:
                PanCanvas(-pan, 0);
                e.Handled = true;
                break;
            case Key.Right:
                PanCanvas(pan, 0);
                e.Handled = true;
                break;
            case Key.Up:
                PanCanvas(0, -pan);
                e.Handled = true;
                break;
            case Key.Down:
                PanCanvas(0, pan);
                e.Handled = true;
                break;
        }
    }

    private void PanCanvas(double dx, double dy)
    {
        Vector desired = new(CanvasScroll.Offset.X + dx, CanvasScroll.Offset.Y + dy);
        CanvasScroll.Offset = ClampCanvasOffset(desired);
    }

    private void CircuitCanvas_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        Point p = e.GetPosition(CircuitCanvas);
        UpdateLastCanvasPointer(p);

        if (e.GetCurrentPoint(CircuitCanvas).Properties.IsRightButtonPressed)
        {
            CancelConnection();
            _pendingAddGateType = null;
            ResetPaletteStatus();
            ShowCanvasContextMenu();
            e.Handled = true;
            return;
        }

        if (!string.IsNullOrEmpty(_pendingAddGateType) && e.GetCurrentPoint(CircuitCanvas).Properties.IsLeftButtonPressed)
        {
            AddGateFromPalette(_pendingAddGateType, p);
            e.Handled = true;
            return;
        }

        if (e.Source == CircuitCanvas)
        {
            SelectGate(null);
        }
    }

    private void CircuitCanvas_PointerMoved(object sender, PointerEventArgs e)
    {
        Point cursor = e.GetPosition(CircuitCanvas);
        UpdateLastCanvasPointer(cursor);

        if (!_isConnecting || _connectionPreview == null)
        {
            return;
        }

        UpdateConnectionPreview(cursor);
    }

    private void CircuitCanvas_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        Point cursor = e.GetPosition(CircuitCanvas);
        UpdateLastCanvasPointer(cursor);

        if (!_isConnecting)
        {
            return;
        }

        (GatePlacement Gate, int Port)? target = FindNearestInputTerminal(cursor, 18);

        if (target.HasValue)
        {
            ConnectWire(_connectFromGate, _connectFromPort, target.Value.Gate, target.Value.Port);
        }

        CancelConnection();
    }

    private void CircuitCanvas_DragOver(object sender, DragEventArgs e)
    {
        if (_activeCircuit == null || !TryGetPaletteGateTypeFromData(e.Data, out _))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void CircuitCanvas_Drop(object sender, DragEventArgs e)
    {
        if (_activeCircuit == null || !TryGetPaletteGateTypeFromData(e.Data, out string type))
        {
            return;
        }

        Point dropPoint = e.GetPosition(CircuitCanvas);
        UpdateLastCanvasPointer(dropPoint);
        AddGateFromPalette(type, dropPoint);
        e.Handled = true;
    }

    private static bool TryGetPaletteGateTypeFromData(IDataObject data, out string type)
    {
        type = string.Empty;

        if (!data.Contains(PaletteGateTypeDataFormat))
        {
            return false;
        }

        if (data.Get(PaletteGateTypeDataFormat) is not string raw || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        type = raw;
        return true;
    }

    private void LoadProject(string path)
    {
        try
        {
            StopAllCircuits();

            _project = LegacyCircuitLoader.LoadProject(path);
            _loadedPath = path;
            _pendingAddGateType = null;
            ResetPaletteStatus();
            SyncNamedIcTemplatesFromProject();

            PathText.Text = path;
            ReloadButton.IsEnabled = true;

            _isSwitchingCircuit = true;
            CircuitPicker.ItemsSource = _project.Circuits.Select(c => c.DisplayName).ToList();
            CircuitPicker.SelectedIndex = _project.DefaultCircuitIndex;
            _isSwitchingCircuit = false;

            ActivateCircuit(_project.DefaultCircuitIndex);

            StatusText.Text = "Running";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Load failed";
            InfoText.Text = "Failed to load file: " + ex.Message;
        }
    }

    private void ActivateCircuit(int index)
    {
        if (_project == null || index < 0 || index >= _project.Circuits.Count)
        {
            return;
        }

        StopAllCircuits();

        _activeCircuit = _project.Circuits[index];
        _selectedGate = null;

        RenderCircuit(_activeCircuit);
        RenderInputPanel(_activeCircuit);

        _activeCircuit.Circuit.Start();
        StatusText.Text = "Running";
        InfoLineText.Text = "Ready";
    }

    private void ApplyZoom()
    {
        CircuitCanvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        CircuitCanvas.RenderTransform = new ScaleTransform(_zoom, _zoom);
    }

    private void SetZoom(double newZoom, Point? anchorCanvasPoint, bool syncZoomSlider)
    {
        double clampedZoom = Math.Clamp(newZoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        if (Math.Abs(clampedZoom - _zoom) < 0.0001)
        {
            return;
        }

        double previousZoom = _zoom;
        Vector previousOffset = CanvasScroll.Offset;
        Point anchor = anchorCanvasPoint ?? GetViewportCenterInCanvas();
        _zoom = clampedZoom;
        ApplyZoom();

        if (CanvasScroll.Viewport.Width > 1 && CanvasScroll.Viewport.Height > 1)
        {
            Vector desiredOffset;
            if (anchorCanvasPoint.HasValue)
            {
                // Keep anchored zoom stable around pointer/anchor rather than snapping to center.
                desiredOffset = new Vector(
                    previousOffset.X + (anchor.X * (_zoom - previousZoom)),
                    previousOffset.Y + (anchor.Y * (_zoom - previousZoom)));
            }
            else
            {
                desiredOffset = new Vector(
                    (anchor.X * _zoom) - (CanvasScroll.Viewport.Width / 2.0),
                    (anchor.Y * _zoom) - (CanvasScroll.Viewport.Height / 2.0));
            }

            CanvasScroll.Offset = ClampCanvasOffset(desiredOffset);
        }

        if (syncZoomSlider && Math.Abs(ZoomSlider.Value - _zoom) > 0.0001)
        {
            _isUpdatingZoomSlider = true;
            ZoomSlider.Value = _zoom;
            _isUpdatingZoomSlider = false;
        }
    }

    private void ZoomByFactor(double factor, Point anchorCanvasPoint)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
        {
            return;
        }

        SetZoom(_zoom * factor, anchorCanvasPoint, syncZoomSlider: true);
    }

    private Vector ClampCanvasOffset(Vector desiredOffset)
    {
        double maxX = Math.Max(0, (CircuitCanvas.Width * _zoom) - CanvasScroll.Viewport.Width);
        double maxY = Math.Max(0, (CircuitCanvas.Height * _zoom) - CanvasScroll.Viewport.Height);

        return new Vector(
            Math.Clamp(desiredOffset.X, 0, maxX),
            Math.Clamp(desiredOffset.Y, 0, maxY));
    }

    private Point GetViewportCenterInCanvas()
    {
        if (CanvasScroll.Viewport.Width <= 1 || CanvasScroll.Viewport.Height <= 1)
        {
            return GetZoomAnchorFallback();
        }

        double z = Math.Max(0.0001, _zoom);
        return new Point(
            (CanvasScroll.Offset.X + (CanvasScroll.Viewport.Width / 2.0)) / z,
            (CanvasScroll.Offset.Y + (CanvasScroll.Viewport.Height / 2.0)) / z);
    }

    private void UpdateLastCanvasPointer(Point canvasPoint)
    {
        _lastCanvasPointer = canvasPoint;
        _hasLastCanvasPointer = true;
    }

    private Point GetZoomAnchorFallback()
    {
        if (_hasLastCanvasPointer)
        {
            return _lastCanvasPointer;
        }

        return new Point(CircuitCanvas.Width / 2.0, CircuitCanvas.Height / 2.0);
    }

    private void CircuitCanvas_PointerTouchPadGestureMagnify(object sender, PointerDeltaEventArgs e)
    {
        double delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
        if (Math.Abs(delta) < 0.0001)
        {
            return;
        }

        // Faster than before, but still clamped to avoid jumpy snap zoom.
        double factor = Math.Exp(delta * _gestureZoomSensitivity);
        factor = Math.Clamp(factor, _zoomMinFactor, _zoomMaxFactor);

        Point anchor = GetZoomAnchorFallback();
        try
        {
            anchor = e.GetPosition(CircuitCanvas);
            UpdateLastCanvasPointer(anchor);
        }
        catch
        {
            // Gesture event may not always expose a local pointer position.
        }

        ZoomByFactor(factor, anchor);
        e.Handled = true;
    }

    private void CircuitCanvas_PointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        if (!HasCommandModifier(e.KeyModifiers))
        {
            return;
        }

        double delta = _invertWheelZoom ? -e.Delta.Y : e.Delta.Y;
        if (Math.Abs(delta) < 0.0001)
        {
            return;
        }

        double factor = Math.Exp(delta * _wheelZoomSensitivity);
        factor = Math.Clamp(factor, _zoomMinFactor, _zoomMaxFactor);

        Point anchor = e.GetPosition(CircuitCanvas);
        UpdateLastCanvasPointer(anchor);
        ZoomByFactor(factor, anchor);
        e.Handled = true;
    }

    private void RenderCircuit(LoadedCircuit circuit)
    {
        CancelConnection();

        CircuitCanvas.Children.Clear();
        _gateRoots.Clear();
        _gateStates.Clear();
        _wireVisuals.Clear();
        _userInputIndicators.Clear();
        _userOutputIndicators.Clear();
        _numericEditors.Clear();
        _numericRepButtons.Clear();
        _clockEditors.Clear();
        _terminalVisuals.Clear();

        if (circuit.Gates.Count == 0)
        {
            CircuitCanvas.Width = 900;
            CircuitCanvas.Height = 600;
            DrawCanvasGrid();
            ApplyZoom();
            RefreshLiveState();
            return;
        }

        double minX = circuit.Gates.Min(g => g.X);
        double minY = circuit.Gates.Min(g => g.Y);
        double maxX = circuit.Gates.Max(g => g.X + g.Width);
        double maxY = circuit.Gates.Max(g => g.Y + g.Height);

        _offsetX = 80 - Math.Min(minX, 0);
        _offsetY = 80 - Math.Min(minY, 0);

        CircuitCanvas.Width = maxX + _offsetX + 120;
        CircuitCanvas.Height = maxY + _offsetY + 120;

        DrawCanvasGrid();

        foreach (WirePlacement wire in circuit.Wires)
        {
            CreateAndAttachWireVisual(wire);
        }

        foreach (GatePlacement gate in circuit.Gates)
        {
            foreach (TerminalLayout terminal in gate.Terminals)
            {
                TerminalVisual visual = CreateTerminalVisual(gate, terminal);
                _terminalVisuals[(gate, terminal.IsInput, terminal.PortIndex)] = visual;
                CircuitCanvas.Children.Add(visual.StemRoot);
                CircuitCanvas.Children.Add(visual.Root);
            }
        }

        foreach (GatePlacement gate in circuit.Gates)
        {
            Border visual = CreateGateVisual(gate);
            visual.ZIndex = 3;

            visual.PointerPressed += (_, e) => GateVisual_PointerPressed(gate, visual, e);
            visual.PointerMoved += (_, e) => GateVisual_PointerMoved(gate, visual, e);
            visual.PointerReleased += (_, e) => GateVisual_PointerReleased(gate, visual, e);
            visual.PointerEntered += (_, _) => InfoLineText.Text = gate.Name;
            visual.PointerExited += (_, _) => InfoLineText.Text = "Ready";

            _gateRoots[gate] = visual;
            CircuitCanvas.Children.Add(visual);
            UpdateGateVisualPosition(gate);
        }

        UpdateAllTerminalVisualPositions();
        UpdateAllWireGeometry();

        ApplyZoom();
        RefreshLiveState();
    }

    private void DrawCanvasGrid()
    {
        if (!_showGrid)
        {
            return;
        }

        const int grid = 32;

        for (int x = 0; x <= CircuitCanvas.Width; x += grid)
        {
            Line line = new()
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, CircuitCanvas.Height),
                Stroke = new SolidColorBrush(Color.Parse("#FFE5E5E5")),
                StrokeThickness = 1,
                ZIndex = 0,
            };
            CircuitCanvas.Children.Add(line);
        }

        for (int y = 0; y <= CircuitCanvas.Height; y += grid)
        {
            Line line = new()
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(CircuitCanvas.Width, y),
                Stroke = new SolidColorBrush(Color.Parse("#FFE5E5E5")),
                StrokeThickness = 1,
                ZIndex = 0,
            };
            CircuitCanvas.Children.Add(line);
        }
    }

    private void CreateAndAttachWireVisual(WirePlacement wire)
    {
        ShapePath outer = new()
        {
            Stroke = Brushes.Black,
            StrokeThickness = 4,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            ZIndex = 1,
        };

        ShapePath inner = new()
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            ZIndex = 2,
        };

        outer.PointerPressed += (_, e) => WireLine_PointerPressed(wire, e);
        outer.PointerEntered += (_, _) => InfoLineText.Text = "Click or right-click wire to disconnect";
        outer.PointerExited += (_, _) => InfoLineText.Text = "Ready";

        _wireVisuals[wire] = new WireVisual
        {
            Outer = outer,
            Inner = inner,
        };

        CircuitCanvas.Children.Add(outer);
        CircuitCanvas.Children.Add(inner);
    }

    private TerminalVisual CreateTerminalVisual(GatePlacement gate, TerminalLayout terminal)
    {
        Canvas stemRoot = new()
        {
            Width = 10,
            Height = 22,
            Background = Brushes.Transparent,
            ZIndex = 2,
            IsHitTestVisible = false,
        };

        Line stem = new()
        {
            StartPoint = new Point(5, 10),
            EndPoint = new Point(5, 22),
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        stemRoot.Children.Add(stem);

        Canvas root = new()
        {
            Width = 10,
            Height = 22,
            Background = Brushes.Transparent,
            ZIndex = 4,
        };

        Ellipse dot = new()
        {
            Width = 10,
            Height = 10,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            Fill = Brushes.White,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, 0);
        Canvas.SetTop(dot, 0);

        Polygon arrow = new()
        {
            Stroke = Brushes.DarkGray,
            Fill = Brushes.DarkGray,
            Points = terminal.IsInput
                ? new Points { new Point(3, 2), new Point(5, 8), new Point(7, 2) }
                : new Points { new Point(3, 8), new Point(5, 2), new Point(7, 8) },
            IsHitTestVisible = false,
        };

        root.Children.Add(dot);
        root.Children.Add(arrow);
        root.PointerPressed += (_, e) => TerminalDot_PointerPressed(gate, terminal, e);
        root.PointerEntered += (_, _) =>
        {
            if (terminal.IsInput)
            {
                InfoLineText.Text = $"Input {terminal.PortIndex} on {gate.Name}. Click to disconnect or connect.";
            }
            else
            {
                InfoLineText.Text = $"Output {terminal.PortIndex} on {gate.Name}. Drag to connect.";
            }
        };
        root.PointerExited += (_, _) => InfoLineText.Text = "Ready";

        return new TerminalVisual
        {
            StemRoot = stemRoot,
            Root = root,
            Dot = dot,
            Arrow = arrow,
        };
    }

    private Border CreateGateVisual(GatePlacement gate)
    {
        Border root = new()
        {
            Width = gate.Width,
            Height = gate.Height,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
        };

        Canvas surface = new()
        {
            Width = gate.Width,
            Height = gate.Height,
            Background = Brushes.Transparent,
        };

        if (gate.Gate is UserInput input)
        {
            DrawUserInput(surface, gate, input);
        }
        else if (gate.Gate is UserOutput output)
        {
            DrawUserOutput(surface, gate, output);
        }
        else if (gate.Gate is NumericInput numericInput)
        {
            DrawNumericInput(surface, gate, numericInput);
        }
        else if (gate.Gate is NumericOutput numericOutput)
        {
            DrawNumericOutput(surface, gate, numericOutput);
        }
        else if (gate.Gate is Clock clock)
        {
            DrawClock(surface, gate, clock);
        }
        else if (gate.Type == "IC")
        {
            DrawIntegratedCircuit(surface, gate);
        }
        else if (gate.Type == "Comment")
        {
            DrawComment(surface, gate);
        }
        else
        {
            DrawLogicGate(surface, gate);
        }

        root.Child = surface;
        return root;
    }

    private void DrawLogicGate(Canvas surface, GatePlacement gate)
    {
        if (!GatePath.TryGetValue(gate.Type, out string pathData))
        {
            pathData = GatePath["And"];
        }

        Avalonia.Controls.Shapes.Path shape = new()
        {
            Data = Geometry.Parse(pathData),
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Fill = Brushes.White,
            StrokeLineCap = PenLineCap.Square,
        };

        if (gate.Type is "And" or "Or" or "Nand" or "Nor")
        {
            // Preserve classic look: these shapes expand vertically with additional ports.
            double scaleY = Math.Max(1.0, (gate.Height - 32.0) / 32.0);
            shape.RenderTransform = new TransformGroup
            {
                Children = new Transforms
                {
                    new ScaleTransform(1.0, scaleY),
                    new TranslateTransform(0, 15.0 * (1.0 - scaleY)),
                },
            };
        }

        surface.Children.Add(shape);
    }

    private void DrawIntegratedCircuit(Canvas surface, GatePlacement gate)
    {
        Border body = new()
        {
            Width = Math.Max(20, gate.Width - 24),
            Height = Math.Max(20, gate.Height - 34),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            Background = Brushes.White,
        };

        Canvas.SetLeft(body, 12);
        Canvas.SetTop(body, 17);
        surface.Children.Add(body);

        TextBlock name = new()
        {
            Width = Math.Max(20, gate.Width - 40),
            Height = 24,
            Text = gate.Name,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Canvas.SetLeft(name, 20);
        Canvas.SetTop(name, (gate.Height / 2) - 12);
        surface.Children.Add(name);
    }

    private void DrawComment(Canvas surface, GatePlacement gate)
    {
        Avalonia.Controls.Shapes.Path bubble = new()
        {
            Data = Geometry.Parse(BuildCommentBubblePath(
                Math.Max(20, gate.Width - 30),
                Math.Max(20, gate.Height - 30))),
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Fill = Brushes.White,
        };

        TextBox text = new()
        {
            Text = gate.CommentText ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Width = Math.Max(16, gate.Width - 40),
            Height = Math.Max(16, gate.Height - 40),
        };

        Canvas.SetLeft(bubble, 15);
        Canvas.SetTop(bubble, 15);
        surface.Children.Add(bubble);
        Canvas.SetLeft(text, 20);
        Canvas.SetTop(text, 20);
        surface.Children.Add(text);
    }

    private void DrawUserInput(Canvas surface, GatePlacement gate, UserInput input)
    {
        Border outer = new()
        {
            Width = 34,
            Height = 34,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            Background = Brushes.White,
        };
        Border inner = new()
        {
            Width = 24,
            Height = 24,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            Background = Brushes.Beige,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = "interactive",
        };

        Canvas.SetLeft(outer, (gate.Width - outer.Width) / 2);
        Canvas.SetTop(outer, (gate.Height - outer.Height) / 2);
        Canvas.SetLeft(inner, (gate.Width - inner.Width) / 2);
        Canvas.SetTop(inner, (gate.Height - inner.Height) / 2);

        inner.Tapped += (_, e) =>
        {
            input.Value = !input.Value;
            RefreshLiveState();
            e.Handled = true;
        };

        surface.Children.Add(outer);
        surface.Children.Add(inner);

        TextBlock label = new()
        {
            Text = UserIoLabel(input.Name, "UserInput"),
            Width = 24,
            TextAlignment = TextAlignment.Center,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, (gate.Width - label.Width) / 2);
        Canvas.SetTop(label, (gate.Height - label.FontSize) / 2 - 1);
        surface.Children.Add(label);

        _userInputIndicators[input] = inner;
    }

    private void DrawUserOutput(Canvas surface, GatePlacement gate, UserOutput output)
    {
        Ellipse outer = new()
        {
            Width = 34,
            Height = 34,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Fill = Brushes.White,
        };

        Ellipse inner = new()
        {
            Width = 24,
            Height = 24,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Fill = Brushes.Beige,
        };

        Canvas.SetLeft(outer, (gate.Width - outer.Width) / 2);
        Canvas.SetTop(outer, (gate.Height - outer.Height) / 2);
        Canvas.SetLeft(inner, (gate.Width - inner.Width) / 2);
        Canvas.SetTop(inner, (gate.Height - inner.Height) / 2);

        surface.Children.Add(outer);
        surface.Children.Add(inner);

        TextBlock label = new()
        {
            Text = UserIoLabel(output.Name, "UserOutput"),
            Width = 24,
            TextAlignment = TextAlignment.Center,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, (gate.Width - label.Width) / 2);
        Canvas.SetTop(label, (gate.Height - label.FontSize) / 2 - 1);
        surface.Children.Add(label);

        _userOutputIndicators[output] = inner;
    }

    private void DrawNumericInput(Canvas surface, GatePlacement gate, NumericInput input)
    {
        DrawNumericBody(surface, gate, input.Name, input.Bits);

        TextBox editor = new()
        {
            Width = Math.Max(24, gate.Width - 40),
            Height = 18,
            Text = input.Value,
            TextAlignment = TextAlignment.Center,
            FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"),
            FontSize = 12,
            Background = Brushes.AntiqueWhite,
        };

        Button rep = new()
        {
            Width = editor.Width,
            Height = 14,
            Content = ShortRepresentation(input.SelectedRepresentation),
            FontSize = 9,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        editor.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                ApplyNumericInput(input, editor.Text);
            }
        };
        editor.LostFocus += (_, _) => ApplyNumericInput(input, editor.Text);

        rep.Click += (_, _) =>
        {
            input.SelectedRepresentation = NextRepresentation(input);
            editor.Text = input.Value;
            rep.Content = ShortRepresentation(input.SelectedRepresentation);
            RefreshLiveState();
        };

        double left = (gate.Width - editor.Width) / 2;
        Canvas.SetLeft(editor, left);
        Canvas.SetTop(editor, 20);
        Canvas.SetLeft(rep, left);
        Canvas.SetTop(rep, 36);

        surface.Children.Add(editor);
        surface.Children.Add(rep);

        _numericEditors[input] = editor;
        _numericRepButtons[input] = rep;
    }

    private void DrawNumericOutput(Canvas surface, GatePlacement gate, NumericOutput output)
    {
        DrawNumericBody(surface, gate, output.Name, output.Bits);

        TextBlock state = new()
        {
            Width = Math.Max(24, gate.Width - 40),
            TextAlignment = TextAlignment.Center,
            FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"),
            FontSize = 12,
        };

        Canvas.SetLeft(state, (gate.Width - state.Width) / 2);
        Canvas.SetTop(state, 23);

        surface.Children.Add(state);
        _gateStates[gate] = state;
    }

    private void DrawNumericBody(Canvas surface, GatePlacement gate, string title, int bits)
    {
        Border body = new()
        {
            Width = Math.Max(20, gate.Width - 30),
            Height = Math.Max(20, gate.Height - 30),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            Background = Brushes.White,
        };

        Canvas.SetLeft(body, 15);
        Canvas.SetTop(body, 15);
        surface.Children.Add(body);
    }

    private void DrawClock(Canvas surface, GatePlacement gate, Clock clock)
    {
        Border body = new()
        {
            Width = Math.Max(22, gate.Width - 10),
            Height = Math.Max(20, gate.Height - 34),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            Background = Brushes.White,
        };

        Canvas.SetLeft(body, 5);
        Canvas.SetTop(body, 17);
        surface.Children.Add(body);

        Avalonia.Controls.Shapes.Path wave = new()
        {
            Data = Geometry.Parse("M 10,22 h 5 v 5 h -5 v 5 h 5 v 5 h -5 v 5 h 5"),
            Stroke = Brushes.Black,
            StrokeThickness = 2,
        };
        surface.Children.Add(wave);

        TextBox editor = new()
        {
            Width = 34,
            Height = 18,
            Text = clock.Milliseconds.ToString(),
            TextAlignment = TextAlignment.Center,
            FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"),
            FontSize = 12,
            Background = Brushes.AntiqueWhite,
        };
        editor.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                ApplyClockValue(clock, editor.Text);
            }
        };
        editor.LostFocus += (_, _) => ApplyClockValue(clock, editor.Text);

        Canvas.SetLeft(editor, Math.Max(8, gate.Width - editor.Width - 10));
        Canvas.SetTop(editor, 23);
        surface.Children.Add(editor);

        _clockEditors[clock] = editor;
    }

    private void GateVisual_PointerPressed(GatePlacement gate, Border visual, PointerPressedEventArgs e)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        if (e.GetCurrentPoint(visual).Properties.IsRightButtonPressed)
        {
            SelectGate(gate);
            ShowGateContextMenu(gate, visual);
            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed)
        {
            SelectGate(gate);

            if (e.Source is TextBox || e.Source is Button || e.Source is ToggleButton)
            {
                return;
            }

            if (e.Source is Control { Tag: "interactive" })
            {
                return;
            }

            _draggingGate = gate;
            _isDragging = true;

            Point p = e.GetPosition(CircuitCanvas);
            Point gateCanvas = new(_offsetX + gate.X, _offsetY + gate.Y);
            _dragOffset = new Point(p.X - gateCanvas.X, p.Y - gateCanvas.Y);

            e.Pointer.Capture(visual);
            e.Handled = true;
        }
    }

    private void GateVisual_PointerMoved(GatePlacement gate, Border visual, PointerEventArgs e)
    {
        if (!_isDragging || _draggingGate != gate)
        {
            return;
        }

        Point p = e.GetPosition(CircuitCanvas);

        double nx = p.X - _dragOffset.X;
        double ny = p.Y - _dragOffset.Y;

        bool snap = _snapToGrid && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (snap)
        {
            nx = SnapToGrid(nx);
            ny = SnapToGrid(ny);
        }
        else
        {
            nx = SnapToStep(nx, 2.0);
            ny = SnapToStep(ny, 2.0);
        }

        gate.X = nx - _offsetX;
        gate.Y = ny - _offsetY;

        UpdateGateVisualPosition(gate);
        UpdateTerminalDotsForGate(gate);
        UpdateAllWireGeometry();

        e.Handled = true;
    }

    private void GateVisual_PointerReleased(GatePlacement gate, Border visual, PointerReleasedEventArgs e)
    {
        if (_isDragging && _draggingGate == gate)
        {
            _isDragging = false;
            _draggingGate = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void ShowCanvasContextMenu()
    {
        MenuItem paste = new() { Header = "Paste Gate" };
        paste.Click += (_, _) => PasteCopiedGate();

        MenuItem actualSize = new() { Header = "Actual Size" };
        actualSize.Click += (_, _) => ActualSizeButton_Click(this, new RoutedEventArgs());

        MenuItem fit = new() { Header = "Fit Circuit" };
        fit.Click += (_, _) => FitButton_Click(this, new RoutedEventArgs());

        ContextMenu menu = new()
        {
            ItemsSource = new object[]
            {
                paste,
                new Separator(),
                actualSize,
                fit,
            },
        };

        menu.Open(CircuitCanvas);
    }

    private void ShowGateContextMenu(GatePlacement gate, Control target)
    {
        MenuItem copy = new() { Header = "Copy Gate" };
        copy.Click += (_, _) =>
        {
            SelectGate(gate);
            CopySelectedGate();
        };

        MenuItem paste = new() { Header = "Paste Gate" };
        paste.Click += (_, _) => PasteCopiedGate();

        MenuItem delete = new() { Header = "Delete Gate" };
        delete.Click += (_, _) =>
        {
            SelectGate(gate);
            DeleteGate(gate);
        };

        List<object> items = new()
        {
            copy,
            paste,
            new Separator(),
        };

        if (gate.Gate is IC)
        {
            MenuItem inline = new() { Header = "Inline Component" };
            inline.Click += (_, _) =>
            {
                SelectGate(gate);
                InlineSelectedComponent();
            };
            items.Add(inline);
        }

        MenuItem inlineAll = new() { Header = "Inline All Components" };
        inlineAll.Click += (_, _) => InlineAllComponents();
        items.Add(inlineAll);
        items.Add(new Separator());
        items.Add(delete);

        ContextMenu menu = new()
        {
            ItemsSource = items,
        };

        menu.Open(target);
    }

    private void TerminalDot_PointerPressed(GatePlacement gate, TerminalLayout terminal, PointerPressedEventArgs e)
    {
        if (_activeCircuit == null || !e.GetCurrentPoint(CircuitCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        SelectGate(gate);

        if (!terminal.IsInput)
        {
            StartConnection(gate, terminal.PortIndex);
            e.Handled = true;
            return;
        }

        if (_isConnecting)
        {
            ConnectWire(_connectFromGate, _connectFromPort, gate, terminal.PortIndex);
            CancelConnection();
            e.Handled = true;
            return;
        }

        DisconnectInput(gate, terminal.PortIndex);
        e.Handled = true;
    }

    private void WireLine_PointerPressed(WirePlacement wire, PointerPressedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(CircuitCanvas).Properties;
        if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed)
        {
            return;
        }

        DisconnectWire(wire);
        e.Handled = true;
    }

    private void StartConnection(GatePlacement fromGate, int fromPort)
    {
        CancelConnection();

        _isConnecting = true;
        _connectFromGate = fromGate;
        _connectFromPort = fromPort;
        _connectHoverTarget = null;

        Point start = GetTerminalPoint(fromGate, false, fromPort);
        _connectionPreviewPoint = start;

        _connectionPreview = new ShapePath
        {
            Stroke = Brushes.SteelBlue,
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 3, 2 },
            ZIndex = 5,
            IsHitTestVisible = false,
        };

        _connectionPreview.Data = BuildWireGeometry(start, start);
        CircuitCanvas.Children.Add(_connectionPreview);

        InfoLineText.Text = "Connecting wire: release near an input terminal.";
    }

    private void UpdateConnectionPreview(Point current)
    {
        if (!_isConnecting || _connectionPreview == null)
        {
            return;
        }

        Point start = GetTerminalPoint(_connectFromGate, false, _connectFromPort);
        _connectionPreviewPoint = current;
        _connectHoverTarget = FindNearestInputTerminal(current, 18);
        _connectionPreview.Data = BuildWireGeometry(start, current);
        RefreshLiveState();
    }

    private void CancelConnection()
    {
        _isConnecting = false;
        _connectFromGate = null;
        _connectHoverTarget = null;
        if (_connectionPreview != null)
        {
            CircuitCanvas.Children.Remove(_connectionPreview);
            _connectionPreview = null;
        }

        InfoLineText.Text = "Ready";
        RefreshLiveState();
    }

    private (GatePlacement Gate, int Port)? FindNearestInputTerminal(Point point, double maxDistance)
    {
        (GatePlacement Gate, int Port)? best = null;
        double bestDist = double.MaxValue;

        foreach (((GatePlacement gate, bool isInput, int port), TerminalVisual _) in _terminalVisuals)
        {
            if (!isInput)
            {
                continue;
            }

            Point dp = GetTerminalPoint(gate, true, port);

            double d = Math.Sqrt(Math.Pow(dp.X - point.X, 2) + Math.Pow(dp.Y - point.Y, 2));
            if (d < bestDist && d <= maxDistance)
            {
                bestDist = d;
                best = (gate, port);
            }
        }

        return best;
    }

    private void ConnectWire(GatePlacement fromGate, int fromPort, GatePlacement toGate, int toPort)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        try
        {
            WirePlacement existing = _activeCircuit.Wires.FirstOrDefault(w => w.ToGate == toGate && w.ToPort == toPort);
            if (existing != null)
            {
                DisconnectWire(existing);
            }

            Terminal input = new(toPort, toGate.Gate);
            _activeCircuit.Circuit[input] = new Terminal(fromPort, fromGate.Gate);

            WirePlacement wire = new()
            {
                FromGate = fromGate,
                FromPort = fromPort,
                ToGate = toGate,
                ToPort = toPort,
            };

            _activeCircuit.Wires.Add(wire);
            CreateAndAttachWireVisual(wire);
            UpdateAllWireGeometry();

            StatusText.Text = "Connected";
            InfoLineText.Text = $"Connected {fromGate.Name} -> {toGate.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Connect failed";
            InfoText.Text = ex.Message;
        }
    }

    private void DisconnectInput(GatePlacement gate, int inputPort)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        WirePlacement existing = _activeCircuit.Wires.FirstOrDefault(w => w.ToGate == gate && w.ToPort == inputPort);
        if (existing == null)
        {
            return;
        }

        DisconnectWire(existing);
    }

    private void DisconnectWire(WirePlacement wire)
    {
        if (_activeCircuit == null || wire == null)
        {
            return;
        }

        _activeCircuit.Circuit.Disconnect(new Terminal(wire.ToPort, wire.ToGate.Gate));
        _activeCircuit.Wires.Remove(wire);

        if (_wireVisuals.TryGetValue(wire, out WireVisual visual))
        {
            CircuitCanvas.Children.Remove(visual.Outer);
            CircuitCanvas.Children.Remove(visual.Inner);
            _wireVisuals.Remove(wire);
        }

        StatusText.Text = "Disconnected";
        InfoLineText.Text = "Wire disconnected";
    }

    private void DeleteGate(GatePlacement gate)
    {
        if (_activeCircuit == null || gate == null)
        {
            return;
        }

        List<WirePlacement> touching = _activeCircuit.Wires
            .Where(w => w.FromGate == gate || w.ToGate == gate)
            .ToList();

        foreach (WirePlacement wire in touching)
        {
            DisconnectWire(wire);
        }

        _activeCircuit.Circuit.Remove(gate.Gate);
        _activeCircuit.Gates.Remove(gate);

        if (_gateRoots.TryGetValue(gate, out Border root))
        {
            CircuitCanvas.Children.Remove(root);
            _gateRoots.Remove(gate);
        }

        foreach (((GatePlacement g, bool input, int port), TerminalVisual visual) in _terminalVisuals.Where(kv => kv.Key.Gate == gate).ToList())
        {
            CircuitCanvas.Children.Remove(visual.StemRoot);
            CircuitCanvas.Children.Remove(visual.Root);
            _terminalVisuals.Remove((g, input, port));
        }

        _gateStates.Remove(gate);

        if (_selectedGate == gate)
        {
            _selectedGate = null;
        }

        RenderInputPanel(_activeCircuit);
        RefreshLiveState();

        StatusText.Text = "Deleted";
        InfoLineText.Text = "Gate deleted";
    }

    private void CopySelectedGate()
    {
        if (_selectedGate == null)
        {
            StatusText.Text = "Copy skipped";
            InfoLineText.Text = "Select a gate first.";
            return;
        }

        _gateClipboard = new GateClipboardData
        {
            Type = _selectedGate.Type,
            Gate = _selectedGate.Gate.Clone(),
            Terminals = CloneTerminalLayouts(_selectedGate.Terminals),
            CommentText = _selectedGate.CommentText,
            Width = _selectedGate.Width,
            Height = _selectedGate.Height,
            Angle = _selectedGate.Angle,
        };

        StatusText.Text = "Copied";
        InfoLineText.Text = $"Copied {_selectedGate.Name}.";
    }

    private void PasteCopiedGate()
    {
        if (_activeCircuit == null)
        {
            return;
        }

        if (_gateClipboard == null)
        {
            StatusText.Text = "Paste skipped";
            InfoLineText.Text = "Clipboard is empty.";
            return;
        }

        try
        {
            Point anchor = _selectedGate != null
                ? new Point(
                    _offsetX + _selectedGate.X + (_selectedGate.Width / 2.0) + 28,
                    _offsetY + _selectedGate.Y + (_selectedGate.Height / 2.0) + 28)
                : new Point(GetZoomAnchorFallback().X + 28, GetZoomAnchorFallback().Y + 28);

            GatePlacement placement = CreatePlacementFromClipboard(anchor);
            _activeCircuit.Circuit.Add(placement.Gate);
            _activeCircuit.Gates.Add(placement);

            RenderCircuit(_activeCircuit);
            RenderInputPanel(_activeCircuit);
            SelectGate(placement);

            StatusText.Text = "Pasted";
            InfoLineText.Text = $"Added {placement.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Paste failed";
            InfoText.Text = ex.Message;
        }
    }

    private GatePlacement CreatePlacementFromClipboard(Point canvasPoint)
    {
        if (_activeCircuit == null || _gateClipboard == null)
        {
            throw new InvalidOperationException("Cannot paste without an active circuit and copied gate.");
        }

        AbstractGate gate = _gateClipboard.Gate.Clone();
        List<TerminalLayout> terminals = CloneTerminalLayouts(_gateClipboard.Terminals);

        double left = canvasPoint.X - (_gateClipboard.Width / 2.0);
        double top = canvasPoint.Y - (_gateClipboard.Height / 2.0);

        left = _snapToGrid ? SnapToGrid(left) : SnapToStep(left, 2.0);
        top = _snapToGrid ? SnapToGrid(top) : SnapToStep(top, 2.0);

        int nextId = _activeCircuit.Gates.Count == 0 ? 1 : _activeCircuit.Gates.Max(g => g.Id) + 1;

        GatePlacement placement = new()
        {
            Id = nextId,
            Type = _gateClipboard.Type,
            Name = GenerateGateName(gate.Name),
            Gate = gate,
            X = left - _offsetX,
            Y = top - _offsetY,
            Angle = _gateClipboard.Angle,
            Width = _gateClipboard.Width,
            Height = _gateClipboard.Height,
            CommentText = gate is Comment c ? c.Value : _gateClipboard.CommentText,
        };

        placement.Terminals.AddRange(terminals);
        placement.RebuildTerminalLookup();
        return placement;
    }

    private void InlineSelectedComponent()
    {
        if (_activeCircuit == null || _selectedGate == null)
        {
            return;
        }

        if (_selectedGate.Gate is not IC)
        {
            StatusText.Text = "Inline skipped";
            InfoLineText.Text = "Select a custom component (IC) to inline.";
            return;
        }

        bool wasRunning = _activeCircuit.Circuit.IsRunning;
        try
        {
            if (wasRunning)
            {
                _activeCircuit.Circuit.Stop();
            }

            GatePlacement selected = _selectedGate;
            if (!InlineComponentPlacement(selected, selectFocus: true, out _))
            {
                StatusText.Text = "Inline skipped";
                InfoLineText.Text = "Component has no internal gates.";
                return;
            }

            StatusText.Text = "Inlined";
            InfoLineText.Text = $"Inlined {selected.Name}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Inline failed";
            InfoText.Text = ex.Message;
        }
        finally
        {
            if (wasRunning && _activeCircuit != null)
            {
                _activeCircuit.Circuit.Start();
            }
        }
    }

    private void InlineAllComponents()
    {
        if (_activeCircuit == null)
        {
            return;
        }

        bool wasRunning = _activeCircuit.Circuit.IsRunning;
        try
        {
            if (wasRunning)
            {
                _activeCircuit.Circuit.Stop();
            }

            int inlinedCount = 0;
            GatePlacement firstFocus = null;

            while (true)
            {
                GatePlacement nextIc = _activeCircuit.Gates.FirstOrDefault(g => g.Gate is IC);
                if (nextIc == null)
                {
                    break;
                }

                if (!InlineComponentPlacement(nextIc, selectFocus: false, out GatePlacement focus))
                {
                    StatusText.Text = "Flatten skipped";
                    InfoLineText.Text = $"Component '{nextIc.Name}' has no internal gates.";
                    return;
                }

                firstFocus ??= focus;
                inlinedCount++;

                if (inlinedCount > 512)
                {
                    throw new InvalidOperationException("Flatten safety limit reached (possible recursive component loop).");
                }
            }

            if (inlinedCount == 0)
            {
                StatusText.Text = "Flatten skipped";
                InfoLineText.Text = "No custom components found.";
                return;
            }

            if (firstFocus != null && _activeCircuit.Gates.Contains(firstFocus))
            {
                SelectGate(firstFocus);
            }
            else
            {
                SelectGate(null);
            }

            StatusText.Text = "Flattened";
            InfoLineText.Text = $"Inlined {inlinedCount} component(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Flatten failed";
            InfoText.Text = ex.Message;
        }
        finally
        {
            if (wasRunning && _activeCircuit != null)
            {
                _activeCircuit.Circuit.Start();
            }
        }
    }

    private bool InlineComponentPlacement(GatePlacement icPlacement, bool selectFocus, out GatePlacement focus)
    {
        focus = null;
        if (_activeCircuit == null || icPlacement?.Gate is not IC icGate)
        {
            return false;
        }

        Circuit clonedInnerCircuit = icGate.Circuit.Clone();
        List<AbstractGate> clonedInnerGates = clonedInnerCircuit.ToList();
        if (clonedInnerGates.Count == 0)
        {
            return false;
        }

        UserInput[] clonedInputs = icGate.Inputs
            .Select(input => (UserInput)clonedInnerGates[icGate.Circuit.IndexOf(input)])
            .ToArray();
        UserOutput[] clonedOutputs = icGate.Outputs
            .Select(output => (UserOutput)clonedInnerGates[icGate.Circuit.IndexOf(output)])
            .ToArray();

        int nextId = _activeCircuit.Gates.Count == 0 ? 1 : _activeCircuit.Gates.Max(g => g.Id) + 1;
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(clonedInnerGates.Count)));
        const double spacing = 96.0;
        double baseX = icPlacement.X - 16.0;
        double baseY = icPlacement.Y - 16.0;

        var placementByInnerGate = new Dictionary<AbstractGate, GatePlacement>();
        var newPlacements = new List<GatePlacement>(clonedInnerGates.Count);

        for (int i = 0; i < clonedInnerGates.Count; i++)
        {
            AbstractGate innerGate = clonedInnerGates[i];
            string type = ResolveGateType(innerGate);
            List<TerminalLayout> terminals = BuildGateTerminalLayoutsForPlacement(type, innerGate);
            (double width, double height) = EstimateGateSize(
                type,
                innerGate.Name,
                innerGate is Comment cmt ? cmt.Value : null,
                terminals);

            int col = i % columns;
            int row = i / columns;
            double modelLeft = baseX + (col * spacing);
            double modelTop = baseY + (row * spacing);

            double snappedCanvasLeft = _snapToGrid
                ? SnapToGrid(_offsetX + modelLeft)
                : SnapToStep(_offsetX + modelLeft, 2.0);
            double snappedCanvasTop = _snapToGrid
                ? SnapToGrid(_offsetY + modelTop)
                : SnapToStep(_offsetY + modelTop, 2.0);

            GatePlacement placement = new()
            {
                Id = nextId++,
                Type = type,
                Name = GenerateGateName(innerGate.Name),
                Gate = innerGate,
                X = snappedCanvasLeft - _offsetX,
                Y = snappedCanvasTop - _offsetY,
                Angle = 0,
                Width = width,
                Height = height,
                CommentText = innerGate is Comment comment ? comment.Value : null,
            };
            placement.Terminals.AddRange(terminals);
            placement.RebuildTerminalLookup();

            _activeCircuit.Circuit.Add(innerGate);
            _activeCircuit.Gates.Add(placement);
            placementByInnerGate[innerGate] = placement;
            newPlacements.Add(placement);
        }

        foreach (AbstractGate targetGate in clonedInnerGates)
        {
            for (int inputPort = 0; inputPort < targetGate.NumberOfInputs; inputPort++)
            {
                Terminal source = clonedInnerCircuit.GetSource(new Terminal(inputPort, targetGate));
                if (source == null)
                {
                    continue;
                }

                ConnectWireModel(
                    placementByInnerGate[source.gate],
                    source.portNumber,
                    placementByInnerGate[targetGate],
                    inputPort);
            }
        }

        List<WirePlacement> incomingExternal = _activeCircuit.Wires.Where(w => w.ToGate == icPlacement).ToList();
        List<WirePlacement> outgoingExternal = _activeCircuit.Wires.Where(w => w.FromGate == icPlacement).ToList();

        foreach (WirePlacement incoming in incomingExternal)
        {
            if (incoming.ToPort < 0 || incoming.ToPort >= clonedInputs.Length)
            {
                continue;
            }

            List<Terminal> targets = clonedInnerCircuit.GetTargets(new Terminal(0, clonedInputs[incoming.ToPort]));
            foreach (Terminal target in targets)
            {
                if (placementByInnerGate.TryGetValue(target.gate, out GatePlacement targetPlacement))
                {
                    ConnectWireModel(incoming.FromGate, incoming.FromPort, targetPlacement, target.portNumber);
                }
            }
        }

        foreach (WirePlacement outgoing in outgoingExternal)
        {
            if (outgoing.FromPort < 0 || outgoing.FromPort >= clonedOutputs.Length)
            {
                continue;
            }

            Terminal source = clonedInnerCircuit.GetSource(new Terminal(0, clonedOutputs[outgoing.FromPort]));
            if (source == null)
            {
                continue;
            }

            if (placementByInnerGate.TryGetValue(source.gate, out GatePlacement sourcePlacement))
            {
                ConnectWireModel(sourcePlacement, source.portNumber, outgoing.ToGate, outgoing.ToPort);
            }
        }

        RemoveGateModel(icPlacement);
        foreach (GatePlacement endpoint in newPlacements.Where(p => p.Gate is UserInput or UserOutput).ToList())
        {
            RemoveGateModel(endpoint);
        }

        RenderCircuit(_activeCircuit);
        RenderInputPanel(_activeCircuit);

        focus = newPlacements.FirstOrDefault(p => p.Gate is not UserInput and not UserOutput);
        if (selectFocus)
        {
            if (focus != null && _activeCircuit.Gates.Contains(focus))
            {
                SelectGate(focus);
            }
            else
            {
                SelectGate(null);
            }
        }

        return true;
    }

    private static string ResolveGateType(AbstractGate gate)
    {
        return gate switch
        {
            Gates.BasicGates.And => "And",
            Gates.BasicGates.Not => "Not",
            Gates.BasicGates.Or => "Or",
            Gates.BasicGates.Nand => "Nand",
            Gates.BasicGates.Nor => "Nor",
            Gates.BasicGates.Xor => "Xor",
            Gates.BasicGates.Xnor => "Xnor",
            Gates.BasicGates.Buffer => "Buffer",
            UserInput => "UserInput",
            UserOutput => "UserOutput",
            NumericInput => "NumericInput",
            NumericOutput => "NumericOutput",
            Clock => "Clock",
            Comment => "Comment",
            IC => "IC",
            _ => throw new InvalidOperationException($"Unsupported inner gate type {gate.GetType().Name}."),
        };
    }

    private List<TerminalLayout> BuildGateTerminalLayoutsForPlacement(string type, AbstractGate gate)
    {
        if (type == "IC" && gate is IC ic && _namedIcTemplates.TryGetValue(ic.Name, out NamedIcTemplate template))
        {
            return CloneTerminalLayouts(template.Terminals);
        }

        if (type == "IC")
        {
            var descriptors = new List<(bool IsInput, int PortIndex, PortSide Side)>();
            for (int i = 0; i < gate.NumberOfInputs; i++)
            {
                descriptors.Add((true, i, PortSide.Left));
            }

            for (int i = 0; i < gate.Output.Length; i++)
            {
                descriptors.Add((false, i, PortSide.Right));
            }

            return BuildTerminalLayouts(descriptors);
        }

        return BuildGateTerminalLayouts(type, gate);
    }

    private void ConnectWireModel(GatePlacement fromGate, int fromPort, GatePlacement toGate, int toPort)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        WirePlacement existing = _activeCircuit.Wires.FirstOrDefault(w => w.ToGate == toGate && w.ToPort == toPort);
        if (existing != null)
        {
            _activeCircuit.Circuit.Disconnect(new Terminal(existing.ToPort, existing.ToGate.Gate));
            _activeCircuit.Wires.Remove(existing);
        }

        _activeCircuit.Circuit[new Terminal(toPort, toGate.Gate)] = new Terminal(fromPort, fromGate.Gate);
        _activeCircuit.Wires.Add(new WirePlacement
        {
            FromGate = fromGate,
            FromPort = fromPort,
            ToGate = toGate,
            ToPort = toPort,
        });
    }

    private void RemoveGateModel(GatePlacement gate)
    {
        if (_activeCircuit == null || gate == null)
        {
            return;
        }

        List<WirePlacement> touching = _activeCircuit.Wires
            .Where(w => w.FromGate == gate || w.ToGate == gate)
            .ToList();

        foreach (WirePlacement wire in touching)
        {
            _activeCircuit.Circuit.Disconnect(new Terminal(wire.ToPort, wire.ToGate.Gate));
            _activeCircuit.Wires.Remove(wire);
        }

        _activeCircuit.Circuit.Remove(gate.Gate);
        _activeCircuit.Gates.Remove(gate);

        if (_selectedGate == gate)
        {
            _selectedGate = null;
        }
    }

    private void AddGateFromPalette(string type, Point canvasPoint)
    {
        if (_activeCircuit == null)
        {
            return;
        }

        try
        {
            GatePlacement placement = CreatePlacementFromType(type, canvasPoint);
            _activeCircuit.Circuit.Add(placement.Gate);
            _activeCircuit.Gates.Add(placement);

            RenderCircuit(_activeCircuit);
            RenderInputPanel(_activeCircuit);
            SelectGate(placement);

            StatusText.Text = $"Added {GetPaletteDisplayName(type)}";
            InfoLineText.Text = $"Added {placement.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Add gate failed";
            InfoText.Text = ex.Message;
        }
    }

    private GatePlacement CreatePlacementFromType(string type, Point canvasPoint)
    {
        AbstractGate gate;
        string placementType;
        string placementName;
        List<TerminalLayout> terminals;

        if (TryResolveIcPaletteType(type, out NamedIcTemplate template))
        {
            placementType = "IC";
            gate = template.Template.Clone();
            placementName = template.Name;
            terminals = CloneTerminalLayouts(template.Terminals);
        }
        else
        {
            placementType = type;
            gate = type switch
            {
                "And" => new Gates.BasicGates.And(),
                "Not" => new Gates.BasicGates.Not(),
                "Or" => new Gates.BasicGates.Or(),
                "Nand" => new Gates.BasicGates.Nand(),
                "Nor" => new Gates.BasicGates.Nor(),
                "Xor" => new Gates.BasicGates.Xor(),
                "Xnor" => new Gates.BasicGates.Xnor(),
                "Buffer" => new Gates.BasicGates.Buffer(),
                "UserInput" => new UserInput(),
                "UserOutput" => new UserOutput(),
                "NumericInput" => new NumericInput(2),
                "NumericOutput" => new NumericOutput(2),
                "Clock" => new Clock(0),
                "Comment" => new Comment(),
                _ => throw new InvalidOperationException($"Unsupported palette gate '{type}'."),
            };
            placementName = gate.Name;
            terminals = BuildGateTerminalLayoutsForPlacement(placementType, gate);
        }

        (double width, double height) = EstimateGateSize(placementType, placementName, null, terminals);

        double left = canvasPoint.X - width / 2;
        double top = canvasPoint.Y - height / 2;

        left = _snapToGrid ? SnapToGrid(left) : SnapToStep(left, 2.0);
        top = _snapToGrid ? SnapToGrid(top) : SnapToStep(top, 2.0);

        int nextId = _activeCircuit.Gates.Count == 0 ? 1 : _activeCircuit.Gates.Max(g => g.Id) + 1;

        GatePlacement placement = new()
        {
            Id = nextId,
            Type = placementType,
            Name = GenerateGateName(gate.Name),
            Gate = gate,
            X = left - _offsetX,
            Y = top - _offsetY,
            Angle = 0,
            Width = width,
            Height = height,
            CommentText = gate is Comment c ? c.Value : null,
        };

        placement.Terminals.AddRange(terminals);
        placement.RebuildTerminalLookup();
        return placement;
    }

    private string GenerateGateName(string baseName)
    {
        if (_activeCircuit == null)
        {
            return baseName;
        }

        if (_activeCircuit.Gates.All(g => !string.Equals(g.Name, baseName, StringComparison.Ordinal)))
        {
            return baseName;
        }

        int seq = 2;
        while (_activeCircuit.Gates.Any(g => string.Equals(g.Name, $"{baseName} {seq}", StringComparison.Ordinal)))
        {
            seq++;
        }

        return $"{baseName} {seq}";
    }

    private static List<TerminalLayout> BuildGateTerminalLayouts(string type, AbstractGate gate)
    {
        List<(bool IsInput, int PortIndex, PortSide Side)> descriptors = type switch
        {
            "And" or "Not" or "Or" or "Nand" or "Nor" or "Xor" or "Xnor" or "Buffer" => BuildShapeGateDescriptors(gate),
            "UserInput" => new List<(bool, int, PortSide)> { (false, 0, PortSide.Right) },
            "UserOutput" => new List<(bool, int, PortSide)> { (true, 0, PortSide.Left) },
            "NumericInput" or "NumericOutput" => BuildNumericDescriptors(gate),
            "Clock" => new List<(bool, int, PortSide)> { (false, 0, PortSide.Top) },
            "Comment" => new List<(bool, int, PortSide)>(),
            _ => throw new InvalidOperationException($"Unsupported gate type {type}"),
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

        if (gate.Output.Length > 0)
        {
            descriptors.Add((false, 0, PortSide.Right));
        }

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

    private static List<TerminalLayout> BuildTerminalLayouts(IReadOnlyList<(bool IsInput, int PortIndex, PortSide Side)> descriptors)
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

    private static (double Width, double Height) EstimateGateSize(string type, string name, string comment, IReadOnlyList<TerminalLayout> terminals)
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

    private void RenderInputPanel(LoadedCircuit circuit)
    {
        InputsPanel.Children.Clear();
        _userInputButtons.Clear();

        foreach (GatePlacement placement in circuit.Gates.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            switch (placement.Gate)
            {
                case UserInput input:
                {
                    Button button = new();
                    button.Click += (_, _) =>
                    {
                        input.Value = !input.Value;
                        RefreshLiveState();
                    };
                    button.Content = $"{input.Name}: {(input.Value ? 1 : 0)}";
                    InputsPanel.Children.Add(button);
                    _userInputButtons[input] = button;
                    break;
                }
                case NumericInput numeric:
                {
                    TextBlock row = new()
                    {
                        Text = $"{numeric.Name}: {numeric.Value} ({ShortRepresentation(numeric.SelectedRepresentation)})",
                        FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"),
                        FontSize = 11,
                    };
                    InputsPanel.Children.Add(row);
                    break;
                }
                case Clock clock:
                {
                    TextBlock row = new()
                    {
                        Text = $"Clock: {clock.Milliseconds} ms",
                        FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"),
                        FontSize = 11,
                    };
                    InputsPanel.Children.Add(row);
                    break;
                }
            }
        }

        if (InputsPanel.Children.Count == 0)
        {
            InputsPanel.Children.Add(new TextBlock
            {
                Text = "No interactive inputs in this circuit.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void ApplyNumericInput(NumericInput gate, string value)
    {
        try
        {
            gate.Value = value ?? string.Empty;
            StatusText.Text = "Running";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Invalid numeric input";
            InfoText.Text = ex.Message;
        }

        RefreshLiveState();
    }

    private void ApplyClockValue(Clock clock, string value)
    {
        try
        {
            clock.Milliseconds = int.Parse(value ?? "0");
            StatusText.Text = "Running";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Invalid clock period";
            InfoText.Text = ex.Message;
        }

        RefreshLiveState();
    }

    private void RefreshLiveState()
    {
        if (_activeCircuit == null)
        {
            return;
        }

        _wireFlowOffset -= 1.2;
        if (_wireFlowOffset < -4096)
        {
            _wireFlowOffset = 0;
        }

        foreach ((WirePlacement wire, WireVisual visual) in _wireVisuals)
        {
            bool hot = false;
            if (_showTrueFalse && wire.FromPort >= 0 && wire.FromPort < wire.FromGate.Gate.Output.Length)
            {
                hot = wire.FromGate.Gate.Output[wire.FromPort];
            }

            visual.Outer.Stroke = Brushes.Black;
            visual.Inner.Stroke = hot ? Brushes.IndianRed : Brushes.White;
            visual.Inner.StrokeDashArray = hot
                ? new AvaloniaList<double> { 7, 4 }
                : null;
            visual.Inner.StrokeDashOffset = hot ? _wireFlowOffset : 0;

            double opacity = !_endUserMode || IsUserFacing(wire.FromGate.Gate) || IsUserFacing(wire.ToGate.Gate)
                ? 1.0
                : 0.15;
            visual.Outer.Opacity = opacity;
            visual.Inner.Opacity = opacity;
        }

        foreach ((GatePlacement gate, Border root) in _gateRoots)
        {
            root.Background = Brushes.Transparent;
            root.Opacity = !_endUserMode || IsUserFacing(gate.Gate) ? 1.0 : 0.2;

            if (_gateStates.TryGetValue(gate, out TextBlock state))
            {
                state.Text = BuildGateStateText(gate.Gate);
            }

            if (_selectedGate == gate)
            {
                root.BorderBrush = Brushes.DodgerBlue;
                root.BorderThickness = new Thickness(2);
            }
            else
            {
                root.BorderBrush = Brushes.Transparent;
                root.BorderThickness = new Thickness(2);
            }
        }

        foreach (((GatePlacement gate, bool isInput, int port), TerminalVisual visual) in _terminalVisuals)
        {
            bool lit = false;
            if (_showTrueFalse)
            {
                try
                {
                    lit = isInput
                        ? port >= 0 && port < gate.Gate.NumberOfInputs && gate.Gate[port]
                        : port >= 0 && port < gate.Gate.Output.Length && gate.Gate.Output[port];
                }
                catch
                {
                    lit = false;
                }
            }

            visual.Arrow.Fill = lit ? Brushes.Red : Brushes.DarkGray;
            visual.Arrow.Stroke = lit ? Brushes.Red : Brushes.DarkGray;
            visual.Dot.Fill = _isConnecting && isInput && _connectHoverTarget.HasValue &&
                              _connectHoverTarget.Value.Gate == gate &&
                              _connectHoverTarget.Value.Port == port
                ? Brushes.LightGreen
                : Brushes.White;
            visual.StemRoot.Opacity = !_endUserMode || IsUserFacing(gate.Gate) ? 1.0 : 0.2;
            visual.Root.Opacity = !_endUserMode || IsUserFacing(gate.Gate) ? 1.0 : 0.2;
        }

        foreach ((UserInput gate, Border indicator) in _userInputIndicators)
        {
            indicator.Background = (_showTrueFalse && gate.Value) ? Brushes.IndianRed : Brushes.Beige;

            if (_userInputButtons.TryGetValue(gate, out Button button))
            {
                button.Content = $"{gate.Name}: {(gate.Value ? 1 : 0)}";
            }
        }

        foreach ((UserOutput gate, Ellipse indicator) in _userOutputIndicators)
        {
            indicator.Fill = (_showTrueFalse && gate.Value) ? Brushes.IndianRed : Brushes.Beige;
        }

        foreach ((NumericInput gate, TextBox editor) in _numericEditors)
        {
            if (!editor.IsFocused)
            {
                editor.Text = gate.Value;
            }

            if (_numericRepButtons.TryGetValue(gate, out Button button))
            {
                button.Content = ShortRepresentation(gate.SelectedRepresentation);
            }
        }

        foreach ((Clock gate, TextBox editor) in _clockEditors)
        {
            if (!editor.IsFocused)
            {
                editor.Text = gate.Milliseconds.ToString();
            }
        }

        OutputText.Text = BuildOutputSnapshot(_activeCircuit);
    }

    private void SelectGate(GatePlacement gate)
    {
        _selectedGate = gate;
        RefreshLiveState();
    }

    private static bool IsUserFacing(AbstractGate gate)
    {
        return gate is UserInput or UserOutput or NumericInput or NumericOutput or Clock or Comment;
    }

    private void UpdateGateVisualPosition(GatePlacement gate)
    {
        if (_gateRoots.TryGetValue(gate, out Border root))
        {
            Canvas.SetLeft(root, _offsetX + gate.X);
            Canvas.SetTop(root, _offsetY + gate.Y);

            root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            root.RenderTransform = new RotateTransform(gate.Angle);
        }
    }

    private void UpdateAllTerminalVisualPositions()
    {
        foreach (((GatePlacement gate, bool isInput, int port), TerminalVisual visual) in _terminalVisuals)
        {
            UpdateTerminalVisualPosition(gate, isInput, port, visual);
        }
    }

    private void UpdateTerminalDotsForGate(GatePlacement gate)
    {
        foreach (((GatePlacement g, bool isInput, int port), TerminalVisual visual) in _terminalVisuals.Where(kv => kv.Key.Gate == gate))
        {
            UpdateTerminalVisualPosition(g, isInput, port, visual);
        }
    }

    private void UpdateTerminalVisualPosition(GatePlacement gate, bool isInput, int port, TerminalVisual visual)
    {
        Point p = GetTerminalPoint(gate, isInput, port);
        Canvas.SetLeft(visual.StemRoot, p.X - 5);
        Canvas.SetTop(visual.StemRoot, p.Y - 5);
        Canvas.SetLeft(visual.Root, p.X - 5);
        Canvas.SetTop(visual.Root, p.Y - 5);

        TerminalLayout terminal = gate.GetTerminal(isInput, port);
        visual.StemRoot.RenderTransformOrigin = new RelativePoint(0.5, 5.0 / 22.0, RelativeUnit.Relative);
        visual.StemRoot.RenderTransform = new RotateTransform(PortSideToAngle(terminal.Side) + gate.Angle);
        visual.Root.RenderTransformOrigin = new RelativePoint(0.5, 5.0 / 22.0, RelativeUnit.Relative);
        visual.Root.RenderTransform = new RotateTransform(PortSideToAngle(terminal.Side) + gate.Angle);
    }

    private void UpdateAllWireGeometry()
    {
        foreach ((WirePlacement wire, WireVisual visual) in _wireVisuals)
        {
            Point from = GetTerminalPoint(wire.FromGate, false, wire.FromPort);
            Point to = GetTerminalPoint(wire.ToGate, true, wire.ToPort);
            Geometry geometry = BuildWireGeometry(from, to);
            visual.Outer.Data = geometry;
            visual.Inner.Data = geometry;
        }
    }

    private static double SnapToGrid(double value)
    {
        const double grid = 32.0;
        return Math.Round(value / grid) * grid;
    }

    private static double SnapToStep(double value, double step)
    {
        return Math.Round(value / step) * step;
    }

    private Geometry BuildWireGeometry(Point from, Point to)
    {
        return _useCurvedWires
            ? BuildCurvedWireGeometry(from, to)
            : BuildOrthogonalWireGeometry(from, to);
    }

    private static Geometry BuildCurvedWireGeometry(Point from, Point to)
    {
        PathFigure figure = new()
        {
            StartPoint = from,
            IsClosed = false,
            IsFilled = false,
        };

        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(from.X * 0.6 + to.X * 0.4, from.Y),
            Point2 = new Point(from.X * 0.4 + to.X * 0.6, to.Y),
            Point3 = to,
        });

        return new PathGeometry
        {
            Figures = new PathFigures { figure },
        };
    }

    private static Geometry BuildOrthogonalWireGeometry(Point from, Point to)
    {
        double midX = (from.X + to.X) / 2.0;

        PathFigure figure = new()
        {
            StartPoint = from,
            IsClosed = false,
            IsFilled = false,
        };

        figure.Segments.Add(new LineSegment { Point = new Point(midX, from.Y) });
        figure.Segments.Add(new LineSegment { Point = new Point(midX, to.Y) });
        figure.Segments.Add(new LineSegment { Point = to });

        return new PathGeometry
        {
            Figures = new PathFigures { figure },
        };
    }

    private static double PortSideToAngle(PortSide side)
    {
        return side switch
        {
            PortSide.Top => 0,
            PortSide.Left => -90,
            PortSide.Right => 90,
            PortSide.Bottom => 180,
            _ => 0,
        };
    }

    private static string BuildOutputSnapshot(LoadedCircuit circuit)
    {
        List<string> lines = new();

        foreach (GatePlacement placement in circuit.Gates.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            switch (placement.Gate)
            {
                case UserOutput output:
                    lines.Add($"{output.Name}: {(output.Value ? 1 : 0)}");
                    break;
                case NumericOutput numeric:
                    lines.Add($"{numeric.Name}: {SafeNumericValue(numeric)} ({ShortRepresentation(numeric.SelectedRepresentation)})");
                    break;
            }
        }

        return lines.Count == 0
            ? "No user outputs in this circuit."
            : string.Join(Environment.NewLine, lines);
    }

    private static string BuildGateStateText(AbstractGate gate)
    {
        return gate switch
        {
            UserInput ui => $"OUT {Bit(ui.Value)}",
            UserOutput uo => $"IN {Bit(uo.Value)}",
            NumericInput ni => $"{ShortRepresentation(ni.SelectedRepresentation)} {ni.Value}",
            NumericOutput no => $"{ShortRepresentation(no.SelectedRepresentation)} {SafeNumericValue(no)}",
            Clock clk => $"{clk.Milliseconds} ms",
            _ when gate.Output.Length == 0 => string.Empty,
            _ => "OUT " + string.Join(string.Empty, gate.Output.Select(Bit)),
        };
    }

    private static string UserIoLabel(string name, string defaultName)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, defaultName, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return name.Substring(0, 1).ToUpperInvariant();
    }

    private static string BuildCommentBubblePath(double width, double height)
    {
        string path = "M0,0 ";

        for (int i = 20; i < width - 20; i += 9)
        {
            path += "a 5,5 45 1 1 9,0 ";
        }

        path += "a 5,5 45 1 1 9,0 ";

        for (int i = 20; i < height - 20; i += 9)
        {
            path += "a 5,5 45 1 1 0,9 ";
        }

        path += "a 5,5 45 1 1 0,9 ";

        for (int i = 20; i < width - 20; i += 9)
        {
            path += "a 5,5 45 1 1 -9,0 ";
        }

        path += "a 5,5 45 1 1 -9,0 ";

        for (int i = 20; i < height - 20; i += 9)
        {
            path += "a 5,5 45 1 1 0,-9 ";
        }

        path += "a 5,5 45 1 1 0,-9 ";
        return path;
    }

    private Point GetTerminalPoint(GatePlacement gate, bool isInput, int portIndex)
    {
        TerminalLayout terminal = gate.GetTerminal(isInput, portIndex);

        double ratio = (terminal.SideOrdinal + 0.5) / (terminal.SideCount + 2.0);

        double localX;
        double localY;

        switch (terminal.Side)
        {
            case PortSide.Top:
                localX = ratio * gate.Width;
                localY = 0;
                break;
            case PortSide.Bottom:
                localX = ratio * gate.Width;
                localY = gate.Height;
                break;
            case PortSide.Right:
                localX = gate.Width;
                localY = ratio * gate.Height;
                break;
            case PortSide.Left:
                localX = 0;
                localY = gate.Height - ratio * gate.Height;
                break;
            default:
                localX = gate.Width / 2;
                localY = gate.Height / 2;
                break;
        }

        Point rotated = RotatePoint(localX, localY, gate.Width / 2.0, gate.Height / 2.0, gate.Angle);

        return new Point(
            _offsetX + gate.X + rotated.X,
            _offsetY + gate.Y + rotated.Y);
    }

    private static Point RotatePoint(double x, double y, double cx, double cy, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        double dx = x - cx;
        double dy = y - cy;

        double rx = cx + (Math.Cos(radians) * dx - Math.Sin(radians) * dy);
        double ry = cy + (Math.Sin(radians) * dx + Math.Cos(radians) * dy);

        return new Point(rx, ry);
    }

    private static string Bit(bool v)
    {
        return v ? "1" : "0";
    }

    private static string SafeNumericValue(AbstractNumeric numeric)
    {
        try
        {
            return numeric.Value;
        }
        catch
        {
            return "[invalid]";
        }
    }

    private static string ShortRepresentation(AbstractNumeric.Representation representation)
    {
        return representation switch
        {
            AbstractNumeric.Representation.BINARY => "BIN",
            AbstractNumeric.Representation.OCTAL => "OCT",
            AbstractNumeric.Representation.DECIMAL => "DEC",
            AbstractNumeric.Representation.HEXADECIMAL => "HEX",
            AbstractNumeric.Representation.D2C => "D2C",
            AbstractNumeric.Representation.BCD => "BCD",
            _ => representation.ToString(),
        };
    }

    private static AbstractNumeric.Representation NextRepresentation(AbstractNumeric numeric)
    {
        return numeric.SelectedRepresentation switch
        {
            AbstractNumeric.Representation.BINARY => AbstractNumeric.Representation.OCTAL,
            AbstractNumeric.Representation.OCTAL => AbstractNumeric.Representation.DECIMAL,
            AbstractNumeric.Representation.DECIMAL => AbstractNumeric.Representation.HEXADECIMAL,
            AbstractNumeric.Representation.HEXADECIMAL => AbstractNumeric.Representation.D2C,
            AbstractNumeric.Representation.D2C => numeric.Bits % 4 == 0
                ? AbstractNumeric.Representation.BCD
                : AbstractNumeric.Representation.BINARY,
            AbstractNumeric.Representation.BCD => AbstractNumeric.Representation.BINARY,
            _ => AbstractNumeric.Representation.BINARY,
        };
    }
}
