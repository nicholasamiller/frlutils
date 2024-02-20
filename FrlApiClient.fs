namespace FrlUtils  

open System.IO
open System.Net.Http
open FrlUtils.Errors
open System.Globalization
open NodaTime
open Newtonsoft.Json.Linq
open System
open System.Web



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
