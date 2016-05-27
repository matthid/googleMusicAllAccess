// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#I @"packages/build/FAKE/tools"
#r @"packages/build/FAKE/tools/FakeLib.dll"
#r @"packages/build/sharpcompress/lib/net40/SharpCompress.dll"

#load "InstallPython.fsx"
open InstallPython

let winPythonTag = "1.3.20160420"
let sysConfig =
  { Arch = Amd64; PythonVersion = new System.Version(3,5,1,3) }
let pythonnetVersion = "2.1.0"

let nugetVersion = "10.0.0-alpha2"

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

let correctFsi =
  let any =
    let any = Path.GetDirectoryName(fsiPath) @@ "fsiAnyCPU.exe"
    if File.Exists any then any else fsiPath
  match sysConfig.Arch with
  | Amd64 -> any
  | Win32 -> fsiPath

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
        setVar "FSI" correctFsi)


let fsiStartInfo script workingDirectory args environmentVars =
    (fun (info: System.Diagnostics.ProcessStartInfo) ->
        info.FileName <- correctFsi
        info.Arguments <- sprintf "%s -d:FAKE \"%s\"" args script
        info.WorkingDirectory <- workingDirectory
        let setVar k v =
            info.EnvironmentVariables.[k] <- v
        for (k, v) in environmentVars do
            setVar k v
        setVar "MSBuild" msBuildExe
        setVar "GIT" Git.CommandHelper.gitPath
        setVar "FSI" correctFsi)

Target "SetupPython" (fun _ ->
    if not (Directory.Exists ("temp"@@"python")) then
      installPython_Windows winPythonTag sysConfig ("temp"@@"python")
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
    let simplePyVersion = sprintf "%d%d" sysConfig.PythonVersion.Major sysConfig.PythonVersion.Minor
    let simpleDotPyVersion = sprintf "%d.%d" sysConfig.PythonVersion.Major sysConfig.PythonVersion.Minor
    let pyNetArchDistString =
      match sysConfig.Arch with
      | Amd64 -> sprintf "win_%s" sysConfig.Arch.ArchString
      | _ -> sysConfig.Arch.ArchString
    let distName = sprintf "pythonnet-%s-cp%s-cp%sm-%s.whl" pythonnetVersion simplePyVersion simplePyVersion pyNetArchDistString
    python_ (sprintf "-m pip install --no-cache-dir --force-reinstall --ignore-installed dist\%s" distName)

    let pythonTest = pythonPW (Path.GetFullPath("temp"@@"pythonnet"@@"testdir")) ("temp"@@"pythonnet")
    let pyNetArchString =
      match sysConfig.Arch with
      | Amd64 -> sprintf "win-%s" sysConfig.Arch.ArchString
      | _ -> sysConfig.Arch.ArchString
    CopyDir ("temp"@@"pythonnet"@@"testdir") ("temp"@@"pythonnet"@@"build"@@ sprintf "lib.%s-%s" pyNetArchString simpleDotPyVersion) (fun _ -> true)
    pythonTest @"src\tests\runtests.py"
)

let buildArchString =
  match sysConfig.Arch with
  | Amd64 -> "x64"
  | Win32 -> "x86"
let buildNameString = sprintf "Release_%s" buildArchString
Target "Build" (fun _ ->
    !! "src/GMusicApi.sln"
    |> MSBuildReleaseExt "" [ "Configuration", buildNameString ] "Rebuild"
    |> ignore
)

Target "CopyToRelease" (fun _ ->
    let files = [ "GMusicAPI.dll"; "GMusicAPI.XML" ]
    ensureDirectory ("release"@@"lib")
    for f in files do
      CopyFile ("release"@@"lib") ("src"@@"GMusicAPI"@@"bin"@@buildNameString@@f)
)

Target "Test" (fun _ ->
  execute
    "Starting Fake Tests"
    "Some Tests failed"
    (fsiStartInfo "Tests.fsx" "" "" [])
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
    let packSetup = packSetup nugetVersion
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
        let packSetup = packSetup nugetVersion
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
  ==> "Test"
  ==> "PackageNuGet"
  ==> "All"

"All"
  ==> "NuGetPush"
  ==> "Release"

RunTargetOrDefault "All"


