namespace FrlUtils

open System.IO
open System.Xml.Linq
open DocumentFormat.OpenXml
open DocumentFormat.OpenXml.Packaging
open DocumentFormat.OpenXml.Wordprocessing
open System
open System.Collections.Generic
open System.Linq
open FrlUtils.Domain
open OpenXmlPowerTools

module WordParaNumbering =
    
    open HtmlAgilityPack

    /// Represents a paragraph parsed from HTML exported from Word
    type ParagraphFromHtml = {
        WordId: string
        WordStyle: string
        WordNumberingLogical: string option
        WordNumberingText: string option
    }
    
    
    let addParaIds (docxBytes: byte[]) : byte[] =
        if isNull docxBytes || docxBytes.Length = 0 then
            invalidArg "docxBytes" "Empty input"

        let tmpPath = Path.Combine(Path.GetTempPath(), $"frlutils-{Guid.NewGuid():N}.docx")
        try
            File.WriteAllBytes(tmpPath, docxBytes)

            // Ensure the package handle is closed before reading the file
            do
                use doc = WordprocessingDocument.Open(tmpPath, true)
                let main = doc.MainDocumentPart
                if not (isNull main) && not (isNull main.Document) && not (isNull main.Document.Body) then
                    let body = main.Document.Body
                    for p in body.Descendants<Paragraph>() do
                        // set w14:paraId to a guid always, even if already present
                          let guid = Guid.NewGuid().ToString("D")
                          let attr = OpenXmlAttribute("w14", "paraId", "http://schemas.microsoft.com/office/word/2010/wordml", guid)
                          p.SetAttribute attr 
                       
                    main.Document.Save()

            // Now the file is closed; read the updated bytes
            File.ReadAllBytes(tmpPath)
        finally
            try File.Delete(tmpPath) with _ -> () 
   

    let ofBytesToHtmlString (docxBytes: byte[]) : string =
        if docxBytes = null || docxBytes.Length = 0 then
            invalidArg (nameof docxBytes) "Empty input"

        let wml = new WmlDocument("in-memory.docx", new MemoryStream(docxBytes, 0, docxBytes.Length, false, true))
        let settings = new WmlToHtmlConverterSettings()
        settings.PageTitle <- "Export"
        settings.FabricateCssClasses <- true
        settings.CssClassPrefix <- "pt-"
        settings.RestrictToSupportedLanguages <- false
        settings.RestrictToSupportedNumberingFormats <- false

        let xhtml : XElement = WmlToHtmlConverter.ConvertToHtml(wml, settings)
        "<!DOCTYPE html>\n" + xhtml.ToString(SaveOptions.DisableFormatting) 

    /// Parse Word-exported HTML and return one ParagraphFromHtml per <p> element.
    /// - WordId comes from data-w-paraId on the <p>
    /// - WordStyle comes from data-w-style on the <p>
    /// - WordNumbering comes from data-w-listItemRun on the first child <span> of the <p> (if present)
    let  parseParagraphsFromHtml (html: string) : ParagraphFromHtml list =
        if String.IsNullOrWhiteSpace html then
            []
        else
            let doc = HtmlDocument()
            doc.OptionFixNestedTags <- true
            doc.LoadHtml(html)

            let pNodes = doc.DocumentNode.SelectNodes("//p") |> Seq.filter (fun n -> not (String.IsNullOrEmpty (n.GetAttributeValue("data-w-paraId", ""))))
                    
            [ for p in pNodes do
                let wordId = p.GetAttributeValue("data-w-paraId", "")
                let wordStyle = p.GetAttributeValue("data-w-style", "")

                // Per requirement: numbering is taken from the first span child of the p
                let firstSpan = p.SelectSingleNode("./span[1]")
                let numberingLogical =
                    if isNull firstSpan then None
                    else
                        let v = firstSpan.GetAttributeValue("data-w-listItemRun", null)
                        if String.IsNullOrEmpty v then None else Some v
                let numberingText =
                    if isNull firstSpan then None
                    else
                        let t = firstSpan.InnerText
                        if String.IsNullOrWhiteSpace t then None else Some t

                yield { WordId = wordId; WordStyle = wordStyle; WordNumberingLogical = numberingLogical; WordNumberingText = numberingText } ]
            
    let getMapOfParasToNumbering (docxBytes: byte[]) : Map<string, ParagraphFromHtml> =
        // converted html has sequential numbers of paras in attributes
        let html = ofBytesToHtmlString docxBytes
        let paras = parseParagraphsFromHtml html
        paras |> List.map (fun p -> p.WordId,  p) |> Map.ofList
   
    // the word doc must be opened with MC processing enabled and be at least Office2010 format
    // to have the w14:paraId attributes surfaced in the strongly-typed SDK property
    let getParaId (p: Paragraph) =
            // Prefer the strongly-typed property (available with MC processing);
            let w14ns = "http://schemas.microsoft.com/office/word/2010/wordml"
            // fall back to raw attribute to be extra robust.
            match p.ParagraphId with
            | null ->
                try
                    let a = p.GetAttribute("paraId", w14ns)
                    if String.IsNullOrEmpty a.Value then None else Some a.Value
                with _ ->
                    None
            | sv when isNull sv.Value ->
                let a = p.GetAttribute("paraId", w14ns)
                if String.IsNullOrEmpty a.Value then None else Some a.Value
            | sv -> Some sv.Value 
    
    let printParaIds (docxBytes: byte[]) =
    // Ensure MC processing so w14:* is surfaced to SDK properties
        let settings = OpenSettings()
        settings.MarkupCompatibilityProcessSettings <-
            MarkupCompatibilityProcessSettings(
                MarkupCompatibilityProcessMode.ProcessAllParts,
                FileFormatVersions.Office2010)

        use ms  = new MemoryStream(docxBytes)      // read-only open is fine
        use doc = WordprocessingDocument.Open(ms, false, settings)

        let w14ns = "http://schemas.microsoft.com/office/word/2010/wordml"

        let tryParaId (p: Paragraph) =
            // Prefer the strongly-typed property (available with MC processing);
            // fall back to raw attribute to be extra robust.
            match p.ParagraphId with
            | null -> 
                let a = p.GetAttribute("paraId", w14ns)
                if String.IsNullOrEmpty a.Value then None else Some a.Value
            | sv when isNull sv.Value ->
                let a = p.GetAttribute("paraId", w14ns)
                if String.IsNullOrEmpty a.Value then None else Some a.Value
            | sv -> Some sv.Value

        let allParas : seq<Paragraph> =
            seq {
                let body = doc.MainDocumentPart.Document.Body
                if not (isNull body) then yield! body.Descendants<Paragraph>()
                for hp in doc.MainDocumentPart.HeaderParts  do yield! hp.Header.Descendants<Paragraph>()
                for fp in doc.MainDocumentPart.FooterParts  do yield! fp.Footer.Descendants<Paragraph>()
                match doc.MainDocumentPart.FootnotesPart with
                | null -> ()
                | fp   -> yield! fp.Footnotes.Descendants<Paragraph>()
                match doc.MainDocumentPart.EndnotesPart with
                | null -> ()
                | ep   -> yield! ep.Endnotes.Descendants<Paragraph>()
            }

        allParas
        |> Seq.mapi (fun i p -> i, tryParaId p |> Option.defaultValue "<none>")
        |> Seq.iter (fun (i, pid) -> printfn "p[%04d] paraId=%s" i pid) 