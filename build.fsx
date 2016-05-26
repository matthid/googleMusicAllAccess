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

let ipyW workingDir args =
  execute
    (sprintf "Starting IronPython with '%s'" args)
    "Failed to process ironpython command"
    (fun info ->
      info.FileName <- System.IO.Path.GetFullPath "temp/IronPython/ipy.exe"
      info.Arguments <- args
      info.WorkingDirectory <- workingDir
      let setVar k v =
          info.EnvironmentVariables.[k] <- v
      setVar "PYTHONPATH" (Path.GetFullPath "temp/IronPython"))
let ipy args = ipyW "temp/IronPython" args

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


Target "SetupIronPython" (fun _ ->
    CleanDir "temp/IronPython"
    CopyDir ("temp"@@"IronPython") ("packages"@@"IronPython"@@"lib"@@"Net45") (fun _ -> true)
    CopyDir ("temp"@@"IronPython") ("packages"@@"IronPython"@@"tools") (fun _ -> true)

    CopyDir ("temp"@@"IronPython") ("packages"@@"IronPython.StdLib"@@"content") (fun _ -> true)
    ipy "-X:Frames -m ensurepip"

    let installPackageE ext name version md5 =
      let targetFile = sprintf "temp/IronPython/%s-%s%s" name version ext
      downloadFile
        targetFile
        (sprintf "https://pypi.python.org/packages/source/p/%s/%s-%s%s#md5=%s" name name version ext md5)
        |> Async.RunSynchronously
      let distDir = "temp/IronPython/dist" // sprintf "temp/IronPython/%s-%s" name version
      let targetDir = sprintf "%s/%s-%s" distDir name version
      CleanDir targetDir
      extract targetDir targetFile
      let containsSetup dir = File.Exists (sprintf "%s/setup.py" dir)
      if containsSetup targetDir then
        ipyW targetDir "-X:Frames setup.py install"
      else
        let subDir = sprintf "%s/%s-%s" targetDir name version
        if containsSetup subDir then
          ipyW subDir "-X:Frames setup.py install"
        else
          failwith "Could not find setup.py in package!"
    let installPackage = installPackageE ".tar.gz"

    // Install patch, such that we can apply our patches :)
    installPackageE ".zip" "patch" "1.16" "dbcbbd4e45ddd8baeb02bddf663a3176"
    
    let patch patchFile =
      ipy (sprintf "-X:Frames -m patch -v ../../patches/%s" patchFile)

    patch "patch_pip.patch"
    
    // install protobuf manually
    downloadFile
      "temp/IronPython/protobuf-3.0.0a3-py2-none-any.whl"
      "https://github.com/GoogleCloudPlatform/gcloud-python-wheels/raw/master/wheelhouse/protobuf-3.0.0a3-py2-none-any.whl"
      |> Async.RunSynchronously
    ipy "-X:Frames -m pip install protobuf-3.0.0a3-py2-none-any.whl"

    // install pyasn1 manually
    installPackage "pyasn1" "0.1.8" "7f6526f968986a789b1e5e372f0b7065"

    // install pyasn1-modules manually
    installPackage "pyasn1-modules" "0.0.6" "3b94e7a4999bc7477b76c46c30a56727"

    // TODO pycryptodome, 3.4 c626ba69eff9190d152537f93d488d4b

    // Install pycrypto
    ipy "-X:Frames -m easy_install http://www.voidspace.org.uk/downloads/pycrypto26/pycrypto-2.6.win32-py2.7.exe"
    CopyDir 
      ("temp"@@"IronPython"@@"Lib"@@"site-packages"@@"Crypto")
      ("temp"@@"IronPython"@@"Lib"@@"site-packages"@@"pycrypto-2.6-py2.7-cli.egg"@@"Crypto")
      (fun _ -> true)
    
    Git.Repository.cloneSingleBranch
      ("temp")
      ("https://github.com/matthid/ironpycrypto.git")
      "master"
      "ironpycrypto"
    execute
      "Restoring packages for ironpycrypto"
      "Restoring packages for ironpycrypto failed"
      (paketStartInfo "temp/ironpycrypto" "restore")
    execute
      "Building ironpycrypto, this could take some time, please wait..."
      "building ironpycrypto failed"
      (fakeStartInfo "build.fsx" "temp/ironpycrypto" "" "" [])
    CopyDir
      ("temp"@@"IronPython"@@"Lib"@@"site-packages"@@"Crypto")
      ("temp"@@"ironpycrypto"@@"Crypto")
      (fun _ -> true)
    CopyFile 
      ("temp"@@"IronPython") 
      ("temp"@@"ironpycrypto"@@"IronPyCrypto"@@"bin"@@"Release"@@"IronPyCrypto.dll")

    DeleteDir ("temp"@@"IronPython"@@"Lib"@@"site-packages"@@"pycrypto-2.6-py2.7-cli.egg")

    ipy "-X:Frames -m pip install http"
    ipy "-X:Frames -m pip install gmusicapi"
)

Target "All" DoNothing

"SetupIronPython" ==> "All"

RunTargetOrDefault "All"
