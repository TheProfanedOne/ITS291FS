open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open ITS291FS.User
open ITS291FS.Extensions
open Spectre.Console
open Spectre.Console.Rendering

let users = Dictionary<string, User>()

let loadUsers path =
    use stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read)
    try
        let jsonList =
            let opts = JsonSerializerOptions()
            opts.Converters.Add(User.UserJsonConverter())
            JsonSerializer.Deserialize<List<User>>(stream, opts)
        
        if jsonList |> isNull then JsonException() |> raise
        
        users.Clear()
        for user in jsonList do users.Add(user.Username, user)
    with | :? JsonException ->
        users.Clear()
        users.Add("admin", User("admin", "admin"))

let saveUsers path =
    use stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write)
    try
        let opts = JsonSerializerOptions()
        opts.WriteIndented <- true
        opts.Converters.Add(User.UserJsonConverter())
        JsonSerializer.Serialize(stream, users.Values |> List.ofSeq, opts)
    with | :? JsonException as ex ->
        AnsiConsole.MarkupLine $"[red]Error saving users: {ex.Message}[/]"
        exit 1

let logon () =
    let user =
        let prompt = TextPrompt<User> "[bold green]login[/] ([dim]username[/]):"
        prompt.AddChoices(users.Values).HideChoices().WithConverter(fun user -> user.Username) |> ignore
        prompt.InvalidChoiceMessage <- "[red]unknown login[/]"   
        prompt.Show AnsiConsole.Console
    
    let _ =
        let prompt = TextPrompt<string> "Enter [cyan1]password[/]?"
        prompt.Secret().PromptStyle <- "mediumorchid1_1"
        user.CheckPassword |> prompt.Validate |> ignore
        prompt.ValidationErrorMessage <- "[red]invalid password[/]"
        prompt.Show AnsiConsole.Console
    
    user

let listUsers _ =
    let table = Table().AddColumns(
        TableColumn "[bold yellow]ID[/]",
        TableColumn "[bold green]Name[/]",
        TableColumn "[bold mediumorchid1_1]Item Count[/]",
        TableColumn "[bold blue]Balance[/]"
    )
    
    table.AddRows(users.Values, fun user -> [|
        Markup $"[yellow]{user.UserId}[/]" :> IRenderable
        Markup $"[green]{user.Username}[/]"
        Markup $"[mediumorchid1_1]{user.Items.Count}[/]"
        user.AccountBalanceMarkup
    |]) |> AnsiConsole.Write
    
let passValidator =
    let inline err s = ValidationResult.Error s
    let inline ok () = ValidationResult.Success()
    let isNullOrWS = String.IsNullOrWhiteSpace
    let whiteSpace = Char.IsWhiteSpace
    
    function
    | p when p |> isNullOrWS     -> err "[red]Password cannot be empty[/]"
    | p when p.Length < 8        -> err "[red]Password must be at least 8 characters[/]"
    | p when p.Any whiteSpace    -> err "[red]Password cannot contain whitespace[/]"
    | p when p.None Char.IsUpper -> err "[red]Password must contain at least one uppercase letter[/]"
    | p when p.None Char.IsLower -> err "[red]Password must contain at least one lowercase letter[/]"
    | p when p.All Char.IsLetter -> err "[red]Password must contain at least one non-letter character[/]"
    | _                          -> ok()
    
let addUser _ =
    let name =
        let prompt = TextPrompt<string> "Enter [green]username[/]?"
        users.ContainsKey >> not |> prompt.Validate |> ignore
        prompt.ValidationErrorMessage <- "[red]Username already exists[/]"
        prompt.Show AnsiConsole.Console
        
    let pass =
        let prompt = TextPrompt<string> "Enter [cyan1]password[/]:"
        prompt.Secret().PromptStyle <- "mediumorchid1_1"
        prompt.Validator <- passValidator
        prompt.Show AnsiConsole.Console
        
    let bal =
        let prompt = TextPrompt<decimal> "Enter an initial [blue]balance[/] [dim](Must be positive)[/]:"
        ((<=) 0m, "[red]Balance must be positive[/]") |> prompt.Validate |> ignore
        prompt.Show AnsiConsole.Console
    
    users.Add(name, User(name, pass, bal))
    
let removeUser _ =
    let name =
        let prompt = SelectionPrompt<string>()
        prompt.Title <- "Select [green]user[/] to remove:"
        "<cancel>" :: List.ofSeq users.Keys |> prompt.AddChoices |> ignore
        prompt.Show AnsiConsole.Console
    
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

let markupWriteLine: Markup -> unit =
    AnsiConsole.Write >> AnsiConsole.WriteLine

let incBalance (user: User) =
    let amount =
        let amtPrompt = TextPrompt<decimal> "How much do you want to [green]add[/]?"
        ((<=) 0m, "[red]Amount must be positive[/]") |> amtPrompt.Validate |> ignore
        amtPrompt.Show AnsiConsole.Console
    
    AnsiConsole.Markup $"Adding [{balColor amount}]{amount:C}[/] to "
    markupWriteLine user.AccountBalanceMarkup
    
    user.IncrementBalance amount
    
    AnsiConsole.Markup "New balance: "
    markupWriteLine user.AccountBalanceMarkup

let decBalance (user: User) =
    let amount =
        let amtPrompt = TextPrompt<decimal> "How much do you want to [red]remove[/]?"
        ((<=) 0m, "[red]Amount must be positive[/]") |> amtPrompt.Validate |> ignore
        amtPrompt.Show AnsiConsole.Console
    
    try
        let oldMarkup = user.AccountBalanceMarkup
        user.DecrementBalance amount
        AnsiConsole.Markup $"Removing [{balColor <| amount * -1m}]{amount:C}[/] from "
        markupWriteLine oldMarkup
    with | :? BalanceOverdrawEcxeption as ex ->
        AnsiConsole.MarkupLine $"[red]{ex.Message}[/]"
    
    AnsiConsole.Markup "Account Balance: "
    markupWriteLine user.AccountBalanceMarkup
        
let listItems (user: User) =
    let table = Table().AddColumns(
        TableColumn "[bold green]Name[/]",
        TableColumn "[bold blue]Price[/]"
    )
    
    table.AddRows(user.Items, fun item -> [|
        Markup $"[green]{item.Name}[/]" :> IRenderable
        Markup $"[blue]{item.Price:C}[/]"
    |]) |> AnsiConsole.Write
    
let addItem (user: User) =
    let name =
        let namePrompt = TextPrompt<string> "What is the [green]name[/] of the item you wish to add?"
        String.IsNullOrWhiteSpace >> not |> namePrompt.Validate |> ignore
        namePrompt.ValidationErrorMessage <- "[red]Name cannot be empty[/]"
        namePrompt.Show AnsiConsole.Console
    
    let price =
        let pricePrompt = TextPrompt<decimal> "What is the [blue]price[/] of the item?"
        ((<=) 0m, "[red]Price must be positive[/]") |> pricePrompt.Validate |> ignore
        pricePrompt.Show AnsiConsole.Console
    
    user.AddItem name price
    
let removeItem (user: User) =
    let item =
        let itemPrompt = SelectionPrompt<Item>()
        itemPrompt.Title <- "What is the [green]name[/] of the item you wish to remove?"
        itemPrompt.AddChoices(user.Items).UseConverter(fun item -> item.Name) |> ignore
        itemPrompt.Show AnsiConsole.Console
    
    user.RemoveItem item

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
        
        // menu.AddChoiceGroup >> ignore |> List.iter <| selGroups
        selGroups |> List.iter (menu.AddChoiceGroup >> ignore)
        
        menu.AddChoices(SENTINEL).UseConverter(fst).Show AnsiConsole.Console |> snd

    if user |> sel then user |> doMenu
    
[<EntryPoint>]
let main argv =
    Console.OutputEncoding <- System.Text.Encoding.UTF8
    
    if argv.Length <> 1 then
        printfn "Usage: `dotnet run -- <path to users.json file>`"
        exit 1
    
    argv[0] |> loadUsers
    logon() |> doMenu
    argv[0] |> saveUsers
    
    0
