# Logic Gate Simulator (GateSim)

A classic digital logic simulator originally created by **Steve Kollmansberger**.

This repository preserves the original project structure and logic engine, and includes a modern macOS-compatible UI runner (`GateSimMac`) built on Avalonia.

## Credit and Original Project

- **Original author:** Steve Kollmansberger
- **Original product/site:** [kolls.net/gatesim](http://kolls.net/gatesim)
- **Original copyright headers:** `Copyright (C) 2011 Steve Kollmansberger`
- Core simulation classes in `Gates/` are the original logic engine used by the compatibility UI.

## What Is In This Repository

- `Gates/`: original simulation core (gates, circuits, propagation, IC behavior)
- `GatesWpf/`: legacy WPF application (original desktop UI)
- `GateSimMac/`: Avalonia-based compatibility UI for macOS/Linux/Windows
- `scripts/run-gatesim-mac.sh`: convenience launcher for `GateSimMac`

## Running the Compatibility App (GateSimMac)

### Prerequisites

- .NET SDK 10.0+

### Start the app

```bash
./scripts/run-gatesim-mac.sh
```

Or run directly with `dotnet`:

```bash
dotnet run --project GateSimMac/GateSimMac.csproj
```

### Open a circuit file on launch

```bash
./scripts/run-gatesim-mac.sh /absolute/path/to/circuit.gcg
```

### Validate a circuit file from terminal (no GUI)

```bash
./scripts/run-gatesim-mac.sh --validate /absolute/path/to/circuit.gcg
```

## File Formats

The compatibility app opens legacy GateSim formats:

- `.gcg`
- `.gcf`
- `.ic`

Use the **Open** button in the top toolbar, or pass a file path on launch.

## Loading, Editing, and Saving

### Loading

- Use the **Open** button in the toolbar to load `.gcg`, `.gcf`, or `.ic`
- Use **Reload** to re-open the currently loaded file from disk
- Use the circuit picker to switch between circuits in a loaded group

### Editing

The compatibility UI supports classic interaction patterns including gate placement, drag/drop, wiring, custom components, and live simulation controls.

### Saving

At this stage, `GateSimMac` does **not** yet include a save/export UI path for writing `.gcg/.gcf/.ic` back to disk.

If you need save/export today, use the legacy `GatesWpf` application workflow.

### Original-Style Workflow

1. Open a circuit group (`.gcg` / `.gcf` / `.ic`).
2. Pick the active circuit from the top circuit dropdown.
3. Drag gates/components from the left shelf onto the canvas.
4. Wire outputs to inputs, then run live by toggling inputs/clocks.
5. Use the right panel for input controls and output snapshot.

## Notes on Intent

This project aims to keep the original logic behavior and file compatibility while modernizing runtime/UI support.

## License

The original source headers state GNU GPL licensing terms (GPL v3 or later). Keep the original notices and license terms intact when redistributing or modifying.
