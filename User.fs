module ITS291FS.User

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Collections.Generic
open System.Security.Cryptography
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
    
    member this.UserId with get() = _userId
    member this.Username with get() = _name
    member this.PasswordHash with set value = _pass <- value
    member this.AccountBalance with get() = _bal
    member this.Items with get(): IReadOnlyList<Item> = _items

    member private this.InitId id = _userId <- id
    member private this.InitName username = _name <- username
    member private this.InitBal balance = _bal <- balance
    member private this.InitSalt salt = _salt <- salt
    member private this.InitItems items =
        _items.Clear()
        _items.AddRange items
    
    member private this.PassSpan with get() = ReadOnlySpan _pass
    member private this.SaltSpan with get() = ReadOnlySpan _salt
    
    member this.AccountBalanceMarkup with get() = Markup $"[{balColor _bal}]{_bal:C}[/]"
    
    member this.AddItem name price = { Name = name; Price = price; } |> _items.Add
    member this.RemoveItem item = _items.Remove item |> ignore
    
    member this.IncrementBalance amount =
        if amount < 0m then ArgumentException "Amount must be positive" |> raise
        _bal <- _bal + amount
    
    member this.DecrementBalance(amount, ?preventOverdraw) =
        if amount < 0m then ArgumentException "Amount must be positive" |> raise
        if (defaultArg preventOverdraw true) && amount > _bal then
            BalanceOverdrawEcxeption "Insufficient funds" |> raise
        _bal <- _bal - amount
    
    member this.CheckPassword pass = getPassHash _salt pass = _pass
    
    static member UserJsonConverter() =
        { new JsonConverter<User>() with
            override this.Read(reader, typeToConvert, options) = User &reader
            override this.Write(writer, value, options) =
                writer.WriteStartObject()
                writer.WriteString("userid", value.UserId)
                writer.WriteString("username", value.Username)
                writer.WriteBase64String("salt", value.SaltSpan)
                writer.WriteBase64String("password", value.PassSpan)
                writer.WriteNumber("balance", value.AccountBalance)
                writer.WritePropertyName "items"
                JsonSerializer.Serialize(writer, value.Items)
                writer.WriteEndObject()
        }
    
    new reader as this = User("", "") then
        if reader.TokenType <> JsonTokenType.StartObject then
            JsonException "Expected StartObject token" |> raise
        
        while reader.Read() && reader.TokenType <> JsonTokenType.EndObject do
            if reader.TokenType <> JsonTokenType.PropertyName then
                JsonException "Expected PropertyName token" |> raise
            
            let pName = reader.GetString()
            reader.Read() |> ignore
            match pName with
            | "userid"   -> reader.GetGuid() |> this.InitId
            | "username" -> reader.GetString() |> this.InitName
            | "salt"     -> reader.GetBytesFromBase64() |> this.InitSalt
            | "password" -> this.PasswordHash <- reader.GetBytesFromBase64()
            | "balance"  -> reader.GetDecimal() |> this.InitBal
            | "items"    -> JsonSerializer.Deserialize<List<Item>> &reader |> this.InitItems
            | _          -> JsonException $"Unexpected property {pName}" |> raise
        