namespace QBLint

open System.IO
open Google.Apis.Drive.v3
open Google.Apis.Docs.v1
open Google.Apis.Docs.v1.Data
open QBLint.Lib
open QBLint.Lib.Actions

open org.languagetool
open org.languagetool.language
open org.languagetool.tools

open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
    
type State =
    { Config: IWritable<Config>
      Email: IReadable<string>
      ParentId: IReadable<string>
      Output: IWritable<string> }

    member this.Lint (config : Config) = async {
        this.Output.Set "Starting..."
        let stopWatch = System.Diagnostics.Stopwatch.StartNew()
        let parentId = config.Google.FolderId

        let ctx = System.Threading.SynchronizationContext.Current
        do! Async.SwitchToThreadPool()

        let clientService = Authorization.authorize config.Google.Email
        let driveService = new DriveService(clientService)
        let docsService = new DocsService(clientService)

        let docsIds = 
            getFiles driveService parentId (MimeType.Google(Doc))
            |> (fun f -> f.Files)
            |> Seq.choose (fun doc ->
                match doc.Id with
                | null -> None
                | id -> Some (id))

        let maybeFolder =
            getFiles driveService parentId (MimeType.Google(Folder))
            |> (fun f -> f.Files)
            |> Seq.tryPick (fun folder ->
                if folder.Name[..5]="qblint" then Some(folder) else None)

        let timestamp = System.DateTime.Now.ToString("s")
        let name = $"qblint.{timestamp}"
        let linterFolder =
            match maybeFolder with
            | None -> createFolder driveService parentId name
            | Some folder ->
                trashFile driveService folder.Id |> ignore
                createFolder driveService parentId name

        let lt = MultiThreadedJLanguageTool(new AmericanEnglish())
        let customRulesLoader = rules.patterns.PatternRuleLoader()
        let customRulesFile = Path.Combine(config.LanguageTool.CustomRules)
        use fs = File.OpenRead(customRulesFile)
        use isw = new ikvm.io.InputStreamWrapper(fs)
        let customRules = customRulesLoader.getRules(isw, customRulesFile)
        let iter : java.util.Iterator = customRules.iterator()
        while iter.hasNext() do
            lt.addRule(iter.next() :?> rules.Rule)
        Seq.iter (fun s -> lt.disableRule(s)) (config.LanguageTool.DisableRules)
        do! Async.SwitchToContext(ctx)

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
            |> Seq.choose paragraphs |> Seq.concat
            |> Seq.choose textruns |> Seq.fold (+) ""

        for docId in docsIds do
            do! Async.SwitchToThreadPool()
            let doc = docsService.Documents.Get(docId).Execute()
            let txt = doc |> getText
            let matches = lt.check(txt)
            let dt = DetectedLanguage(lt.getLanguage(), lt.getLanguage())
            let serializer = RuleMatchesAsJsonSerializer()
            let json = serializer.ruleMatchesToJson(matches, txt, 50, dt)
            let upload = uploadFile driveService linterFolder.Id docId json
            let output =
                if (upload.Status = Google.Apis.Upload.UploadStatus.Completed) then
                    [this.Output.Current; $"{doc.Title} linted and uploaded."]
                    |> String.concat "\n"
                else
                    [this.Output.Current; $"Error occurred while uploading {doc.Title}: {upload.Exception.Message}" ]
                    |> String.concat "\n"
            do! Async.SwitchToContext(ctx)
            this.Output.Set output

        stopWatch.Stop()
        let time = sprintf "Executed in %f seconds." stopWatch.Elapsed.TotalSeconds
        this.Output.Set ([this.Output.Current; time] |> String.concat "\n")
    }

    static member Validate (config : Config) =
        match Config.validate (config) with
        | Ok _ -> "Configuration validated. Click Run to lint."
        | Error e -> e |> String.concat "\n"

    static member Init () =
        let config : IWritable<Config> = new State<Config>(Config.load())
        let email: IReadable<string> = new State<string>(config.Current.Google.Email)
        let parentId : IReadable<string> = new State<string>(config.Current.Google.FolderId)
        let output : IWritable<string> = new State<string>(State.Validate config.Current)
        { Config = config
          Email = email
          ParentId = parentId
          Output = output }

[<RequireQualifiedAccess>]
module State =
    let shared = State.Init ()

[<AbstractClass; Sealed>]
type Views =

    static member mainView () =
        Component (fun ctx ->
            let config = ctx.usePassed State.shared.Config
            let email = ctx.usePassedRead State.shared.Email
            let parentId = ctx.usePassedRead State.shared.ParentId
            let output = ctx.usePassed State.shared.Output
            DockPanel.create [
                DockPanel.margin 10.0
                DockPanel.children [
                    TextBox.create [
                        TextBox.dock Dock.Top
                        TextBox.verticalAlignment VerticalAlignment.Center
                        TextBox.horizontalAlignment HorizontalAlignment.Left
                        TextBox.margin (0.0, 0.0, 0.0, 8.0)
                        TextBox.width 360.0
                        TextBox.watermark "Email"
                        TextBox.useFloatingWatermark true
                        TextBox.text (string email.Current)
                        TextBox.onTextChanged (fun txt ->
                            config.Current.Google.Email <- txt
                            config.Current.Save(@"_Config.yaml")
                            State.Validate config.Current |> output.Set
                        )
                    ]
                    TextBox.create [
                        TextBox.dock Dock.Top
                        TextBox.verticalAlignment VerticalAlignment.Center
                        TextBox.horizontalAlignment HorizontalAlignment.Left
                        TextBox.margin (0.0, 0.0, 0.0, 8.0)
                        TextBox.width 360.0
                        TextBox.watermark "Folder ID"
                        TextBox.useFloatingWatermark true
                        TextBox.text (string parentId.Current)
                        TextBox.onTextChanged (fun txt ->
                            config.Current.Google.FolderId <- txt
                            config.Current.Save(@"_Config.yaml")
                            State.Validate config.Current |> output.Set
                        )
                    ]
                    TextBlock.create [
                        TextBlock.dock Dock.Top
                        TextBlock.verticalAlignment VerticalAlignment.Stretch
                        TextBlock.horizontalAlignment HorizontalAlignment.Left
                        TextBlock.text (string output.Current)
                    ]
                    Button.create [
                        Button.dock Dock.Bottom
                        Button.width 100.0
                        Button.onClick (fun _ ->
                            State.shared.Lint config.Current
                            |> Async.StartImmediate
                            )
                        Button.content "Run"
                        Button.horizontalAlignment HorizontalAlignment.Right
                        Button.verticalAlignment VerticalAlignment.Bottom
                        Button.horizontalContentAlignment HorizontalAlignment.Center
                    ]
                ]
            ]
        )