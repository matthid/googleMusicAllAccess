// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#I @"packages/build/FAKE/tools"
#r @"packages/build/FAKE/tools/FakeLib.dll"
#r @"packages/build/sharpcompress/lib/net40/SharpCompress.dll"

#load "InstallPython.fsx"
open InstallPython

open System
open System.IO
open System.Net
open Fake
open SharpCompress
open SharpCompress.Reader
open SharpCompress.Archive
open SharpCompress.Common

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let extract dir file =
  use stream = File.OpenRead(file)
  let reader = ReaderFactory.Open(stream)
  reader.WriteAllToDirectory (dir, ExtractOptions.ExtractFullPath)


let pythonPW pythonPath workingDir args =
  execute
    (sprintf "Starting Python with '%s'" args)
    "Failed to process python command"
    (fun info ->
      info.FileName <- System.IO.Path.GetFullPath "temp/python/python.exe"
      info.Arguments <- args
      info.WorkingDirectory <- workingDir
      let setVar k v =
          info.EnvironmentVariables.[k] <- v
      setVar "PYTHONPATH" (pythonPath))
let pythonW = pythonPW (Path.GetFullPath "temp/python")
let python args = pythonW "temp/python" args

let paketPath = ".paket" @@ "paket.exe"
let paketStartInfo workingDirectory args =
    (fun (info: System.Diagnostics.ProcessStartInfo) ->
        info.FileName <- System.IO.Path.GetFullPath paketPath
        info.Arguments <- sprintf "%s" args
        info.WorkingDirectory <- workingDirectory
        let setVar k v =
            info.EnvironmentVariables.[k] <- v
        setVar "MSBuild" msBuildExe
        setVar "GIT" Git.CommandHelper.gitPath
        setVar "FSI" fsiPath)

let fakePath = "packages" @@ "build" @@ "FAKE" @@ "tools" @@ "FAKE.exe"
let fakeStartInfo script workingDirectory args fsiargs environmentVars =
    (fun (info: System.Diagnostics.ProcessStartInfo) ->
        info.FileName <- System.IO.Path.GetFullPath fakePath
        info.Arguments <- sprintf "%s --fsiargs -d:FAKE %s \"%s\"" args fsiargs script
        info.WorkingDirectory <- workingDirectory
        let setVar k v =
            info.EnvironmentVariables.[k] <- v
        for (k, v) in environmentVars do
            setVar k v
        setVar "MSBuild" msBuildExe
        setVar "GIT" Git.CommandHelper.gitPath
        setVar "FSI" fsiPath)


Target "SetupPython" (fun _ ->
    if not (Directory.Exists ("temp"@@"python")) then
      installPython ("temp"@@"python")
    python "-m pip install gmusicapi"
    python "-m pip install pythonnet"

    
    if not (Directory.Exists ("temp"@@"pythonnet")) then
      Git.Repository.cloneSingleBranch
        ("temp")
        ("https://github.com/matthid/pythonnet.git")
        "myfixes"
        "pythonnet"
    let python_ = pythonW ("temp"@@"pythonnet")
    python_ "-m pip install wheel"
    python_ "-m pip install six"

    python_ "setup.py bdist_wheel"
    //python_ "-m pip install --no-cache-dir --force-reinstall --ignore-installed dist\pythonnet-2.1.0-cp34-cp34m-win32.whl"
    python_ "-m pip install --no-cache-dir --force-reinstall --ignore-installed dist\pythonnet-2.1.0-cp35-cp35m-win_amd64.whl"
    
    let pythonTest = pythonPW (Path.GetFullPath("temp"@@"pythonnet"@@"testdir")) ("temp"@@"pythonnet")
    
    CopyDir ("temp"@@"pythonnet"@@"testdir") ("temp"@@"pythonnet"@@"build"@@"lib.win-amd64-3.5") (fun _ -> true)
    pythonTest @"src\tests\runtests.py"
)

Target "Build" (fun _ ->
    !! "src/GMusicApi.sln"
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

Target "CopyToRelease" (fun _ ->
    let files = [ "GMusicAPI.dll"; "GMusicAPI.XML" ]
    ensureDirectory ("release"@@"lib")
    for f in files do
      CopyFile ("release"@@"lib") ("src"@@"GMusicAPI"@@"bin"@@"Release"@@f)
)

/// push package (and try again if something fails), FAKE Version doesn't work on mono
/// From https://raw.githubusercontent.com/fsharp/FAKE/master/src/app/FakeLib/NuGet/NugetHelper.fs
let rec private publish parameters =
    let replaceAccessKey key (text : string) =
        if isNullOrEmpty key then text
        else text.Replace(key, "PRIVATEKEY")
    let nuspec = sprintf "%s.%s.nupkg" parameters.Project parameters.Version
    traceStartTask "MyNuGetPublish" nuspec
    let tracing = enableProcessTracing
    enableProcessTracing <- false
    let source =
        if isNullOrEmpty parameters.PublishUrl then ""
        else sprintf "-s %s" parameters.PublishUrl

    let args = sprintf "push \"%s\" %s %s" (parameters.OutputPath @@ nuspec) parameters.AccessKey source
    tracefn "%s %s in WorkingDir: %s Trials left: %d" parameters.ToolPath (replaceAccessKey parameters.AccessKey args)
        (FullName parameters.WorkingDir) parameters.PublishTrials
    try
      try
        let result =
            ExecProcess (fun info ->
                info.FileName <- parameters.ToolPath
                info.WorkingDirectory <- FullName parameters.WorkingDir
                info.Arguments <- args) parameters.TimeOut
        enableProcessTracing <- tracing
        if result <> 0 then failwithf "Error during NuGet push. %s %s" parameters.ToolPath args
        true
      with exn ->
        let existsError = exn.Message.Contains("already exists and cannot be modified")
        if existsError then
          trace exn.Message
          false
        else
          if parameters.PublishTrials > 0 then publish { parameters with PublishTrials = parameters.PublishTrials - 1 }
          else
            (if not (isNull exn.InnerException) then exn.Message + "\r\n" + exn.InnerException.Message
             else exn.Message)
            |> replaceAccessKey parameters.AccessKey
            |> failwith
    finally
      traceEndTask "MyNuGetPublish" nuspec

let packSetup version p =
  { p with
      Authors = ["Matthias Dittrich"]
      Project = "GMusicApi"
      Summary = "Wrapper around https://github.com/simon-weber/gmusicapi"
      Version = version
      Description = "Wrapper around https://github.com/simon-weber/gmusicapi"
      Tags = "python google music all access fsharp csharp dll"
      WorkingDir = "."
      OutputPath = "release"@@"nuget"
      AccessKey = getBuildParamOrDefault "nugetkey" ""
      Publish = false
      Dependencies = [ ] }

Target "PackageNuGet" (fun _ ->
    ensureDirectory ("release"@@"nuget")
    let packSetup = packSetup "10.0.0-alpha"
    NuGet (fun p -> 
      { (packSetup) p with 
          Publish = false
          Dependencies = 
            [ "FSharp.Interop.Dynamic"
              "FSharp.Core" ]
            |> List.map (fun name -> name, (GetPackageVersion "packages" name))
      }) (Path.Combine("nuget", "GMusicApi.nuspec"))
)

Target "NuGetPush" (fun _ ->
    let packagePushed =
      try
        let packSetup = packSetup "10.0.0-alpha"
        let parameters = NuGetDefaults() |> (fun p -> 
          { packSetup p with 
              Publish = true
              Dependencies = 
                [ "FSharp.Interop.Dynamic"
                  "FSharp.Core" ]
                |> List.map (fun name -> name, (GetPackageVersion "packages" name)) })
        // This allows us to specify packages which we do not want to push...
        if hasBuildParam "nugetkey" && parameters.Publish then publish parameters
        else true
      with e -> 
        trace (sprintf "Could not push package '%s': %O" ("GMusicApi") e)
        false

    if not packagePushed then
      failwithf "No package could be pushed!"
)

Target "All" DoNothing
Target "Release" DoNothing

"SetupPython"
  ==> "Build"
  ==> "CopyToRelease"
  ==> "PackageNuGet"
  ==> "All"

"All"
  ==> "NuGetPush"
  ==> "Release"

RunTargetOrDefault "All"


