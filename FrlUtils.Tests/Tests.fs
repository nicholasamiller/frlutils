﻿namespace FrlUtilsTests
open Microsoft.VisualStudio.TestTools.UnitTesting
open System.Net.Http
open FrlUtils.WebScraping
open FrlUtils.DocParsing
open FrlUtils.EmailParsing
open System.IO
open System.Collections.Generic
open DocumentFormat.OpenXml.Wordprocessing
open DocumentFormat.OpenXml
open FrlUtils
open System
open Newtonsoft.Json.Linq


[<TestClass>]
type TestClass () =

    let mockFetcher : asyncPageFetcher = fun url -> async { 
       
       let d = Map [
            ("https://www.legislation.gov.au/Details/F2021C00349/Download", "downloadPage.html");
            ("https://www.legislation.gov.au/Details/F2021C00349/0040fe4e-c964-498d-b282-bd37647b4cd3", "F2021C00349.docx");
            ("https://www.legislation.gov.au/Series/C2004A03268/Compilations", "veaCompilationsPage.html");
            ("https://www.legislation.gov.au/Series/C2004A03268/Amendments", "veaPrincipalPage.html");
            ("https://www.legislation.gov.au/Series/F2022L00663/RepealedBy", "noRepealedBy.html");
            ("https://www.legislation.gov.au/Series/F2014L00022/RepealedBy", "repealedBy.html");
            ("https://www.legislation.gov.au/WhatsNew", "whatsNew.html");
       ]

       return
        match d.TryFind url with
        | None -> failwith "Test data not found"
        | Some(r) -> Ok(new MemoryStream(File.ReadAllBytes($"TestData/{r}")))
    }


    [<TestMethod>]
    member this.TestGetDocX() =
          let result = getInstrumentUnauthorisedDoc "F2021C00349" mockFetcher |> Async.RunSynchronously
          Assert.IsNotNull result

  
    [<TestMethod>]
    member this.TestTableParser() = 
        let testDocument = (System.IO.File.ReadAllBytes("TestData/F2022C00414.docx"))
        let result = getTablesBetweenParas "Schedule 1—Warlike service" "Endnotes" (getBodyParts (testDocument))
        printfn "%s" (result.ToString())
        Assert.IsTrue(result.headerRow.items.Length = 5)
        Assert.IsTrue(result.bodyRows.Length = 23)
     
     
    [<TestMethod>]
    member this.ParseEmailUpdate() =
        let testData = File.ReadAllText("Testdata/EmailUpdateSample.txt")
        let result = ParseEmailUpdate(testData)
        Assert.IsTrue(result.Length > 0)


    [<TestMethod>]
    member this.ParseCompilationsPage() =
        let result = FrlUtils.WebScraping.getCompilationsList "C2004A03268" mockFetcher |> Async.RunSynchronously
        match result with
        | Ok(r) -> Assert.IsTrue(r.Compilations.Length = 10)
        | _ -> Assert.Fail()
     
    [<TestMethod>]
    member this.GetLatestCompilation() =
        let result = FrlUtils.WebScraping.getLatestCompilation "C2004A03268" mockFetcher  |> Async.RunSynchronously
        match result with
        | Ok(r) -> Assert.IsTrue(r.Value.RegisterId = "C2022C00150")
        | _ -> Assert.Fail()
    
    [<TestMethod>]
    member this.GetPrincipalData() =
        let result = FrlUtils.WebScraping.getPrincipal "C2004A03268" mockFetcher |> Async.RunSynchronously
        match result with 
        | Ok(r) -> Assert.IsTrue(r.RegisterId = "C2004A03268")
        | _ -> Assert.Fail()

    [<TestMethod>]
    member this.GetRepealedBy() =
        let result = FrlUtils.WebScraping.getRepealedBy "F2014L00022"  mockFetcher |> Async.RunSynchronously
        match result with
        | Ok(r) -> Assert.IsTrue(r.Value = {InstrumentName = "Statement of Principles concerning morbid obesity (Balance of Probabilities) (No. 44 of 2022)"; RegisterId = "F2022L00663"}) 
        | _ -> Assert.Fail()

    [<TestMethod>]
    member this.GetEmptyRepealedBy() =
        let result = FrlUtils.WebScraping.getRepealedBy "F2022L00663" mockFetcher |> Async.RunSynchronously
        match result with
        | Ok(r) -> Assert.IsTrue r.IsNone
        | _ -> Assert.Fail()
     
    [<TestMethod>]
    member this.GetWhatsNew() =
        let r = FrlUtils.WebScraping.getWhatsNew(mockFetcher) |> Async.RunSynchronously
        match r with
        | Ok(r) -> Assert.IsNotNull r
        | _ -> Assert.Fail()

    [<TestMethodAttribute>]
    member this.TestAmendmentParse() =
        let testData = """Statement of Principles concerning external burn (Reasonable Hypothesis) (No. 110 of 2015)
Statement of Principles concerning external burn.
Item was amended
https://www.legislation.gov.au/Details/F2015L01330""".Split('\n') |> List.ofArray
        let result = FrlUtils.EmailParsing.parseLineItemGroup testData
        
        Assert.IsTrue(Result.isError(result))

    [<TestMethod>]
    member this.TestAncensorStackUnwind() =
        let sequenceOfStyleLevels = ["Plainheader"; "LV1"; "LV2"; "LV3"; "LV4"; "LV5"; "LV6"; "LV7"; "LV8"; "LV9"; "LV10"]
        let testDoc = System.IO.File.ReadAllBytes("TestData/treeParseTest.docx")
        let bodyParts = getBodyParts testDoc
        let testStack = new Stack<DocNode>();

        let isParagraphNode (e : OpenXmlElement) =
            match e with
            | :? Paragraph  -> true
            | _ -> false
        
        let testDocNodes = bodyParts |> Seq.filter (fun e -> isParagraphNode e)
        
        testDocNodes |> Seq.iter (fun e -> testStack.Push {Element = e; Children = []})
        Assert.IsTrue (testStack.Count = 10)
        
        
        let testNode = testStack.Peek();
        
        let testNodeLevel = getNodeLevel testNode sequenceOfStyleLevels
        Assert.IsTrue (testNodeLevel.Value = 3)
        unwindToNextAncestor testNode testStack sequenceOfStyleLevels
        Assert.IsTrue (testStack.Count = 8)
        
        let topOfStack = testStack.Peek()
        let topOfStackText = topOfStack.Element.InnerText
        Assert.AreEqual (topOfStackText, "Level 2 D")
    
    
    [<TestMethod>]
    member this.TestParseDocXToTree() = 
        let sequenceOfStyleLevels = ["Plainheader"; "LV1"; "LV2"; "LV3"; "LV4"; "LV5"; "LV6"; "LV7"; "LV8"; "LV9"; "LV10"]
        let testDoc = System.IO.File.ReadAllBytes("TestData/treeParseTest.docx")
        let bodyParts = getBodyParts testDoc |> List.ofSeq
        let rootElement = bodyParts |> List.find (fun e -> getElementStyle e = Some("Plainheader"))
        let rootElementIndex = bodyParts |> List.findIndex (fun e -> e = rootElement)
        let remaineder = bodyParts |> List.skip (rootElementIndex + 1)
        let result = parseElementListToTree rootElement remaineder sequenceOfStyleLevels
        Assert.IsTrue (result.Children.Length = 2)
        
    
    [<TestMethod>]
    member this.ParseRealDocXToTree() = 
        let sequenceOfStyleLevels = ["Plainheader"; "LV1"; "LV2"; "LV3"; "LV4"; "LV5"; "LV6"; "LV7"; "LV8"; "LV9"; "LV10"]
        let testDoc = System.IO.File.ReadAllBytes("TestData/F2023L01180.docx")
        let bodyParts = getBodyParts testDoc |> List.ofSeq
        let rootElement = bodyParts |> List.find (fun e -> getElementStyle e = Some("Plainheader"))
        let remainder = bodyParts |> List.skipWhile (fun e -> getElementOutlineLevel e sequenceOfStyleLevels <> Some(1)) |> List.takeWhile (fun i -> getElementStyle i <> Some("SHHeader"))
        let result = parseElementListToTree rootElement remainder sequenceOfStyleLevels
        Assert.IsTrue (result.Children.Length = 10)
        printfn "%s" (result.PrettyPrint())

    [<TestMethod>]
    member this.TestSyntheticRootElement() = 
        let style = "Plainheader"
        let testDoc = System.IO.File.ReadAllBytes("TestData/F2023L01180.docx")
        let bodyParts = getBodyParts testDoc |> List.ofSeq
        let result = buildSyntheticRootElementForStyle style bodyParts
        Assert.IsTrue (Option.isSome result)
        printfn "%s" result.Value.InnerText
        
    

    [<TestMethod>]
    member this.TestTreeTraversal() =
        let sequenceOfStyleLevels = ["Plainheader"; "LV1"; "LV2"; "LV3"; "LV4"; "LV5"; "LV6"; "LV7"; "LV8"; "LV9"; "LV10"]
        let testDoc = System.IO.File.ReadAllBytes("TestData/F2023L01180.docx")
        let bodyParts = getBodyParts testDoc |> List.ofSeq
        let rootElement = bodyParts |> List.find (fun e -> getElementStyle e = Some("Plainheader"))
        let remainder = bodyParts |> List.skipWhile (fun e -> getElementOutlineLevel e sequenceOfStyleLevels <> Some(1)) |> List.takeWhile (fun i -> getElementStyle i <> Some("SHHeader"))
        let tree = parseElementListToTree rootElement remainder sequenceOfStyleLevels
        let firstLvl1Header = FrlUtils.DocParsing.findFirstNode tree (fun i -> getNodeLevel i sequenceOfStyleLevels = Some(1))
        Assert.IsTrue (firstLvl1Header.IsSome)
        Assert.IsTrue (firstLvl1Header.Value.Element.InnerText = "Name")
        printfn "%s" (firstLvl1Header.Value.PrettyPrint())

        let conditionDescription = FrlUtils.DocParsing.findFirstNode tree (fun i -> i.Element.InnerText = "Kind of injury, disease or death to which this Statement of Principles relates")
        printfn "%s" (conditionDescription.Value.PrettyPrint())


    [<TestMethod>]
    member this.TestDocumentTypeDoc() = 
        let testDocument = File.ReadAllBytes("TestData/F2010L02846.doc")
        let result =  FrlUtils.WebScraping.inferDocumentTypeFromMagicNumbers testDocument
        match result with
        | Ok(i) -> Assert.AreEqual(FrlUtils.Domain.DocumentType.WordDoc,i)
        | _ -> Assert.Fail()


    [<TestMethod>]
    member this.TestDocumentTypeDocX() = 
        let testDocument = File.ReadAllBytes("TestData/F2023L01180.docx")
        let result =  FrlUtils.WebScraping.inferDocumentTypeFromMagicNumbers testDocument
        match result with
        | Ok(i) -> Assert.AreEqual(FrlUtils.Domain.DocumentType.WordDocx,i)
        | _ -> Assert.Fail()
    

    
     
    [<TestMethod>]
    [<TestCategory("Integration")>]
    member this.TestOdataPaging() =
        let testQueryUrl = @"https://api.prod.legislation.gov.au/v1/titles/search(criteria='and(text(%22Statement%20of%20Principles%22,name,contains),pointintime(Latest),type(Principal,Amending),collection(LegislativeInstrument),administeringdepartments(%22O-000944%22))')?=administeringDepartments%2Ccollection%2ChasCommencedUnincorporatedAmendments%2Cid%2CisInForce%2CisPrincipal%2Cname%2Cnumber%2CoptionalSeriesNumber%2CsearchContexts%2CseriesType%2CsubCollection%2Cyear&=administeringDepartments%2CsearchContexts%28%3DfullTextVersion%2Ctext%29&=searchcontexts%2Ftext%2Frelevance%20desc&=true&=0"

        let fetcher = FrlApiClient.createApiFetcher(new HttpClient())
        let result = FrlApiClient.runODataQuery fetcher (new Uri(testQueryUrl)) [] |> Async.RunSynchronously
        match result with
        | Ok(r) -> 
            Assert.IsTrue(r.Length > 0)
            // make a jarray of the results and print
            let jArray = new JArray(r)
            printf "%s" (jArray.ToString())
        | Error(e) ->
            Assert.Fail()

        
        
   
