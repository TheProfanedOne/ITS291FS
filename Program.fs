module ITS291FS.Program

open ITS291FS.Utilities
open ITS291FS.User
open type User

open Spectre.Console
open Spectre.Console.Rendering

open System
open System.Collections.Generic
open System.Text.Json

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

let users = Dictionary<string, User>()

let loadUsers path =
    let connStrB = SqliteConnectionStringBuilder()
    connStrB.DataSource <- path
    connStrB.Mode <- SqliteOpenMode.ReadWrite
    
    try
        use conn = new SqliteConnection(connStrB.ConnectionString)
        conn.Open()
        
        LoadUsersFromDatabase conn users
    with | :? SqliteException ->
        connStrB.Mode <- SqliteOpenMode.ReadWriteCreate
        use conn = new SqliteConnection(connStrB.ConnectionString)
        conn.Open()
        
        InitDatabaseAndUsers conn users

let saveUsers path =
    let connStrB = SqliteConnectionStringBuilder()
    connStrB.DataSource <- path
    connStrB.Mode <- SqliteOpenMode.ReadWrite
    
    try
        use conn = new SqliteConnection(connStrB.ConnectionString)
        conn.Open()
        
        SaveUsersToDatabase conn users
    with | :? SqliteException as ex ->
        AnsiConsole.MarkupLine $"[red]Error saving users: {ex.Message}[/]"

let logon () =
    let user =
        let prompt = TextPrompt<User> "[bold green]login[/] ([dim]username[/]):"
        prompt.InvalidChoiceMessage <- "[red]unknown login[/]"
        prompt.AddChoices(users.Values).HideChoices().WithConverter (fun user -> user.Username)
        |> AnsiConsole.Prompt
    
    let _ =
        let prompt = TextPrompt<string> "Enter [cyan1]password[/]?"
        prompt.Secret().PromptStyle <- "mediumorchid1_1"
        (user.CheckPassword, "[red]invalid password[/]") |> prompt.Validate
        |> AnsiConsole.Prompt
    
    user

let listUsers _ =
    let table = Table().AddColumns(
        TableColumn "[bold yellow]ID[/]",
        TableColumn "[bold green]Name[/]",
        TableColumn "[bold mediumorchid1_1]Item Count[/]",
        TableColumn "[bold blue]Balance[/]"
    )
    
    table.AddRows users.Values (fun user -> [
        Markup $"[yellow]{user.UserId}[/]" :> IRenderable
        Markup $"[green]{user.Username}[/]"
        Markup $"[mediumorchid1_1]{user.Items.Count}[/]"
        user.AccountBalanceMarkup
    ]) |> AnsiConsole.Write
    
let isNullOrWS = String.IsNullOrWhiteSpace

let validateName = function
    | n when n |> isNullOrWS     -> Error "[red]Username cannot be empty[/]"
    | n when users.ContainsKey n -> Error "[red]Username already exists[/]"
    | _                          -> Ok ()

let validatePass = function
    | p when p |> isNullOrWS         -> Error "[red]Password cannot be empty[/]"
    | p when p.Length < 8            -> Error "[red]Password must be at least 8 characters[/]"
    | p when p.Any Char.IsWhiteSpace -> Error "[red]Password cannot contain whitespace[/]"
    | p when p.None Char.IsUpper     -> Error "[red]Password must contain at least one uppercase letter[/]"
    | p when p.None Char.IsLower     -> Error "[red]Password must contain at least one lowercase letter[/]"
    | p when p.All Char.IsLetter     -> Error "[red]Password must contain at least one non-letter character[/]"
    | _                              -> Ok ()

let nameValidator = validateName >> function
    | Error msg -> ValidationResult.Error msg
    | _         -> ValidationResult.Success()

let passValidator = validatePass >> function
    | Error msg -> ValidationResult.Error msg
    | _         -> ValidationResult.Success()
    
let addUser _ =
    let name =
        let prompt = TextPrompt<string> "Enter [green]username[/]:"
        prompt.Validator <- nameValidator
        prompt |> AnsiConsole.Prompt
        
    let pass =
        let prompt = TextPrompt<string> "Enter [cyan1]password[/]:"
        prompt.Secret().PromptStyle <- "mediumorchid1_1"
        prompt.Validator <- passValidator
        prompt |> AnsiConsole.Prompt
        
    let bal =
        let prompt = TextPrompt<decimal> "Enter an initial [blue]balance[/] [dim](Must be positive)[/]:"
        prompt.Validate (flip (>=) 0m, "[red]Balance must be positive[/]")
        |> AnsiConsole.Prompt
    
    users.Add(name, User(name, pass, bal))
    
let removeUser _ =
    let name =
        let prompt = SelectionPrompt<string>()
        prompt.Title <- "Select [green]user[/] to remove:"
        "<cancel>" :: List.ofSeq users.Keys |> prompt.AddChoices
        |> AnsiConsole.Prompt
    
    match name with
    | "<cancel>" -> ()
    | "admin" -> AnsiConsole.MarkupLine "[red]Cannot remove admin user[/]"
    | n -> users.Remove n |> ignore
    
let showUserDetails (user: User) =
    let table = Table().AddColumns(
        TableColumn "[bold mediumorchid1_1]Property[/]",
        TableColumn "[bold green]Value[/]"
    )
    
    table.AddRow("[mediumorchid1_1]ID[/]", $"[green]{user.UserId}[/]") |> ignore
    table.AddRow("[mediumorchid1_1]Name[/]", $"[green]{user.Username}[/]") |> ignore
    table.AddRow("[mediumorchid1_1]Item Count[/]", $"[green]{user.Items.Count}[/]") |> ignore
    table.AddRow(
        Markup "[mediumorchid1_1]Balance[/]",
        user.AccountBalanceMarkup
    ) |> AnsiConsole.Write

let markupWriteLine str (mark: IRenderable) =
    AnsiConsole.Markup str
    AnsiConsole.Write mark
    AnsiConsole.WriteLine()

let incBalance (user: User) =
    let amount =
        let amtPrompt = TextPrompt<decimal> "How much do you want to [green]add[/]?"
        (flip (>=) 0m, "[red]Amount must be positive[/]") |> amtPrompt.Validate
        |> AnsiConsole.Prompt
    
    markupWriteLine $"Adding [{balColor amount}]{amount:C}[/] to " user.AccountBalanceMarkup
    user.IncrementBalance amount
    markupWriteLine "Account Balance: " user.AccountBalanceMarkup

let decBalance (user: User) =
    let amount =
        let amtPrompt = TextPrompt<decimal> "How much do you want to [red]remove[/]?"
        (flip (>=) 0m, "[red]Amount must be positive[/]") |> amtPrompt.Validate
        |> AnsiConsole.Prompt
    
    try
        let oldMarkup = user.AccountBalanceMarkup
        user.DecrementBalance amount
        markupWriteLine $"Removing [{balColor <| -amount}]{amount:C}[/] from " oldMarkup
    with | :? BalanceOverdrawException as ex ->
        AnsiConsole.MarkupLine $"[red]{ex.Message}[/]"
    
    markupWriteLine "Account Balance: " user.AccountBalanceMarkup
        
let listItems (user: User) =
    let table = Table().AddColumns(
        TableColumn "[bold green]Name[/]",
        TableColumn "[bold blue]Price[/]"
    )
    
    table.AddRows user.Items (fun item -> [
        Markup $"[green]{item.Name}[/]" :> IRenderable
        Markup $"[blue]{item.Price:C}[/]"
    ]) |> AnsiConsole.Write
    
let addItem (user: User) =
    let name =
        let prompt = TextPrompt<string> "What is the [green]name[/] of the item you wish to add?"
        (isNullOrWS >> not, "[red]Name cannot be empty[/]") |> prompt.Validate
        |> AnsiConsole.Prompt

    let price =
        let prompt = TextPrompt<decimal> "What is the [blue]price[/] of the item?"
        (flip (>=) 0m, "[red]Price must be positive[/]") |> prompt.Validate
        |> AnsiConsole.Prompt
    
    user.AddItem name price
    
let removeItem (user: User) =
    user.RemoveItem (
        let prompt = SelectionPrompt<Item>()
        prompt.Title <- "What is the [green]name[/] of the item you wish to remove?"
        prompt.AddChoices(user.Items).UseConverter(fun item -> item.Name)
        |> AnsiConsole.Prompt
    )

let makeTrue _ = true
let selGroups = [
    (("Account", makeTrue), [
        ("Increment Balance", incBalance >> makeTrue)
        ("Decrement Balance", decBalance >> makeTrue)
    ])
    (("Users", makeTrue), [
        ("List Users", listUsers >> makeTrue)
        ("Add User", addUser >> makeTrue)
        ("Remove User", removeUser >> makeTrue)
        ("Show User Details", showUserDetails >> makeTrue)
    ])
    (("Items", makeTrue), [
        ("List Items", listItems >> makeTrue)
        ("Add Item", addItem >> makeTrue)
        ("Remove Item", removeItem >> makeTrue)
    ])
]
let SENTINEL = ("Quit", makeTrue >> not)

let rec doMenu user =
    let sel =
        let menu = SelectionPrompt<string * (User -> bool)>()
        menu.Title <- "What would you like to do?"
        selGroups |> List.iter (menu.AddChoiceGroup >> ignore)
        menu.AddChoices(SENTINEL).UseConverter(fst)
        |> AnsiConsole.Prompt |> snd

    if sel user then doMenu user

let startWebApi (argv: string[]) =
    let builder = WebApplication.CreateBuilder argv
    
    builder.Services.AddEndpointsApiExplorer() |> ignore
    builder.Services.AddSwaggerGen() |> ignore
    
    let app = builder.Build()
    
    app.UseSwagger() |> ignore
    app.UseSwaggerUI() |> ignore
    
    app.UseHttpsRedirection() |> ignore
    
    let serializerOptions = JsonSerializerOptions.Default
    let getUsers () = Results.Json(users.Values |> Seq.map ToShortUser, serializerOptions)
    let postUser body = (validateName body.username, validatePass body.password) |> function
        | Error msg, _ | _, Error msg -> Results.BadRequest msg
        | _ when body.account_balance < 0m -> Results.BadRequest "Account balance cannot be negative"
        | _ -> users.Add(body.username, User body) |> Results.NoContent
    let deleteUser username = users.Remove username |> function
        | true -> Results.NoContent()
        | _ -> Results.NotFound $"Unknown user: `{username}`"
    let getUser username = users.TryGetValue username |> function
        | true, user -> Results.Json(user.LongUser, serializerOptions)
        | _ -> Results.NotFound $"Unknown user: `{username}`"
    let putUser username op amount = users.TryGetValue username |> function
        | true, _ when amount < 0m -> Results.BadRequest "Amount cannot be negative"
        | true, user -> op |> function
            | "inc" -> user.IncrementBalance amount |> Results.NoContent
            | "dec" ->
                try user.DecrementBalance amount |> Results.NoContent
                with | :? BalanceOverdrawException as ex -> Results.BadRequest ex.Message
            | _ -> Results.BadRequest $"Invalid operation: `{op}`"
        | _ -> Results.NotFound $"Unknown user: `{username}`"
    let getItems username = users.TryGetValue username |> function
        | true, user -> Results.Json(user.Items |> Seq.map ToItemJson, serializerOptions)
        | _ -> Results.NotFound $"Unknown user: `{username}`"
    let postItem username body = users.TryGetValue username |> function
        | true, _ when body.name |> isNullOrWS -> Results.BadRequest "Name cannot be empty"
        | true, _ when body.price <= 0m -> Results.BadRequest "Price cannot be negative"
        | true, user -> body |> user.AddItemPost |> Results.NoContent
        | _ -> Results.NotFound $"Unknown user: `{username}`"
    let deleteItem username name = users.TryGetValue username |> function
        | true, user ->
            try user.Items |> Seq.find (fun i -> i.Name = name) |> user.RemoveItem |> Results.NoContent
            with | :? KeyNotFoundException -> Results.NotFound $"Unknown item: `{name}`"
        | _ -> Results.NotFound $"Unknown user: `{username}`"
    
    app.MapGet("/users", Func<_>(getUsers)) |> ignore
    app.MapPost("/users", Func<_, _>(postUser)) |> ignore
    app.MapDelete("/{username}", Func<_, _>(deleteUser)) |> ignore
    app.MapGet("/{username}", Func<_, _>(getUser)) |> ignore
    app.MapPut("/{username}/accountBalance", Func<_, _, _, _>(putUser)) |> ignore
    app.MapGet("/{username}/items", Func<_, _>(getItems)) |> ignore
    app.MapPost("/{username}/items", Func<_, _, _>(postItem)) |> ignore
    app.MapDelete("/{username}/items/{name}", Func<_, _, _>(deleteItem)) |> ignore
    
    app.RunAsync "https://localhost:5000" |> ignore    
    app
    
[<EntryPoint>]
let main argv =
    Console.OutputEncoding <- System.Text.Encoding.UTF8
    
    if argv.Length <> 1 then
        printfn "Usage: `dotnet run -- <path to users.json file>`"
        exit 1
    
    argv[0] |> loadUsers
    let app = startWebApi argv
    
    Console.CancelKeyPress.Add(fun _ ->
        AnsiConsole.WriteLine()
        if ConfirmationPrompt "Do you want to save what you have?" |> AnsiConsole.Prompt then
            argv[0] |> saveUsers
    )
    
    logon() |> doMenu
    argv[0] |> saveUsers
    
    app.StopAsync().Wait()
    
    0
