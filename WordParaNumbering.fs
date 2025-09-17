namespace FrlUtils

open System.IO
open System.Xml.Linq
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

            let pNodes = doc.DocumentNode.SelectNodes("//p")
            if isNull pNodes then
                []
            else
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
    
    let createNumberingProvider (wordDoc : WordprocessingDocument) : SequentialNumberingProvider =
        let paras = wordDoc.MainDocumentPart.Document.Body.Elements<Paragraph>() |> Seq.toList
        fun (p: Paragraph) ->
            let index = paras |> List.tryFindIndex (fun x -> x.Equals(p))
            match index with
                | Some i -> (i).ToString() 
                | None -> "" // not found, should not happen
                