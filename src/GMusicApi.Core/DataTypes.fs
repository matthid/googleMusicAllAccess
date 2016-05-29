namespace GMusicApi.Core

open System
type Timestamp = { Raw : string }


/// A registered device.
type RegisteredDevice =
  { Kind : string
    FriendlyName : string option
    Id : string
    LastAccessedTimeMs : string
    Type : string
    SmartPhone : bool }

    
type VidThumb =
  { Width : int
    Height : int
    Url : string }

type VidRef =
  { Kind : string
    Thumbnails : VidThumb list
    /// Youtube Id https://www.youtube.com/watch?v=%s for Url
    Id : string }

type ArtRef =
  { Kind : string
    Autogen : bool
    Url : string
    AspectRatio : string }


type Rating =
  | NoRating
  | ThumbDown
  | ThumbUp
  | Other of int

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


type StreamQuality =
  | HighQuality
  | MediumQuality
  | LowQuality


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

