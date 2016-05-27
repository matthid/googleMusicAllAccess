
#r @"temp/python/Lib/site-packages/Python.Runtime.dll"
#r @"packages/Dynamitey/lib/net40/Dynamitey.dll"
#r @"packages/FSharp.Interop.Dynamic/lib/portable-net45+sl50+win/FSharp.Interop.Dynamic.dll"
#r @"release/lib/GMusicApi.dll"

open GMusicAPI


GMusicAPI.initialize (System.IO.Path.Combine(__SOURCE_DIRECTORY__, "temp", "python"))

let mb = GMusicAPI.createMobileClient() |> PythonInterop.runInPython
printfn "Success!"
