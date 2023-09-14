open System.CommandLine.Invocation
open System.CommandLine.Help
open System.Collections
open System.Collections.Generic
open System.IO

open FSharp.Configuration
open FSharp.SystemCommandLine

open Google.Apis.Auth.OAuth2
open Google.Apis.Docs.v1
open Google.Apis.Docs.v1.Data
open Google.Apis.Drive.v3
open Google.Apis.Drive.v3.Data
open Google.Apis.Services
open Google.Apis.Util.Store

open org.languagetool
open org.languagetool.language
open org.languagetool.tools

type Secrets = YamlConfig<"ClientSecrets.yaml">
let authorize email =
    let appSecrets = new Secrets()
    let secrets = new ClientSecrets()
    secrets.ClientId <- appSecrets.installed.client_id
    secrets.ClientSecret <- appSecrets.installed.client_secret
    let path = Path.Combine("credentials")
    let scopes = [|
        "https://www.googleapis.com/auth/documents"
        "https://www.googleapis.com/auth/drive"
    |]
    let credentials =
        task {
            return! GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                scopes,
                email,
                System.Threading.CancellationToken.None,
                FileDataStore(path, true))
        } |> Async.AwaitTask |> Async.RunSynchronously
    BaseClientService.Initializer(
        HttpClientInitializer = credentials,
        ApplicationName = "qblint")

let getFiles (driveService : DriveService) parentId mimeType =
    let files = driveService.Files.List()
    files.Q <- $"'{parentId}' in parents and mimeType='{mimeType}' and trashed=false"
    task {
        return! files.ExecuteAsync()
    } |> Async.AwaitTask |> Async.RunSynchronously

let createFolder (driveService : DriveService) parentId =
    let folderMetadata = new File()
    let timestamp = System.DateTime.Now.ToString("s")
    folderMetadata.Name <- $"qblint.{timestamp}"
    folderMetadata.MimeType <- "application/vnd.google-apps.folder"
    folderMetadata.Parents <- [ parentId ] |> ResizeArray
    let request = driveService.Files.Create(folderMetadata)
    task {
        return! request.ExecuteAsync()
    } |> Async.AwaitTask |> Async.RunSynchronously

let deleteFolder (driveService : DriveService) (folder : File) =
    let folderMetadata = new File()
    folderMetadata.Trashed <- true
    let request = driveService.Files.Update(folderMetadata, folder.Id)
    task {
        return! request.ExecuteAsync()
    } |> Async.AwaitTask |> Async.RunSynchronously

let uploadFiles (driveService : DriveService) folderId docId (json : string) =
    let fileMetadata = new File()
    fileMetadata.Name <- $"{docId}.json"
    fileMetadata.Parents <- [ folderId ] |> ResizeArray
    let bytes = System.Text.Encoding.UTF8.GetBytes json
    let stream = new MemoryStream(bytes)
    let request = driveService.Files.Create(fileMetadata, stream, "text/plain")
    task {
        return! request.UploadAsync()
    } |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    printfn "%s.json uploaded successfully." docId

type Config = YamlConfig<"_Config.yaml">
let lint () =
    let stopWatch = System.Diagnostics.Stopwatch.StartNew()

    let config = new Config()
    config.Load(@"_Config.yaml")
    let email = config.Google.Email
    let parentId = config.Google.FolderId
    if (email = "string" || parentId = "string") then
        printfn "Fill out email and folder ID in _Config.yaml."; exit 1
    let clientService = authorize email
    let driveService = new DriveService(clientService)
    let docs = getFiles driveService parentId "application/vnd.google-apps.document"
    let docsIds =
        docs.Files
        |> Seq.choose (fun doc ->
            match doc.Id with
            | null -> None
            | id -> Some (id))
    
    let paragraphs = fun (element : StructuralElement) ->
        match element.Paragraph with
        | null -> None
        | pa -> Some (pa.Elements)
    let textruns = fun (element : ParagraphElement) ->
        match element.TextRun with
        | null -> None
        | tr -> Some (tr.Content)
    let getText (doc : Document) =
        doc.Body.Content
        |> Seq.choose paragraphs
        |> Seq.concat
        |> Seq.choose textruns
        |> Seq.fold (+) ""
    
    let folders = getFiles driveService parentId "application/vnd.google-apps.folder"
    let maybeFolder =
        folders.Files
        |> Seq.tryPick (fun folder ->
            if folder.Name[..5]="qblint" then Some(folder) else None)

    let linterFolder =
        match maybeFolder with
        | None -> createFolder driveService parentId
        | Some folder ->
            deleteFolder driveService folder |> ignore
            createFolder driveService parentId
    let docsService = new DocsService(clientService)

    let language = config.LanguageTool.Language
    let lang = new AmericanEnglish()
    let lt = MultiThreadedJLanguageTool(lang)
    let customRulesLoader = rules.patterns.PatternRuleLoader()
    let customRulesFile = Path.Combine(config.LanguageTool.CustomRules)
    if not <| File.Exists(customRulesFile) then
        printfn "Custom rules not found. Check _Config.yaml."; exit 1
    use fs = File.OpenRead(customRulesFile)
    use isw = new ikvm.io.InputStreamWrapper(fs)
    let customRules = customRulesLoader.getRules(isw, customRulesFile)
    let iter : java.util.Iterator = customRules.iterator();
    while iter.hasNext() do
        let rule = iter.next() :?> rules.Rule
        lt.addRule(rule)
    let disableRules = config.LanguageTool.DisableRules
    Seq.iter (fun s -> lt.disableRule(s)) (disableRules)

    for docId in docsIds do
        let txt =
            task {
                return! docsService.Documents.Get(docId).ExecuteAsync()
            } |> Async.AwaitTask |> Async.RunSynchronously |> getText
        let matches = lt.check(txt)
        let dt = DetectedLanguage(lt.getLanguage(), lt.getLanguage())
        let serializer = RuleMatchesAsJsonSerializer()
        let json = serializer.ruleMatchesToJson(matches, txt, 50, dt)
        uploadFiles driveService linterFolder.Id docId json

    stopWatch.Stop()
    printfn "Executed in %f seconds." stopWatch.Elapsed.TotalSeconds

[<EntryPoint>]
let main argv = 
    let showHelp (ctx: InvocationContext) =
        let layout = HelpBuilder.Default.GetLayout()
        ctx.HelpBuilder.CustomizeLayout(fun _ -> layout |> Seq.removeAt 1)
        let hc = HelpContext(
            ctx.HelpBuilder,
            ctx.Parser.Configuration.RootCommand,
            System.Console.Out)
        ctx.HelpBuilder.Write(hc)
    
    let run = Input.Option<bool>(["--run"; "-r"], false, "Run the linter.")
    let ctx = Input.Context()
    
    rootCommand argv {
        description ("Lints a Google Drive directory of quizbowl packets using LanguageTool rules.")
        inputs (run, ctx)
        setHandler (fun (run, ctx) -> if not run then showHelp(ctx) else lint())
    }