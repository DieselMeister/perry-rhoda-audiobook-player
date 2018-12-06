﻿// Copyright 2018 Fabulous contributors. See LICENSE.md for license.
namespace PerryRhodan.AudiobookPlayer

open System.Diagnostics
open Fabulous.Core
open Fabulous.DynamicViews
open Xamarin.Forms
open Plugin.Permissions.Abstractions
open Common
open Domain

module App = 
    open Xamarin.Essentials
    open System.Net
    open Fabulous.DynamicViews
    
    type Pages = 
        | MainPage
        | LoginPage
        | BrowserPage
        | AudioPlayerPage
        | PermissionDeniedPage
    
    type Model = 
      { IsNav:bool
        MainPageModel:MainPage.Model
        LoginPageModel:LoginPage.Model option
        BrowserPageModel:BrowserPage.Model option
        AudioPlayerPageModel:AudioPlayerPage.Model option
        CurrentPage: Pages
        NavIsVisible:bool 
        PageStack: Pages list}

    type Msg = 
        | MainPageMsg of MainPage.Msg 
        | LoginPageMsg of LoginPage.Msg 
        | BrowserPageMsg of BrowserPage.Msg 
        | AudioPlayerPageMsg of AudioPlayerPage.Msg

        | GotoMainPage
        | GotoBrowserPage
        | SetBrowserPageCookieContainerAfterSucceededLogin of Map<string,string>
        | GotoAudioPlayerPage of AudioBook
        | GotoLoginPage
        | GotoPermissionDeniedPage
        | NavigationPopped of Pages
        | ChangeNavVisibility of bool
        

    let initModel = { IsNav = false
                      MainPageModel = MainPage.initModel
                      LoginPageModel = None
                      BrowserPageModel = None
                      AudioPlayerPageModel = None 
                      CurrentPage = MainPage
                      NavIsVisible = false 
                      PageStack = [ MainPage] }


    let addPageToPageStack page model =
        let hasItem = model.PageStack |> List.tryFind (fun i -> i = page)
        match hasItem with
        | None ->
            {model with PageStack = model.PageStack @ [page]}
        | Some _ ->
            let pageStackWithoutNewPage = 
                model.PageStack 
                |> List.filter (fun i -> i <> page)
            {model with PageStack = pageStackWithoutNewPage @ [page]}

    let init () = 
        let mainPageModel, mainPageMsg = MainPage.init ()

        {initModel with MainPageModel = mainPageModel}, Cmd.batch [ (Cmd.map MainPageMsg mainPageMsg)]

    

    let browserExternalMsgToCommand externalMsg =
        match externalMsg with
        | None -> Cmd.none
        | Some excmd -> 
            match excmd with
            | BrowserPage.ExternalMsg.OpenLoginPage ->
                Cmd.ofMsg (GotoLoginPage)
            | BrowserPage.ExternalMsg.OpenAudioBookPlayer ab ->
                Cmd.ofMsg (GotoAudioPlayerPage ab)

    let mainPageExternalMsgToCommand externalMsg =
        match externalMsg with
        | None -> Cmd.none
        | Some excmd -> 
            match excmd with
            | MainPage.ExternalMsg.GotoPermissionDeniedPage ->
                Cmd.ofMsg GotoPermissionDeniedPage
            | MainPage.ExternalMsg.OpenAudioBookPlayer ab ->
                Cmd.ofMsg (GotoAudioPlayerPage ab)

    let loginPageExternalMsgToCommand externalMsg =
        match externalMsg with
        | None -> Cmd.none
        | Some excmd -> 
            match excmd with
            | LoginPage.ExternalMsg.GotoForwardToBrowsing c ->
                Cmd.batch ([ Cmd.ofMsg (SetBrowserPageCookieContainerAfterSucceededLogin c); Cmd.ofMsg GotoBrowserPage ])

    let audioPlayerExternalMsgToCommand externalMsg =
        match externalMsg with
        | None -> Cmd.none
        | Some excmd -> 
           Cmd.none


    let update msg model =
        match msg with
        | MainPageMsg msg ->
            let m,cmd, externalMsg = MainPage.update msg model.MainPageModel

            let externalCmds =
                externalMsg |> mainPageExternalMsgToCommand

            {model with MainPageModel = m}, Cmd.batch [(Cmd.map MainPageMsg cmd); externalCmds ]

        | LoginPageMsg msg ->
            match model.LoginPageModel with
            | Some loginPageModel ->
                let m,cmd, externalMsg = LoginPage.update msg loginPageModel

                let externalCmds =
                    externalMsg |> loginPageExternalMsgToCommand
                   

                {model with LoginPageModel = Some m}, Cmd.batch [(Cmd.map LoginPageMsg cmd); externalCmds ]
            | None -> model, Cmd.none

        | BrowserPageMsg msg ->
            match model.BrowserPageModel with
            | Some browserPageModel ->
                let m,cmd,externalMsg = BrowserPage.update msg browserPageModel

                let externalCmds =
                    externalMsg |> browserExternalMsgToCommand

                {model with BrowserPageModel = Some m}, Cmd.batch [(Cmd.map BrowserPageMsg cmd); externalCmds ]

            | None -> model, Cmd.none
        | AudioPlayerPageMsg msg ->
            match model.AudioPlayerPageModel with
            | Some audioPlayerPageModel ->
                let m,cmd,externalMsg = AudioPlayerPage.update msg audioPlayerPageModel

                let externalCmds = 
                    externalMsg |> audioPlayerExternalMsgToCommand

                {model with AudioPlayerPageModel = Some m}, Cmd.batch [(Cmd.map AudioPlayerPageMsg cmd); externalCmds]

            | None -> model, Cmd.none

        | GotoMainPage ->
            let newModel = model |> addPageToPageStack MainPage
            {newModel with CurrentPage = MainPage}, Cmd.batch [ (Cmd.ofMsg (MainPageMsg MainPage.Msg.LoadLocalAudiobooks)); Cmd.ofMsg (ChangeNavVisibility false) ]

        | GotoLoginPage ->
            let newPageModel = model |> addPageToPageStack LoginPage
            match model.LoginPageModel with
            | None ->
                let m,cmd = LoginPage.init ()
                {newPageModel with CurrentPage = LoginPage; LoginPageModel = Some m},Cmd.batch [ (Cmd.map LoginPageMsg cmd); Cmd.ofMsg (ChangeNavVisibility false) ]
            | Some lpm  -> 
                let newModel = 
                    if not lpm.RememberLogin then
                        {lpm with Username = ""; Password = ""}
                    else
                        lpm
                {newPageModel with CurrentPage = LoginPage; LoginPageModel = Some newModel}, Cmd.ofMsg (ChangeNavVisibility false)

        | GotoBrowserPage ->
            let newPageModel = model |> addPageToPageStack BrowserPage
            match model.BrowserPageModel with
            | None ->
                let m,cmd, externalMsg = BrowserPage.init ()
                let externalCmds =
                    externalMsg |> browserExternalMsgToCommand

                {newPageModel with CurrentPage = BrowserPage; BrowserPageModel = Some m}, Cmd.batch [(Cmd.map BrowserPageMsg cmd); externalCmds; Cmd.ofMsg (ChangeNavVisibility false); Cmd.ofMsg (ChangeNavVisibility false)  ]
            | Some _  -> 
                {newPageModel with CurrentPage = BrowserPage}, Cmd.ofMsg (ChangeNavVisibility false)
            
        | GotoAudioPlayerPage audioBook ->
            let newPageModel = model |> addPageToPageStack AudioPlayerPage
            let brandNewPage () = 
                let m,cmd = AudioPlayerPage.init audioBook
                {newPageModel with CurrentPage = AudioPlayerPage; AudioPlayerPageModel = Some m}, Cmd.batch [ (Cmd.map AudioPlayerPageMsg cmd) ]

            match model.AudioPlayerPageModel with
            | None ->
                brandNewPage()

            | Some abModel ->
                if (abModel.AudioBook <> audioBook) then
                    brandNewPage()
                else
                    newPageModel, Cmd.none
        
        | GotoPermissionDeniedPage ->
            let newPageModel = model |> addPageToPageStack PermissionDeniedPage
            {newPageModel with CurrentPage = PermissionDeniedPage}, Cmd.none
        
        | NavigationPopped page ->
            if page = MainPage then
               model, Cmd.none
            else
               let newPageStack = model.PageStack |> List.filter ( fun i -> i <> page)
               {model with PageStack = newPageStack}, Cmd.none

        | ChangeNavVisibility b ->
            { model with NavIsVisible = b}, Cmd.none

        | SetBrowserPageCookieContainerAfterSucceededLogin cc ->
            match model.BrowserPageModel with 
            | None -> model, Cmd.none
            | Some bm ->
                
            let downloadQueueModel = {bm.DownloadQueueModel with CurrentSessionCookieContainer = Some cc}
            let bModel = {bm with CurrentSessionCookieContainer = Some cc; DownloadQueueModel = downloadQueueModel}
            { model with BrowserPageModel = Some bModel}, Cmd.batch [Cmd.ofMsg (BrowserPageMsg BrowserPage.Msg.LoadLocalAudiobooks) ]
        
        
    

    let view (model: Model) dispatch =
        // it's the same as  (MainPageMsg >> dispatch)
        // I had to do this, to get m head around this
        let mainPageDispatch mainMsg =
            let msg = mainMsg |> MainPageMsg
            dispatch msg

        let audioPlayerOverlay apmodel =
            dependsOn apmodel (fun _ mdl ->
                mdl
                |> Option.map (
                    fun (m:AudioPlayerPage.Model) ->
                        let cmd = m.AudioBook |> GotoAudioPlayerPage 
                        (AudioPlayerPage.viewSmall 
                            (fun () -> dispatch cmd) 
                            m 
                            (AudioPlayerPageMsg >> dispatch))                        
                )            
            )


        let mainPage = 
            dependsOn (model.MainPageModel, model.AudioPlayerPageModel) (fun _ (mdl, abMdl)->
                (Controls.contentPageWithBottomOverlay 
                    (audioPlayerOverlay abMdl)
                    (MainPage.view mdl (mainPageDispatch))
                    model.MainPageModel.IsLoading
                    "Home")
                        .ToolbarItems([
                            View.ToolbarItem(
                                icon="browse_icon.png",
                                command=(fun ()-> dispatch GotoBrowserPage
                            ))
                                
                        ])
                    .HasNavigationBar(true)
                    .HasBackButton(false)
                    .WithAttribute(AttributeKey("pageType"),MainPage)
                
            )

        // you can do an explict match or an Option map
        let loginPage =             
            dependsOn model.LoginPageModel (fun _ mdl ->
                mdl
                |> Option.map (
                    fun m -> 
                        (LoginPage.view m (LoginPageMsg >> dispatch))
                            .HasNavigationBar(false)
                            .HasBackButton(true)
                            .WithAttribute(AttributeKey("pageType"),LoginPage)
                )
            )

        let browserPage =
            dependsOn (model.BrowserPageModel, model.AudioPlayerPageModel) (fun _ (mdl, abMdl) ->
                mdl
                |> Option.map(
                    fun m ->
                        (Controls.contentPageWithBottomOverlay 
                            (audioPlayerOverlay abMdl)
                            (BrowserPage.view m (BrowserPageMsg >> dispatch))
                            (model.BrowserPageModel |> Option.map (fun bm -> bm.IsLoading) |> Option.defaultValue false)
                            "Browse your AudioBooks")
                            .ToolbarItems([
                                View.ToolbarItem(
                                    icon="home_icon.png",
                                    command=(fun ()-> dispatch GotoMainPage))
                                    ])
                            .HasNavigationBar(true)
                            .HasBackButton(true)
                            
                            
                )
            )
            
        
        let audioPlayerPage =
            dependsOn model.AudioPlayerPageModel (fun _ mdl ->
                mdl
                |> Option.map (
                    fun m ->
                        (AudioPlayerPage.view m (AudioPlayerPageMsg >> dispatch))
                            .ToolbarItems([
                                View.ToolbarItem(
                                    icon="home_icon.png",
                                    command=(fun ()-> dispatch GotoMainPage))
                                View.ToolbarItem(
                                    icon="browse_icon.png",
                                    command=(fun ()-> dispatch GotoBrowserPage))
                                    ])
                            .HasNavigationBar(true)
                            .HasBackButton(true)
                            
                )
            )
        


        let determinatePageByTitle title =            
            match title with
            | "Home" -> MainPage
            | "Browse your AudioBooks" -> BrowserPage
            | "Player" -> AudioPlayerPage
            | "Login" -> LoginPage
            | _ -> MainPage

        // Workaround iOS bug: https://github.com/xamarin/Xamarin.Forms/issues/3509
        let dispatchNavPopped =
            let mutable lastRemovedPageIdentifier: int = -1
            let apply dispatch (e: Xamarin.Forms.NavigationEventArgs) =
                let removedPageIdentifier = e.Page.GetHashCode()
                match lastRemovedPageIdentifier = removedPageIdentifier with
                | false ->
                    lastRemovedPageIdentifier <- removedPageIdentifier
                    let pageType = e.Page.Title |> determinatePageByTitle
                    dispatch (NavigationPopped pageType)
                | true ->
                    ()
            apply

        View.NavigationPage(barBackgroundColor = Consts.appBarColor,
            barTextColor=Consts.primaryTextColor,           
            popped = (dispatchNavPopped dispatch),
            pages = [
                for page in model.PageStack do
                    match page with
                    | MainPage -> 
                        yield mainPage  
                    | LoginPage ->
                        if loginPage.IsSome then
                            yield loginPage.Value
                    | BrowserPage ->
                        if browserPage.IsSome then
                            yield browserPage.Value
                    | AudioPlayerPage ->
                        if audioPlayerPage.IsSome then
                            yield audioPlayerPage.Value
                    | PermissionDeniedPage ->
                        yield View.ContentPage(
                            title="Login Page",useSafeArea=true,
                            content = View.Label(text="Sorry without Permission the App is not useable!", horizontalOptions = LayoutOptions.Center, widthRequest=200.0, horizontalTextAlignment=TextAlignment.Center,fontSize=20.0)
                        )

            ]            
        )



    let program = Program.mkProgram init update view

type App () as app = 
    inherit Application ()

    let runner =
        
        App.program
//#if DEBUG
//        |> Program.withConsoleTrace
//#endif    
        |> Program.runWithDynamicView app

#if DEBUG
    // Uncomment this line to enable live update in debug mode. 
    // See https://fsprojects.github.io/Fabulous/tools.html for further  instructions.
    //
    //do runner.EnableLiveUpdate()
#endif    

    // Uncomment this code to save the application state to app.Properties using Newtonsoft.Json
    // See https://fsprojects.github.io/Fabulous/models.html for further  instructions.
#if APPSAVE
    let modelId = "model"
    override __.OnSleep() = 

        let json = Newtonsoft.Json.JsonConvert.SerializeObject(runner.CurrentModel)
        Console.WriteLine("OnSleep: saving model into app.Properties, json = {0}", json)

        app.Properties.[modelId] <- json

    override __.OnResume() = 
        Console.WriteLine "OnResume: checking for model in app.Properties"
        try 
            match app.Properties.TryGetValue modelId with
            | true, (:? string as json) -> 

                Console.WriteLine("OnResume: restoring model from app.Properties, json = {0}", json)
                let model = Newtonsoft.Json.JsonConvert.DeserializeObject<App.Model>(json)

                Console.WriteLine("OnResume: restoring model from app.Properties, model = {0}", (sprintf "%0A" model))
                runner.SetCurrentModel (model, Cmd.none)

            | _ -> ()
        with ex -> 
            App.program.onError("Error while restoring model found in app.Properties", ex)

    override this.OnStart() = 
        Console.WriteLine "OnStart: using same logic as OnResume()"
        this.OnResume()
#endif

