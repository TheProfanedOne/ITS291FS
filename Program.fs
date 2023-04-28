﻿module ITS291FS.Program

open Giraffe
open ITS291FS.Utilities
open ITS291FS.User
open ITS291FS.SwaggerJson
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open type User
open Spectre.Console
open Spectre.Console.Rendering
open System
open System.Collections.Generic
open Microsoft.AspNetCore.Builder
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Swashbuckle.AspNetCore.SwaggerUI

let users = Dictionary<string, User>()

let loadUsers path =
    let connStrB = SqliteConnectionStringBuilder()
    connStrB.DataSource <- path
    connStrB.Mode <- SqliteOpenMode.ReadWrite
    
    try
        use conn = new SqliteConnection(connStrB.ConnectionString)
        conn.Open()
        
        LoadUsersFromDatabase conn users
    with :? SqliteException ->
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
    with :? SqliteException as ex ->
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
    let table = Table().AddColumns (
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
    | n when n |> isNullOrWS     -> Some "Username cannot be empty"
    | n when users.ContainsKey n -> Some "Username already exists"
    | _                          -> None

let validatePass = function
    | p when p |> isNullOrWS         -> Some "Password cannot be empty"
    | p when p.Length < 8            -> Some "Password must be at least 8 characters"
    | p when p.Any Char.IsWhiteSpace -> Some "Password cannot contain whitespace"
    | p when p.None Char.IsUpper     -> Some "Password must contain at least one uppercase letter"
    | p when p.None Char.IsLower     -> Some "Password must contain at least one lowercase letter"
    | p when p.All Char.IsLetter     -> Some "Password must contain at least one non-letter character"
    | _                              -> None

let nameValidator = validateName >> function
    | Some msg -> ValidationResult.Error $"[red]{msg}[/]"
    | _        -> ValidationResult.Success()

let passValidator = validatePass >> function
    | Some msg -> ValidationResult.Error $"[red]{msg}[/]"
    | _        -> ValidationResult.Success()

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
    let prompt = SelectionPrompt<string>()
    prompt.Title <- "Select [green]user[/] to remove:"
    "<cancel>" :: List.ofSeq users.Keys |> prompt.AddChoices
    |> AnsiConsole.Prompt |> function
    | "<cancel>" -> ()
    | "admin" -> AnsiConsole.MarkupLine "[red]Cannot remove admin user[/]"
    | n -> users.Remove n |> ignore

let showUserDetails (user: User) =
    let table = Table().AddColumns (
        TableColumn "[bold mediumorchid1_1]Property[/]",
        TableColumn "[bold green]Value[/]"
    )
    
    table.AddRow("[mediumorchid1_1]ID[/]", $"[green]{user.UserId}[/]") |> ignore
    table.AddRow("[mediumorchid1_1]Name[/]", $"[green]{user.Username}[/]") |> ignore
    table.AddRow("[mediumorchid1_1]Item Count[/]", $"[green]{user.Items.Count}[/]") |> ignore
    table.AddRow (
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
        markupWriteLine $"Removing [{balColor -amount}]{amount:C}[/] from " oldMarkup
    with :? BalanceOverdrawException as ex ->
        AnsiConsole.MarkupLine $"[red]{ex.Message}[/]"
    
    markupWriteLine "Account Balance: " user.AccountBalanceMarkup

let listItems (user: User) =
    let table = Table().AddColumns (
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

let startWebApi argv =
    let redirects = choose [
        route "/" >=> redirectTo true "/swagger/index.html"
        route "/index.html" >=> redirectTo true "/"
        route "/swagger" >=> redirectTo true "/"
    ]
    
    let swaggerRoute =
        route "/swagger/v1/swagger.json"
        >=> GET
        >=> setContentType "application/json"
        >=> setBodyFromString swaggerJson
    
    let getUsers = route "/list" >=> GET >=> Successful.ok (warbler <| fun _ ->
        users.Values |> Seq.map ToShortUser |> json
    )
    
    let postUser = POST >=> warbler (fun _ -> fun next ctx -> task {
        let! body = ctx.BindJsonAsync<UserPost>()
        let { username = un; password = pass; account_balance = bal } = body
        
        let status = (validateName un, validatePass pass) |> function
            | Some msg, _ | _, Some msg -> RequestErrors.BAD_REQUEST msg
            | _ when bal < 0m -> RequestErrors.BAD_REQUEST "Account balance cannot be negative"
            | _ -> users.Add(un, User body); Successful.NO_CONTENT
        
        return! status next ctx
    })
    
    let getUser username = warbler <| fun _ -> users.TryGetValue username |> function
        | true, user -> Successful.ok (json user.LongUser)
        | _ -> RequestErrors.NOT_FOUND $"Unknown user: `{username}`"
    
    let delUser username = warbler <| fun _ -> users.Remove username |> function
        | true -> Successful.NO_CONTENT
        | _ -> RequestErrors.NOT_FOUND $"Unknown user: `{username}`"
    
    let putUser username = route "/accountBalance" >=> warbler (fun _ -> fun next ctx ->
        let { op = op; amount = amt } = ctx.BindQueryString<PutQuery>()
        
        let status = users.TryGetValue username |> function
            | true, _ when amt < 0m -> RequestErrors.BAD_REQUEST "Amount cannot be negative"
            | true, user -> op |> function
                | "inc" -> user.IncrementBalance amt; Successful.NO_CONTENT
                | "dec" ->
                    try user.DecrementBalance amt; Successful.NO_CONTENT
                    with :? BalanceOverdrawException as ex -> RequestErrors.BAD_REQUEST ex.Message
                | _ -> RequestErrors.BAD_REQUEST $"Invalid operation: `{op}`"
            | _ -> RequestErrors.NOT_FOUND $"Unknown user: `{username}`"
        
        status next ctx
    )
    
    let getItems username = warbler <| fun _ -> users.TryGetValue username |> function
        | true, user -> user.Items |> Seq.map ToItemJson |> json |> Successful.ok
        | _ -> RequestErrors.NOT_FOUND $"Unknown user: `{username}`"
    
    let postItem username = warbler <| fun _ -> fun next ctx -> task {
        let! { name = name; price = price } = ctx.BindJsonAsync<ItemPost>()
        
        let status = users.TryGetValue username |> function
            | true, _ when name |> isNullOrWS -> RequestErrors.BAD_REQUEST "Name cannot be empty"
            | true, _ when price <= 0m -> RequestErrors.BAD_REQUEST "Price cannot be negative"
            | true, user -> user.AddItem name price; Successful.NO_CONTENT
            | _ -> RequestErrors.NOT_FOUND $"Unknown user: `{username}`"
        
        return! status next ctx
    }
    
    let delItem username = routef "/%s" <| fun name -> warbler <| fun _ -> users.TryGetValue username |> function
        | true, user ->
            try user.Items |> Seq.find (fun i -> i.Name = name) |> user.RemoveItem; Successful.NO_CONTENT
            with :? KeyNotFoundException -> RequestErrors.NOT_FOUND $"Unknown item: `{name}`"
        | _ -> RequestErrors.NOT_FOUND $"Unknown user: `{username}`"
    
    let webApp = choose [
        redirects; swaggerRoute
        subRoute "/users" <| choose [getUsers; postUser]
        subRoutef "/%s" <| fun username -> choose [
            subRoute "/items" <| choose [
                GET >=> getItems username
                POST >=> postItem username
                DELETE >=> delItem username
            ]
            GET >=> getUser username
            DELETE >=> delUser username
            PUT >=> putUser username
        ]
    ]
    
    let servicesConfig (services: IServiceCollection) =
        services.AddGiraffe() |> ignore
    
    let swaggerConfig (opts: SwaggerUIOptions) =
        opts.SwaggerEndpoint("/swagger/v1/swagger.json", "v1")
    
    let appConfig (builder: IApplicationBuilder) =
        builder.UseHttpsRedirection()
            .UseSwaggerUI(swaggerConfig)
            .UseGiraffe webApp
    
    let webHostConfig (builder: IWebHostBuilder) =
        ignore <| builder.Configure(appConfig)
            .ConfigureServices(servicesConfig)
            .UseUrls "https://localhost:5000"
    
    let app =
        Host.CreateDefaultBuilder(argv)
            .ConfigureWebHostDefaults(webHostConfig)
            .Build()
    
    app.StartAsync() |> ignore
    app

[<EntryPoint>]
let main argv =
    Console.OutputEncoding <- System.Text.Encoding.UTF8
    
    if argv.Length <> 1 then
        printfn "Usage: `ITS291FS <path to database file>`"
        exit 1
    
    argv[0] |> loadUsers
    let app = startWebApi argv
    
    Console.CancelKeyPress.Add <| fun _ ->
        AnsiConsole.WriteLine()
        if ConfirmationPrompt "Do you want to save what you have?" |> AnsiConsole.Prompt then
            argv[0] |> saveUsers
        app.StopAsync().Wait()
    
    logon() |> doMenu
    argv[0] |> saveUsers
    
    app.StopAsync().Wait()
    
    0
