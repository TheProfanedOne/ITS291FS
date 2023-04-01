module ITS291FS.Extensions

open System.Runtime.CompilerServices
open Spectre.Console
open Spectre.Console.Rendering

[<Extension>]
type Extensions() =
    // Table Extensions
    [<Extension>]
    static member AddRows(table: Table, rows: 'T seq, rowFun: 'T -> IRenderable[]) =
        rowFun >> table.AddRow >> ignore |> Seq.iter <| rows
        table
    
    [<Extension>]
    static member AddRows(table: Table, rows: 'T seq, rowFun: 'T -> string[]) =
        rowFun >> table.AddRow >> ignore |> Seq.iter <| rows
        table
    
    // String Extensions
    [<Extension>] static member Any(str: string, pred) = String.exists pred str
    [<Extension>] static member None(str: string, pred) = pred |> str.Any |> not
    [<Extension>] static member All(str: string, pred) = String.forall pred str
