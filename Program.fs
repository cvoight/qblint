namespace QBLint

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml.Styling
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts

type MainWindow() =
    inherit HostWindow()

    do
        base.Title <- "qblint"
        base.Width <- 640.0
        base.Height <- 640.0
        base.Content <- Views.mainView ()

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())

        this.Styles.Add(
            StyleInclude(baseUri = null, Source = System.Uri("avares://qblint/Styles.axaml"))
        )

        this.RequestedThemeVariant <- Styling.ThemeVariant.Light

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            desktopLifetime.MainWindow <- MainWindow()
        | _ -> ()

module Program =

    [<EntryPoint>]
    let main (args: string[]) =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .StartWithClassicDesktopLifetime(args)
