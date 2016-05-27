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
    member x.ReturnFrom (d) = d
    member x.Delay (f : unit -> PythonData<_>) = 
      (fun () -> f() |> getPythonData) |> pythonFunc
    member x.Combine (v, next) = x.Bind(v, fun () -> next)
    member x.Run (f) = f
    member x.Zero () = (fun () -> ()) |> pythonFunc
    member x.TryWith (d, recover) =
      (fun () -> 
        try
          d |> getPythonData
        with e -> recover e |> getPythonData) |> pythonFunc
    member x.TryFinally (d, final) =
      (fun () -> 
        try
          d |> getPythonData
        finally final ()) |> pythonFunc
    member x.While (condF, body) =
      (fun () -> 
        while condF() do
          body |> getPythonData) |> pythonFunc
    member x.Using (var, block) =
      (fun () -> 
        use v = var
        block v |> getPythonData) |> pythonFunc
    member x.For (seq:seq<'T>, action:'T -> PythonData<_>) = 
      (fun () -> 
        for item in seq do
          action item |> getPythonData) |> pythonFunc

let python = PythonBuilder()

module PythonData =
  let map f d =
    python {
      let! data = d
      return f data
    }

type IPythonEnumerator<'T> =
  abstract MoveNext : unit -> PythonData<'T option>
  inherit System.IDisposable

type IPythonEnumerable<'T> =
  abstract GetEnumerator : unit -> IPythonEnumerator<'T>
type PythonSeq<'T> = IPythonEnumerable<'T>

module PythonSeq =
  let private dispose (d:System.IDisposable) = match d with null -> () | _ -> d.Dispose()

  [<GeneralizableValue>]
  let empty<'T> : PythonSeq<'T> = 
        { new IPythonEnumerable<'T> with 
              member x.GetEnumerator() = 
                  { new IPythonEnumerator<'T> with 
                        member x.MoveNext() = python { return None }
                        member x.Dispose() = () } }
 
  let singleton (v:'T) : PythonSeq<'T> = 
        { new IPythonEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref 0
                  { new IPythonEnumerator<'T> with 
                        member x.MoveNext() = python { let res = state.Value = 0
                                                      incr state; 
                                                      return (if res then Some v else None) }
                        member x.Dispose() = () } }
    
  [<RequireQualifiedAccess>]
  type AppendState<'T> =
     | NotStarted1     of PythonSeq<'T> * PythonSeq<'T>
     | HaveEnumerator1 of IPythonEnumerator<'T> * PythonSeq<'T>
     | NotStarted2     of PythonSeq<'T>
     | HaveEnumerator2 of IPythonEnumerator<'T> 
     | Finished        

  let append (inp1: PythonSeq<'T>) (inp2: PythonSeq<'T>) : PythonSeq<'T> =
        { new IPythonEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref (AppendState.NotStarted1 (inp1, inp2) )
                  { new IPythonEnumerator<'T> with 
                        member x.MoveNext() = 
                           python { match !state with 
                                    | AppendState.NotStarted1 (inp1, inp2) -> 
                                        return! 
                                         (let enum1 = inp1.GetEnumerator()
                                          state := AppendState.HaveEnumerator1 (enum1, inp2)
                                          x.MoveNext())
                                    | AppendState.HaveEnumerator1 (enum1, inp2) ->   
                                        let! res = enum1.MoveNext() 
                                        match res with 
                                        | None -> 
                                            return! 
                                              (state := AppendState.NotStarted2 inp2
                                               dispose enum1
                                               x.MoveNext())
                                        | Some _ -> 
                                            return res
                                    | AppendState.NotStarted2 inp2 -> 
                                        return! 
                                         (let enum2 = inp2.GetEnumerator()
                                          state := AppendState.HaveEnumerator2 enum2
                                          x.MoveNext())
                                    | AppendState.HaveEnumerator2 enum2 ->   
                                        let! res = enum2.MoveNext() 
                                        return (match res with
                                                | None -> 
                                                    state := AppendState.Finished
                                                    dispose enum2
                                                    None
                                                | Some _ -> 
                                                    res)
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | AppendState.HaveEnumerator1 (enum, _) 
                            | AppendState.HaveEnumerator2 enum -> 
                                state := AppendState.Finished
                                dispose enum 
                            | _ -> () } }


  let delay (f: unit -> PythonSeq<'T>) : PythonSeq<'T> = 
      { new IPythonEnumerable<'T> with 
          member x.GetEnumerator() = f().GetEnumerator() }


  [<RequireQualifiedAccess>]
  type BindState<'T,'U> =
     | NotStarted of PythonData<'T>
     | HaveEnumerator of IPythonEnumerator<'U>
     | Finished        

  let bindAsync (f: 'T -> PythonSeq<'U>) (inp : PythonData<'T>) : PythonSeq<'U> = 
        { new IPythonEnumerable<'U> with 
              member x.GetEnumerator() = 
                  let state = ref (BindState.NotStarted inp)
                  { new IPythonEnumerator<'U> with 
                        member x.MoveNext() = 
                           python { match !state with 
                                    | BindState.NotStarted inp -> 
                                        let! v = inp 
                                        return! 
                                           (let s = f v
                                            let e = s.GetEnumerator()
                                            state := BindState.HaveEnumerator e
                                            x.MoveNext())
                                    | BindState.HaveEnumerator e ->   
                                        let! res = e.MoveNext() 
                                        return (match res with
                                                | None -> x.Dispose()
                                                | Some _ -> ()
                                                res)
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | BindState.HaveEnumerator e -> 
                                state := BindState.Finished
                                dispose e 
                            | _ -> () } }



  type PythonSeqBuilder() =
    member x.Yield(v) = singleton v
    // This looks weird, but it is needed to allow:
    //
    //   while foo do
    //     do! something
    //
    // because F# translates body as Bind(something, fun () -> Return())
    member x.Return _ = empty
    member x.YieldFrom(s:PythonSeq<'T>) = s
    member x.Zero () = empty
    member x.Bind (inp:PythonData<'T>, body : 'T -> PythonSeq<'U>) : PythonSeq<'U> = bindAsync body inp
    member x.Combine (seq1:PythonSeq<'T>,seq2:PythonSeq<'T>) = append seq1 seq2
    member x.While (guard, body:PythonSeq<'T>) = 
      // Use F#'s support for Landin's knot for a low-allocation fixed-point
      let rec fix = delay (fun () -> if guard() then append body fix else empty)
      fix
    member x.Delay (f:unit -> PythonSeq<'T>) = 
      delay f
  
  let pythonSeq = new PythonSeqBuilder()


  let emitEnumerator (ie: IPythonEnumerator<'T>) = pythonSeq {
      let! moven = ie.MoveNext() 
      let b = ref moven 
      while b.Value.IsSome do
          yield b.Value.Value 
          let! moven = ie.MoveNext() 
          b := moven }

  [<RequireQualifiedAccess>]
  type TryWithState<'T> =
     | NotStarted of PythonSeq<'T>
     | HaveBodyEnumerator of IPythonEnumerator<'T>
     | HaveHandlerEnumerator of IPythonEnumerator<'T>
     | Finished 

  /// Implements the 'TryWith' functionality for computation builder
  let tryWith (inp: PythonSeq<'T>) (handler : exn -> PythonSeq<'T>) : PythonSeq<'T> = 
        // Note: this is put outside the object deliberately, so the object doesn't permanently capture inp1 and inp2
        { new IPythonEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref (TryWithState.NotStarted inp)
                  { new IPythonEnumerator<'T> with 
                        member x.MoveNext() = 
                           python { match !state with 
                                    | TryWithState.NotStarted inp -> 
                                        let res = ref Unchecked.defaultof<_>
                                        try 
                                            res := Choice1Of2 (inp.GetEnumerator())
                                        with exn ->
                                            res := Choice2Of2 exn
                                        match res.Value with
                                        | Choice1Of2 r ->
                                            return! 
                                              (state := TryWithState.HaveBodyEnumerator r
                                               x.MoveNext())
                                        | Choice2Of2 exn -> 
                                            return! 
                                               (x.Dispose()
                                                let enum = (handler exn).GetEnumerator()
                                                state := TryWithState.HaveHandlerEnumerator enum
                                                x.MoveNext())
                                    | TryWithState.HaveBodyEnumerator e ->   
                                        let res = ref Unchecked.defaultof<_>
                                        try 
                                            let! r = e.MoveNext()
                                            res := Choice1Of2 r
                                        with exn -> 
                                            res := Choice2Of2 exn
                                        match res.Value with 
                                        | Choice1Of2 res -> 
                                            return 
                                                (match res with 
                                                 | None -> x.Dispose()
                                                 | _ -> ()
                                                 res)
                                        | Choice2Of2 exn -> 
                                            return! 
                                              (x.Dispose()
                                               let e = (handler exn).GetEnumerator()
                                               state := TryWithState.HaveHandlerEnumerator e
                                               x.MoveNext())
                                    | TryWithState.HaveHandlerEnumerator e ->   
                                        let! res = e.MoveNext() 
                                        return (match res with 
                                                | Some _ -> res
                                                | None -> x.Dispose(); None)
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | TryWithState.HaveBodyEnumerator e | TryWithState.HaveHandlerEnumerator e -> 
                                state := TryWithState.Finished
                                dispose e 
                            | _ -> () } }
 

  [<RequireQualifiedAccess>]
  type TryFinallyState<'T> =
     | NotStarted    of PythonSeq<'T>
     | HaveBodyEnumerator of IPythonEnumerator<'T>
     | Finished 

  // This pushes the handler through all the async computations
  // The (synchronous) compensation is run when the Dispose() is called
  let tryFinally (inp: PythonSeq<'T>) (compensation : unit -> unit) : PythonSeq<'T> = 
        { new IPythonEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref (TryFinallyState.NotStarted inp)
                  { new IPythonEnumerator<'T> with 
                        member x.MoveNext() = 
                           python { match !state with 
                                    | TryFinallyState.NotStarted inp -> 
                                        return! 
                                           (let e = inp.GetEnumerator()
                                            state := TryFinallyState.HaveBodyEnumerator e
                                            x.MoveNext())
                                    | TryFinallyState.HaveBodyEnumerator e ->   
                                        let! res = e.MoveNext() 
                                        return 
                                           (match res with 
                                            | None -> x.Dispose()
                                            | Some _ -> ()
                                            res)
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | TryFinallyState.HaveBodyEnumerator e-> 
                                state := TryFinallyState.Finished
                                dispose e 
                                compensation()
                            | _ -> () } }


  [<RequireQualifiedAccess>]
  type CollectState<'T,'U> =
     | NotStarted    of PythonSeq<'T>
     | HaveInputEnumerator of IPythonEnumerator<'T>
     | HaveInnerEnumerator of IPythonEnumerator<'T> * IPythonEnumerator<'U>
     | Finished 

  let collect (f: 'T -> PythonSeq<'U>) (inp: PythonSeq<'T>) : PythonSeq<'U> = 
        { new IPythonEnumerable<'U> with 
              member x.GetEnumerator() = 
                  let state = ref (CollectState.NotStarted inp)
                  { new IPythonEnumerator<'U> with 
                        member x.MoveNext() = 
                           python { match !state with 
                                    | CollectState.NotStarted inp -> 
                                        return! 
                                           (let e1 = inp.GetEnumerator()
                                            state := CollectState.HaveInputEnumerator e1
                                            x.MoveNext())
                                    | CollectState.HaveInputEnumerator e1 ->   
                                        let! res1 = e1.MoveNext() 
                                        return! 
                                           (match res1 with
                                            | Some v1 ->
                                                let e2 = (f v1).GetEnumerator()
                                                state := CollectState.HaveInnerEnumerator (e1, e2)
                                            | None -> 
                                                x.Dispose()
                                            x.MoveNext())
                                    | CollectState.HaveInnerEnumerator (e1, e2) ->   
                                        let! res2 = e2.MoveNext() 
                                        match res2 with 
                                        | None ->
                                            state := CollectState.HaveInputEnumerator e1
                                            dispose e2
                                            return! x.MoveNext()
                                        | Some _ -> 
                                            return res2
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | CollectState.HaveInputEnumerator e1 -> 
                                state := CollectState.Finished
                                dispose e1 
                            | CollectState.HaveInnerEnumerator (e1, e2) -> 
                                state := CollectState.Finished
                                dispose e2
                                dispose e1 
                            | _ -> () } }

  [<RequireQualifiedAccess>]
  type CollectSeqState<'T,'U> =
     | NotStarted    of seq<'T>
     | HaveInputEnumerator of System.Collections.Generic.IEnumerator<'T>
     | HaveInnerEnumerator of System.Collections.Generic.IEnumerator<'T> * IPythonEnumerator<'U>
     | Finished 

  // Like collect, but the input is a sequence, where no bind is required on each step of the enumeration
  let collectSeq (f: 'T -> PythonSeq<'U>) (inp: seq<'T>) : PythonSeq<'U> = 
        { new IPythonEnumerable<'U> with 
              member x.GetEnumerator() = 
                  let state = ref (CollectSeqState.NotStarted inp)
                  { new IPythonEnumerator<'U> with 
                        member x.MoveNext() = 
                           python { match !state with 
                                    | CollectSeqState.NotStarted inp -> 
                                        return! 
                                           (let e1 = inp.GetEnumerator()
                                            state := CollectSeqState.HaveInputEnumerator e1
                                            x.MoveNext())
                                    | CollectSeqState.HaveInputEnumerator e1 ->   
                                        return! 
                                          (if e1.MoveNext()  then 
                                               let e2 = (f e1.Current).GetEnumerator()
                                               state := CollectSeqState.HaveInnerEnumerator (e1, e2)
                                           else
                                               x.Dispose()
                                           x.MoveNext())
                                    | CollectSeqState.HaveInnerEnumerator (e1, e2)->   
                                        let! res2 = e2.MoveNext() 
                                        match res2 with 
                                        | None ->
                                            return! 
                                              (state := CollectSeqState.HaveInputEnumerator e1
                                               dispose e2
                                               x.MoveNext())
                                        | Some _ -> 
                                            return res2
                                    | _ -> return None}
                        member x.Dispose() = 
                            match !state with 
                            | CollectSeqState.HaveInputEnumerator e1 -> 
                                state := CollectSeqState.Finished
                                dispose e1 
                            | CollectSeqState.HaveInnerEnumerator (e1, e2) -> 
                                state := CollectSeqState.Finished
                                dispose e2
                                dispose e1
                                x.Dispose()
                            | _ -> () } }

  [<RequireQualifiedAccess>]
  type MapState<'T> =
     | NotStarted    of seq<'T>
     | HaveEnumerator of System.Collections.Generic.IEnumerator<'T>
     | Finished 

  let ofSeq (inp: seq<'T>) : PythonSeq<'T> = 
        { new IPythonEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref (MapState.NotStarted inp)
                  { new IPythonEnumerator<'T> with 
                        member x.MoveNext() = 
                           python { match !state with 
                                    | MapState.NotStarted inp -> 
                                        let e = inp.GetEnumerator()
                                        state := MapState.HaveEnumerator e
                                        return! x.MoveNext()
                                    | MapState.HaveEnumerator e ->   
                                        return 
                                            (if e.MoveNext()  then 
                                                 Some e.Current
                                             else 
                                                 x.Dispose()
                                                 None)
                                    | _ -> return None }
                        member x.Dispose() = 
                            match !state with 
                            | MapState.HaveEnumerator e -> 
                                state := MapState.Finished
                                dispose e 
                            | _ -> () } }

  let iteriPythonData f (source : PythonSeq<_>) = 
      python { 
          use ie = source.GetEnumerator()
          let count = ref 0
          let! move = ie.MoveNext()
          let b = ref move
          while b.Value.IsSome do
              do! f !count b.Value.Value
              let! moven = ie.MoveNext()
              do incr count
                 b := moven
      }

  
  let iterPythonData (f: 'T -> PythonData<unit>) (inp: PythonSeq<'T>)  = iteriPythonData (fun i x -> f x) inp
  let iteri (f: int -> 'T -> unit) (inp: PythonSeq<'T>) = iteriPythonData (fun i x -> python.Return (f i x)) inp
  
  // Add additional methods to the 'asyncSeq' computation builder
  type PythonSeqBuilder with

    member x.TryFinally (body: PythonSeq<'T>, compensation) = 
      tryFinally body compensation   

    member x.TryWith (body: PythonSeq<_>, handler: (exn -> PythonSeq<_>)) = 
      tryWith body handler

    member x.Using (resource: 'T, binder: 'T -> PythonSeq<'U>) = 
      tryFinally (binder resource) (fun () -> 
        if box resource <> null then dispose resource)

    member x.For (seq:seq<'T>, action:'T -> PythonSeq<'TResult>) = 
      collectSeq action seq

    member x.For (seq:PythonSeq<'T>, action:'T -> PythonSeq<'TResult>) = 
      collect action seq

       
  // Add asynchronous for loop to the 'async' computation builder
  type PythonBuilder with
    member internal x.For (seq:PythonSeq<'T>, action:'T -> PythonData<unit>) = 
      seq |> iterPythonData action 

  let rec unfoldPythonData (f:'State -> PythonData<('T * 'State) option>) (s:'State) : PythonSeq<'T> = 
    pythonSeq {       
      let s = ref s
      let fin = ref false
      while not !fin do
        let! next = f !s
        match next with
        | None ->
          fin := true
        | Some (a,s') ->
          yield a
          s := s' }

  let replicateInfinite (v:'T) : PythonSeq<'T> =    
    pythonSeq { 
        while true do 
            yield v }

  let replicateInfinitePythonData (v:PythonData<'T>) : PythonSeq<'T> =
    pythonSeq { 
        while true do 
            let! v = v
            yield v }

  let replicate (count:int) (v:'T) : PythonSeq<'T> =    
    pythonSeq { 
        for i in 1 .. count do 
           yield v }

  // --------------------------------------------------------------------------
  // Additional combinators (implemented as python/PythonSeq computations)

  let mapPythonData f (source : PythonSeq<'T>) : PythonSeq<'TResult> = pythonSeq {
    for itm in source do 
      let! v = f itm
      yield v }

  let mapiPythonData f (source : PythonSeq<'T>) : PythonSeq<'TResult> = pythonSeq {
    let i = ref 0L
    for itm in source do 
      let! v = f i.Value itm
      i := i.Value + 1L
      yield v }

  let choosePythonData f (source : PythonSeq<'T>) : PythonSeq<'R> = pythonSeq {
    for itm in source do
      let! v = f itm
      match v with 
      | Some v -> yield v 
      | _ -> () }

  let filterPythonData f (source : PythonSeq<'T>) = pythonSeq {
    for v in source do
      let! b = f v
      if b then yield v }

  let tryLast (source : PythonSeq<'T>) = python { 
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let res = ref None
      while b.Value.IsSome do
          res := b.Value
          let! moven = ie.MoveNext()
          b := moven
      return res.Value }

  let lastOrDefault def (source : PythonSeq<'T>) = python { 
      let! v = tryLast source
      match v with
      | None -> return def
      | Some v -> return v }


  let tryFirst (source : PythonSeq<'T>) = python {
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      if b.Value.IsSome then 
          return b.Value
      else 
         return None }

  let firstOrDefault def (source : PythonSeq<'T>) = python {
      let! v = tryFirst source
      match v with
      | None -> return def
      | Some v -> return v }

  let scanPythonData f (state:'TState) (source : PythonSeq<'T>) = pythonSeq { 
        yield state 
        let z = ref state
        use ie = source.GetEnumerator() 
        let! moveRes0 = ie.MoveNext()
        let b = ref moveRes0
        while b.Value.IsSome do
          let! zNext = f z.Value b.Value.Value
          z := zNext
          yield z.Value
          let! moveResNext = ie.MoveNext()
          b := moveResNext }

  let pairwise (source : PythonSeq<'T>) = pythonSeq {
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let prev = ref None
      while b.Value.IsSome do
          let v = b.Value.Value
          match prev.Value with 
          | None -> ()
          | Some p -> yield (p, v)
          prev := Some v
          let! moven = ie.MoveNext()
          b := moven }

  let pickPythonData (f:'T -> PythonData<'U option>) (source:PythonSeq<'T>) = python { 
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let res = ref None
      while b.Value.IsSome && not res.Value.IsSome do
          let! fv = f b.Value.Value
          match fv with 
          | None -> 
              let! moven = ie.MoveNext()
              b := moven
          | Some _ as r -> 
              res := r
      match res.Value with
      | Some _ -> return res.Value.Value
      | None -> return raise(System.Collections.Generic.KeyNotFoundException()) }

  let pick f (source:PythonSeq<'T>) =
    pickPythonData (f >> python.Return) source

  let tryPickPythonData f (source : PythonSeq<'T>) = python { 
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let res = ref None
      while b.Value.IsSome && not res.Value.IsSome do
          let! fv = f b.Value.Value
          match fv with 
          | None -> 
              let! moven = ie.MoveNext()
              b := moven
          | Some _ as r -> 
              res := r
      return res.Value }

  let tryPick f (source : PythonSeq<'T>) = 
    tryPickPythonData (f >> python.Return) source 

  let contains value (source : PythonSeq<'T>) = 
    source |> tryPick (fun v -> if v = value then Some () else None) |> PythonData.map Option.isSome

  let tryFind f (source : PythonSeq<'T>) = 
    source |> tryPick (fun v -> if f v then Some v else None)

  let exists f (source : PythonSeq<'T>) = 
    source |> tryFind f |> PythonData.map Option.isSome

  let forall f (source : PythonSeq<'T>) = 
    source |> exists (f >> not) |> PythonData.map not

  let foldPythonData f (state:'State) (source : PythonSeq<'T>) = 
    source |> scanPythonData f state |> lastOrDefault state

  let fold f (state:'State) (source : PythonSeq<'T>) = 
    foldPythonData (fun st v -> f st v |> python.Return) state source 

  let length (source : PythonSeq<'T>) = 
    fold (fun st _ -> st + 1L) 0L source 

  let inline sum (source : PythonSeq<'T>) : PythonData<'T> = 
    (LanguagePrimitives.GenericZero, source) ||> fold (+)

  let scan f (state:'State) (source : PythonSeq<'T>) = 
    scanPythonData (fun st v -> f st v |> python.Return) state source 

  let unfold f (state:'State) = 
    unfoldPythonData (f >> python.Return) state 

  let initInfinitePythonData f = 
    0L |> unfoldPythonData (fun n -> 
        python { let! x = f n 
                return Some (x,n+1L) }) 

  let initPythonData (count:int64) f = 
    0L |> unfoldPythonData (fun n -> 
        python { 
            if n >= count then return None 
            else 
                let! x = f n 
                return Some (x,n+1L) }) 


  let init count f  = 
    initPythonData count (f >> python.Return) 

  let initInfinite f  = 
    initInfinitePythonData (f >> python.Return) 

  let mapi f (source : PythonSeq<'T>) = 
    mapiPythonData (fun i x -> f i x |> python.Return) source

  let map f (source : PythonSeq<'T>) = 
    mapPythonData (f >> python.Return) source

  let indexed (source : PythonSeq<'T>) = 
    mapi (fun i x -> (i,x)) source 

  let iter f (source : PythonSeq<'T>) = 
    iterPythonData (f >> python.Return) source

  let choose f (source : PythonSeq<'T>) = 
    choosePythonData (f >> python.Return) source

  let filter f (source : PythonSeq<'T>) =
    filterPythonData (f >> python.Return) source

let pythonSeq = new PythonSeq.PythonSeqBuilder()


[<AutoOpen>]
module PythonSeqExtensions = 

  // Add asynchronous for loop to the 'async' computation builder
  type PythonBuilder with
    member x.For (seq:PythonSeq<'T>, action:'T -> PythonData<unit>) = 
      seq |> PythonSeq.iterPythonData action 

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
  let d =
    asEnumerable (pyDict.Keys())
    |> Seq.map (fun k -> 
      let str = k.ToString()
      str,
      pyDict.[str])
    |> dict
  { new System.Collections.Generic.IReadOnlyDictionary<string, PyObject> with
      member x.TryGetValue(s, v) = 
        match d.TryGetValue(s) with
        | true, m -> v <- m; true
        | _ -> false
      member x.ContainsKey k = d.ContainsKey k
      member x.get_Item k = 
        match d.TryGetValue k with
        | true, v -> v
        | _ -> raise <| System.Collections.Generic.KeyNotFoundException(sprintf "Key '%s' was not found in object" k)
      member x.Keys = d.Keys :> System.Collections.Generic.IEnumerable<_>
      member x.Values = d.Values :> System.Collections.Generic.IEnumerable<_>
      member x.Count = d.Count
      member x.GetEnumerator() = d.GetEnumerator() :> System.Collections.IEnumerator
      member x.GetEnumerator () = d.GetEnumerator() }