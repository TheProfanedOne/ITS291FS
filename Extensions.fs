module ITS291FS.Extensions

open System.Runtime.CompilerServices
open Spectre.Console
open Spectre.Console.Rendering

[<Extension>]
type Extensions() =
    // Table Extensions
    [<Extension>]
    static member AddRows<'T>(table: Table, rows: 'T seq, rowFun: 'T -> IRenderable[]) =
        rowFun >> table.AddRow >> ignore |> Seq.iter <| rows
        table
    
    // String Extensions
    [<Extension>] static member Any(str: string, predicate) = String.exists predicate str
    [<Extension>] static member None(str: string, predicate) = predicate |> str.Any |> not
    [<Extension>] static member All(str: string, predicate) = String.forall predicate str
