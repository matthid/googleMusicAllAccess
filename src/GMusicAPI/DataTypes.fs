namespace GMusicAPI

open FSharp.Data

type RegisteredDevices = JsonProvider<"""
[ { "lastAccessedTimeMs": "1000000000000", 
    "friendlyName": "iPhone", 
    "smartPhone": false, 
    "id": "ios:00AA000-A000-000A-A0A0-00AA00000000", 
    "type": "IOS", 
    "kind": "sj#devicemanagementinfo" }, 
  { "lastAccessedTimeMs": "1000000000000", 
    "kind": "sj#devicemanagementinfo", 
    "friendlyName": "Google LGE Nexus 4", 
    "id": "0xe000000000000000", 
    "type": "ANDROID" }, 
  { "lastAccessedTimeMs": "1000000000000", 
    "kind": "sj#devicemanagementinfo", 
    "friendlyName": "desktop-pcname1", 
    "id": "0000000000000000000000000000000000000000000000000000000000000000", 
    "type": "DESKTOP_APP"},
  { "lastAccessedTimeMs": "1000000000000", 
    "kind": "sj#devicemanagementinfo", 
    "smartPhone": false, 
    "id": "0000000000000000000000000000000000000000000000000000000000000000", 
    "type": "DESKTOP_APP"}]
""">

type TrackInfo = JsonProvider<"""
{ "artistId": ["A00000000000000000000000000"], 
  "explicitType": "2", 
  "kind": "sj#track", 
  "artist": "Artist Name", 
  "albumAvailableForPurchase": false, 
  "albumId": "B00000000000000000000000000", 
  "title": "Track Title", 
  "composer": "", 
  "albumArtist": "Album Artist Name", 
  "trackAvailableForSubscription": true, 
  "estimatedSize": "8000000", 
  "trackType": "7", 
  "nid": "T00000000000000000000000000", 
  "album": "Album Name", 
  "durationMillis": "218000", 
  "trackAvailableForPurchase": true, 
  "albumArtRef": 
    [ { "kind": "sj#imageRef", 
        "autogen": false, 
        "url": "http://lh3.googleusercontent.com/mzfzWVG-g6nG8L12itb3bA8tLk-w0ablXdT6Jo7rV-gLwTI2BGc4lyqFl7YgFVVaROLGRmcI", 
        "aspectRatio": "1" } ], 
  "trackNumber": 1, 
  "year": 2016, 
  "genre": "Alternative/Indie", 
  "storeId": "T00000000000000000000000000", 
  "discNumber": 1 }
""">

