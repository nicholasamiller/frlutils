namespace FrlUtils
open DocumentFormat.OpenXml.Wordprocessing
open DocumentFormat.OpenXml
open Newtonsoft.Json
open System.IO
open DocumentFormat.OpenXml.Packaging
open System.Collections.Generic
open System.Linq
open System.Text
open Domain
open Errors


module DocParsing =
    
    
  
        
   
    let getWordDoc (ms : MemoryStream) =
        WordprocessingDocument.Open(ms,false)

    let getBodyParts (ba : byte[]) =
        use ms = new MemoryStream(ba)
        let wd = getWordDoc ms
        wd.MainDocumentPart.Document.Body.OfType<OpenXmlElement>()

    let getCellCount(tableElement : Table) =
        tableElement.ChildElements |> Seq.filter (fun i -> i :? TableRow) |> Seq.map (fun i -> i.OfType<TableCell>().Count()) |> Seq.max
   
    
    let stringifyPara (p : Paragraph) = 
        
        let stringifyLeaf (x : OpenXmlElement) =
            match x with
            | :? NoBreakHyphen -> "\u2011"
            | :? Text as t -> t.Text
            | _ -> ""
        
        let mutable sb = new StringBuilder();
        let visitor x = sb.Append(stringifyLeaf x) |> ignore

        let rec recurseTree (x : OpenXmlElement) (leafVisitor : OpenXmlElement -> unit) =
            let isLeaf = not (x.HasChildren)
            match isLeaf with
            | true -> leafVisitor x |> ignore
            | false ->
                let children = x.ChildElements
                children |> Seq.iter (fun c -> recurseTree c leafVisitor)

        recurseTree p visitor |> ignore
        sb.ToString()

   
 
    let stringifyTableCellText (t: TableCell) =
        
        let rec getText (x : OpenXmlElement) = 
            match x with
            | :? NoBreakHyphen -> "-"
            | :? Text as t -> t.Text
            | :? Paragraph as p -> 
                let texts = p.ChildElements |> Seq.map ( fun c -> getText c) |> String.concat ""
                let withLineBreak = texts + System.Environment.NewLine
                withLineBreak
                
            | x -> "" 
        let text = t.Descendants() |> Seq.map (fun c -> (getText c)) |> String.concat ""
        text.Trim()
        

    
    let tableRowToRow (tr : TableRow) = tr.OfType<TableCell>() |> Seq.map (fun i -> stringifyTableCellText i) |> List.ofSeq
        

    let getTablesBetweenParas (paraStartText : string) (paraEndText : string) (elements: IEnumerable<OpenXmlElement>)  =     
        let isMatchingPara (x: OpenXmlElement) (s: string)=
            match x with 
            | :? Paragraph as p -> ((stringifyPara p) = s)
            | _ -> false
        
        let followingElements = elements |> Seq.skipWhile (fun i -> not (isMatchingPara i paraStartText))
        let inBetweenTableElements = followingElements |> Seq.takeWhile (fun i -> not (isMatchingPara i paraEndText)) |> Seq.filter (fun i -> i :? Table) |> Seq.map (fun i -> i :?> Table)
        let maxColumns = inBetweenTableElements |> Seq.map (fun i-> getCellCount i) |> Seq.max
        let rows = inBetweenTableElements |> Seq.collect (fun i -> i.OfType<TableRow>()) |> Seq.map (fun i -> {items = tableRowToRow i}) |> Seq.filter (fun i -> i.items.Count() = maxColumns) |> List.ofSeq
        let h = rows |> List.head
        let i = rows |> List.tail
        { headerRow = h; bodyRows = i}
            
    
    let getTables (paraStartText: string) (paraEndText: string) (docxBinary : byte[]) : Result<LegTable,DocParsingError>=
        try
            Ok( getTablesBetweenParas paraStartText paraEndText (getBodyParts docxBinary))
        with 
        | ex -> Error(DocParsingError.Message("Could not get tables."))
            
    


     

