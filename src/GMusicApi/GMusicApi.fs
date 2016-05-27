module GMusicAPI.GMusicAPI
open GMusicAPI.PythonInterop
open Python.Runtime
open FSharp.Interop.Dynamic

/// Call this when you application starts
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

type Timestamp = { Raw : string }
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

/// Authenticates the Mobileclient. Returns `true` on success, `false` on failure.
///
/// - `locale`: ICU locale used to localize certain responses. This must be a locale supported by Android. Defaults to 'en_US'.
/// - `username`: eg 'test@gmail.com' or just 'test'.
/// - `password`: password or app-specific password for 2-factor users. This is not stored locally, and is sent securely over SSL.
/// - `deviceId`: 16 hex digits, eg '1234567890abcdef'.
///   Pass Mobileclient.FROM_MAC_ADDRESS instead to attempt to use this machine’s MAC address as an android id. Use this at your own risk: the id will be a non-standard 12 characters, but appears to work fine in testing. If a valid MAC address cannot be determined on this machine (which is often the case when running on a VPS), raise OSError.
/// - `mobileClient`: a Mobileclient instance as created by `createMobileClient`
let login { MobileClientHandle = mobileClient } (locale:System.Globalization.CultureInfo option) (username:string) (password:string) (deviceId:string) =
  let locale = defaultArg locale (System.Globalization.CultureInfo.GetCultureInfo("en-US"))
  python {
    let (res : PyObject) = mobileClient?login(username, password, deviceId, locale.Name)
    return asType<bool> res
  }

/// Forgets local authentication and cached properties in this Api instance. Returns `true` on success.
/// - `mobileClient`: a Mobileclient instance as created by `createMobileClient`
let logout { MobileClientHandle = mobileClient } =
  python {
    let (res : PyObject) = mobileClient?logout()
    return asType<bool> res
  }

/// Returns `true` if the Api can make an authenticated request.
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

/// Sets the current locale.
let setLocale (newLocale:System.Globalization.CultureInfo) { MobileClientHandle = mobileClient } =
  python {
    mobileClient?locale <- newLocale.Name
  }

/// Returns the subscription status of the Google Music account.
/// Result is cached with a TTL of 10 minutes. To get live status before the TTL is up, use `deleteSubscribed` before calling this member.
let isSubscribed { MobileClientHandle = mobileClient } =
  python {
    let res : PyObject = mobileClient?is_subscribed
    return asType<bool> res
  }
/// Deletes the currently cached isSubscribed status.
let deleteSubscribed { MobileClientHandle = mobileClient } =
  python {
    mobileClient.DelAttr("is_subscribed")
  }

/// A registered device.
type RegisteredDevice =
  { Kind : string
    FriendlyName : string option
    Id : string
    LastAccessedTimeMs : string
    Type : string
    SmartPhone : bool }

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

/// Returns a list of registered devices associated with the account.
/// Performing the Musicmanager OAuth flow will register a device of type 'DESKTOP_APP'.
/// Installing the Android or iOS Google Music app and logging into it will register a device of type 'ANDROID' or 'IOS' respectively, which is required for streaming with the Mobileclient.
let getRegisteredDevices { MobileClientHandle = mobileClient } =
  python {
    let (rawDevicesList:PyObject) = mobileClient?get_registered_devices()
    return
      asEnumerable rawDevicesList
      |> Seq.map (parseRegisteredDevice)
      |> Seq.toList
  }

/// Deauthorize a registered device.
/// Returns True on success, False on failure.
/// - deviceId: A mobile device id as a string. Android ids are 16 characters with ‘0x’ prepended, iOS ids are uuids with ‘ios:’ prepended, while desktop ids are in the form of a MAC address.
///   Providing an invalid or unregistered device id will result in a 400 HTTP error.
/// Google limits the number of device deauthorizations to 4 per year. Attempts to deauthorize a device when that limit is reached results in a 403 HTTP error with: X-Rejected-Reason: TOO_MANY_DEAUTHORIZATIONS.
let deauthorizeDevice { MobileClientHandle = mobileClient } (deviceId:string) =
  python {
    let res : PyObject = mobileClient?deauthorize_device(deviceId)
    return asType<bool> res
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

type Rating =
  | NoRating
  | ThumbDown
  | ThumbUp
  | Other of int
let tryGet key (d:System.Collections.Generic.IReadOnlyDictionary<string,PyObject>) =  
  match d.TryGetValue key with
  | true, vla -> Some vla
  | _ -> None
type TrackInfo =
  { Album : string
    ExplicitType : string
    Kind : string
    PrimaryVideo : VidRef option
    StoreId : string
    Artist : string
    AlbumArtRef : ArtRef list
    ArtistArtRef : ArtRef list option
    Title : string
    NId : string
    EstimatedSize : string
    Rating : Rating option
    Year : int option
    AlbumId : string
    ArtistId : string list
    AlbumArtist : string
    DurationMillis : string
    Composer : string
    Genre : string
    TrackNumber : int
    DiscNumber : int
    PlayCount : int option
    TrackAvailableForPurchase : bool
    TrackAvailableForSubscription : bool
    TrackType : string
    AlbumAvailableForPurchase : bool
    LastRatingChangeTimestamp : Timestamp option }
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
    Genre = asType<string> d.["genre"]
    TrackNumber = asType<int> d.["trackNumber"]
    DiscNumber = asType<int> d.["discNumber"]
    TrackAvailableForPurchase = asType<bool> d.["trackAvailableForPurchase"]
    TrackAvailableForSubscription = asType<bool> d.["trackAvailableForSubscription"]
    TrackType = asType<string> d.["trackType"]
    AlbumAvailableForPurchase =  asType<bool> d.["albumAvailableForPurchase"] }

/// Songs are uniquely referred to within a library with a track id in uuid format.
/// Store tracks also have track ids, but they are in a different format than library track ids. song_id.startswith('T') is always True for store track ids and False for library track ids.
/// Adding a store track to a library will yield a normal song id.
/// Store track ids can be used in most places that normal song ids can (e.g. playlist addition or streaming). Note that sometimes they are stored under the 'nid' key, not the 'id' key.
type SongInfo =
  { Comment : string
    Track : TrackInfo
    CreationTimestamp : Timestamp
    Id : string
    TotalDiscCount : int
    RecentTimestamp : Timestamp
    Deleted : bool 
    TotalTrackCount : int 
    BeatsPerMinute : int
    LastModifiedTimestamp : Timestamp 
    ClientId : string }
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

/// Returns a list of dictionaries that each represent a song.
/// - `include_deleted`: if True, include tracks that have been deleted in the past.
let getAllSongs { MobileClientHandle = mobileClient } (includeDeleted:bool) =
  python {
    let trackInfo:PyObject = mobileClient?get_all_songs(false, includeDeleted)
    return 
      asEnumerable trackInfo
      |> Seq.map (parseSongInfoPy)
      |> Seq.toList
  }
  
/// Returns a list of dictionaries that each represent a song.
/// - `include_deleted`: if True, include tracks that have been deleted in the past.
let getAllSongsLazy { MobileClientHandle = mobileClient } (includeDeleted:bool) =
  pythonSeq {
    let trackInfo:PyObject = mobileClient?get_all_songs(true, includeDeleted)
    for item in asEnumerable trackInfo do
      yield parseSongInfoPy item
  }

type StreamQuality =
  | HighQuality
  | MediumQuality
  | LowQuality

/// Returns a url that will point to an mp3 file.
/// - `song_id`: a single song id
/// - `device_id`: (optional) defaults to android_id from login.
///   
///   Otherwise, provide a mobile device id as a string. Android device ids are 16 characters, while iOS ids are uuids with ‘ios:’ prepended.
///   
///   If you have already used Google Music on a mobile device, Mobileclient.get_registered_devices will provide at least one working id. Omit '0x' from the start of the string if present.
///   
///   Registered computer ids (a MAC address) will not be accepted and will 403.
///   
///   Providing an unregistered mobile device id will register it to your account, subject to Google’s device limits. Registering a device id that you do not own is likely a violation of the TOS.
/// - `quality`: (optional) stream bits per second quality One of three possible values, hi: 320kbps, med: 160kbps, low: 128kbps. The default is hi
///
/// When handling the resulting url, keep in mind that:
///  - you will likely need to handle redirects
///  - the url expires after a minute
///  - only one IP can be streaming music at once. This can result in an http 403 with X-Rejected-Reason: ANOTHER_STREAM_BEING_PLAYED.
/// The file will not contain metadata. Use Webclient.get_song_download_info or Musicmanager.download_song to download files with metadata.
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

/// Changes the metadata of tracks. Returns a list of the song ids changed.
/// You can also use this to rate store tracks that aren’t in your library
let rate { MobileClientHandle = mobileClient } (trackInfo : TrackInfo) (rating:Rating) =
  python {
    let song:PyObject = mobileClient?get_track_info(trackInfo.NId)
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

/// Deletes songs from the library. Returns a list of deleted song ids.
/// - song_ids – a list of song ids, or a single song id.
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
  
/// Promoted tracks are determined in an unknown fashion, but positively-rated library tracks are common.
let getPromotedSongs { MobileClientHandle = mobileClient } =
  python {
    let ret = mobileClient?get_promoted_songs()
    return 
      asEnumerable ret
      |> Seq.map (parseTrackInfoPy)
      |> Seq.toList
  }
  
/// Increments a song’s playcount and returns its song id.
/// - `song_id`: a song id. Providing the id of a store track that has been added to the library will not increment the corresponding library song’s playcount. To do this, use the ‘id’ field (which looks like a uuid and doesn’t begin with ‘T’), not the ‘nid’ field.
/// - `plays`: (optional) positive number of plays to increment by. The default is 1.
/// - `playtime`: (optional) a datetime.datetime of the time the song was played. It will default to the time of the call.
let incrementSongPlaycount { MobileClientHandle = mobileClient } (songId:string) (plays:uint32) (dateTime:System.DateTime option) =
  python {
    let ret : PyObject = mobileClient?increment_song_playcount(songId, plays)
    return asType<string> ret
  }

/// Adds a store track to the library
/// Returns the library track id of added store track.
/// - `store_song_id`: store song id
let addStoreTrack { MobileClientHandle = mobileClient } (songId:string) =
  python {
    let ret : PyObject = mobileClient?add_store_track(songId)
    return asType<string> ret
  }


type PlaylistType =
  | SharedPlaylist
  | MagicPlaylist
  | UserPlaylist
  | UnknownPlaylistType
type PlaylistEntry =
  { Kind : string
    Deleted : bool
    TrackId : string
    LastModifiedTimestamp : Timestamp
    PlaylistId : string option
    AbsolutePosition : string
    Source : string
    ClientId : string option
    Track : TrackInfo option
    CreationTimestamp : Timestamp
    Id : string }
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
/// Like songs, playlists have unique ids within a library. However, their names do not need to be unique.
/// The tracks making up a playlist are referred to as ‘playlist entries’, and have unique entry ids within the entire library (not just their containing playlist).
type Playlist =
  { AccessControlled : bool
    CreationTimestamp : Timestamp
    Type : PlaylistType
    Deleted : bool
    Id : string
    Kind : string
    LastModifiedTimestamp : Timestamp
    Name : string
    OwnerName : string
    OwnerProfilePhotoUrl : string
    RecentTimestamp : Timestamp
    ShareToken : string
    Tracks : PlaylistEntry list option }

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

/// Retrieves the contents of all user-created playlists – the Mobileclient does not support retrieving only the contents of one playlist.
/// This will not return results for public playlists that the user is subscribed to; use getSharedPlaylistContents() instead.
let getAllUserPlayListContents { MobileClientHandle = mobileClient } =
  python {
    let trackInfo:PyObject = mobileClient?get_all_user_playlist_contents()
    return 
      asEnumerable trackInfo
      |> Seq.map parsePlaylist
      |> Seq.toList
  }

/// Retrieves the contents of a public playlist.
/// - share_token: from playlist['shareToken'], or a playlist share url (https://play.google.com/music/playlist/<token>).
///   Note that tokens from urls will need to be url-decoded, eg AM...%3D%3D becomes AM...==.
let getSharedPlaylistContents { MobileClientHandle = mobileClient } (shareToken:string) =
  python {
    let trackInfo:PyObject = mobileClient?get_shared_playlist_contents(shareToken)
    return 
      asEnumerable trackInfo
      |> Seq.map parsePlaylistEntry
      |> Seq.toList
  }

/// Creates a new empty playlist and returns its id.
let createPlaylist { MobileClientHandle = mobileClient } (name:string) (description:string) (isPublic:bool) =
  python {
    let trackInfo:PyObject = mobileClient?create_playlist(name, description, isPublic)
    return asType<string> trackInfo
  }

/// Deletes a playlist and returns its id.
let deletePlaylist { MobileClientHandle = mobileClient } (playlistId:string) =
  python {
    let trackInfo:PyObject = mobileClient?delete_playlist(playlistId)
    return asType<string> trackInfo
  }

/// Changes the name of a playlist and returns its id.
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

/// Appends songs to the end of a playlist. Returns a list of playlist entry ids that were added.
/// Playlists have a maximum size of 1000 songs. Calls may fail before that point (presumably) due to an error on Google’s end (see #239).
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

/// Removes specific entries from a playlist. Returns a list of entry ids that were removed.
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

/// Retrieves information about a store track.
let getTrackInfo { MobileClientHandle = mobileClient } (trackId:string)  =
  python {
    let trackInfo:PyObject = mobileClient?get_track_info(trackId)
    return parseTrackInfoPy trackInfo
  }