﻿namespace InfinityRpg
open System
open Nu
open InfinityRpg

// this is a plugin for the Nu game engine that directs the execution of your application and editor
type InfinityRpgPlugin () =
    inherit NuPlugin ()

    // specify the game dispatcher to use at run-time
    override this.GetStandAloneGameDispatcher () =
        typeof<InfinityDispatcher>

    // route overlays to specific dispatchers
    override this.MakeOverlayRoutes () =
        [typeof<ButtonDispatcher>.Name, Some "InfinityButtonDispatcher"]
        
// this is the module where main is defined (the entry-point for your Nu application)
module Program =

    // this the entry point for the InfinityRpg application
    let [<EntryPoint; STAThread>] main _ =

        // this specifies the window configuration used to display the game
        let sdlWindowConfig = { SdlWindowConfig.defaultConfig with WindowTitle = "Infinity RPG" }
        
        // this specifies the configuration of the game engine's use of SDL
        let sdlConfig = { SdlConfig.defaultConfig with ViewConfig = NewWindow sdlWindowConfig }

        // use the default world config with the above SDL config
        let worldConfig = { WorldConfig.defaultConfig with SdlConfig = sdlConfig }

        // initialize Nu
        Nu.init worldConfig.NuConfig
        
        // run the engine with the given config and plugin
        World.run worldConfig (InfinityRpgPlugin ())