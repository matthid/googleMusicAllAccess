// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#I @"packages/build/FAKE/tools"
#r @"packages/build/FAKE/tools/FakeLib.dll"
#r @"packages/build/sharpcompress/lib/net40/SharpCompress.dll"


open System
open System.IO
open System.Net
open Fake
open SharpCompress
open SharpCompress.Reader
open SharpCompress.Archive
open SharpCompress.Common

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__ 

/// Run the given buildscript with FAKE.exe
let executeWithOutput configStartInfo =
    let exitCode =
        ExecProcessWithLambdas
            configStartInfo
            TimeSpan.MaxValue false ignore ignore
    System.Threading.Thread.Sleep 1000
    exitCode

let extract dir file =
  use stream = File.OpenRead(file)
  let reader = ReaderFactory.Open(stream)
  reader.WriteAllToDirectory (dir, ExtractOptions.ExtractFullPath)

// Documentation
let execute traceMsg failMessage configStartInfo =
    trace traceMsg
    let exit = executeWithOutput configStartInfo
    if exit <> 0 then
        failwith failMessage
    ()

let downloadFile target url =
  async {
    use c = new WebClient()
    do! c.AsyncDownloadFile(new Uri(url), target)
  }

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
    // TODO if Windows then
    let tag = "1.3.20160420"
    let installer = "WinPython-64bit-3.5.1.3Zero.exe"
    let installerPath = ("temp"@@installer)
    downloadFile
      installerPath
      ("https://github.com/winpython/winpython/releases/download/" + tag + "/" + installer)
      |> Async.RunSynchronously
    let installerArgs = sprintf "/S \"/D=%s\"" (System.IO.Path.GetFullPath ("temp"@@"python_zero"))
    execute
      (sprintf "Starting Python Installer with '%s'" installerArgs)
      "Failed to process python command"
      (fun info ->
        info.FileName <- System.IO.Path.GetFullPath (installerPath)
        info.Arguments <- installerArgs
        info.WorkingDirectory <- ""
        let setVar k v =
            info.EnvironmentVariables.[k] <- v
        setVar "PYTHONPATH" (Path.GetFullPath "temp/python"))
    if Directory.Exists ("temp"@@ (Path.GetFileNameWithoutExtension installer)) then
      DeleteDir ("temp"@@"python_zero")
      Directory.Move("temp"@@ (Path.GetFileNameWithoutExtension installer), "temp"@@"python_zero")
    
    DeleteDir ("temp"@@"python")
    CopyDir ("temp"@@"python") ("temp"@@"python_zero"@@"python-3.5.1.amd64") (fun _ -> true)

    python "-m pip install gmusicapi"
    python "-m pip install pythonnet"

    
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
    python_ @"src\tests\runtests.py"
)

Target "All" DoNothing

"SetupPython" ==> "All"

RunTargetOrDefault "All"
