﻿// Nu Game Engine.
// Copyright (C) Bryan Edds, 2013-2020.

namespace Nu
open System
open System.Collections.Generic
open System.IO
open FSharpx.Collections
open Prime
open Nu

[<AutoOpen; ModuleBinding>]
module WorldScreenModule =

    type Screen with
    
        member this.GetDispatcher world = World.getScreenDispatcher this world
        member this.Dispatcher = lensReadOnly Property? Dispatcher this.GetDispatcher this
        member this.GetModel<'a> world = World.getScreenModel<'a> this world
        member this.SetModel<'a> value world = World.setScreenModel<'a> value this world |> snd'
        member this.Model<'a> () = lens Property? Model this.GetModel<'a> this.SetModel<'a> this
        member this.GetEcs world = World.getScreenEcs this world
        member this.Ecs = lensReadOnly Property? Ecs this.GetEcs this
        member this.GetTransitionState world = World.getScreenTransitionState this world
        member this.SetTransitionState value world = World.setScreenTransitionState value this world |> snd'
        member this.TransitionState = lens Property? TransitionState this.GetTransitionState this.SetTransitionState this
        member this.GetTransitionTicks world = World.getScreenTransitionTicks this world
        member this.SetTransitionTicks value world = World.setScreenTransitionTicks value this world |> snd'
        member this.TransitionTicks = lens Property? TransitionTicks this.GetTransitionTicks this.SetTransitionTicks this
        member this.GetIncoming world = World.getScreenIncoming this world
        member this.SetIncoming value world = World.setScreenIncoming value this world |> snd'
        member this.Incoming = lens Property? Incoming this.GetIncoming this.SetIncoming this
        member this.GetOutgoing world = World.getScreenOutgoing this world
        member this.SetOutgoing value world = World.setScreenOutgoing value this world |> snd'
        member this.Outgoing = lens Property? Outgoing this.GetOutgoing this.SetOutgoing this
        member this.GetPersistent world = World.getScreenPersistent this world
        member this.SetPersistent value world = World.setScreenPersistent value this world |> snd'
        member this.Persistent = lens Property? Persistent this.GetPersistent this.SetPersistent this
        member this.GetDestroying world = World.getScreenDestroying this world
        member this.Destroying = lensReadOnly Property? Destroying this.GetDestroying this
        member this.GetScriptFrame world = World.getScreenScriptFrame this world
        member this.ScriptFrame = lensReadOnly Property? Script this.GetScriptFrame this
        member this.GetCreationTimeStamp world = World.getScreenCreationTimeStamp this world
        member this.CreationTimeStamp = lensReadOnly Property? CreationTimeStamp this.GetCreationTimeStamp this
        member this.GetId world = World.getScreenId this world
        member this.Id = lensReadOnly Property? Id this.GetId this

        member this.ChangeEvent propertyName = Events.Change propertyName --> this
        member this.RegisterEvent = Events.Register --> this
        member this.UnregisteringEvent = Events.Unregistering --> this
        member this.UpdateEvent = Events.Update --> this
        member this.PostUpdateEvent = Events.PostUpdate --> this
        member this.SelectEvent = Events.Select --> this
        member this.DeselectEvent = Events.Deselect --> this
        member this.IncomingStartEvent = Events.IncomingStart --> this
        member this.IncomingFinishEvent = Events.IncomingFinish --> this
        member this.OutgoingStartEvent = Events.OutgoingStart --> this
        member this.OutgoingFinishEvent = Events.OutgoingFinish --> this

        /// Try to get a property value and type.
        member this.TryGetProperty propertyName world =
            let mutable property = Unchecked.defaultof<_>
            if World.tryGetScreenProperty (propertyName, this, world, &property)
            then Some property
            else None

        /// Get a property value and type.
        member this.GetProperty propertyName world = World.getScreenProperty propertyName this world

        /// Get a property value.
        member this.Get<'a> propertyName world : 'a = (World.getScreenProperty propertyName this world).PropertyValue :?> 'a

        /// Try to set a property value with explicit type.
        member this.TrySetProperty propertyName property world = World.trySetScreenProperty propertyName property this world

        /// Set a property value with explicit type.
        member this.SetProperty propertyName property world = World.setScreenProperty propertyName property this world |> snd'

        /// Set a property value.
        member this.Set<'a> propertyName (value : 'a) world = World.setScreenProperty propertyName { PropertyType = typeof<'a>; PropertyValue = value } this world |> snd'

        /// Check that a screen is in an idling state (not transitioning in nor out).
        member this.IsIdling world = match this.GetTransitionState world with IdlingState -> true | _ -> false

        /// Check that a screen is selected.
        member this.IsSelected world =
            let gameState = World.getGameState world
            match gameState.OmniScreenOpt with
            | Some omniScreen when Address.head this.ScreenAddress = Address.head omniScreen.ScreenAddress -> true
            | _ ->
                match gameState.SelectedScreenOpt with
                | Some screen when Address.head this.ScreenAddress = Address.head screen.ScreenAddress -> true
                | _ -> false

        /// Check that a screen exists in the world.
        member this.Exists world = World.getScreenExists this world

        /// Check that a screen dispatches in the same manner as the dispatcher with the given type.
        member this.Is (dispatcherType, world) = Reflection.dispatchesAs dispatcherType (this.GetDispatcher world)

        /// Check that a screen dispatches in the same manner as the dispatcher with the given type.
        member this.Is<'a> world = this.Is (typeof<'a>, world)

        /// Resolve a relation in the context of a screen.
        member this.Resolve relation = resolve<Screen> this relation

        /// Relate a screen to a simulant.
        member this.Relate simulant = relate<Screen> this simulant

        /// Get a screen's change event address.
        member this.GetChangeEvent propertyName = Events.Change propertyName --> this.ScreenAddress

        /// Try to signal a screen.
        member this.TrySignal signal world = (this.GetDispatcher world).TrySignal (signal, this, world)

    type World with

        static member internal updateScreen (screen : Screen) world =

            // update ecs
            let ecs = World.getScreenEcs screen world
#if ECS_BUFFERED_PLUS
            let updateTask = ecs.PublishAsync EcsEvents.UpdateParallel () ecs.SystemGlobal
            let world = ecs.Publish EcsEvents.Update () ecs.SystemGlobal world
#else
            let world = ecs.Publish EcsEvents.Update () ecs.SystemGlobal world
            let updateTask = ecs.PublishAsync EcsEvents.UpdateParallel () ecs.SystemGlobal
#endif

            // update via dispatcher
            let dispatcher = World.getScreenDispatcher screen world
            let world = dispatcher.Update (screen, world)

            // finish update task
            ignore<unit array> updateTask.Result

            // publish update event
            let eventTrace = EventTrace.debug "World" "updateScreen" "" EventTrace.empty
            World.publishPlus () (Events.Update --> screen) eventTrace Simulants.Game false world

        static member internal postUpdateScreen (screen : Screen) world =

            // post-update ecs
            let ecs = World.getScreenEcs screen world
#if ECS_BUFFERED_PLUS
            let postUpdateTask = ecs.PublishAsync EcsEvents.PostUpdateParallel () ecs.SystemGlobal
            let world = ecs.Publish EcsEvents.PostUpdate () ecs.SystemGlobal world
#else
            let world = ecs.Publish EcsEvents.PostUpdate () ecs.SystemGlobal world
            let postUpdateTask = ecs.PublishAsync EcsEvents.PostUpdateParallel () ecs.SystemGlobal
#endif
                
            // post-update via dispatcher
            let dispatcher = World.getScreenDispatcher screen world
            let world = dispatcher.PostUpdate (screen, world)

            // finish post-update task
            ignore<unit array> postUpdateTask.Result

            // publish post-update event
            let eventTrace = EventTrace.debug "World" "postUpdateScreen" "" EventTrace.empty
            World.publishPlus () (Events.PostUpdate --> screen) eventTrace Simulants.Game false world

        static member internal actualizeScreen (screen : Screen) world =

            // actualize ecs
            let ecs = World.getScreenEcs screen world
            let world = ecs.Publish EcsEvents.Actualize () ecs.SystemGlobal world

            // actualize via dispatcher
            let dispatcher = screen.GetDispatcher world
            dispatcher.Actualize (screen, world)

        /// Get all the world's screens.
        [<FunctionBinding>]
        static member getScreens world =
            World.getScreenDirectory world |> UMap.toSeq |> Seq.map (fun (_, entry) -> entry.Key)

        /// Set the dissolve properties of a screen.
        [<FunctionBinding>]
        static member setScreenDissolve dissolveDescriptor songOpt (screen : Screen) world =
            let dissolveImageOpt = Some dissolveDescriptor.DissolveImage
            let world = screen.SetIncoming { Transition.make Incoming with TransitionLifeTime = dissolveDescriptor.IncomingTime; DissolveImageOpt = dissolveImageOpt; SongOpt = songOpt } world
            let world = screen.SetOutgoing { Transition.make Outgoing with TransitionLifeTime = dissolveDescriptor.OutgoingTime; DissolveImageOpt = dissolveImageOpt; SongOpt = songOpt } world
            world

        /// Destroy a screen in the world immediately. Can be dangerous if existing in-flight publishing depends on the
        /// screen's existence. Consider using World.destroyScreen instead.
        static member destroyScreenImmediate (screen : Screen) world =
            let world = World.tryRemoveSimulantFromDestruction screen world
            let destroyGroupsImmediate screen world =
                let groups = World.getGroups screen world
                World.destroyGroupsImmediate groups world
            EventSystemDelegate.cleanEventAddressCache screen.ScreenAddress
            World.removeScreen3 destroyGroupsImmediate screen world

        /// Destroy a screen in the world at the end of the current update.
        [<FunctionBinding>]
        static member destroyScreen (screen : Screen) world =
            World.addSimulantToDestruction screen world

        /// Create a screen and add it to the world.
        [<FunctionBinding "createScreen">]
        static member createScreen3 dispatcherName nameOpt world =
            let dispatchers = World.getScreenDispatchers world
            let dispatcher =
                match Map.tryFind dispatcherName dispatchers with
                | Some dispatcher -> dispatcher
                | None -> failwith ("Could not find ScreenDispatcher '" + dispatcherName + "'. Did you forget to expose this dispatcher from your NuPlugin?")
            let ecs = world.WorldExtension.Plugin.MakeEcs ()
            let screenState = ScreenState.make nameOpt dispatcher ecs
            let screenState = Reflection.attachProperties ScreenState.copy screenState.Dispatcher screenState world
            let screen = ntos screenState.Name
            let world =
                if World.getScreenExists screen world then
                    if screen.GetDestroying world
                    then World.destroyScreenImmediate screen world
                    else failwith ("Screen '" + scstring screen + " already exists and cannot be created."); world
                else world
            let world = World.addScreen false screenState screen world
            (screen, world)

        /// Create a screen from a simulant descriptor.
        static member createScreen2 descriptor world =
            let (screen, world) =
                World.createScreen3 descriptor.SimulantDispatcherName descriptor.SimulantNameOpt world
            let world =
                List.fold (fun world (propertyName, property) ->
                    World.setScreenProperty propertyName property screen world |> snd')
                    world descriptor.SimulantProperties
            let world =
                List.fold (fun world childDescriptor ->
                    World.createGroup3 childDescriptor screen world |> snd)
                    world descriptor.SimulantChildren
            (screen, world)

        /// Create a screen and add it to the world.
        static member createScreen<'d when 'd :> ScreenDispatcher> nameOpt world =
            World.createScreen3 typeof<'d>.Name nameOpt world

        /// Create a screen with a dissolving transition, and add it to the world.
        [<FunctionBinding "createDissolveScreen">]
        static member createDissolveScreen5 dispatcherName nameOpt dissolveDescriptor songOpt world =
            let (screen, world) = World.createScreen3 dispatcherName nameOpt world
            let world = World.setScreenDissolve dissolveDescriptor songOpt screen world
            (screen, world)
        
        /// Create a screen with a dissolving transition, and add it to the world.
        static member createDissolveScreen<'d when 'd :> ScreenDispatcher> nameOpt dissolveDescriptor world =
            World.createDissolveScreen5 typeof<'d>.Name nameOpt dissolveDescriptor world

        /// Write a screen to a screen descriptor.
        static member writeScreen screen screenDescriptor world =
            let writeGroups screen screenDescriptor world =
                let groups = World.getGroups screen world
                World.writeGroups groups screenDescriptor world
            World.writeScreen4 writeGroups screen screenDescriptor world

        /// Write multiple screens to a game descriptor.
        static member writeScreens screens gameDescriptor world =
            screens |>
            Seq.sortBy (fun (screen : Screen) -> screen.GetCreationTimeStamp world) |>
            Seq.filter (fun (screen : Screen) -> screen.GetPersistent world) |>
            Seq.fold (fun screenDescriptors screen -> World.writeScreen screen ScreenDescriptor.empty world :: screenDescriptors) gameDescriptor.ScreenDescriptors |>
            fun screenDescriptors -> { gameDescriptor with ScreenDescriptors = screenDescriptors }

        /// Write a screen to a file.
        [<FunctionBinding>]
        static member writeScreenToFile (filePath : string) screen world =
            let filePathTmp = filePath + ".tmp"
            let prettyPrinter = (SyntaxAttribute.getOrDefault typeof<GameDescriptor>).PrettyPrinter
            let screenDescriptor = World.writeScreen screen ScreenDescriptor.empty world
            let screenDescriptorStr = scstring screenDescriptor
            let screenDescriptorPretty = PrettyPrinter.prettyPrint screenDescriptorStr prettyPrinter
            File.WriteAllText (filePathTmp, screenDescriptorPretty)
            File.Delete filePath
            File.Move (filePathTmp, filePath)

        /// Read a screen from a screen descriptor.
        static member readScreen screenDescriptor nameOpt world =
            World.readScreen4 World.readGroups screenDescriptor nameOpt world

        /// Read multiple screens from a game descriptor.
        static member readScreens gameDescriptor world =
            List.foldBack
                (fun screenDescriptor (screens, world) ->
                    let screenNameOpt = ScreenDescriptor.getNameOpt screenDescriptor
                    let (screen, world) = World.readScreen screenDescriptor screenNameOpt world
                    (screen :: screens, world))
                gameDescriptor.ScreenDescriptors
                ([], world)

        /// Read a screen from a file.
        [<FunctionBinding>]
        static member readScreenFromFile (filePath : string) nameOpt world =
            let screenDescriptorStr = File.ReadAllText filePath
            let screenDescriptor = scvalue<ScreenDescriptor> screenDescriptorStr
            World.readScreen screenDescriptor nameOpt world

        /// Apply a screen behavior to a screen.
        static member applyScreenBehavior setScreenSplash behavior (screen : Screen) world =
            match behavior with
            | Vanilla ->
                world
            | Dissolve (dissolveDescriptor, songOpt) ->
                World.setScreenDissolve dissolveDescriptor songOpt screen world
            | Splash (dissolveDescriptor, splashDescriptor, songOpt, destination) ->
                let world = World.setScreenDissolve dissolveDescriptor songOpt screen world
                setScreenSplash (Some splashDescriptor) destination screen world
            | OmniScreen ->
                World.setOmniScreen screen world

        /// Turn screen content into a live screen.
        static member expandScreenContent setScreenSplash content origin game world =
            match ScreenContent.expand content game world with
            | Left (_, descriptor, handlers, binds, behavior, groupStreams, entityStreams, groupFilePaths, entityFilePaths, entityContents) ->
                let (screen, world) =
                    World.createScreen2 descriptor world
                let world =
                    List.fold (fun world (_ : string, groupName, filePath) ->
                        World.readGroupFromFile filePath (Some groupName) screen world |> snd)
                        world groupFilePaths
                let world =
                    List.fold (fun world (_ : string, groupName, entityName, filePath) ->
                        World.readEntityFromFile filePath (Some entityName) (screen / groupName) world |> snd)
                        world entityFilePaths
                let world =
                    List.fold (fun world (simulant, left : World Lens, right) ->
                        WorldModule.bind5 simulant left right world)
                        world binds
                let world =
                    List.fold (fun world (handler, address, simulant) ->
                        World.monitor (fun (evt : Event) world ->
                            let signal = handler evt
                            let simulant = match origin with SimulantOrigin simulant -> simulant | FacetOrigin (simulant, _) -> simulant
                            let world = WorldModule.trySignal signal simulant world
                            (Cascade, world))
                            address simulant world)
                        world handlers
                let world =
                    List.fold (fun world (screen, lens, sieve, unfold, mapper) ->
                        World.expandGroups lens sieve unfold mapper origin screen world)
                        world groupStreams
                let world =
                    List.fold (fun world (group, lens, sieve, unfold, mapper) ->
                        World.expandEntities lens sieve unfold mapper origin group group world)
                        world entityStreams
                let world =
                    List.fold (fun world (owner : Entity, entityContents) ->
                        let group = owner.Parent
                        List.fold (fun world entityContent ->
                            World.expandEntityContent entityContent origin owner group world |> snd)
                            world entityContents)
                        world entityContents
                let world =
                    World.applyScreenBehavior setScreenSplash behavior screen world
                (screen, world)
            | Right (name, behavior, Some dispatcherType, groupFilePath) ->
                let (screen, world) = World.createScreen3 dispatcherType.Name (Some name) world
                let world = World.readGroupFromFile groupFilePath None screen world |> snd
                let world = World.applyScreenBehavior setScreenSplash behavior screen world
                (screen, world)
            | Right (name, behavior, None, filePath) ->
                let (screen, world) = World.readScreenFromFile filePath (Some name) world
                let world = World.applyScreenBehavior setScreenSplash behavior screen world
                (screen, world)

namespace Debug
open Nu
type Screen =

    /// Provides a full view of all the member properties of a screen. Useful for debugging such
    /// as with the Watch feature in Visual Studio.
    static member view screen world = World.viewScreenProperties screen world