namespace FrlUtils  

open System.IO
open System.Net.Http
open FrlUtils.Errors
open System.Globalization
open NodaTime
open Newtonsoft.Json.Linq
open System
open System.Web
open FrlUtils.Domain



module FrlApiClient =
    
    type asyncPageFetcher =  string ->  Async<Result<Stream,ScrapeError>>
    
    [<Literal>]
    let private frlBaseUrl = "https://www.legislation.gov.au"
    
    
    open NodaTime

    let getTodaysDateInSydneyAsIsoString () =
        let now = SystemClock.Instance.GetCurrentInstant()
        let sydneyZone = DateTimeZoneProviders.Tzdb["Australia/Sydney"]
        let sydneyToday = now.InZone(sydneyZone).LocalDateTime.Date
        sydneyToday.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)


    let createApiFetcher (httpClient: HttpClient) =
        fun (url:string) -> async {
            try
                let! response = httpClient.GetAsync(url) |> Async.AwaitTask
                let! contentStream = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
                return Ok contentStream
            with
            | ex -> return Error(ScrapeError.Exception(ex))
        }

    let getWordDocInstrumentById(id: string) (fetcher: asyncPageFetcher) =
        async {
            
            let todaysDate = getTodaysDateInSydneyAsIsoString()
            let url = $"{frlBaseUrl}/{id}/{todaysDate}/{todaysDate}/original/word"
            // read stream as binary and convert to word doc
            let! contentResult = fetcher url
            match contentResult with
            | Ok stream ->
                try
                    use ms = new MemoryStream()
                    do! stream.CopyToAsync(ms) |> Async.AwaitTask
                    ms.Seek(0L, SeekOrigin.Begin) |> ignore
                    let array = ms.ToArray()
                    return Ok array
                with
                | ex -> return Error(ScrapeError.Exception(ex)) // Assuming Error takes a string message
            | Error e -> return Error(e)
        }

    
    let deserializeTitle(titleJObject : JObject) : Result<LegislativeInstrumentInfo, FrlApiDeserialisationError> =
       
        let stringToAffect (input: string) : Affect =
            match input with
            | "AsMade" -> AsMade
            | "Amend" -> Amend
            | "Repeal" -> Repeal
            | "Cease" -> Cease
            | "ChangeDate" -> ChangeDate
            | _ -> failwithf "Unsupported Affect value: %s" input
                    
        let stringToCollection (input: string) : Collection =
            match input with
            | "Act" -> Collection.Act
            | "LegislativeInstrument" -> LegislativeInstrument
            | "NotifiableInstrument" -> NotifiableInstrument
            | "AdministrativeArrangementsOrder" -> AdministrativeArrangementsOrder
            | "Constitution" -> Constitution
            | "ContinuedLaw" -> ContinuedLaw
            | "Gazette" -> Gazette
            | "PrerogativeInstrument" -> PrerogativeInstrument
            | _ -> failwithf "Unsupported Collection value: %s" input
        
        let stringToSubCollection (input: string) : SubCollection option =
            match input with
            | null | "" -> None
            | "Regulations" -> Some Regulations
            | "CourtRules" -> Some CourtRules
            | "Rules" -> Some Rules
            | "ByLaws" -> Some ByLaws
            | _ -> failwithf "Unsupported SubCollection value: %s" input

        let stringToStatus (input: string) : Status =
            match input with
            | "InForce" -> InForce
            | "Ceased" -> Ceased
            | "Repealed" -> Repealed
            | "NeverEffective" -> NeverEffective
            | _ -> failwithf "Unsupported Status value: %s" input

        let stringToFrlSeriesType (input: string) : FrlSeriesType option =
            match input with
            | null | "" -> None
            | "Act" -> Some Act
            | "SR" -> Some SR
            | "SLI" -> Some SLI
            | _ -> failwithf "Unsupported FrlSeriesType value: %s" input
        
        try
            let id = titleJObject.["id"].Value<string>()
            let name = titleJObject.["name"].Value<string>()
            let makingDate = titleJObject.["makingDate"].Value<DateTime>()
            let collection = titleJObject.["collection"].Value<string>() |> stringToCollection
            let subCollection = titleJObject.["subCollection"].Value<string>() |> stringToSubCollection
            let isPrincipal = titleJObject.["isPrincipal"].Value<bool>()
            let isInForce = titleJObject.["isInForce"].Value<bool>()
            let status = titleJObject.["status"].Value<string>() |> stringToStatus
            let hasCommencedUnincorporatedAmendments = titleJObject.["hasCommencedUnincorporatedAmendments"].Value<bool>()
            let asMadeRegisteredAt = titleJObject.["asMadeRegisteredAt"].Value<DateTime>()
            let optionalSeriesNumber =
                match titleJObject.["optionalSeriesNumber"].HasValues with
                | true -> Some(titleJObject.["optionalSeriesNumber"].Value<string>())
                | false -> None     
            let nameHistory = 
                titleJObject.["nameHistory"].Children()
                |> Seq.map (fun j -> 
                    let name = j.["name"].Value<string>()
                    let start = j.["start"].Value<DateTime>()
                    {name = name; start = start}
                )
                |> Seq.toList
            
            let namePossibleFuture = 
                titleJObject.["namePossibleFuture"].Children()
                |> Seq.map (fun j -> 
                    let name = j.["name"].Value<string>()
                    let start = j.["start"].Value<DateTime>()
                    {name = name; start = start}
                )
                |> Seq.toList

            let parseStatusHistoryItem (j : JToken) =
                let status = stringToStatus (j.["status"].Value<string>())
                let start = j.["start"].Value<DateTime>() 
                let reasons = 
                    j.["reasons"].Children()
                    |> Seq.map (fun r ->
                        let affect = 
                            r.["affect"].Value<string>() |> stringToAffect
                        let markdown = r.["markdown"].Value<string>()
                        let affectedByTitle = 
                            match r.["affectedByTitle"].HasValues with
                            | true -> 
                                let titleId = r.["affectedByTitle"].["titleId"].Value<string>()
                                let name = r.["affectedByTitle"].["name"].Value<string>()
                                let provisions = r.["affectedByTitle"].["provisions"].Value<string>()
                                Some {titleId = titleId; name = name; provisions = provisions}
                            | false -> None
                        let amendedByTitle = 
                            match r.["amendedByTitle"].HasValues with
                            | true -> 
                                let titleId = r.["amendedByTitle"].["titleId"].Value<string>()
                                let name = r.["amendedByTitle"].["name"].Value<string>()
                                let provisions = r.["amendedByTitle"].["provisions"].Value<string>()
                                Some {titleId = titleId; name = name; provisions = provisions}
                            | false -> None
                        let dateChanged = 
                            match r.["dateChanged"].HasValues with
                            | true -> 
                                let fromDate = r.["dateChanged"].["fromDate"].Value<DateTime>()
                                let toDate = r.["dateChanged"].["to"].Value<DateTime>()
                                Some({fromDate = fromDate; toDate = toDate})
                            | false -> None
                        {affect = affect; markdown = markdown; affectedByTitle = affectedByTitle; amendedByTitle = amendedByTitle; dateChanged = dateChanged}
                    ) |> Seq.toList
                {status = status; start = start; reasons = reasons}
            

            let statusHistory =
                titleJObject.["statusHistory"].Children()
                |> Seq.map (fun j -> parseStatusHistoryItem j)
                |> Seq.toList
            
            let statusPossibleFuture =
                titleJObject.["statusPossibleFuture"].Children()
                |> Seq.map (fun j -> parseStatusHistoryItem j)
                |> Seq.toList

            Ok({
                id = id;
                makingDate = makingDate;
                collection = collection;
                subCollection = subCollection;
                isPrincipal = isPrincipal;
                isInForce = isInForce;
                status = status;
                hasCommencedUnincorporatedAmendments = hasCommencedUnincorporatedAmendments;
                asMadeRegisteredAt = asMadeRegisteredAt;
                optionalSeriesNumber = optionalSeriesNumber;
                nameHistory = nameHistory;
                namePossibleFuture = nameHistory;
                statusHistory = statusHistory;
                statusPossibleFuture = statusPossibleFuture;
                })

        with
        | ex -> Error(FrlApiDeserialisationError.Message(ex.Message))
        
            


    // run odata query and page through results, following the next link, aggregating results
    let rec runODataQuery (fetcher: asyncPageFetcher) (url: Uri) (results: JObject list) : Async<Result<JObject list,ScrapeError>> =
            
            let withSkipParameterValue (uri: Uri) (skipValue: int) : Uri =
                let query = HttpUtility.ParseQueryString(uri.Query)
                query.["$skip"] <- skipValue.ToString()
                let newQuery = query.ToString()
                let uriBuilder = UriBuilder(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath)
                uriBuilder.Query <- newQuery
                uriBuilder.Uri
            
            
            async {
                let! contentResult = fetcher (url.ToString())
                match contentResult with
                | Ok stream ->
                    try
                        use sr = new StreamReader(stream)
                        let json = sr.ReadToEnd()
                        let jobj = JObject.Parse(json)
                        // get values as array of JObjects
                        let values = jobj.["value"].Children<JObject>() |> Seq.toList
                        // check if there is a next link
                        let nextLinkKey = "@odata.nextLink"
                        match jobj.[nextLinkKey] with
                        | null -> // last page
                            return Ok (results @ values)
                        | nextLinkString -> 
                        // get the skip parameter from the next link
                            let nextLinkAsUri = new Uri(nextLinkString.Value<string>())
                            let queryParamsInNextLink = HttpUtility.ParseQueryString(nextLinkAsUri.Query)
                            let skipValueOrNull = queryParamsInNextLink.Get("$skip")
                            match skipValueOrNull with
                            | null -> // last page
                                return Ok (results @ values)
                            | "" -> // last page
                                return Ok (results @ values)
                            | v -> // more pages
                                let skip = int v
                                // add skip param tu url parameter, not nextLink
                                let nextLinkWithSkip = withSkipParameterValue url skip
                                return! runODataQuery fetcher (nextLinkWithSkip) (results @ values)
                        with
                    | ex -> return Error(ScrapeError.Exception(ex))
                | Error e -> return Error(e)
            }
