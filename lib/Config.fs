namespace QBLint.Lib

open System.IO

open FSharp.Configuration
open FsToolkit.ErrorHandling

type Config = YamlConfig<"_Config.yaml">

module Config =
    let tryCreateEmail email =
        match System.Net.Mail.MailAddress.TryCreate email with
        | true, _ when email.EndsWith("gmail.com") -> Ok email
        | _, _ -> Error(sprintf "Unable to validate email '%s'." email)

    let checkFolderId (folderId: string) =
        match folderId.Length with
        | 33 -> Ok folderId
        | _ -> Error(sprintf "Expected folder ID to be 33 characters.")

    let tryFindFile path =
        match File.Exists path with
        | true -> Ok path
        | _ -> Error(sprintf "Unable to find custom rules file.")

    let validate (config: Config) =
        validation {
            let! email = config.Google.Email |> tryCreateEmail
            and! folderId = config.Google.FolderId |> checkFolderId
            and! customRules = config.LanguageTool.CustomRules |> tryFindFile
            return config
        }

    let load () =
        let config = new Config()
        config.Load(@"_Config.yaml")
        config
