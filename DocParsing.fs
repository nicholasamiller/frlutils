namespace FrlUtils
open System
open System.Diagnostics
open System.Xml
open System.Xml.Linq
open DocumentFormat.OpenXml.Wordprocessing
open DocumentFormat.OpenXml
open FrlUtils.WordParaNumbering
open Microsoft.FSharp.Core
open Newtonsoft.Json
open System.IO
open DocumentFormat.OpenXml.Packaging
open System.Collections.Generic
open System.Linq
open System.Text
open Domain
open Errors


module DocParsing =
            
    type DocNode = {
        Element: OpenXmlElement;
        Parent: DocNode option;
        mutable Children: DocNode list;
    }
    
    with 
        member this.PrettyPrint() =
            let rec recurse (node : DocNode) (indent : int) =
                let indentString = String.replicate indent " "
                let elementString = node.Element.InnerText 
                let childrenString = node.Children |> List.map (fun i -> recurse i (indent + 2)) |> String.concat ""
                indentString + elementString + System.Environment.NewLine + childrenString
            recurse this 0
            
        member this.PrettyPrintWithParaNumbering(provider: ParagraphNumberTextProvider) =
            let rec recurse (node : DocNode) (indent : int) =
                
                let indentString = String.replicate indent " "
                let elementString =  node.Element.InnerText
                let paragraph = match node.Element with 
                                | :? Paragraph as p -> p
                                | _ -> null
                
                 
                let paraNumberText = provider paragraph
               
                let elementString = 
                    match paraNumberText with
                    | Some(num) -> sprintf "%s %s" num elementString
                    | None -> elementString
                 
                let childrenString = node.Children |> List.map (fun i -> recurse i (indent + 2)) |> String.concat ""
                indentString + elementString + System.Environment.NewLine + childrenString
            recurse this 0 

    let findFirstNode (node: DocNode) (predicate : DocNode -> bool) : DocNode option =
            let rec recurse (node : DocNode) =
                let isMatch = predicate node
                match isMatch with
                | true -> Some(node)
                | false -> 
                    let children = node.Children
                    let childResults = children |> List.map (fun i -> recurse i) |> List.filter (fun i -> i.IsSome) 
                    match childResults with
                    | [] -> None
                    | _ -> childResults |> List.head
            recurse node
    
    // do bfs of all nodes matching predicate
    let findAllNodes (node: DocNode) (predicate : DocNode -> bool) : DocNode list =
        let rec recurse (node : DocNode) =
            let isMatch = predicate node
            let children = node.Children
            let childResults = children |> List.collect (fun i -> recurse i) 
            match isMatch with
            | true -> node :: childResults
            | false -> childResults
        recurse node
    
    let getWordDoc (ms : MemoryStream) =
        let settings = new OpenSettings()
        settings.MarkupCompatibilityProcessSettings <-
            MarkupCompatibilityProcessSettings(
                MarkupCompatibilityProcessMode.ProcessAllParts,
                FileFormatVersions.Office2010) // or Office2013/2016 if you prefer
        WordprocessingDocument.Open(ms,false, settings)
    
    type WordData = {
        WordDoc: WordprocessingDocument;
        ParaNumberTextProvider: ParagraphNumberTextProvider
        LogicalNumberingProvider: ParagraphNumberTextProvider
    }
    

    
    let getWordDocWithParaTextProvider (docxBytes: byte[]) : WordprocessingDocument * (Paragraph -> string option) =
        let wordDocWithParaIdsAdded = WordParaNumbering.addParaIds docxBytes
        let wordDoc = getWordDoc (new MemoryStream(wordDocWithParaIdsAdded, 0, wordDocWithParaIdsAdded.Length, false, true))
        let mapOfParaIds = WordParaNumbering.getMapOfParasToNumbering wordDocWithParaIdsAdded
                    
        (wordDoc,fun (p : Paragraph) ->
            match getParaId p with
            | None -> None
            | Some pid ->
                match mapOfParaIds.TryFind pid with
                | None -> None
                | Some paraFromHtml -> paraFromHtml.WordNumberingText)
    let getWordData (docxBytes: byte[]) : WordData =
        let wordDocWithParaIdsAdded = WordParaNumbering.addParaIds docxBytes
        let wordDoc = getWordDoc (new MemoryStream(wordDocWithParaIdsAdded, 0, wordDocWithParaIdsAdded.Length, false, true))
        let html = ofBytesToHtmlString wordDocWithParaIdsAdded
        let paras = parseParagraphsFromHtml html
        let mapOfLogicalNumbering = paras |> List.choose (fun p -> match p.WordNumberingLogical with | Some(n) -> Some(p.WordId, n) | None -> None) |> Map.ofList
        let mapOfParaIds = paras |> List.map (fun p -> p.WordId,  p) |> Map.ofList
        let logicalNumberingProvider = fun (p : Paragraph) ->
            match getParaId p with
            | None -> None
            | Some pid ->
                match mapOfLogicalNumbering.TryFind pid with
                | None -> None
                | Some num -> Some(num)
        let paraNumberTextProvider = fun (p : Paragraph) ->
            match getParaId p with
            | None -> None
            | Some pid ->
                match mapOfParaIds.TryFind pid with
                | None -> None
                | Some paraFromHtml -> paraFromHtml.WordNumberingText
        {WordDoc = wordDoc; ParaNumberTextProvider = paraNumberTextProvider; LogicalNumberingProvider = logicalNumberingProvider}
        
        
    let getBodyParts (ba : byte[]) =
        use ms = new MemoryStream(ba)
        let wd = getWordDoc ms
        wd.MainDocumentPart.Document.Body.OfType<OpenXmlElement>()
                
    
    let getParagraphStyle (p: Paragraph) =
        match p.ParagraphProperties with
        | null -> None
        | ppr -> 
            match ppr.ParagraphStyleId with
            | null -> None
            | id -> Some(id.Val.Value)
    
    let getElementStyle (e : OpenXmlElement) =
        match e with 
        | :? Paragraph as p -> getParagraphStyle p
        | _ -> None
        
    let getParagraphOutlineLevel (p: Paragraph) (styleList: string list) =
        let style = getParagraphStyle p 
        match style with
        | None -> None
        | s -> styleList |> List.tryFindIndex (fun i -> i = s.Value)
    
    let getElementOutlineLevel (e : OpenXmlElement) (styleList: string list) =
        match e with
        | :? Paragraph as p -> getParagraphOutlineLevel p styleList
        | _ -> None
    
    let getNodeLevel (node : DocNode) (styleList: string list) = getElementOutlineLevel node.Element styleList
    
    let buildStyleBasedParaLevelProvider (styleList: string list) : ParaLevelProvider =
        fun (e: OpenXmlElement) -> getElementOutlineLevel e styleList
    
    let unwindToNextAncestor (node: DocNode) (ancestorsStack : Stack<DocNode>) (cplp: ParaLevelProvider) (levelOffset: int) =
      
        match cplp(node.Element) with 
        | None ->
            while (ancestorsStack.Count > 1 && (cplp(ancestorsStack.Peek().Element).IsNone)) do
                ancestorsStack.Pop() |> ignore
        | Some(level) ->
            
            let shouldPop = fun (n : DocNode) -> 
                match cplp(n.Element) with 
                | None -> true // remove nodes with no level, to get back to the last node with a level
                | Some(nl) -> nl + levelOffset >= level
                
            while (ancestorsStack.Count > 1 && shouldPop (ancestorsStack.Peek())) do
                ancestorsStack.Pop() |> ignore
    
    let parseElementListToTree (rootElement: OpenXmlElement) (subsequentElements: OpenXmlElement list) (cplp:
            ParaLevelProvider) (op: ParaLevelOffsetProvider)  : DocNode =
        let rootNode = {Element = rootElement; Children = []; Parent = None}
        let ancestorStack = new Stack<DocNode>()
        ancestorStack.Push(rootNode)
        for e in subsequentElements do
           let levelOffset = op e
           let newNode = {Element = e; Children = []; Parent = None}
           unwindToNextAncestor newNode ancestorStack cplp levelOffset  
           let parent = ancestorStack.Pop()
           let newNode = {newNode with Parent = Some(parent)}
           parent.Children <- parent.Children @ [newNode]
           ancestorStack.Push(parent)
           ancestorStack.Push(newNode)
        rootNode 
    
     
    
    
    let parseElementListToTreeResult (rootElement: OpenXmlElement) (subsequentElements: OpenXmlElement list) (cplp:
            ParaLevelProvider) (op: ParaLevelOffsetProvider): Result<DocNode,DocParsingError> =
        try
            Ok(parseElementListToTree rootElement subsequentElements cplp op)
        with 
        | ex -> Error(DocParsingError.Exception(ex)) 
   
    let parseSectionParaToTree (paraList : Paragraph list) (cplp: ParaLevelProvider) (op: ParaLevelOffsetProvider) : Result<DocNode,DocParsingError>       =    
        match paraList with
        | [] -> Error(DocParsingError.Message("No paragraphs provided"))
        | x :: xs -> parseElementListToTreeResult (x :> OpenXmlElement) (xs |> List.map (fun p -> p :> OpenXmlElement)) cplp op
      
    // given a leaf docnode and a para numberer, get the full para number text by concatenating all ancestor para numbers
    let getFullParaNumberText (leafNode: DocNode) (provider: ParagraphNumberTextProvider) : string option =
        let rec recurse (node: DocNode) (acc: string list) =
            let para = match node.Element with 
                        | :? Paragraph as p -> p
                        | _ -> null
            let paraNumberText = provider para
            let newAcc = 
                match paraNumberText with
                | Some(num) -> num :: acc
                | None -> acc
            match node.Parent with
            | None -> newAcc
            | Some(parent) -> recurse parent newAcc
        let parts = recurse leafNode []
        match parts with
        | [] -> None
        | _ -> Some(String.concat "" parts)
  
    let hasStyle (p : Paragraph) (styleId : string) =
        match p.ParagraphProperties with
        | null -> false
        | ppr -> match ppr.ParagraphStyleId with
                 | null -> false
                 | id -> id.Val.Value = styleId
        
      
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
        sb.ToString().Trim()

   
    let buildSyntheticRootElementForStyle (styleName : string) (elements: OpenXmlElement list) =
        let elementsWithStyle = elements |> Seq.filter (fun e -> getElementStyle e = Some(styleName) && e.LocalName = "p")  |> Seq.cast<Paragraph> |> Seq.toList
        match elementsWithStyle with
        | [] -> None
        | _ ->
            let elementTextConcatenated = elementsWithStyle  |> Seq.map (fun p -> p |> stringifyPara ) |> String.concat " "
            let paraStyle = new ParagraphStyleId()
            paraStyle.Val <- styleName
            let paraProperties = new ParagraphProperties()
            paraProperties.ParagraphStyleId <- paraStyle
            let text = new Text(elementTextConcatenated)
            let run = new Run()
            run.AddChild(text) |> ignore
            Some(new Paragraph(run, paraProperties))
   
 
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
    
    
    let getSectionParagraphs(name: string, styleSequences: string list, styleLevel : int, wordDoc : WordprocessingDocument) =
        let allParas = wordDoc.MainDocumentPart.Document.Body.Elements<Paragraph>() |> Seq.filter (fun p -> not (String.IsNullOrWhiteSpace(stringifyPara p))) |> Seq.toList
        let isStart (p: Paragraph) = stringifyPara p = name && getParagraphStyle p = Some(styleSequences.[styleLevel])
        let isEnd (p: Paragraph) =
            let outlineLevel = getElementOutlineLevel p styleSequences
            match outlineLevel with
            | Some(lv) when lv <= styleLevel -> true
            | _ -> false
        let indexOfStart = allParas |> Seq.tryFindIndex isStart
        match indexOfStart with
        | None -> []
        | Some(i) ->
            let startPara = allParas.[i]
            let remaining = allParas |> Seq.skip (i + 1) |> Seq.takeWhile (fun p -> not (isEnd p)) |> Seq.toList
            startPara :: remaining
        
        
    
     

