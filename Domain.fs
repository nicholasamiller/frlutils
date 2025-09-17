namespace FrlUtils

open DocumentFormat.OpenXml.Wordprocessing
open NodaTime
open Newtonsoft.Json.Linq
open NodaTime.Text
open System.IO
open System
open Newtonsoft.Json
open Newtonsoft.Json.Converters

module Domain =
    

    // A function that, given a Paragraph, returns the order it appears in the document, starting from 0
    type SequentialNumberingProvider = Paragraph -> string 
           
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
        }
        member this.toResponseJson() =
         
         

            new JObject(
                new JProperty("title", this.Title),
                new JProperty("registeredDate", formatDate this.RegisteredDate),
                new JProperty("registerId", this.RegisterId),
                new JProperty("compilationNumber", formatCompilationNumber this.CompilationNumber ),
                new JProperty("startDate", formatDate this.StartDate),
                new JProperty("endDate", formatDateOpt this.EndDate)
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
    

   
    type NameHistory = {
        name: string
        start: DateTime
    }
    
    type Affect = AsMade | Amend | Repeal | Cease | ChangeDate
    
    type AffectingTitle = {
        titleId: string
        name: string
        provisions: string
    }
    
    type DateChange = {
        fromDate: DateTime
        toDate: DateTime
    }

    type StatusReason = {
        affect: Affect
        markdown: string
        affectedByTitle: AffectingTitle option
        amendedByTitle: AffectingTitle option
        dateChanged: DateChange option
    }


    type Status =  InForce | Ceased | Repealed | NeverEffective
    type StatusHistory = {
        status: Status
        start: DateTime
        reasons: StatusReason list
    }
    
    type Collection =  Act | LegislativeInstrument | NotifiableInstrument | AdministrativeArrangementsOrder | Constitution | ContinuedLaw | Gazette | PrerogativeInstrument

    type SubCollection =  Regulations | CourtRules | Rules | ByLaws
    
    
    type FrlSeriesType =  Act | SR | SLI
   
    type LegislativeInstrumentInfo = {
        id: string
        makingDate: DateTime
        collection: Collection
        subCollection: SubCollection option
        isPrincipal: bool
        isInForce: bool
        status: Status
        hasCommencedUnincorporatedAmendments: bool
        asMadeRegisteredAt: DateTime
        optionalSeriesNumber: string option
        nameHistory: NameHistory list
        namePossibleFuture: NameHistory list
        statusHistory: StatusHistory list
        statusPossibleFuture: StatusHistory list
    }


    type VersionInfo = {
        titleId: string option
        start: DateTime
        retrospectiveStart: DateTime
        endDate: DateTime option
        isLatest: Boolean
        isCurrent: Boolean
        name: string option
        status: Status
        registerId: string option
        compilationNumber: int option
        hasUnincorporatedAmendments: bool
        reasons: StatusReason list
    }


    


