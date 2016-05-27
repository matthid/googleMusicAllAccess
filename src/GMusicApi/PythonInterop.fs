module GMusicAPI.PythonInterop

open FSharp.Interop.Dynamic

type PythonData<'a> =
  private { 
    Delayed : (unit -> 'a)
    mutable Cache : 'a option
  }

let pythonFunc f = { Delayed = f; Cache = None }
let internal unsafeExecute (f:PythonData<_>) =
  match f.Cache with
  | Some d -> d
  | None ->
    let res = f.Delayed()
    f.Cache <- Some res
    res
let private getPythonData = unsafeExecute
let runInPython f = 
  use __ = Python.Runtime.Py.GIL()
  f |> getPythonData
  
type PythonBuilder() =
    member x.Bind((d : PythonData<_>), f) =
        (fun () -> 
          let r = d |> getPythonData
          f r |> getPythonData
          ) |> pythonFunc

    member x.Return(d) = 
        (fun () -> d) |> pythonFunc

    member x.Delay (f : unit -> PythonData<_>) = 
      (fun () -> f() |> getPythonData) |> pythonFunc
    member x.Combine (v, next) = x.Bind(v, fun () -> next)
    member x.Run (f) = f
    member x.Zero () = (fun () -> ()) |> pythonFunc

let python = PythonBuilder()

let asType<'t> item = (item >>?>> typeof<'t>) : 't

open Python.Runtime

let internal asEnumerable (p : PyObject) = 
  { new System.Collections.Generic.IEnumerable<obj> with
      override x.GetEnumerator() = x.GetEnumerator() :> System.Collections.IEnumerator
      override x.GetEnumerator () = new PyIter(p) :> System.Collections.Generic.IEnumerator<obj> }
  |> Seq.cast<PyObject>

let internal ofPyList (p : PyObject) =
    PyList.AsList(p)
    |> Seq.cast<PyObject>
    |> Seq.toList
  
let internal ofPyDict (p : PyObject) =
  let pyDict = new PyDict(p)
  asEnumerable (pyDict.Keys())
  |> Seq.map (fun k -> 
    let str = k.ToString()
    str,
    pyDict.[str])
  |> dict