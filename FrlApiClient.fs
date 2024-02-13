namespace FrlUtils

open System.IO
open System.Net.Http
open FrlUtils.Errors
open System.Globalization
open NodaTime



module FrlApiClient =
    
    [<Literal>]
    let private frlBaseUrl = "https://www.legislation.gov.au"
    
    type asyncPageFetcher =  string ->  Async<Result<Stream,ScrapeError>>
    
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
