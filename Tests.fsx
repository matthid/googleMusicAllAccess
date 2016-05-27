
#r @"temp/python/Lib/site-packages/Python.Runtime.dll"
#r @"release/lib/GMusicApi.dll"

open GMusicAPI


GMusicAPI.initialize (System.IO.Path.Combine(__SOURCE_DIRECTORY__, "temp", "python"))

let mb = GMusicAPI.createMobileClient() |> PythonInterop.runInPython
printfn "Success!"
