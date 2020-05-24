﻿module Domain

open System
open System.Text.RegularExpressions
open FSharp.Data
open Common
open System.Threading.Tasks

type AudioBookPosition = 
    { Filename: string
      Position: TimeSpan }

type AudioBookState = 
    { Completed:bool
      CurrentPosition: AudioBookPosition option
      Downloaded:bool
      DownloadedFolder:string option
      LastTimeListend: DateTime option }
    
    with
        static member Empty = {Completed = false; CurrentPosition = None; Downloaded=false; DownloadedFolder = None; LastTimeListend = None}

type AudioBook =
    { Id:int
      FullName:string 
      EpisodeNo:int option
      EpisodenTitel: string
      Group:string
      Picture:string option
      Thumbnail:string option
      DownloadUrl: string option
      ProductSiteUrl:string option
      State: AudioBookState }

    with
        static member Empty = 
            { Id = 0
              FullName="" 
              EpisodeNo=None
              EpisodenTitel = ""
              Group = ""
              Picture=None
              Thumbnail=None
              DownloadUrl=None
              ProductSiteUrl = None
              State=AudioBookState.Empty }

type NameGroupedAudioBooks = (string * AudioBook[])[]

type AudioBookListType =
    | GroupList of NameGroupedAudioBooks
    | AudioBookList of string * AudioBook[]



[<AutoOpen>]
module Helpers =

    let downloadNameRegex = Regex(@"([A-Za-z .-]*)(\d*)(:| - )([\w\säöüÄÖÜ.:!\-]*[\(\)Teil \d]*)(.*)(( - Multitrack \/ Onetrack)|( - Multitrack)|( - Onetrack))")

    let getKey innerText =
        if not (downloadNameRegex.IsMatch(innerText)) then "Other"
        else
            let matchTitle = downloadNameRegex.Match(innerText)
            matchTitle.Groups.[1].Value.Replace("Nr.", "").Trim()


    let tryGetEpisodenNumber innerText =
        if not (downloadNameRegex.IsMatch(innerText)) then None
        else
            let epNumRes = Int32.TryParse(downloadNameRegex.Match(innerText).Groups.[2].Value)
            match epNumRes with
            | true, x -> Some x
            | _ -> None


    let getEpisodenTitle innerText =
        if not (downloadNameRegex.IsMatch(innerText)) then innerText.Trim()
        else 
            let ept = downloadNameRegex.Match(innerText).Groups.[4].Value.Trim()
            ept.Substring(0,(ept.Length-2)).ToString().Trim().Replace(":"," -")


    let tryGetLinkForMultiDownload (i:HtmlNode) =
        i.Descendants["a"]
        |> Seq.filter (fun i ->  i.Attribute("href").Value().ToLower().Contains("productfiletypeid=2"))
        |> Seq.map (fun i -> i.Attribute("href").Value())
        |> Seq.tryHead


    let tryGetProductionPage (i:HtmlNode) =
        i.Descendants["a"]
        |> Seq.filter (fun i -> i.InnerText() = "ansehen")
        |> Seq.map (fun i -> i.Attribute("href").Value())
        |> Seq.tryHead


    let buildFullName episodeNumber key episodeTitle =
        match episodeNumber with
        | None -> sprintf "%s - %s" key episodeTitle
        | Some no -> sprintf "%s %i - %s" key no episodeTitle


    let tryParseInt str =
        let (is,v) = Int32.TryParse(str)
        if is then Some v else None

    let tryGetProductId linkProductSite =
        linkProductSite
        |> RegExHelper.regexMatchGroupOpt 2 "(productID=)(\d*)"
        |> Option.bind (tryParseInt)
        
        



let parseDownloadData htmlData =    
    HtmlDocument.Parse(htmlData).Descendants("div")
    |> Seq.toArray 
    |> Array.filter (fun i -> i.AttributeValue("id") = "downloads")
    |> Array.tryHead
    // only the audiobooks
    |> Option.map (
        fun i -> 
            i.Descendants("li") 
            |> Seq.filter (fun i -> not (i.InnerText().Contains("Impressum"))) 
            |> Seq.filter (fun i -> 
                i.Descendants("a") 
                |> Seq.exists (fun i -> 
                    i.TryGetAttribute("href") 
                    |> Option.map(fun m -> m.Value().Contains("butler.php?action=audio")) 
                    |> Option.defaultValue false) 
                )
            |> Seq.toArray
    )
    |> Option.defaultValue ([||])
    |> Array.Parallel.map (
        fun i ->
            let innerText = i.InnerText()
            //   little title work
            let key = innerText |> getKey
            let episodeNumber = innerText |> tryGetEpisodenNumber                
            let episodeTitle = innerText |> getEpisodenTitle
            let linkForMultiDownload = i |> tryGetLinkForMultiDownload
            let linkProductSite = i |> tryGetProductionPage
            let fullName = buildFullName episodeNumber key episodeTitle
            let productId = 
                linkProductSite 
                |> tryGetProductId
                |> Option.defaultValue -1
                        
            {   Id = productId
                FullName = fullName 
                EpisodeNo = episodeNumber
                EpisodenTitel = episodeTitle
                Group = key
                DownloadUrl = linkForMultiDownload 
                ProductSiteUrl = linkProductSite
                Picture = None
                Thumbnail = None
                State = AudioBookState.Empty }
    )
    |> Array.filter (fun i -> i.Id <> -1)
    |> Array.sortBy (fun ab -> 
            match ab.EpisodeNo with
            | None -> -1
            | Some x -> x
    )
    |> Array.distinct


let filterNewAudioBooks (local:AudioBook[]) (online:AudioBook[]) =    
    online
    |> Array.filter (fun i -> local |> Array.exists (fun l -> l.Id = i.Id) |> not)


let synchronizeAudiobooks (local:AudioBook[]) (online:AudioBook[]) =
    let differences = filterNewAudioBooks local online
    Array.concat [|local; differences|] 


//type ProductSite = HtmlProvider< SampleData.productSiteHtml >

let parseProductPageForDescription html =
    let paragraphs =
        HtmlDocument
            .Parse(html)                        
            .Descendants ["p"]
        |> Seq.toList
    
    let productDetail = 
        paragraphs
        |> List.tryFindIndex (fun i -> 
            let idAttribute = i.TryGetAttribute("class")
            match idAttribute with
            | None -> false
            | Some a ->
                a.Value() = "pricetag"
            )
        |> Option.bind( 
            fun idx ->
                //get next entry
                let nextIdx = idx + 1
                if (nextIdx + 1) > paragraphs.Length then
                    None
                else
                    let nextEntry = paragraphs.[nextIdx]
                    let description = nextEntry.InnerText()
                    Some description
        )
        
    
    productDetail

let parseProductPageForImage html =
    let image =
        HtmlDocument
            .Parse(html)                        
            .Descendants ["img"]
        |> Seq.tryFind (fun i -> i.AttributeValue("id") = "imgzoom")
        |> Option.bind (
            fun i -> 
                let imgSrc = i.AttributeValue("src")
                if imgSrc = "" then None else Some imgSrc
        )
        
    image
    
    
    
        

module Filters = 

    let nameFilter audiobooks =          
        audiobooks
        |> Array.groupBy (fun i -> i.Group)
        |> Array.sortBy (fun (key,_) -> key)
        

    let nameGroupFilter groupname audiobooks =
        match audiobooks with
        | AudioBookList (key,x) -> x
        | GroupList gl ->
            gl
            |> Array.filter (fun (key,_) -> key = groupname)
            |> Array.Parallel.collect (fun (_,items) -> items)

    
    let mapFromToName items =
        let sortedItem = 
            items 
            |> Array.sortBy (
                fun i -> 
                    i.EpisodeNo |> Option.defaultValue 0                            
            )
        let groupName = sortedItem.[0].Group
        let firstEpNo = sortedItem.[0].EpisodeNo |> Option.defaultValue 0 
        let lastEpNo = sortedItem.[sortedItem.Length - 1].EpisodeNo |> Option.defaultValue 0
        sprintf "%s %i - %i" groupName firstEpNo lastEpNo

    let ``100 episode filter`` audiobooks =
        audiobooks
        |> Array.groupBy (
            fun i -> 
                let epno = i.EpisodeNo |> Option.defaultValue 0
                let rest = epno % 100
                let episodeNo = epno - rest
                episodeNo
        )
        |> Array.map (fun (key,items) -> items |> mapFromToName, items)
        |> GroupList

    let ``10 episode filter`` audiobooks : AudioBookListType =
        audiobooks
        |> Array.groupBy (
            fun i -> 
                let epno = i.EpisodeNo |> Option.defaultValue 0
                let rest = epno % 10
                let episodeNo = epno - rest
                episodeNo
        )
        |> Array.map (fun (key,items) -> items |> mapFromToName, items)
        |> GroupList
    
    let (|EpisodeNoMore100|_|) audiobooks =
        let episodeNoGreater100 =
            audiobooks |> Array.exists (fun i -> (i.EpisodeNo |> Option.defaultValue 0) > 100)
                
        let episodeNoSame100 =                    
            match audiobooks with
            | [||] -> true
            | x -> 
                let epBase = (x.[0].EpisodeNo |> Option.defaultValue 0) / 100
                audiobooks |> Array.forall (fun i -> ((i.EpisodeNo |> Option.defaultValue 0) / 100) = epBase)

        if episodeNoGreater100 && (episodeNoSame100 |> not) then Some() else None

    let (|EpisodeNoMore10|_|) audiobooks =
        let episodeNoGreater10 =
            audiobooks |> Array.exists (fun i -> (i.EpisodeNo |> Option.defaultValue 0) > 10)
                
        let episodeNoSame10 = 
            match audiobooks with
            | [||] -> true
            | x -> 
                let epBase = (x.[0].EpisodeNo |> Option.defaultValue 0) / 10
                audiobooks |> Array.forall (fun i -> ((i.EpisodeNo |> Option.defaultValue 0) / 10) = epBase)

        if episodeNoGreater10 && (episodeNoSame10 |> not) then Some() else None


    let groupsFilter (groups:string list) (audiobooks:AudioBookListType) =
        (audiobooks,groups)
        ||> List.fold (
            fun state item -> 
                let nameFilterResult =
                    state
                    |> nameGroupFilter item
                
                
                match nameFilterResult with
                | EpisodeNoMore100 ->
                    (nameFilterResult |> ``100 episode filter``)
                | EpisodeNoMore10 ->
                    (nameFilterResult |> ``10 episode filter``)
                | _ ->
                    AudioBookList (item ,nameFilterResult)
        )

    
    
let getAudiobooksFromGroup group (audiobooks:(string*AudioBook[])[]) =
    audiobooks
    |> Array.tryFind (fun (key,_) -> key = group)
    |> Option.map (fun (_,items) -> 
        items 
        |> Array.chunkBySize 10 
        |> Array.mapi (fun idx cItems -> 
            let cItems = cItems |> Array.sortByDescending (fun si -> si.EpisodeNo)
            let firstEpisode = cItems.[0].EpisodeNo |> Option.defaultValue 0
            let lastEpisode = (cItems |> Array.last).EpisodeNo |> Option.defaultValue 0
            
            (sprintf "%s %i - %i" group firstEpisode lastEpisode),cItems
            )
        |> Array.sortByDescending (fun (key,_) -> key)
        )
    

let getAudiobooksFromChunk chunk (audiobooks:(string*AudioBook[])[]) =
    audiobooks
    |> Array.tryFind (fun (key,_) -> key = chunk)
    |> Option.map (fun (_,items) -> 
            items |> Array.sortByDescending (fun si -> si.EpisodeNo)
        )


module AudioBooks =
    
    let flatten (input:NameGroupedAudioBooks) =
        input |> Array.collect (fun (_,items) -> items)
        
    
    




