module ITS291FS.Utilities

open System

type String with
    member this.any = fun pred -> String.exists pred this
    member this.none = this.any >> not
    member this.all = fun pred -> String.forall pred this
    