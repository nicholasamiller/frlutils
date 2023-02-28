namespace FrlUtils

open Domain
open System.Net
open HtmlAgilityPack
open Shoshin.HtmlUtils.Errors

module Errors =


    type XPathFoundNoNodes = {Html : string; XPath : string }
    type MissingAttribute = {Node: HtmlNode; Attribute: string }

    type DocParsingError = 
    | Exception of System.Exception
    | Message of string
    | RowParseError of LegRow * string
    | TableParseError of LegTable * string
   
     type ScrapeError =
        | HtmlParseError of Shoshin.HtmlUtils.Errors.HtmlParseError
        | DocParsingError of DocParsingError
        | NotFound of string
        | UnexpectedHttpStatusCode of HttpStatusCode
        | Exception of System.Exception
        | Message of string
        | XPath of XPathFoundNoNodes
        | MissingAttribute of MissingAttribute
        | CompositeScrapeError of ScrapeError list


  
