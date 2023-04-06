module ITS291FS.User

open System
open System.Data
open System.Collections.Generic
open System.Security.Cryptography
open Microsoft.Data.Sqlite
open Spectre.Console

let balColor = function
    | b when b < 0m -> "red"
    | b when b > 0m -> "green"
    | _             -> "yellow"

type Item = { Name: string; Price: decimal; }

exception BalanceOverdrawEcxeption of string

type User(name: string, pass: string, ?bal: decimal) =
    let getPassHash (salt: byte[]) (pass: string) =
        let pBytes = System.Text.Encoding.UTF8.GetBytes pass
        SHA256.HashData salt |> Array.append pBytes |> SHA256.HashData |> Array.append salt |> SHA256.HashData
        
    let mutable _userId = Guid.NewGuid()
    let mutable _name = name
    let mutable _salt = Guid.NewGuid().ToByteArray()
    let mutable _pass = getPassHash _salt pass
    let mutable _bal = defaultArg bal 0.00m
    let _items = List<Item>()
    
    member _.UserId with get() = _userId
    member _.Username with get() = _name
    member _.PasswordHash with set value = _pass <- value
    member _.AccountBalance with get() = _bal
    member _.Items with get(): IReadOnlyList<Item> = _items

    member private _.InitId id = _userId <- id
    member private _.InitName username = _name <- username
    member private _.InitSalt salt = _salt <- salt
    member private _.InitPass pass = _pass <- pass
    member private _.InitBal balance = _bal <- balance
    member private _.InitItems items =
        _items.Clear()
        _items.AddRange items
    
    member _.AccountBalanceMarkup with get() = Markup $"[{balColor _bal}]{_bal:C}[/]"
    
    member _.AddItem name price = { Name = name; Price = price; } |> _items.Add
    member _.RemoveItem item = _items.Remove item |> ignore
    
    member _.IncrementBalance amount =
        if amount < 0m then invalidArg "amount" "Amount must be positive"
        _bal <- _bal + amount
    
    member _.DecrementBalance(amount, ?preventOverdraw) =
        if amount < 0m then invalidArg "amount" "Amount must be positive"
        if (defaultArg preventOverdraw true) && amount > _bal then
            BalanceOverdrawEcxeption "Insufficient funds" |> raise
        _bal <- _bal - amount
    
    member _.CheckPassword pass = getPassHash _salt pass = _pass
    
    member _.MapDataToCommand (cmd: SqliteCommand) =
        cmd.Parameters["@userid"].Value <- _userId.ToString()
        cmd.Parameters["@username"].Value <- _name
        cmd.Parameters["@salt"].Value <- _salt
        cmd.Parameters["@pass"].Value <- _pass
        cmd.Parameters["@bal"].Value <- _bal
    
    new (reader: IDataReader) as this =
        User("", "")
        then
            reader.GetOrdinal "userid" |> reader.GetGuid |> this.InitId
            reader.GetOrdinal "username" |> reader.GetString |> this.InitName
            reader.GetOrdinal "salt" |> reader.GetValue |> unbox |> this.InitSalt
            reader.GetOrdinal "pass" |> reader.GetValue |> unbox |> this.InitPass
            reader.GetOrdinal "balance" |> reader.GetDecimal |> this.InitBal
            
            this.InitItems (
                let items = List<Item>()
                let nameOrd = reader.GetOrdinal "name"
                let priceOrd = reader.GetOrdinal "price"
                while reader.IsDBNull nameOrd |> not do
                    items.Add { Name = reader.GetString nameOrd; Price = reader.GetDecimal priceOrd; }
                    reader.Read() |> ignore
                items
            )
