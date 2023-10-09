namespace FrlUtils

open NodaTime
open Newtonsoft.Json.Linq
open NodaTime.Text



module Domain =

       
    let formatDate (d: LocalDate) = LocalDatePattern.Iso.Format(d)
    
    let formatDateOpt (d: LocalDate option) =
        match d with 
        | Some(i) -> formatDate i
        | None -> null
    
    let formatCompilationNumber (n : int option) =
        match n with 
        | Some(n) -> n.ToString()
        | None -> null

    type SeriesType = Compilations | PrincipalAndAmendments | RepealedBy

    type PrincipalActOrInstrument = {
        InstrumentName: string;
        RegisterId: string;
    }

    type DocumentType = PDF | WordDocx | WordDoc
    type Document = {
        Type: DocumentType;
        Content: byte[];
    }



    
    type LegRow = {
        items : string list    
    }
    
    type LegTable = {
        headerRow : LegRow
        bodyRows: LegRow list
    } 
 
    type CompilationInfo =
        {
            Title : string;
            RegisteredDate : LocalDate;
            RegisterId: string;
            CompilationNumber:  int option;
            StartDate: LocalDate;
            EndDate: LocalDate option;
            IncorporatingAmendmentsTo: string
        }
        member this.toResponseJson() =
         
         

            new JObject(
                new JProperty("title", this.Title),
                new JProperty("registeredDate", formatDate this.RegisteredDate),
                new JProperty("registerId", this.RegisterId),
                new JProperty("compilationNumber", formatCompilationNumber this.CompilationNumber ),
                new JProperty("startDate", formatDate this.StartDate),
                new JProperty("endDate", formatDateOpt this.EndDate),
                new JProperty("incorporatingAmendmentsTo", this.IncorporatingAmendmentsTo)
            )
            

    type CompilationsHistory = 
        {
            Compilations : CompilationInfo list
        }
        member this.getLatestCompilation() = 
            this.Compilations 
                |> List.sortByDescending (fun c -> c.StartDate)
                |> List.tryFind (fun c -> c.EndDate.IsNone)
    
    type SeriesInfo =
        {
            Principal : PrincipalActOrInstrument
            Compilations: CompilationsHistory
            RepealedBy : PrincipalActOrInstrument option
        }
    

    type ScrapedInstrument =
        {
            SeriesInfo: SeriesInfo;            
            DocX: byte[];
        }

    
        
        
