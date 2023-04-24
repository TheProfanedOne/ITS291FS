module ITS291FS.User

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Security.Cryptography
open Microsoft.Data.Sqlite
open Spectre.Console

let balColor = function
    | b when b < 0m -> "red"
    | b when b > 0m -> "green"
    | _             -> "yellow"

[<IsReadOnly; Struct>] type Item = { Name: string; Price: decimal; }
[<IsReadOnly; Struct>] type ItemPost = { name: string; price: decimal }
exception BalanceOverdrawException of string

[<IsReadOnly; Struct>] type UserPost = { username: string; password: string; account_balance: decimal }
type User(name: string, pass: string, ?bal: decimal) =
    let getPassHash (salt: byte[]) (pass: string) =
        let pBytes = System.Text.Encoding.UTF8.GetBytes pass in
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
    
    member _.ShortUser with get() = {| user_id = _userId; username = _name |}
    static member ToShortUser (user: User) = user.ShortUser
    member _.LongUser with get() =
        {| user_id = _userId; username = _name; account_balance = _bal; item_count = _items.Count |}
    static member ToItemJson (item: Item) = {| name = item.Name; price = item.Price |}

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
    member _.AddItemPost post = { Name = post.name; Price = post.price; } |> _items.Add
    member _.RemoveItem item = _items.Remove item |> ignore
    
    member _.IncrementBalance amount =
        if amount < 0m then invalidArg (nameof amount) "Amount must be positive"
        _bal <- _bal + amount
    
    member _.DecrementBalance(amount, ?preventOverdraw) =
        if amount < 0m then invalidArg (nameof amount) "Amount must be positive"
        if (defaultArg preventOverdraw true) && amount > _bal then
            BalanceOverdrawException "Insufficient funds" |> raise
        _bal <- _bal - amount
    
    member _.CheckPassword pass = getPassHash _salt pass = _pass
    
    static member LoadUsersFromDatabase (conn: SqliteConnection) (users: Dictionary<string, User>) =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "
            select u.*, i.name, i.price
            from users u left join (
                select * from items union
                select userid, null, null from users
            ) i on u.userid = i.userid
            order by u.userid, i.name desc
        "
        
        use reader = cmd.ExecuteReader()
        users.Clear()
        while reader.Read() do
            let user = User reader
            users.Add(user.Username, user)
            
    static member InitDatabaseAndUsers (conn: SqliteConnection) (users: Dictionary<string, User>) =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "
            create table users (
                userid primary key not null,
                username unique not null,
                salt not null,
                pass not null,
                balance not null
            )
        "
        cmd.ExecuteNonQuery() |> ignore
        
        cmd.CommandText <- "
            create table items (
                userid not null,
                name not null,
                price not null,
                foreign key (userid) references users (userid)
            )
        "
        cmd.ExecuteNonQuery() |> ignore
        
        users.Clear()
        users.Add("admin", User("admin", "admin"))
        
    static member SaveUsersToDatabase (conn: SqliteConnection) (users: Dictionary<string, User>) =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "
            delete from items;
            delete from users;
        "
        cmd.ExecuteNonQuery() |> ignore
        
        cmd.CommandText <- "
            insert into users values (@userid, @username, @salt, @pass, @bal)
        "
        cmd.Parameters.Add("@userid", SqliteType.Blob) |> ignore
        cmd.Parameters.Add("@username", SqliteType.Text) |> ignore
        cmd.Parameters.Add("@salt", SqliteType.Blob) |> ignore
        cmd.Parameters.Add("@pass", SqliteType.Blob) |> ignore
        cmd.Parameters.Add("@bal", SqliteType.Real) |> ignore

        for user in users.Values do
            user.MapDataToCommand cmd.Parameters
            cmd.ExecuteNonQuery() |> ignore
        
        cmd.CommandText <- "
            insert into items values (@userid, @name, @price)
        "
        cmd.Parameters.Add("@name", SqliteType.Text) |> ignore
        cmd.Parameters.Add("@price", SqliteType.Real) |> ignore
        
        for user in users.Values do
            cmd.Parameters["@userid"].Value <- user.UserId
            for item in user.Items do
                cmd.Parameters["@name"].Value <- item.Name
                cmd.Parameters["@price"].Value <- item.Price
                cmd.ExecuteNonQuery() |> ignore
    
    member private _.MapDataToCommand (parameters: SqliteParameterCollection) =
        parameters["@userid"].Value <- _userId
        parameters["@username"].Value <- _name
        parameters["@salt"].Value <- _salt
        parameters["@pass"].Value <- _pass
        parameters["@bal"].Value <- _bal
    
    new reader as this =
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

    new (user: UserPost) = User(user.username, user.password, user.account_balance)
