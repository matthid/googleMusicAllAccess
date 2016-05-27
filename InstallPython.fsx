
#if !FAKE
#I @"packages/build/FAKE/tools"
#r @"packages/build/FAKE/tools/FakeLib.dll"
#endif

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
type SystemArch = Win32 | Amd64 with
  member x.ArchString =
    match x with Win32 -> "win32" | Amd64 -> "amd64"
  member x.BitString =
    match x with Win32 -> "32bit" | Amd64 -> "64bit"
type SystemConfig =
  { Arch : SystemArch
    PythonVersion : System.Version }
let installPython_Windows winPythonTag { Arch = arch; PythonVersion = version } targetDir = 
  let tag = winPythonTag
  let fullPythonVersion = sprintf "%d.%d.%d.%d" version.Major version.Minor version.Build version.Revision
  let pythonVersion = sprintf "%d.%d.%d" version.Major version.Minor version.Build
  let installer = sprintf "WinPython-%s-%sZero.exe" arch.BitString fullPythonVersion
  let installerPath = ("temp"@@installer)
  ensureDirectory "temp"
  if not (File.Exists installerPath) then
    downloadFile
      installerPath
      ("https://github.com/winpython/winpython/releases/download/" + tag + "/" + installer)
      |> Async.RunSynchronously
  if not (Directory.Exists("temp"@@"python_zero")) then
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
  let pythonDirName = 
    match arch with
    | Win32 -> sprintf "python-%s" pythonVersion
    | _ -> sprintf "python-%s.%s" pythonVersion arch.ArchString
  CopyDir targetDir ("temp"@@"python_zero"@@ pythonDirName) (fun _ -> true)