namespace QBLint.Lib

open System.IO

open Google.Apis.Auth.OAuth2
open Google.Apis.Services
open Google.Apis.Util.Store

module Authorization =
    let authorize email =
        let assembly = System.Reflection.Assembly.GetExecutingAssembly()
        let stream = assembly.GetManifestResourceStream("client_secret")
        let secrets = GoogleClientSecrets.FromStream(stream).Secrets
        let path = Path.Combine("credentials")

        let scopes = [| "https://www.googleapis.com/auth/drive" |]

        let credentials =
            task {
                return!
                    GoogleWebAuthorizationBroker.AuthorizeAsync(
                        secrets,
                        scopes,
                        email,
                        System.Threading.CancellationToken.None,
                        FileDataStore(path, true)
                    )
            }
            |> Async.AwaitTask
            |> Async.RunSynchronously

        BaseClientService.Initializer(
            HttpClientInitializer = credentials,
            ApplicationName = "qblint"
        )
