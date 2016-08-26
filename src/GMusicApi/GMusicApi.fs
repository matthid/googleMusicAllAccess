module GMusicAPI.GMusicAPI

open GMusicApi.Core
open GMusicAPI.PythonInterop
open Python.Runtime
open FSharp.Interop.Dynamic

/// Call this when you application starts
let mutable isInitialized = false
let initialize pythonDir =
  if not isInitialized then
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
    isInitialized <- true
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

let toTimestamp s = { Raw = s }
let ofTimestamp { Raw = s } = s

type GMusicAPI = 
#if INTERACTIVE
  internal
#else
  private
#endif 
    { GMusicAPIHandle : PyObject }
    
/// Allows library management and streaming by posing as the googleapis.com mobile clients.
/// Uploading is not supported by this client (use the Musicmanager to upload).
type MobileClient = 
#if INTERACTIVE
  internal
#else
  private
#endif
    { MobileClientHandle : PyObject }

/// The gmusicapi module.
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



/// Create a new Mobileclient
///
/// - `debug_logging`: If this param is True, handlers will be configured to send this client’s debug log output to disk, with warnings and above printed to stderr. Appdirs user_log_dir is used by default. Users can run:
///     from gmusicapi.utils import utils
///     print utils.log_filepath
///   to see the exact location on their system.
///   If False, no handlers will be configured; users must create their own handlers.
///   Completely ignoring logging is dangerous and not recommended. The Google Music protocol can change at any time; if something were to go wrong, the logs would be necessary for recovery.
/// - `validate`: if False, do not validate server responses against known schemas. This helps to catch protocol changes, but requires significant cpu work.
///   This arg is stored as `self.validate` and can be safely modified at runtime.
/// - `verifySsl`: if False, exceptions will not be raised if there are problems verifying SSL certificates. Be wary of using this option; it’s almost always better to fix the machine’s SSL configuration than to ignore errors.
let createMobileClient (debugLogging : bool) (validate:bool) (verifySsl : bool) =
  python {
    let! { GMusicAPIHandle = gmusic } = getGMusicApi 
    return { MobileClientHandle = gmusic?Mobileclient(debugLogging, validate, verifySsl) }
  }

let login { MobileClientHandle = mobileClient } (locale:System.Globalization.CultureInfo option) (username:string) (password:string) (deviceId:string) =
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

let isAuthenticated { MobileClientHandle = mobileClient } =
  python {
    let (res : PyObject) = mobileClient?is_authenticated()
    return asType<bool> res
  }

/// The locale of the Mobileclient session used to localize some responses.
/// Should be an ICU locale supported by Android.
/// Set on authentication with login() but can be changed at any time via `setLocale`.
let locale { MobileClientHandle = mobileClient } =
  python {
    let (res : PyObject) = mobileClient?locale
    let locale = asType<string> res
    return System.Globalization.CultureInfo.GetCultureInfo(locale)
  }

let setLocale (newLocale:System.Globalization.CultureInfo) { MobileClientHandle = mobileClient } =
  python {
    mobileClient?locale <- newLocale.Name
  }

let isSubscribed { MobileClientHandle = mobileClient } =
  python {
    let res : PyObject = mobileClient?is_subscribed
    return asType<bool> res
  }
let deleteSubscribed { MobileClientHandle = mobileClient } =
  python {
    mobileClient.DelAttr("is_subscribed")
  }


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

let deauthorizeDevice { MobileClientHandle = mobileClient } (deviceId:string) =
  python {
    let res : PyObject = mobileClient?deauthorize_device(deviceId)
    return asType<bool> res
  }


let internal parseVidThumb (o : PyObject) =
  let d = ofPyDict o
  debugDict "parseVidRef" ["width"; "height"; "url" ] d
  { Width = asType<int> d.["width"]
    Height = asType<int> d.["height"]
    Url = asType<string> d.["url"] }

let internal parseVidRef (o : PyObject) =
  let d = ofPyDict o
  debugDict "parseVidRef" ["kind"; "thumbnails"; "id" ] d
  { Kind = asType<string> d.["kind"]
    Thumbnails = 
      asEnumerable d.["thumbnails"]
      |> Seq.map parseVidThumb
      |> Seq.toList
    Id = asType<string> d.["id"] }

let internal parseAlbumRef (o:PyObject) =
  let d = ofPyDict o
  debugDict "parseAlbumRef" [ "kind"; "autogen"; "url"; "aspectRatio" ] d
  { Kind = asType<string> d.["kind"]
    Autogen = asType<bool> d.["autogen"]
    Url = asType<string> d.["url"]
    AspectRatio = asType<string> d.["aspectRatio"] }

let tryGet key (d:System.Collections.Generic.IReadOnlyDictionary<string,PyObject>) =  
  match d.TryGetValue key with
  | true, vla -> Some vla
  | _ -> None

let internal trackDebugList = 
  [ "album"; "explicitType"; "kind"; "year"; "storeId"; "artist"; "albumArtRef"; "title";
    "nid"; "estimatedSize"; "albumId"; "artistId"; "albumArtist"; "durationMillis"; 
    "composer"; "genre"; "trackNumber"; "discNumber"; "trackAvailableForPurchase";
    "trackAvailableForSubscription"; "trackType"; "albumAvailableForPurchase"; "rating";
    "playCount"; "primaryVideo"; "artistArtRef"; "lastRatingChangeTimestamp" ]
let internal parseTrackInfo (d : System.Collections.Generic.IReadOnlyDictionary<string, PyObject>) =
  { Album = asType<string> d.["album"]
    ExplicitType = asType<string> d.["explicitType"]
    Kind = asType<string> d.["kind"]
    PrimaryVideo = tryGet "primaryVideo" d |> Option.map parseVidRef
    PlayCount = tryGet "playCount" d |> Option.map asType<int>
    LastRatingChangeTimestamp = tryGet "lastRatingChangeTimestamp" d |> Option.map (asType<string> >> toTimestamp)
    Rating = 
      match d.TryGetValue "rating" with
      | true, rating ->
        let s = asType<string> rating
        match System.Int32.TryParse s with
        | true, 0 -> Some Rating.NoRating
        | true, 1 -> Some Rating.ThumbDown
        | true, 5 -> Some Rating.ThumbUp
        | true, v -> Some (Rating.Other v)
        | _ -> 
          printf "Could not parse rating '%s'" s
          None
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
    ArtistArtRef = 
      tryGet "artistArtRef" d
      |> Option.map (asEnumerable >> Seq.map parseAlbumRef >> Seq.toList)
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
    Genre = tryGet "genre" d |> Option.map asType<string>
    TrackNumber = asType<int> d.["trackNumber"]
    DiscNumber = asType<int> d.["discNumber"]
    TrackAvailableForPurchase = asType<bool> d.["trackAvailableForPurchase"]
    TrackAvailableForSubscription = asType<bool> d.["trackAvailableForSubscription"]
    TrackType = asType<string> d.["trackType"]
    AlbumAvailableForPurchase =  asType<bool> d.["albumAvailableForPurchase"] }

let songAttributes =
  [ "comment"; "rating"; "creationTimestamp"; "id"; "totalDiscCount"; "recentTimestamp";
    "deleted"; "totalTrackCount"; "beatsPerMinute"; "lastModifiedTimestamp"; 
    "clientId" ]
let internal parseSongInfo (d : System.Collections.Generic.IReadOnlyDictionary<string, PyObject>) =
  { Comment = asType<string> d.["comment"]
    Track = parseTrackInfo d
    CreationTimestamp = asType<string> d.["creationTimestamp"] |> toTimestamp
    Id = asType<string> d.["id"]
    TotalDiscCount = asType<int> d.["totalDiscCount"]
    RecentTimestamp = asType<string> d.["recentTimestamp"] |> toTimestamp
    Deleted = asType<bool> d.["deleted"]
    TotalTrackCount = asType<int> d.["totalTrackCount"]
    BeatsPerMinute = asType<int> d.["beatsPerMinute"]
    LastModifiedTimestamp = asType<string> d.["lastModifiedTimestamp"] |> toTimestamp
    ClientId = asType<string> d.["clientId"] }

let internal parseSongInfoPy (o :PyObject) =
  let d = ofPyDict o
  debugDict "parseSongInfoPy" (songAttributes@trackDebugList) d
  parseSongInfo d

let getAllSongs { MobileClientHandle = mobileClient } (includeDeleted:bool) =
  python {
    let trackInfo:PyObject = mobileClient?get_all_songs(false, includeDeleted)
    return 
      asEnumerable trackInfo
      |> Seq.map (parseSongInfoPy)
      |> Seq.toList
  }
  
let getAllSongsLazy { MobileClientHandle = mobileClient } (includeDeleted:bool) =
  pythonSeq {
    let trackInfo:PyObject = mobileClient?get_all_songs(true, includeDeleted)
    for item in asEnumerable trackInfo do
      yield parseSongInfoPy item
  }


let getStreamUrl { MobileClientHandle = mobileClient } (deviceId: string option) (quality: StreamQuality option) (trackId:string) =
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

let rate { MobileClientHandle = mobileClient } (trackInfo : TrackInfo) (rating:Rating) =
  python {
    let song:PyObject = mobileClient?get_track_info(trackInfo.StoreId)
    let ratingStr =
      match rating with
      | NoRating -> "0"
      | ThumbDown -> "1"
      | ThumbUp -> "5"
      | Other i -> string i
    song.SetItem("rating", ratingStr.ToPython())
    let ret = mobileClient?change_song_metadata(song)
    return 
      asEnumerable ret
      |> Seq.map (asType<string>)
      |> Seq.toList
  }

let deleteSongs { MobileClientHandle = mobileClient } (ids: string list) =
  python {
    let l = new PyList()
    for item in ids do
      l.Append(item.ToPython())
    let ret = mobileClient?delete_songs(l)
    return 
      asEnumerable ret
      |> Seq.map (asType<string>)
      |> Seq.toList
  }
let parseTrackInfoPy (o:PyObject) =
  let d = ofPyDict o
  debugDict "parseTrackInfoPy" trackDebugList d
  parseTrackInfo d
  
let getPromotedSongs { MobileClientHandle = mobileClient } =
  python {
    let ret = mobileClient?get_promoted_songs()
    return 
      asEnumerable ret
      |> Seq.map (parseTrackInfoPy)
      |> Seq.toList
  }
  
let incrementSongPlaycount { MobileClientHandle = mobileClient } (songId:string) (plays:uint32) (dateTime:System.DateTime option) =
  python {
    let ret : PyObject = mobileClient?increment_song_playcount(songId, plays)
    return asType<string> ret
  }

let addStoreTrack { MobileClientHandle = mobileClient } (songId:string) =
  python {
    let ret : PyObject = mobileClient?add_store_track(songId)
    return asType<string> ret
  }


let internal parsePlaylistEntry (o:PyObject) =
  let d = ofPyDict o
  debugDict "parsePlaylistEntry"
    ["kind"; "deleted"; "trackId"; "lastModifiedTimestamp"; "id"; "track";
     "playlistId"; "absolutePosition"; "source"; "creationTimestamp"; "clientId" ] d
  { PlaylistId = 
      match d.TryGetValue "playlistId" with
      | true, plId -> Some (asType<string> plId)
      | _ -> None
    Track =
      match d.TryGetValue "track" with
      | true, track -> Some (parseTrackInfoPy track)
      | _ -> None
    ClientId = 
      match d.TryGetValue "clientId" with
      | true, cl -> Some (asType<string> cl)
      | _ -> None
    LastModifiedTimestamp = asType<string> d.["lastModifiedTimestamp"] |> toTimestamp
    TrackId = asType<string> d.["trackId"]
    Deleted = asType<bool> d.["deleted"]
    AbsolutePosition = asType<string> d.["absolutePosition"]
    Source = asType<string> d.["source"]
    Id = asType<string> d.["id"]
    Kind = asType<string> d.["kind"]
    CreationTimestamp = asType<string> d.["creationTimestamp"] |> toTimestamp
  }

let internal parsePlaylist (o:PyObject) =
  let d = ofPyDict o
  debugDict "parsePlayList"
    ["accessControlled"; "creationTimestamp"; "deleted"; "id"; "kind"; "lastModifiedTimestamp";
     "name"; "ownerName"; "ownerProfilePhotoUrl"; "recentTimestamp"; "shareToken"; "type"; "tracks"] d
  { Tracks =
      match d.TryGetValue "tracks" with
      | true, tracks ->
        asEnumerable tracks
        |> Seq.map parsePlaylistEntry
        |> Seq.toList
        |> Some
      | _ -> None
    Type =
      match d.TryGetValue "type" with
      | true, v ->
        match asType<string> v with
        | "USER_GENERATED" -> UserPlaylist
        | "MAGIC" -> MagicPlaylist
        | "SHARED" -> SharedPlaylist
        | u ->
          printfn "Unknown playlist type '%s'" u
          UnknownPlaylistType
      | _ -> UnknownPlaylistType
    Description = tryGet "description" d |> Option.map asType<string>
    ClientId = tryGet "clientId" d |> Option.map asType<string>
    AccessControlled = asType<bool> d.["accessControlled"]
    CreationTimestamp = asType<string> d.["creationTimestamp"] |> toTimestamp
    Deleted = asType<bool> d.["deleted"]
    Id = asType<string> d.["id"]
    Kind = asType<string> d.["kind"]
    LastModifiedTimestamp = asType<string> d.["lastModifiedTimestamp"] |> toTimestamp
    Name = asType<string> d.["name"]
    OwnerName = asType<string> d.["ownerName"]
    OwnerProfilePhotoUrl = asType<string> d.["ownerProfilePhotoUrl"]
    RecentTimestamp = asType<string> d.["recentTimestamp"] |> toTimestamp
    ShareToken = asType<string> d.["shareToken"] }

let getAllPlaylists { MobileClientHandle = mobileClient } (includeDeleted:bool) =
  python {
    let trackInfo:PyObject = mobileClient?get_all_playlists(false, includeDeleted)
    return 
      asEnumerable trackInfo
      |> Seq.map parsePlaylist
      |> Seq.toList
  }


let getAllPlaylistsLazy { MobileClientHandle = mobileClient } (includeDeleted:bool)  =
  pythonSeq {
    let trackInfo:PyObject = mobileClient?get_all_playlists(true, includeDeleted)
    for item in asEnumerable trackInfo do
      yield parsePlaylist item
  }

let getAllUserPlayListContents { MobileClientHandle = mobileClient } =
  python {
    let trackInfo:PyObject = mobileClient?get_all_user_playlist_contents()
    return 
      asEnumerable trackInfo
      |> Seq.map parsePlaylist
      |> Seq.toList
  }

let getSharedPlaylistContents { MobileClientHandle = mobileClient } (shareToken:string) =
  python {
    let trackInfo:PyObject = mobileClient?get_shared_playlist_contents(shareToken)
    return 
      asEnumerable trackInfo
      |> Seq.map parsePlaylistEntry
      |> Seq.toList
  }

let createPlaylist { MobileClientHandle = mobileClient } (name:string) (description:string option) (isPublic:bool option) =
  let isPublic = defaultArg isPublic false
  let description = defaultArg description null
  python {
    let trackInfo:PyObject = mobileClient?create_playlist(name, description, isPublic)
    return asType<string> trackInfo
  }

let deletePlaylist { MobileClientHandle = mobileClient } (playlistId:string) =
  python {
    let trackInfo:PyObject = mobileClient?delete_playlist(playlistId)
    return asType<string> trackInfo
  }


let editPlaylist { MobileClientHandle = mobileClient } (playlistId:string) (name:string option) (description:string option) (isPublic:bool option) =
  python {
    let name = defaultArg name null
    let description = defaultArg description null
    
    let trackInfo:PyObject =
      match isPublic with
      | Some v -> mobileClient?edit_playlist(playlistId, name, description, v)
      | _ -> mobileClient?edit_playlist(playlistId, name, description)

    return asType<string> trackInfo
  }

let addSongsToPlaylist { MobileClientHandle = mobileClient } (playlistId:string) (songs : string list) =
  python {
    let l = new PyList()
    for item in songs do
      l.Append(item.ToPython())
    let entryIds = mobileClient?add_songs_to_playlist(playlistId, l)
    return
      asEnumerable entryIds
      |> Seq.map asType<string>
      |> Seq.toList
  }

let removeEntriesFromPlaylist { MobileClientHandle = mobileClient } (entries : string list) =
  python {
    let l = new PyList()
    for item in entries do
      l.Append(item.ToPython())
    let entryIds = mobileClient?remove_entries_from_playlist(l)
    return
      asEnumerable entryIds
      |> Seq.map asType<string>
      |> Seq.toList
  }

//let reorderPlaylistEntry  { MobileClientHandle = mobileClient }

let getTrackInfo { MobileClientHandle = mobileClient } (trackId:string)  =
  python {
    let trackInfo:PyObject = mobileClient?get_track_info(trackId)
    return parseTrackInfoPy trackInfo
  }

let toSongInterface mb =
  { new ISongManagement with
      member x.GetAllSongs i = getAllSongs mb i |> runInPython
      member x.GetAllSongsLazy i =
         Unchecked.defaultof<_>
         // getAllSongsLazy mb i
      member x.GetStreamUrl (t,d,q) = getStreamUrl mb d q t |> runInPython
      member x.Rate(t, r) = rate mb t r |> runInPython
      member x.DeleteSongs ids = deleteSongs mb ids |> runInPython
      member x.GetPromotedSongs () = getPromotedSongs mb |> runInPython
      member x.IncrementSongPlaycount (s, p, d) = incrementSongPlaycount mb s p d |> runInPython
      member x.AddStoreTrack s = addStoreTrack mb s |> runInPython
      member x.GetTrackInfo t = getTrackInfo mb t |> runInPython
  }
let toPlaylistInterface mb =
  { new IPlaylistManagement with
      member x.GetAllPlaylists i = getAllPlaylists mb i |> runInPython
      member x.GetAllPlaylistsLazy i =
        Unchecked.defaultof<_>
      member x.AllUserPlaylistContents = getAllUserPlayListContents mb |> runInPython
      member x.GetSharedPlaylistContents t = getSharedPlaylistContents mb t |> runInPython
      member x.CreatePlaylist (n, d, p) = createPlaylist mb n d p |> runInPython
      member x.DeletePlaylist id = deletePlaylist mb id |> runInPython
      member x.EditPlaylist(i, n, d, p) = editPlaylist mb i n d p |> runInPython
      member x.AddSongsToPlaylist (i, s) = addSongsToPlaylist mb i s |> runInPython
      member x.RemoveEntriesFromPlaylist (e) = removeEntriesFromPlaylist mb e |> runInPython
  }
let toMobileInterface mb =
  let songs = toSongInterface mb
  let playlists = toPlaylistInterface mb
  { new IMobileClient with
      member x.Login (u,p,d,c) = 
        let d = defaultArg d "dummy"
        login mb c u p d |> runInPython |> ignore
      member x.Logout () = logout mb |> runInPython|>ignore
      member x.IsAuthenticated = isAuthenticated mb |> runInPython
      member x.Locale 
        with get () = locale mb |> runInPython
        and set v = setLocale v mb |> runInPython
      member x.IsSubscribed = isSubscribed mb |> runInPython
      member x.DeleteSubscribedCache () = deleteSubscribed mb |> runInPython
      member x.RegisteredDevices = getRegisteredDevices mb |> runInPython
      member x.DeauthorizeDevice d = deauthorizeDevice mb d |> runInPython |> ignore
      member x.Songs = songs
      member x.Playlists = playlists }

let createMobileClientAndInitialize pythonDir  (debugLogging : bool) (validate:bool) (verifySsl : bool) =
  initialize pythonDir
  let mb = createMobileClient debugLogging validate verifySsl |> runInPython
  toMobileInterface mb