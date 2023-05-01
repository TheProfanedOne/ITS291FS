module ITS291FS.Utilities

open Spectre.Console
open Spectre.Console.Rendering
open System.Runtime.CompilerServices

[<CLIMutable>] type PutQuery = { op: string; amount: decimal }

let flip f x y = f y x
let flip' f x y = f (y, x)
let flip'' f x y z = f (z, x, y)

// Table Extensions
type Table with
    member this.AddRows<'T> rows (rowFun: 'T -> IRenderable seq) =
        rowFun >> this.AddRow >> ignore |> Seq.iter <| rows
        this

// String Extensions
[<Extension>]
type StringExtensions() =
    [<Extension>] static member Any(this, predicate) = String.exists predicate this
    [<Extension>] static member None(this, predicate) = predicate |> this.Any |> not
    [<Extension>] static member All(this, predicate) = String.forall predicate this
