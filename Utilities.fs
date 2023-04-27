module ITS291FS.Utilities

open System
open Spectre.Console
open Spectre.Console.Rendering

[<CLIMutable>] type PutQuery = { op: string; amount: decimal }

let flip f x y = f y x

// String Extensions
type String with
    member this.Any predicate = String.exists predicate this
    member this.None predicate = predicate |> this.Any |> not
    member this.All predicate = String.forall predicate this

// Table Extensions
type Table with
    member this.AddRows<'T> rows (rowFun: 'T -> IRenderable seq) =
        rowFun >> this.AddRow >> ignore |> Seq.iter <| rows
        this
