module ITS291FS.Extensions

open System
open System.Runtime.CompilerServices
open Spectre.Console
open Spectre.Console.Rendering

// String Extensions
type String with
    member this.Any predicate = String.exists predicate this
    member this.None predicate = predicate |> this.Any |> not
    member this.All predicate = String.forall predicate this

[<Extension>]
type SpectreExtensions() =
    // Table Extensions
    [<Extension>]
    static member AddRows<'T>(table: Table, rows: 'T seq, rowFun: 'T -> IRenderable seq) =
        rowFun >> table.AddRow >> ignore |> Seq.iter <| rows
        table
