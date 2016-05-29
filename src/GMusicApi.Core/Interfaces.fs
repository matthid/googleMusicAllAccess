namespace GMusicApi.Core

open System
open System.Globalization


[<System.Serializable>]
type GMusicApiException =
    inherit System.Exception
    new (msg:string, inner:System.Exception) = {
      inherit System.Exception(msg, inner) }
    new (info:System.Runtime.Serialization.SerializationInfo, context:System.Runtime.Serialization.StreamingContext) = {
        inherit System.Exception(info, context) }
    override x.GetObjectData(info, c) =
      base.GetObjectData(info, c)
      
[<System.Serializable>]
type ForbiddenGMusicApiException =
    inherit GMusicApiException
    new (msg:string, inner:System.Exception) = {
      inherit GMusicApiException(msg, inner) }
    new (info:System.Runtime.Serialization.SerializationInfo, context:System.Runtime.Serialization.StreamingContext) = {
        inherit GMusicApiException(info, context) }
    override x.GetObjectData(info, c) =
      base.GetObjectData(info, c)

type ISongManagement =
  /// Returns a list of dictionaries that each represent a song.
  /// - `include_deleted`: if True, include tracks that have been deleted in the past.
  abstract GetAllSongs : includeDeleted:bool -> SongInfo list
  /// Returns a list of dictionaries that each represent a song.
  /// - `include_deleted`: if True, include tracks that have been deleted in the past.
  abstract GetAllSongsLazy : includeDeleted:bool -> SongInfo seq
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
  abstract GetStreamUrl : trackId:string * ?deviceId:string * ?quality :  StreamQuality -> string
  /// Changes the metadata of tracks. Returns a list of the song ids changed.
  /// You can also use this to rate store tracks that aren’t in your library
  abstract Rate : track:TrackInfo * rating:Rating -> string list
  /// Deletes songs from the library. Returns a list of deleted song ids.
  /// - song_ids – a list of song ids, or a single song id.
  abstract DeleteSongs : ids:string list -> string list
  /// Promoted tracks are determined in an unknown fashion, but positively-rated library tracks are common.
  abstract GetPromotedSongs : unit -> TrackInfo list
  /// Increments a song’s playcount and returns its song id.
  /// - `song_id`: a song id. Providing the id of a store track that has been added to the library will not increment the corresponding library song’s playcount. To do this, use the ‘id’ field (which looks like a uuid and doesn’t begin with ‘T’), not the ‘nid’ field.
  /// - `plays`: (optional) positive number of plays to increment by. The default is 1.
  /// - `playtime`: (optional) a datetime.datetime of the time the song was played. It will default to the time of the call.
  abstract IncrementSongPlaycount : songId : string * plays : uint32 * ?dateTime : DateTime -> string
    /// Adds a store track to the library
  /// Returns the library track id of added store track.
  /// - `store_song_id`: store song id
  abstract AddStoreTrack : songId : string -> string
  /// Retrieves information about a store track.
  abstract GetTrackInfo : trackId : string -> TrackInfo

type IPlaylistManagement =
  abstract GetAllPlaylists : includeDeleted : bool -> Playlist list
  abstract GetAllPlaylistsLazy : includeDeleted : bool -> Playlist seq
  /// Retrieves the contents of all user-created playlists – the Mobileclient does not support retrieving only the contents of one playlist.
  /// This will not return results for public playlists that the user is subscribed to; use getSharedPlaylistContents() instead.
  abstract AllUserPlaylistContents : Playlist list with get
  /// Retrieves the contents of a public playlist.
  /// - share_token: from playlist['shareToken'], or a playlist share url (https://play.google.com/music/playlist/<token>).
  ///   Note that tokens from urls will need to be url-decoded, eg AM...%3D%3D becomes AM...==.
  abstract GetSharedPlaylistContents : shareToken:string ->  PlaylistEntry list
  /// Creates a new empty playlist and returns its id.
  abstract CreatePlaylist : name:string * ?description:string * ?isPublic:bool -> string
  /// Deletes a playlist and returns its id.
  abstract DeletePlaylist : id:string -> string
  /// Changes the name of a playlist and returns its id.
  abstract EditPlaylist : id:string * ?name:string * ?description:string * ?isPublic:bool -> string
  /// Appends songs to the end of a playlist. Returns a list of playlist entry ids that were added.
  /// Playlists have a maximum size of 1000 songs. Calls may fail before that point (presumably) due to an error on Google’s end (see #239).
  abstract AddSongsToPlaylist : playlistId:string * songs: string list -> string list
  /// Removes specific entries from a playlist. Returns a list of entry ids that were removed.
  abstract RemoveEntriesFromPlaylist : entries:string list -> string list

type IMobileClient =
  /// Authenticates the Mobileclient. Returns `true` on success, `false` on failure.
  ///
  /// - `locale`: ICU locale used to localize certain responses. This must be a locale supported by Android. Defaults to 'en_US'.
  /// - `username`: eg 'test@gmail.com' or just 'test'.
  /// - `password`: password or app-specific password for 2-factor users. This is not stored locally, and is sent securely over SSL.
  /// - `deviceId`: 16 hex digits, eg '1234567890abcdef'.
  ///   Pass Mobileclient.FROM_MAC_ADDRESS instead to attempt to use this machine’s MAC address as an android id. Use this at your own risk: the id will be a non-standard 12 characters, but appears to work fine in testing. If a valid MAC address cannot be determined on this machine (which is often the case when running on a VPS), raise OSError.
  abstract Login : username : string * password : string * ?deviceId : string * ?cultureInfo : (CultureInfo) -> unit
  /// Forgets local authentication and cached properties in this Api instance. Returns `true` on success.
  abstract Logout : unit -> unit
  /// Returns `true` if the Api can make an authenticated request.
  abstract IsAuthenticated : bool with get
  /// Sets the current locale.
  abstract Locale : CultureInfo with get, set
  /// Returns the subscription status of the Google Music account.
  /// Result is cached with a TTL of 10 minutes. To get live status before the TTL is up, use `deleteSubscribed` before calling this member.
  abstract IsSubscribed : bool with get
  /// Deletes the currently cached isSubscribed status.
  abstract DeleteSubscribedCache : unit -> unit
  /// Returns a list of registered devices associated with the account.
  /// Performing the Musicmanager OAuth flow will register a device of type 'DESKTOP_APP'.
  /// Installing the Android or iOS Google Music app and logging into it will register a device of type 'ANDROID' or 'IOS' respectively, which is required for streaming with the Mobileclient.
  abstract RegisteredDevices : RegisteredDevice list with get
  /// Deauthorize a registered device.
  /// Returns True on success, False on failure.
  /// - deviceId: A mobile device id as a string. Android ids are 16 characters with ‘0x’ prepended, iOS ids are uuids with ‘ios:’ prepended, while desktop ids are in the form of a MAC address.
  ///   Providing an invalid or unregistered device id will result in a 400 HTTP error.
  /// Google limits the number of device deauthorizations to 4 per year. Attempts to deauthorize a device when that limit is reached results in a 403 HTTP error with: X-Rejected-Reason: TOO_MANY_DEAUTHORIZATIONS.
  abstract DeauthorizeDevice : deviceId : string -> unit
  abstract Songs : ISongManagement
  abstract Playlists : IPlaylistManagement
