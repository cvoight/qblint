namespace QBLint.Lib

open System.IO
open Google.Apis.Drive.v3
open Google.Apis.Drive.v3.Data

type MimeType = 
    | Folder
    | Doc
    
    static member Google m =
        match m with
        | Folder -> "application/vnd.google-apps.folder"
        | Doc -> "application/vnd.google-apps.document"

module Actions =

    let create parentId name =
        File(Name = name, Parents = ([ parentId ] |> ResizeArray))

    let execute (request : DriveBaseServiceRequest<'T>)=
        task {
            return! request.ExecuteAsync()
        } |> Async.AwaitTask |> Async.RunSynchronously

    let upload (request : FilesResource.CreateMediaUpload)=
        task {
            return! request.UploadAsync()
        } |> Async.AwaitTask |> Async.RunSynchronously

    let getFiles (driveService : DriveService) parentId mimeType =
        driveService.Files.List()
        |> (fun r -> r.Q <- $"'{parentId}' in parents and\
            mimeType='{mimeType}' and trashed=false"; r)
        |> execute

    let createFolder (driveService : DriveService) parentId name =
        create parentId name
        |> (fun f -> f.MimeType <- MimeType.Google(Folder); f)
        |> (fun f -> driveService.Files.Create(f))
        |> execute

    let trashFile (driveService : DriveService) fileId =
        execute (driveService.Files.Update(File(Trashed = true), fileId))

    let uploadFile (driveService : DriveService) parentId name (data : string) =
        let bytes = System.Text.Encoding.UTF8.GetBytes data
        let stream = new MemoryStream(bytes)
        create parentId name
        |> (fun f -> driveService.Files.Create(f, stream, "text/plain"))
        |> upload