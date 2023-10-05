namespace FrlUtils

open System.Text.RegularExpressions
open System.Linq
open System

module EmailParsing =
    
    type FrlUpdateType = Amendment | Repeal | Enactment | Compilation

    type FrlEmailUpdateItem = {
        InstrumentName : string;
        RegisterId : string;
        UpdateType : FrlUpdateType;
        Url : Uri;
        Date: NodaTime.LocalDate;
    }

    let parseUpdateType (updateDescription: string) =
        match updateDescription with
        | d when d.Contains("amended")  -> FrlUpdateType.Amendment
        | d when d.Contains("repealed") -> FrlUpdateType.Repeal
        | d when d.Contains("Compilation") -> FrlUpdateType.Compilation
        | _ -> FrlUpdateType.Enactment
    
    let dateRegex = new Regex(@"\d{1,2}\/\d{1,2}\/\d{4}");
    let parseDate(updateDescription: string) = 
        let pattern = NodaTime.Text.LocalDatePattern.CreateWithInvariantCulture("d/M/yyyy")
        let dateString = dateRegex.Match(updateDescription)
        match dateString.Success with
        | true ->
            let parseResult = pattern.Parse(dateString.Value)
            match parseResult.Success with
            | true -> parseResult.Value
            | false -> failwith ( "Could not parse date from line: " + updateDescription)
        | false ->  failwith ("Could not parse date from line: " + updateDescription)
        

// parse to text 
    let rec groupSequentially(input:  'T list, whenToSplit: 'T -> bool) : 'T list list = 
        match input with
        | [] -> List.empty
        | x::xs when whenToSplit x -> groupSequentially(xs,whenToSplit)
        | _ ->
            let groupLines = input |> List.takeWhile (fun i -> not (whenToSplit i)) 
            let remaining = input |> List.skip (groupLines |> List.length )
            groupLines :: groupSequentially(remaining,whenToSplit)

    let isFrlItem (lines: string list) = lines.Last().StartsWith("https://")

    let splitToLines (t : string) = t.Split(Environment.NewLine) |> List.ofArray

    let getItemLineGroups (text: string) =  groupSequentially((splitToLines text), fun line -> String.IsNullOrWhiteSpace(line)) |> List.filter (fun g -> isFrlItem g)
  
    let conditionNameString = new Regex("(Statement of Principles concerning)\s+(.*?)\s+(No\.|\()");
    let parseConditionName line = 
        match conditionNameString.IsMatch(line) with
        | true -> Some(conditionNameString.Match(line).Groups.[2].Value)
        | false -> None
   
    let parseSubject line =
        match parseConditionName line with 
        | Some(c) -> Some(c)
        | None -> Some("Service Determinations")
       
   

    let parseRegisterId (urlLine : string) = (new Uri(urlLine)).Segments.Last()

    let parseLineItemGroup (g : string list) : Result<FrlEmailUpdateItem,string> =
       try
            match g with
            | c when c.Length = 3 -> 
                Ok({
                    InstrumentName = g[0];
                    RegisterId = parseRegisterId(g[2]);
                    UpdateType = parseUpdateType(g[1]);
                     Url = new Uri(g[2]);
                    Date = parseDate(g[1]);
                })
            | c when c.Length = 4-> 
                Ok({
                    InstrumentName = g[0];
                    RegisterId = parseRegisterId(g[3]);
                    UpdateType = parseUpdateType(g[2]);
                    Url = new Uri(g[3]);
                    Date = parseDate(g[2]);
                })
            | _ -> failwith (sprintf "Could not parse line item group: %s " (g.ToString()))
       with
       | _ as ex -> Error("Could not parse: " + System.Environment.NewLine + g.ToString() + System.Environment.NewLine + ex.Message)

        
    let ParseEmailUpdate(bodyText: string) : List<Result<FrlEmailUpdateItem,string>> = getItemLineGroups(bodyText) |> List.map (fun g -> parseLineItemGroup g)
        

    





