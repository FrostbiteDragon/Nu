﻿// Nu Game Engine.
// Copyright (C) Bryan Edds, 2013-2020.

namespace Nu
open System
open System.Collections.Generic
open System.IO
open SDL2
open Prime
open Nu

/// Song. Currently just used as a phantom type.
type Song = private { __ : unit }

/// Sound. Currently just used as a phantom type.
type Sound = private { __ : unit }

/// Descrides a song.
type [<NoEquality; NoComparison>] SongDescriptor =
    { FadeOutMs : int
      Volume : single
      Song : Song AssetTag }

/// Describes a sound.
type [<NoEquality; NoComparison>] SoundDescriptor =
    { Volume : single
      Sound : Sound AssetTag }

/// A message to the audio system.
type [<NoEquality; NoComparison>] AudioMessage =
    | HintAudioPackageUseMessage of string
    | HintAudioPackageDisuseMessage of string
    | PlaySoundMessage of SoundDescriptor
    | PlaySongMessage of SongDescriptor
    | FadeOutSongMessage of int
    | StopSongMessage
    | ReloadAudioAssetsMessage

/// An audio asset used by the audio system.
type [<NoEquality; NoComparison>] AudioAsset =
    | WavAsset of nativeint
    | OggAsset of nativeint

/// The audio player. Represents the audio subsystem of Nu generally.
type AudioPlayer =
    /// The master audio volume.
    abstract MasterAudioVolume : single with get, set
    /// The master sound volume.
    abstract MasterSoundVolume : single with get, set
    /// The master song volume.
    abstract MasterSongVolume : single with get, set
    /// Pop all of the audio messages that have been enqueued.
    abstract PopMessages : unit -> AudioMessage List
    /// Clear all of the audio messages that have been enqueued.
    abstract ClearMessages : unit -> unit
    /// Enqueue a message from an external source.
    abstract EnqueueMessage : AudioMessage -> unit
    /// Get the current optionally-playing song
    abstract CurrentSongOpt : SongDescriptor option
    /// 'Play' the audio system. Must be called once per frame.
    abstract Play : AudioMessage List -> unit

/// The mock implementation of AudioPlayer.
type [<ReferenceEquality; NoComparison>] MockAudioPlayer =
    private
        { MockAudioPlayer : unit }
    
    interface AudioPlayer with
        member audioPlayer.PopMessages () = List ()
        member audioPlayer.ClearMessages () = ()
        member audioPlayer.EnqueueMessage _ = ()
        member audioPlayer.CurrentSongOpt = None
        member audioPlayer.Play _ = ()
        member audioPlayer.MasterAudioVolume with get () = 1.0f and set _ = ()
        member audioPlayer.MasterSoundVolume with get () = 1.0f and set _ = ()
        member audioPlayer.MasterSongVolume with get () = 1.0f and set _ = ()

    static member make () =
        { MockAudioPlayer = () }

/// The SDL implementation of AudioPlayer.
type [<ReferenceEquality; NoComparison>] SdlAudioPlayer =
    private
        { AudioContext : unit // audio context, interestingly, is global. Good luck encapsulating that!
          AudioPackages : AudioAsset Packages
          mutable AudioMessages : AudioMessage List
          mutable MasterAudioVolume : single
          mutable MasterSoundVolume : single
          mutable MasterSongVolume : single
          mutable CurrentSongOpt : SongDescriptor option }

    static member private haltSound () =
        SDL_mixer.Mix_HaltMusic () |> ignore
        let (_, _, _, channelCount) =  SDL_mixer.Mix_QuerySpec ()
        for i in [0 .. channelCount - 1] do
            SDL_mixer.Mix_HaltChannel i |> ignore
    
    static member private tryLoadAudioAsset (asset : obj Asset) =
        match Path.GetExtension asset.FilePath with
        | ".wav" ->
            let wavOpt = SDL_mixer.Mix_LoadWAV asset.FilePath
            if wavOpt <> IntPtr.Zero then Some (asset.AssetTag.AssetName, WavAsset wavOpt)
            else
                let errorMsg = SDL.SDL_GetError ()
                Log.debug ("Could not load wav '" + asset.FilePath + "' due to '" + errorMsg + "'.")
                None
        | ".ogg" ->
            let oggOpt = SDL_mixer.Mix_LoadMUS asset.FilePath
            if oggOpt <> IntPtr.Zero then Some (asset.AssetTag.AssetName, OggAsset oggOpt)
            else
                let errorMsg = SDL.SDL_GetError ()
                Log.debug ("Could not load ogg '" + asset.FilePath + "' due to '" + errorMsg + "'.")
                None
        | extension -> Log.debug ("Could not load audio asset '" + scstring asset + "' due to unknown extension '" + extension + "'."); None

    static member private tryLoadAudioPackage packageName audioPlayer =
        match AssetGraph.tryMakeFromFile Assets.Global.AssetGraphFilePath with
        | Right assetGraph ->
            match AssetGraph.tryLoadAssetsFromPackage true (Some Constants.Associations.Audio) packageName assetGraph with
            | Right assets ->
                let audioAssetOpts = List.map SdlAudioPlayer.tryLoadAudioAsset assets
                let audioAssets = List.definitize audioAssetOpts
                match Dictionary.tryFind packageName audioPlayer.AudioPackages with
                | Some audioAssetDict ->
                    for (key, value) in audioAssets do audioAssetDict.Assign (key, value)
                    audioPlayer.AudioPackages.Assign (packageName, audioAssetDict)
                | None ->
                    let audioAssetDict = dictPlus StringComparer.Ordinal audioAssets
                    audioPlayer.AudioPackages.Assign (packageName, audioAssetDict)
            | Left error ->
                Log.info ("Audio package load failed due to unloadable assets '" + error + "' for package '" + packageName + "'.")
        | Left error ->
            Log.info ("Audio package load failed due to unloadable asset graph due to: '" + error)

    static member private tryFindAudioAsset (assetTag : obj AssetTag) audioPlayer =
        match Dictionary.tryFind assetTag.PackageName audioPlayer.AudioPackages with
        | Some assets -> Dictionary.tryFind assetTag.AssetName assets
        | None ->
            Log.info ("Loading audio package '" + assetTag.PackageName + "' for asset '" + assetTag.AssetName + "' on the fly.")
            SdlAudioPlayer.tryLoadAudioPackage assetTag.PackageName audioPlayer
            match Dictionary.tryFind assetTag.PackageName audioPlayer.AudioPackages with
            | Some assets -> Dictionary.tryFind assetTag.AssetName assets
            | None -> None

    static member private playSong playSongMessage audioPlayer =
        let song = playSongMessage.Song
        match SdlAudioPlayer.tryFindAudioAsset (AssetTag.generalize song) audioPlayer with
        | Some audioAsset ->
            match audioAsset with
            | WavAsset _ ->
                Log.info ("Cannot play wav file as song '" + scstring song + "'.")
            | OggAsset oggAsset ->
                SDL_mixer.Mix_HaltMusic () |> ignore // NOTE: have to stop current song in case it is still fading out, causing the next song not to play
                SDL_mixer.Mix_VolumeMusic (int (playSongMessage.Volume * audioPlayer.MasterAudioVolume * audioPlayer.MasterSongVolume * single SDL_mixer.MIX_MAX_VOLUME)) |> ignore
                SDL_mixer.Mix_FadeInMusic (oggAsset, -1, 50) |> ignore // Mix_PlayMusic seems to sometimes cause audio 'popping' when starting a song, so a very short fade is used instead...
            audioPlayer.CurrentSongOpt <- Some playSongMessage
        | None ->
            Log.info ("PlaySongMessage failed due to unloadable assets for '" + scstring song + "'.")
    
    static member private handleHintAudioPackageUse hintPackageName audioPlayer =
        SdlAudioPlayer.tryLoadAudioPackage hintPackageName audioPlayer
    
    static member private handleHintAudioPackageDisuse hintPackageName audioPlayer =
        match Dictionary.tryFind hintPackageName  audioPlayer.AudioPackages with
        | Some assets ->
            // all sounds / music must be halted because one of them might be playing during unload
            // (which is very bad according to the API docs).
            SdlAudioPlayer.haltSound ()
            for asset in assets do
                match asset.Value with
                | WavAsset wavAsset -> SDL_mixer.Mix_FreeChunk wavAsset
                | OggAsset oggAsset -> SDL_mixer.Mix_FreeMusic oggAsset
            audioPlayer.AudioPackages.Remove hintPackageName |> ignore
        | None -> ()

    static member private handlePlaySound playSoundMessage audioPlayer =
        let sound = playSoundMessage.Sound
        match SdlAudioPlayer.tryFindAudioAsset (AssetTag.generalize sound) audioPlayer with
        | Some audioAsset ->
            match audioAsset with
            | WavAsset wavAsset ->
                SDL_mixer.Mix_VolumeChunk (wavAsset, int (playSoundMessage.Volume * audioPlayer.MasterSoundVolume * single SDL_mixer.MIX_MAX_VOLUME)) |> ignore
                SDL_mixer.Mix_PlayChannel (-1, wavAsset, 0) |> ignore
            | OggAsset _ -> Log.info ("Cannot play ogg file as sound '" + scstring sound + "'.")
        | None ->
            Log.info ("PlaySoundMessage failed due to unloadable assets for '" + scstring sound + "'.")
    
    static member private handlePlaySong playSongMessage audioPlayer =
        SdlAudioPlayer.playSong playSongMessage audioPlayer
    
    static member private handleFadeOutSong timeToFadeOutSongMs =
        if SDL_mixer.Mix_PlayingMusic () = 1 then
            if  timeToFadeOutSongMs <> 0 &&
                SDL_mixer.Mix_FadingMusic () <> SDL_mixer.Mix_Fading.MIX_FADING_OUT then
                SDL_mixer.Mix_FadeOutMusic timeToFadeOutSongMs |> ignore
            else
                SDL_mixer.Mix_HaltMusic () |> ignore
    
    static member private handleStopSong =
        if SDL_mixer.Mix_PlayingMusic () = 1 then
            SDL_mixer.Mix_HaltMusic () |> ignore

    static member private handleReloadAudioAssets audioPlayer =
        let packageNames = audioPlayer.AudioPackages |> Seq.map (fun entry -> entry.Key) |> Array.ofSeq
        audioPlayer.AudioPackages.Clear ()
        for packageName in packageNames do
            SdlAudioPlayer.tryLoadAudioPackage packageName audioPlayer

    static member private handleAudioMessage audioMessage audioPlayer =
        match audioMessage with
        | HintAudioPackageUseMessage hintPackageUse -> SdlAudioPlayer.handleHintAudioPackageUse hintPackageUse audioPlayer
        | HintAudioPackageDisuseMessage hintPackageDisuse -> SdlAudioPlayer.handleHintAudioPackageDisuse hintPackageDisuse audioPlayer
        | PlaySoundMessage playSoundMessage -> SdlAudioPlayer.handlePlaySound playSoundMessage audioPlayer
        | PlaySongMessage playSongMessage -> SdlAudioPlayer.handlePlaySong playSongMessage audioPlayer
        | FadeOutSongMessage timeToFadeSongMs -> SdlAudioPlayer.handleFadeOutSong timeToFadeSongMs
        | StopSongMessage -> SdlAudioPlayer.handleStopSong
        | ReloadAudioAssetsMessage -> SdlAudioPlayer.handleReloadAudioAssets audioPlayer
    
    static member private handleAudioMessages audioMessages audioPlayer =
        for audioMessage in audioMessages do
            SdlAudioPlayer.handleAudioMessage audioMessage audioPlayer

    static member private tryUpdateCurrentSongVolume audioPlayer =
        match audioPlayer.CurrentSongOpt with
        | Some currentSong -> SDL_mixer.Mix_VolumeMusic (int (currentSong.Volume * audioPlayer.MasterAudioVolume * audioPlayer.MasterSongVolume * single SDL_mixer.MIX_MAX_VOLUME)) |> ignore
        | None -> ()
    
    static member private tryUpdateCurrentSong audioPlayer =
        if SDL_mixer.Mix_PlayingMusic () = 0 then
            audioPlayer.CurrentSongOpt <- None
    
    /// Make a NuAudioPlayer.
    static member make () =
        if SDL.SDL_WasInit SDL.SDL_INIT_AUDIO = 0u then
            failwith "Cannot create an AudioPlayer without SDL audio initialized."
        let audioPlayer =
            { AudioContext = ()
              AudioPackages = dictPlus StringComparer.Ordinal []
              AudioMessages = List ()
              MasterAudioVolume = 1.0f
              MasterSoundVolume = 1.0f
              MasterSongVolume = 1.0f
              CurrentSongOpt = None }
        audioPlayer
    
    interface AudioPlayer with
    
        member audioPlayer.MasterAudioVolume
            with get () = audioPlayer.MasterAudioVolume
            and  set volume =
                audioPlayer.MasterAudioVolume <- volume
                SdlAudioPlayer.tryUpdateCurrentSongVolume audioPlayer

        member audioPlayer.MasterSoundVolume
            with get () = audioPlayer.MasterSoundVolume
            and  set volume = audioPlayer.MasterSoundVolume <- volume

        member audioPlayer.MasterSongVolume
            with get () = audioPlayer.MasterSongVolume
            and  set volume =
                audioPlayer.MasterSongVolume <- volume
                SdlAudioPlayer.tryUpdateCurrentSongVolume audioPlayer

        member audioPlayer.PopMessages () =
            let messages = audioPlayer.AudioMessages
            audioPlayer.AudioMessages <- List ()
            messages

        member audioPlayer.ClearMessages () =
            audioPlayer.AudioMessages <- List ()

        member audioPlayer.EnqueueMessage audioMessage =
            audioPlayer.AudioMessages.Add audioMessage 

        member audioPlayer.CurrentSongOpt =
            audioPlayer.CurrentSongOpt

        member audioPlayer.Play audioMessages =
            SdlAudioPlayer.handleAudioMessages audioMessages audioPlayer
            SdlAudioPlayer.tryUpdateCurrentSong audioPlayer

[<RequireQualifiedAccess>]
module AudioPlayer =

    /// Clear all of the audio messages that have been enqueued.
    let clearMessages (audioPlayer : AudioPlayer) =
        audioPlayer.ClearMessages ()

    /// Enqueue a message from an external source.
    let enqueueMessage message (audioPlayer : AudioPlayer) =
        audioPlayer.EnqueueMessage message

    /// 'Play' the audio system. Must be called once per frame.
    let play audioMessages (audioPlayer : AudioPlayer) =
        audioPlayer.Play audioMessages