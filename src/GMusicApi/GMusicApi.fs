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
let debugDict name expectedKeys (d:System.Collections.Generic.IReadOnlyDictionary<string, PyObject>) =
  for k in d.Keys do
    if expectedKeys |> Seq.exists (fun exp -> exp = k) |> not then
      let tryVal =
        match d.TryGetValue k with
        | true, value ->
          value.Repr()
        | _ -> "{NOTFOUND}"
      printfn "Dictionary in '%s' contains unexpected key: '%s' (%s)" name k tryVal
  ()

let internal parseRegisteredDevice (o:PyObject) =
  let d = ofPyDict o
  debugDict "getRegisteredDevices" [ "kind"; "friendlyName"; "id"; "lastAccessedTimeMs"; "type"; "smartPhone" ] d
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
      | _ -> false }

let getRegisteredDevices { MobileClientHandle = mobileClient } =
  python {
    let (rawDevicesList:PyObject) = mobileClient?get_registered_devices()
    return
      asEnumerable rawDevicesList
      |> Seq.map (parseRegisteredDevice)
      |> Seq.toList
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

type VidThumb =
  { Width : int
    Height : int
    Url : string }
let internal parseVidThumb (o : PyObject) =
  let d = ofPyDict o
  debugDict "parseVidRef" ["width"; "height"; "url" ] d
  { Width = asType<int> d.["width"]
    Height = asType<int> d.["height"]
    Url = asType<string> d.["url"] }
type VidRef =
  { Kind : string
    Thumbnails : VidThumb list
    /// Youtube Id https://www.youtube.com/watch?v=%s for Url
    Id : string }
let internal parseVidRef (o : PyObject) =
  let d = ofPyDict o
  debugDict "parseVidRef" ["kind"; "thumbnails"; "id" ] d
  { Kind = asType<string> d.["kind"]
    Thumbnails = 
      asEnumerable d.["thumbnails"]
      |> Seq.map parseVidThumb
      |> Seq.toList
    Id = asType<string> d.["id"] }
type ArtRef =
  { Kind : string
    Autogen : bool
    Url : string
    AspectRatio : string }
let internal parseAlbumRef (o:PyObject) =
  let d = ofPyDict o
  debugDict "parseAlbumRef" [ "kind"; "autogen"; "url"; "aspectRatio" ] d
  { Kind = asType<string> d.["kind"]
    Autogen = asType<bool> d.["autogen"]
    Url = asType<string> d.["url"]
    AspectRatio = asType<string> d.["aspectRatio"] }
type TrackInfo =
  { Album : string
    ExplicitType : string
    Kind : string
    PrimaryVideo : VidRef option
    StoreId : string
    Artist : string
    AlbumArtRef : ArtRef list
    Title : string
    NId : string
    EstimatedSize : string
    Year : int option
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

let internal parseTrackInfo (o : PyObject) =
  let d = ofPyDict o
  debugDict "getTrackInfo"
    ["album"; "explicitType"; "kind"; "year"; "storeId"; "artist"; "albumArtRef"; "title";
     "nid"; "estimatedSize"; "albumId"; "artistId"; "albumArtist"; "durationMillis"; 
     "composer"; "genre"; "trackNumber"; "discNumber"; "trackAvailableForPurchase";
     "trackAvailableForSubscription"; "trackType"; "albumAvailableForPurchase" ] d
  { Album = asType<string> d.["album"]
    ExplicitType = asType<string> d.["explicitType"]
    Kind = asType<string> d.["kind"]
    PrimaryVideo =
      match d.TryGetValue "primaryVideo" with
      | true, v -> Some (parseVidRef v)
      | _ -> None
    Year = 
      match d.TryGetValue("year") with
      | true, y -> Some (asType<int> y)
      | _ -> None
    StoreId = asType<string> d.["storeId"]
    Artist = asType<string> d.["artist"]
    AlbumArtRef = 
      asEnumerable d.["albumArtRef"]
      |> Seq.map parseAlbumRef
      |> Seq.toList
    Title = asType<string> d.["title"]
    NId = asType<string> d.["nid"]
    EstimatedSize = asType<string> d.["estimatedSize"]
    AlbumId = asType<string> d.["albumId"]
    ArtistId = 
      asEnumerable d.["artistId"]
      |> Seq.map (asType<string>)
      |> Seq.toList
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

let getTrackInfo (trackId:string) { MobileClientHandle = mobileClient } =
  python {
    let trackInfo:PyObject = mobileClient?get_track_info(trackId)
    return parseTrackInfo trackInfo
  }

