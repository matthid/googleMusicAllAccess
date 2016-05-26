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

Target "All" DoNothing

"SetupPython" ==> "All"

RunTargetOrDefault "All"
