
#I @"packages/build/FAKE/tools"
#r @"packages/build/FAKE/tools/FakeLib.dll"

open System
open System.IO
open System.Net
open Fake


/// Run the given buildscript with FAKE.exe
let executeWithOutput configStartInfo =
    let exitCode =
        ExecProcessWithLambdas
            configStartInfo
            TimeSpan.MaxValue false ignore ignore
    System.Threading.Thread.Sleep 1000
    exitCode


let execute traceMsg failMessage configStartInfo =
    trace traceMsg
    let exit = executeWithOutput configStartInfo
    if exit <> 0 then
        failwith failMessage
    ()

let downloadFile target url =
  async {
    use c = new WebClient()
    printfn "Downloading %s from %s ..." target url
    do! c.AsyncDownloadFile(new Uri(url), target)
  }

let installPython targetDir = 
  // TODO handle linux
  let tag = "1.3.20160420"
  let installer = "WinPython-64bit-3.5.1.3Zero.exe"
  let installerPath = ("temp"@@installer)
  ensureDirectory "temp"
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
      setVar "PYTHONPATH" (Path.GetFullPath targetDir))
  if Directory.Exists ("temp"@@ (Path.GetFileNameWithoutExtension installer)) then
    DeleteDir ("temp"@@"python_zero")
    Directory.Move("temp"@@ (Path.GetFileNameWithoutExtension installer), "temp"@@"python_zero")
    
  DeleteDir targetDir
  CopyDir targetDir ("temp"@@"python_zero"@@"python-3.5.1.amd64") (fun _ -> true)