namespace FrlUtils

open System.Net.Http
open DocumentFormat.OpenXml
open Shoshin.HtmlUtils.HtmlParsing
open DocumentFormat.OpenXml.Packaging
open System.IO
open HtmlAgilityPack
open NodaTime
open NodaTime.Text
open System.Text.RegularExpressions
open System.Net
open System
open Domain
open Errors
open FsToolkit

module WebScraping =
    // extract DocX download link
    
    [<Literal>]
    let private frlBaseUrl = "https://www.legislation.gov.au"
    
    [<Literal>]
    let private idOfDocXDownloadAElement =  "ctl00_MainContent_AttachmentsRepeater_ctl00_ArtifactVersionRenderer_Repeater1_ctl00_ArtifactFormatTableRenderer1_RadGridNonHtml_ctl00_ctl04_hlPrimaryDoc"

    type asyncPageFetcher =  string ->  Async<Result<Stream,ScrapeError>>
            

    [<Literal>]
    let private frlSearchUrl = "https://www.legislation.gov.au/Search"
    
    let  createFetcher (httpClient: HttpClient) =
        fun (url:string) -> async {
            try
                let! response = httpClient.GetAsync(url) |> Async.AwaitTask
                // FRL site redirets to search page when register id is not found, instead of returning 404
                match response with
                | r when r.StatusCode = HttpStatusCode.Redirect -> 
                    let target = r.Headers.Location
                    let isSearch = target.ToString().ToLowerInvariant().Contains("/search")
                    match isSearch with
                    | true -> return Error(ScrapeError.NotFound(url))
                    | false ->

                        let! targetResponse = httpClient.GetAsync(frlBaseUrl + target.ToString()) |> Async.AwaitTask
                        let! targetStream = targetResponse.Content.ReadAsStreamAsync() |> Async.AwaitTask
                        return Ok targetStream
                | r when not (r.StatusCode = HttpStatusCode.OK) -> return Error(ScrapeError.UnexpectedHttpStatusCode(r.StatusCode))
                | _ -> 
                    let! contentStream = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
                    return Ok contentStream
            with
            | ex -> return Error(ScrapeError.Exception(ex))
        }
    
    
    
    let readStreamAsHtml (stream : Stream) : Result<string,ScrapeError> = 
         use sr = new StreamReader(stream,true)
         try
            let text = sr.ReadToEnd()
            Ok text
         with
         | ex -> Error(ScrapeError.Exception(ex))
    
    
    let getHtmlDocForUrl (url : string) (fetcher: asyncPageFetcher)  =
        async {
            let! page = fetcher(url)
            let html = page |> Result.bind (fun p -> readStreamAsHtml p)
            let doc = html |> Result.bind (fun h -> getHtmlDoc h |> Result.mapError (fun e -> ScrapeError.HtmlParseError(e)) )
            return doc
        }

    let private getDocXUrlForRegisterId (registerid: string) (fetcher : asyncPageFetcher)   = async {
        let downloadpageurl = $"{frlBaseUrl}/Details/{registerid}/Download"
        let! contentResult = (fetcher downloadpageurl) 

        let docLink = 
            contentResult
            |> Result.bind (fun s ->  readStreamAsHtml s)
            |> Result.bind (fun htmlOfDownloadPage -> Shoshin.HtmlUtils.HtmlParsing.getLinkTarget idOfDocXDownloadAElement htmlOfDownloadPage |> Result.mapError (fun e -> ScrapeError.HtmlParseError(e)))
        return docLink    
    }
        
    let private convertBinaryToWordDoc (docBinary: byte[]) = 
        try        
            use ms = new MemoryStream(docBinary)
            Ok (WordprocessingDocument.Create(ms,WordprocessingDocumentType.Document))
        with
        | ex -> Error(ScrapeError.Exception(ex))

    
    let getInstrumentDocX (registerId: string) (fetcher : asyncPageFetcher) = async {
        let! urlForDocx = getDocXUrlForRegisterId registerId fetcher
        let! docStream = urlForDocx |> Result.bindAsync(fun url -> fetcher url)
        let bytes = docStream |> Result.bind (fun i -> 
            use ms = new MemoryStream()
            i.CopyTo(ms) |> ignore
            Ok (ms.ToArray())
        )
        return bytes
    } 

    let getInstrumentDocXAsync (registerId: string) (httpClient : HttpClient) = getInstrumentDocX registerId (createFetcher httpClient) |> Async.StartAsTask
    
   
    let parseCompilationsTableData (table : TableData) : Result<CompilationsHistory,ScrapeError> =
        let parseTitleCell (innerText: string)  = innerText.Replace("Superseded","").Replace("Latest","").Trim()
        let datePattern = LocalDatePattern.Create("dd/MMM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
        let parseIncorpAmendments (str: string) = str.Trim()
        let parseCompNumber s = 
            let (|Integer|_|) (str: string) =
                let mutable intvalue = 0
                if System.Int32.TryParse(str, &intvalue) then Some(intvalue)
                else None            
            match s with
                | Integer i -> Some(i)
                | _ -> None
        

        let parseDate d = 
            match datePattern.Parse d with
            | r when r.Success -> r.Value
            | _ -> failwith "Could not parse date in compilations table"
        
        let records = 
            try
                Ok(table.Rows
                    |> Seq.map (fun r -> 
                        let l = r |> List.ofSeq
                        {
                            Title = parseTitleCell l[0];
                            RegisteredDate = parseDate(l[1]);
                            RegisterId = l[2];
                            CompilationNumber = parseCompNumber l[3];
                            StartDate = parseDate(l[4]);
                            EndDate = if (l[5] = "&nbsp;") then None else Some(parseDate l[5])
                            IncorporatingAmendmentsTo = parseIncorpAmendments l[6]
                        }
                    )
                )
                   
            with
            | ex -> Error(ScrapeError.Exception(ex)) 
        
        records |> Result.map (fun r -> {Compilations = r |> List.ofSeq})
        
        

    [<Literal>]
    let idOfRadGrid = "ctl00_MainContent_SeriesCompilations_RadGrid1_ctl00"
    let getCompilationsTableData (doc: HtmlDocument) =
        let xPath = $"//table[@id='{idOfRadGrid}']" 
        getSingleDocumentNode doc xPath
        |> Result.bind (fun r -> getDataFromHtmlTable r)
       
    
    
    // get series info    
    
    [<Literal>]
    let idOfSeriesHeader = "ctl00_MainContent_RadTabStrip1"
    [<Literal>]
    let idOfOfComplitionsTab = "MainContent_RadPageCompilations"
    [<Literal>]
    let idOfPrincipalRegisterIdLink = "MainContent_hlPrincipal" 
    

    let getSeriesPage (registerId: string, seriesType: SeriesType, fetcher: asyncPageFetcher) = 
        async {
            let pagePathFragment = 
                match seriesType with
                | SeriesType.Compilations -> "Compilations"
                | SeriesType.PrincipalAndAmendments -> "Amendments"
                | SeriesType.RepealedBy -> "RepealedBy"
            let url = $"{frlBaseUrl}/Series/{registerId}/{pagePathFragment}"
            let! htmlStream = fetcher(url)
            return htmlStream
                |> Result.bind (fun r -> readStreamAsHtml r)
                |> Result.bind (fun r -> getHtmlDoc r |> Result.mapError (fun e -> ScrapeError.HtmlParseError e ))
        }
    
    let parseRepealedTable (table: TableData)  =
        let firstRow = table.Rows |> List.ofSeq |> List.tryHead
        match firstRow with 
        | None -> Error(ScrapeError.Message("Can't find table with repealing instruments."))
        | Some (r) ->
            let cellCount = Seq.length r
            match (cellCount) with
            | 0 -> Error(ScrapeError.Message("Can't find row for repealing instruments in table."))
            | 1 -> Ok(None)
            | 3 -> 
                let asList = r |> List.ofSeq
                Ok(Some({InstrumentName = asList[0].Trim(); RegisterId = asList[1]}))
            | _ -> Error(ScrapeError.Message("Can't make sense of repealing instruments table"))

    let getPrincipal (registerId: string) (fetcher: asyncPageFetcher)  =
        async {
            let! principalPage = getSeriesPage(registerId, SeriesType.PrincipalAndAmendments, fetcher)
            let principalName = principalPage |> Result.bind (fun p ->   getSingleDocumentNode p "//a[@id='MainContent_hlPrincipal']"  |> Result.mapError (fun e -> ScrapeError.HtmlParseError(e) )) |> Result.map (fun n -> n.InnerText) 
            let principalRegisterId = principalPage |> Result.bind (fun p ->  getSingleDocumentNode p "//span[@id='MainContent_lblPrincipalID']" |> Result.mapError (fun e -> ScrapeError.HtmlParseError(e)))  |> Result.map (fun n -> n.InnerText)
            let r = match (principalName, principalRegisterId) with
                    | (Ok(n),Ok(r)) -> Ok({InstrumentName = n; RegisterId = r })
                    | _ -> Error(ScrapeError.Message("Could not get Principal data for " + registerId ))
            return r
        }
    let getPrincipalAsync (registerId: string, httpClient: HttpClient) = getPrincipal registerId (createFetcher(httpClient)) |> Async.StartAsTask 


    [<Literal>]
    let repealedTableId = "ctl00_MainContent_SeriesRepealedBy_RadGrid1_ctl00"
    let getRepealedBy (registerId: string) (fetcher : asyncPageFetcher) = 
        async {
            let! repealedPage = getSeriesPage(registerId, SeriesType.RepealedBy, fetcher)
            let repealedTable = repealedPage |> Result.bind (fun p -> getTableData repealedTableId p |> Result.mapError (fun e -> ScrapeError.HtmlParseError(e)))
            let parsedTable = repealedTable |> Result.bind (fun t -> parseRepealedTable t)
            return parsedTable    
        }


    let getCompilationsList (registerId : string) (fetcher: asyncPageFetcher) =
        async {
            let! htmlDoc = getSeriesPage(registerId, SeriesType.Compilations, fetcher)
            let compilationsTable = htmlDoc |> Result.bind (fun d -> getCompilationsTableData d |> Result.mapError (fun e -> ScrapeError.HtmlParseError(e)))
            let parsedTableData = compilationsTable |> Result.bind (fun d -> parseCompilationsTableData d)
            return parsedTableData 
        }
    let getLatestCompilation (registerId : string) (fetcher : asyncPageFetcher) =
        async {
            let! compList = getCompilationsList (registerId) (fetcher)
            let latest = compList |> Result.map (fun i -> i.getLatestCompilation())
            return latest
        }
    
    let getLatestCompilationOrError (registerId: string) (fetcher) =
        async {
            let! latest = getLatestCompilation registerId fetcher
            let withNoneAsError =
                match latest with
                | Ok(Some(c)) -> Ok(c)
                | Ok(None) -> Error(ScrapeError.NotFound("Could not get latest compilation for Register ID: " + registerId))
                | Error e -> Error(e)
            return withNoneAsError
        }

    let getLatestCompilationRegisterIdAsync (registerId: string) (httpClient : HttpClient) =  getLatestCompilation registerId (createFetcher httpClient) |> Async.StartAsTask
    
    let registerIdRegex = new Regex("/([A-Z0-9]+)$")
    let tryParseRegisterIdFromUrlString (relativeUri: string) = 
        let m = registerIdRegex.Match(relativeUri)
        match m with
        | m when m.Success -> Some(m.Groups[1].Value)
        | _ -> None
       
    let parseLinkNodeToInstrument (node: HtmlNode) =
       let text = node.InnerText
       let href = node.GetAttributeValue("href",null)
       match href with
       | null -> None
       | t ->
            let rid = tryParseRegisterIdFromUrlString t
            match rid with
                | Some(id) -> Some({InstrumentName = text; RegisterId = id})
                | None -> None

    let regexForIdMatch = new Regex("ctl00_MainContent_RadGrid2_ctl00_ctl[0-9]+_hl")
    let getWhatsNew (fetcher: asyncPageFetcher) =
        async {
            let! htmlDoc = getHtmlDocForUrl($"{frlBaseUrl}/WhatsNew")(fetcher)
            let links = htmlDoc |> Result.map (fun h -> h.DocumentNode.Descendants("a"))
            let registerLinks = 
                links 
                |> Result.map (fun links -> links |> Seq.filter (fun link -> link.GetAttributeValue("id",null) <> null && regexForIdMatch.IsMatch(link.GetAttributeValue("id",null)) ))
           
            let nodeInstruments = registerLinks |> Result.map (fun linkList -> (linkList |> Seq.choose (fun i -> parseLinkNodeToInstrument i)))
            return nodeInstruments |> Result.map (fun r -> r |> List.ofSeq )
        }

    let getSeriesInfo(registerId : string) (fetcher: asyncPageFetcher)  = 
        async {
            let! principal = getPrincipal registerId fetcher
            let! compilationsList = principal |> Result.bindAsync (fun r ->  getCompilationsList r.RegisterId fetcher)
            let! repealedByCeasedBy = principal |> Result.bindAsync (fun r ->   getRepealedBy r.RegisterId fetcher)
            let seriesInfo =
                match (compilationsList, principal, repealedByCeasedBy) with 
                | (Ok(c), Ok(p), Ok(r)) ->Ok({ Compilations = c; Principal = p; RepealedBy = r})
                | _ -> Error(ScrapeError.Message("Could not get series info."))
            return seriesInfo
        }
    

    let getSeriesInfoAsync(registerId : string, httpClient: HttpClient) = getSeriesInfo registerId (createFetcher(httpClient)) |> Async.StartAsTask
    
    
    let getInstrumentData(registerId : string) (fetcher: asyncPageFetcher) =
        async {
            let! seriesInfo = getSeriesInfo registerId fetcher
            let! docXBinary =  getInstrumentDocX registerId fetcher
            let packagedResult =
                match seriesInfo with
                | Ok(si) ->
                    match docXBinary with
                    | Ok(b) -> Ok({SeriesInfo = si; DocX = b})
                    | Error(e) -> Error(e) 
                | Error(e) -> Error(e)
            return packagedResult
        }

    

        
       



    
        

        
      

