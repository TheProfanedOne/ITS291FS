open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open ITS291FS
open Spectre.Console

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
        0
    with | :? JsonException as ex ->
        AnsiConsole.MarkupLine $"[red]Error saving users: {ex.Message}[/]"
        1

let logon () =
    let user =
        let userPrompt = TextPrompt<User> "[bold green]login[/] ([dim]username[/]):"
        userPrompt.AddChoices(users.Values).HideChoices().WithConverter(fun user -> user.Username) |> ignore
        userPrompt.InvalidChoiceMessage <- "[red]unknown login[/]"   
        userPrompt.Show AnsiConsole.Console
    
    let _ =
        let passPrompt = TextPrompt<string> "Enter [cyan1]password[/]?"
        passPrompt.Secret().PromptStyle <- "mediumorchid1_1"
        user.CheckPassword |> passPrompt.Validate |> ignore
        passPrompt.ValidationErrorMessage <- "[red]invalid password[/]"
        passPrompt.Show AnsiConsole.Console
    
    user

let listUsers _ =
    let table = Table().AddColumns(
        TableColumn "[bold yellow]ID[/]",
        TableColumn "[bold green]Name[/]",
        TableColumn "[bold mediumorchid1_1]Item Count[/]",
        TableColumn "[bold blue]Balance[/]"
    )
    
    for user in users.Values do
        table.AddRow(
            Markup $"[yellow]{user.UserId}[/]",
            Markup $"[green]{user.Username}[/]",
            Markup $"[mediumorchid1_1]{user.Items.Count}[/]",
            user.AccountBalanceMarkup
        ) |> ignore
    
    AnsiConsole.Write table
    
let passValidator =
    let err = function | msg -> ValidationResult.Error msg
    let ok = ValidationResult.Success
    let any = String.exists
    let all = String.forall
    function
    | p when String.IsNullOrWhiteSpace p -> err "[red]Password cannot be empty[/]"
    | p when p.Length < 8                -> err "[red]Password must be at least 8 characters[/]"
    | p when any Char.IsWhiteSpace p     -> err "[red]Password cannot contain whitespace[/]"
    | p when any Char.IsUpper p |> not   -> err "[red]Password must contain at least one uppercase letter[/]"
    | p when any Char.IsLower p |> not   -> err "[red]Password must contain at least one lowercase letter[/]"
    | p when all Char.IsLetter p         -> err "[red]Password must contain at least one non-letter character[/]"
    | _                                  -> ok()
    
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
        prompt.Validate((fun b -> b >= 0M), "[red]Balance must be positive[/]") |> ignore
        prompt.Show AnsiConsole.Console
    
    users.Add(name, User(name, pass, bal))
    
let removeUser _ =
    let name =
        let prompt = SelectionPrompt<string>()
        prompt.Title <- "Select [green]user[/] to remove:"
        "<cancel>" :: (users.Keys |> Seq.toList) |> prompt.AddChoices |> ignore
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
    
let incBalance (user: User) =
    let amount =
        let amtPrompt = TextPrompt<decimal> "How much do you want to [green]add[/]?"
        amtPrompt.Validate(fun amt -> amt >= 0M) |> ignore
        amtPrompt.ValidationErrorMessage <- "[red]Amount must be positive[/]"
        amtPrompt.Show AnsiConsole.Console
    
    AnsiConsole.Markup $"Adding [{User.balColor amount}]{amount:C}[/] to "
    AnsiConsole.Write user.AccountBalanceMarkup; AnsiConsole.WriteLine()
    
    user.IncrementBalance amount
    
    AnsiConsole.Markup "New balance: "
    AnsiConsole.Write user.AccountBalanceMarkup; AnsiConsole.WriteLine()

let decBalance (user: User) =
    let amount =
        let amtPrompt = TextPrompt<decimal> "How much do you want to [red]remove[/]?"
        amtPrompt.Validate(fun amt -> amt >= 0M) |> ignore
        amtPrompt.ValidationErrorMessage <- "[red]Amount must be positive[/]"
        amtPrompt.Show AnsiConsole.Console
    
    try
        let oldMarkup = user.AccountBalanceMarkup
        user.DecrementBalance amount
        AnsiConsole.Markup $"Removing [{User.balColor (amount * -1M)}]{amount:C}[/] from "
        AnsiConsole.Write oldMarkup; AnsiConsole.WriteLine()
    with
    | :? BalanceOverdrawEcxeption as ex -> AnsiConsole.MarkupLine ex.Message
    
    AnsiConsole.Markup "Account Balance: "
    AnsiConsole.Write user.AccountBalanceMarkup; AnsiConsole.WriteLine()
        
let listItems (user: User) =
    let table = Table().AddColumns(
        TableColumn "[bold green]Name[/]",
        TableColumn "[bold blue]Price[/]"
    )
    
    for item in user.Items do
        table.AddRow(
            $"[green]{item.Name}[/]", $"[blue]{item.Price:C}[/]"
        ) |> ignore

    AnsiConsole.Write table
    
let addItem (user: User) =
    let name =
        let namePrompt = TextPrompt<string> "What is the [green]name[/] of the item you wish to add?"
        String.IsNullOrWhiteSpace >> not |> namePrompt.Validate |> ignore
        namePrompt.ValidationErrorMessage <- "[red]Name cannot be empty[/]"
        namePrompt.Show AnsiConsole.Console
    
    let price =
        let pricePrompt = TextPrompt<decimal> "What is the [blue]price[/] of the item?"
        pricePrompt.Validate((fun price -> price >= 0M), "[red]Price must be positive[/]") |> ignore
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

let rec doMenu (user: User) =
    let sel =
        let menu = SelectionPrompt<string * (User -> bool)>()
        menu.Title <- "What would you like to do?"
        
        for group in selGroups do
            group |> menu.AddChoiceGroup |> ignore
            
        menu.AddChoices(SENTINEL).UseConverter(fst).Show AnsiConsole.Console |> snd

    match user |> sel with
    | true -> doMenu user
    | false -> ()
    
[<EntryPoint>]
let main = function
    | argv when argv.Length <> 1 ->
        printfn "Usage: `dotnet run -- <path to users.json file>`"
        1
    | argv ->
        Console.OutputEncoding <- System.Text.Encoding.UTF8
        argv[0] |> loadUsers
        logon() |> doMenu
        argv[0] |> saveUsers
