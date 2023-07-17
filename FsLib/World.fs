﻿module FsLib.World

open Godot
open System

let mutable cell: Sprite2D option = None

let mutable aliveColor: Color option = None

[<ExportAttribute(PropertyHint.None, "blah")>]
let mutable deadColor: Color option = Some(new Color(32u))

// let mutable godotCells: Collections.Dictionary<Vector2, Sprite2D> =
//     Collections.Dictionary<Vector2, Sprite2D>()

let mutable godotCells: Map<Grid.position, Sprite2D> = Map.empty

let mutable cells: Grid.Cells = Grid.Cells.empty

let mutable zoom = 1.0f

let zoomStep = 0.1f

let cellSize = 32.0f

let mutable running: bool = false


let NullableToOption (n: Nullable<_>) =
    if n.HasValue then Some n.Value else None

let vectorToPosition (pos: Vector2) : Grid.position =
    let newPos = pos.Snapped(new Vector2(cellSize, cellSize)) / cellSize

    { Grid.x = int newPos.X
      Grid.y = int newPos.Y }

let positionToVector (pos: Grid.position) : Vector2 =
    new Vector2(float32 pos.x, float32 pos.y) * cellSize

let reconcileGodotCells (this: Node2D) : unit =
    let statusToColor =
        function
        | Grid.Alive -> aliveColor.Value
        | Grid.Dead -> deadColor.Value

    cells
    |> Grid.Cells.cells
    |> List.iter
        (fun
            { Grid.position = pos
              Grid.status = status } ->
            godotCells <-
                godotCells.Change(
                    pos,
                    fun c ->
                        match c with
                        | Some(c) ->
                            c.Position <- positionToVector pos
                            c.Modulate <- statusToColor status
                            Some(c)

                        | None ->
                            let newCell = cell.Value.Duplicate() :?> Sprite2D
                            newCell.Position <- positionToVector pos
                            newCell.Modulate <- statusToColor status
                            this.AddChild(newCell)
                            newCell.Show()
                            Some(newCell)
                ))

// let vectorToPosition (pos: Vector2) : Grid.position =
//       pos |> getGridPosition

// let positionToVector (pos: Grid.cell) : Vector2 =
//     getGodotPosition pos.position

let upsertCell (this: Node2D) (gridPosition: Vector2) : unit =
    let cell = Grid.makeCell Grid.Alive (gridPosition |> vectorToPosition)
    cells <- Grid.Cells.upsertCell cell cells
    reconcileGodotCells this
// TODO: reconcile with Godot cells

// This would modify the Godot Cells, which we don't want anymore
// match cell with
// | None -> failwith "Could not find the `Cell` node!"
// | Some(c) ->
//     if (cells.ContainsKey(gridPosition)) then
//         failwith "Cell already exists at this position!"
//     else
//         let mutable newCell = c.Duplicate() :?> Sprite2D
//         newCell.Position <- gridPosition * cellSize
//         this.AddChild(newCell)
//         newCell.Show()
//         cells.Add(gridPosition, newCell)

let changeZoom (this: Node2D) (delta: float32) : unit =
    zoom <- Mathf.Clamp(zoom + delta, 0.1f, 8.0f)

    this.GetNode<Camera2D>("Camera").Zoom <- new Vector2(zoom, zoom)

let markCellDead (this: Node2D) (pos: Vector2) : unit =
    cells <- Grid.Cells.markDead (vectorToPosition pos) cells
    reconcileGodotCells this

let _ready (this: Node2D) : unit =
    cell <- Some(this.GetNode<Sprite2D>("Cell"))

    running <- false

    match cell with
    | None -> failwith "Could not find the `Cell` node!"
    | Some(c) ->
        c.Hide()
        aliveColor <- Some(c.Modulate)

let (|IsActionPressed|_|) (action: string) (event: InputEvent) =
    if event.IsActionPressed(action) then Some() else None



let _unhandledInput (this: Node2D) (event: InputEvent) : unit =
    match event with
    | :? InputEventMouseButton as e ->
        match e.ButtonIndex, e.IsPressed() with
        | MouseButton.Left, true ->
            this.GetGlobalMousePosition() |> upsertCell this
        | MouseButton.Right, true ->
            this.GetGlobalMousePosition() |> markCellDead this
        | MouseButton.WheelDown, _ -> changeZoom this zoomStep
        | MouseButton.WheelUp, _ -> changeZoom this -zoomStep
        | _ -> ()
    | :? InputEventMouseMotion as e ->
        match e.ButtonMask with
        | MouseButtonMask.Left ->
            this.GetGlobalMousePosition() |> upsertCell this
        | MouseButtonMask.Right ->
            this.GetGlobalMousePosition() |> markCellDead this
        | MouseButtonMask.Middle ->
            let mutable cameraNode = this.GetNode<Camera2D>("Camera")
            cameraNode.Offset <- cameraNode.Offset - e.Relative
        | _ -> ()
    | IsActionPressed "ui_accept" -> running <- not running
    | _ -> ()

let _process (this: Node2D) (delta: double) : unit =
    let mutable debug = this.GetNode<Label>("Wall/DebugInfo")

    let pos = this.GetGlobalMousePosition()
    let gridPos = pos |> vectorToPosition

    debug.Text <-
        sprintf
            "MousePosition: %A
GridPosition: %A
Zoom: %f
Running: %b"
            pos
            gridPos
            zoom
            running

let _runSimulationStep (this: Node2D) : unit =
    if running then
        // Need to get the list of updated cells and then add any new ones
        //   that spontaneously came into existence
        let (updatedCells, newCells) = cells |> Grid.Cells.getCellsNextStatus
        cells <- updatedCells
        newCells |> List.iter (fun c -> upsertCell this (positionToVector c))

        reconcileGodotCells this
