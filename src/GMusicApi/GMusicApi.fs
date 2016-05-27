module GMusicAPI.GMusicAPI
open GMusicAPI.PythonInterop
open Python.Runtime
open FSharp.Interop.Dynamic

let initialize pythonDir =
  let prev = System.Environment.CurrentDirectory
  try
    System.Environment.CurrentDirectory <- pythonDir
    let oldPyPaths =
      let envVar = System.Environment.GetEnvironmentVariable("PYTHONPATH")
      if isNull envVar then []
      else
        envVar.Split([|System.IO.Path.PathSeparator|], System.StringSplitOptions.RemoveEmptyEntries)
        |> Seq.toList
    let join (paths: _ seq) = System.String.Join(string System.IO.Path.PathSeparator, paths)
    let newPaths =
      [ "."; "lib"; System.IO.Path.Combine("lib", "site-packages"); "DLLs" ]
      |> List.map (fun e -> System.IO.Path.Combine(pythonDir, e))
    let pythonZips = System.IO.Directory.EnumerateFiles(pythonDir, "python*.zip") |> Seq.toList
    let combinedPaths = pythonZips @ newPaths @ oldPyPaths
    System.Environment.SetEnvironmentVariable ("PYTHONPATH", join combinedPaths)
    Python.Runtime.PythonEngine.Initialize()
  finally
    System.Environment.CurrentDirectory <- prev

type GMusicAPI = 
#if INTERACTIVE
  internal
#else
  private
#endif 
    { GMusicAPIHandle : PyObject }
type MobileClient = 
#if INTERACTIVE
  internal
#else
  private
#endif
    { MobileClientHandle : PyObject }

let getGMusicApi =
  python {
    let imported = Py.Import("gmusicapi")
    if isNull imported || imported.Handle = System.IntPtr.Zero then
      printfn "import gmusicapi returned null"
      if (Exceptions.ErrorOccurred()) then
        raise <| new PythonException()
      failwith "Could not import gmusicapi"
      |> ignore
    return { GMusicAPIHandle = imported }
  }


let createMobileClient () =
  python {
    let! { GMusicAPIHandle = gmusic } = getGMusicApi 
    return { MobileClientHandle = gmusic?Mobileclient() }
  }

let login (locale:System.Globalization.CultureInfo option) (username:string) (password:string) (deviceId:string) { MobileClientHandle = mobileClient } =
  let locale = defaultArg locale (System.Globalization.CultureInfo.GetCultureInfo("en-US"))
  python {
    let (res : PyObject) = mobileClient?login(username, password, deviceId, locale.Name)
    return asType<bool> res
  }

let logout { MobileClientHandle = mobileClient } =
  python {
    let (res : PyObject) = mobileClient?logout()
    return asType<bool> res
  }

type RegisteredDevice =
  { Kind : string
    FriendlyName : string option
    Id : string
    LastAccessedTimeMs : string
    Type : string
    SmartPhone : bool }
let debugDict name expectedMax (d:System.Collections.Generic.IDictionary<string,_>) =
  if d.Count > expectedMax then
    printf "Dictionary in '%s' contains unexpected values: %s" name (System.String.Join(",", d.Keys))
  ()
let getRegisteredDevices { MobileClientHandle = mobileClient } =
  python {
    let (rawDevicesList:PyObject) = mobileClient?get_registered_devices()
    return
      asEnumerable rawDevicesList
      |> Seq.map (ofPyDict)
      |> Seq.map (fun (d) ->
        debugDict "getRegisteredDevices" 6 d
        { Kind = asType<string> d.["kind"]
          FriendlyName =
            match d.TryGetValue "friendlyName" with
            | true, v -> Some (asType<string> v)
            | _ -> None
          Id = asType<string> d.["id"]
          LastAccessedTimeMs = asType<string> d.["lastAccessedTimeMs"]
          Type = asType<string> d.["type"]
          SmartPhone =
            match d.TryGetValue("smartPhone") with
            | true, v -> asType<bool> v
            | _ -> false })
  }
let isSubscribed { MobileClientHandle = mobileClient } =
  python {
    let res : PyObject = mobileClient?is_subscribed
    return asType<bool> res
  }

let deauthorizeDevice (deviceId:string) { MobileClientHandle = mobileClient } =
  python {
    let res : PyObject = mobileClient?deauthorize_device(deviceId)
    return asType<bool> res
  }
type StreamQuality =
  | HighQuality
  | MediumQuality
  | LowQuality
let getStreamUrl (deviceId: string option) (quality: StreamQuality option) (trackId:string) { MobileClientHandle = mobileClient } =
  let quality = defaultArg quality HighQuality
  let deviceId = defaultArg deviceId null
  let qualityString =
    match quality with
    | HighQuality -> "hi"
    | MediumQuality -> "med"
    | LowQuality -> "low"
  python {
    let streamUrl:PyObject = mobileClient?get_stream_url(trackId, deviceId, qualityString)
    return asType<string> streamUrl
  }

type ArtRef =
  { Kind : string
    Autogen : bool
    Url : string
    AspectRation : string }
type TrackInfo =
  { Album : string
    ExplicitType : string
    Kind : string
    StoreId : string
    Artist : string
    AlbumArtRef : ArtRef list
    Title : string
    NId : string
    EstimatedSize : string
    Year : int
    AlbumId : string
    ArtistId : string list
    AlbumArtist : string
    DurationMillis : string
    Composer : string
    Genre : string
    TrackNumber : int
    DiscNumber : int
    TrackAvailableForPurchase : bool
    TrackAvailableForSubscription : bool
    TrackType : string
    AlbumAvailableForPurchase : bool }
let getTrackInfo (trackId:string) { MobileClientHandle = mobileClient } =
  python {
    let trackInfo:PyObject = mobileClient?get_track_info(trackId)
    let d = ofPyDict trackInfo
    debugDict "getTrackInfo" 22 d
    return 
      { Album = asType<string> d.["album"]
        ExplicitType = asType<string> d.["explicitType"]
        Kind = asType<string> d.["kind"]
        Year = asType<int> d.["year"]
        StoreId = asType<string> d.["storeId"]
        Artist = asType<string> d.["artist"]
        AlbumArtRef = []
        Title = asType<string> d.["title"]
        NId = asType<string> d.["nid"]
        EstimatedSize = asType<string> d.["estimatedSize"]
        AlbumId = asType<string> d.["albumId"]
        ArtistId = []
        AlbumArtist = asType<string> d.["albumArtist"]
        DurationMillis = asType<string> d.["durationMillis"]
        Composer = asType<string> d.["composer"]
        Genre = asType<string> d.["genre"]
        TrackNumber = asType<int> d.["trackNumber"]
        DiscNumber = asType<int> d.["discNumber"]
        TrackAvailableForPurchase = asType<bool> d.["trackAvailableForPurchase"]
        TrackAvailableForSubscription = asType<bool> d.["trackAvailableForSubscription"]
        TrackType = asType<string> d.["trackType"]
        AlbumAvailableForPurchase =  asType<bool> d.["albumAvailableForPurchase"] }
  }

