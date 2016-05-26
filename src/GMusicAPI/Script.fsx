// Learn more about F# at http://fsharp.org. See the 'F# Tutorial' project
// for more guidance on F# programming.

#r @"C:\PROJ\googleMusicAllAccess\packages\Dynamitey\lib\net40\Dynamitey.dll"
#r @"C:\PROJ\googleMusicAllAccess\packages\FSharp.Interop.Dynamic\lib\portable-net45+sl50+win\FSharp.Interop.Dynamic.dll"
#r @"C:\PROJ\googleMusicAllAccess\temp\pythonnet\build\lib.win32-3.4\Python.Runtime.dll";;

#load "StringCypher.fs"
#load "PythonInterop.fs"
#load "GMusicApi.fs"

open GMusicAPI
open GMusicAPI.PythonInterop
open GMusicAPI.GMusicAPI

let getEnv envVar =
  System.Environment.GetEnvironmentVariable envVar
  |> StringCypher.decrypt "V@/H!!}N)-]6N'%k\"Vje"

// Use F# interactive and run
// StringCypher.encrypt "V@/H!!}N)-]6N'%k\"Vje" "myprivatedata" 
// and add the result as environment variable
let email = getEnv "GoogleEmail"
let password = getEnv "GooglePassword"
let android_id = getEnv "AndroidId"

//let gil = Python.Runtime.Py.GIL()

let trackId = "Teaherncwq37g2ebmoaqzl775d4";;

let mb = createMobileClient() |> runInPython
let mobileClient = mb.MobileClientHandle

let d = login None email password android_id mb  |> runInPython;;
let devices = getRegisteredDevices mb |> runInPython |> Seq.toList
let trackInfo = getTrackInfo "Teaherncwq37g2ebmoaqzl775d4" mb |> runInPython


//gil.Dispose()
// Define your library scripting code here

