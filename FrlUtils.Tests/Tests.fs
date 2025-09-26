namespace FrlUtilsTests
open System.Linq
open System.Xml.Linq
open Microsoft.VisualStudio.TestTools.UnitTesting
open System.Net.Http
open FrlUtils.Domain
open FrlUtils.DocParsing
open FrlUtils.EmailParsing
open FrlUtils.FrlApiClient
open System.IO
open System.Collections.Generic
open DocumentFormat.OpenXml.Wordprocessing
open DocumentFormat.OpenXml
open FrlUtils
open System
open Newtonsoft.Json.Linq
open Newtonsoft.Json
open FrlUtils.WordParaNumbering


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
        let result = parseElementListToTree rootElement remaineder sequenceOfStyleLevels (fun i -> 0)
        printfn "%s" (result.PrettyPrint())
        Assert.IsTrue (result.Children.Length = 2)
    
 
        
        
    [<TestMethod>]
    member this.ParseRealDocXToTree() = 
        let sequenceOfStyleLevels = ["Plainheader"; "LV1"; "LV2"; "LV3"; "LV4"; "LV5"; "LV6"; "LV7"; "LV8"; "LV9"; "LV10"]
        let testDoc = System.IO.File.ReadAllBytes("TestData/F2023L01180.docx")
        let bodyParts = getBodyParts testDoc |> List.ofSeq
        let rootElement = bodyParts |> List.find (fun e -> getElementStyle e = Some("Plainheader"))
        let remainder = bodyParts |> List.skipWhile (fun e -> getElementOutlineLevel e sequenceOfStyleLevels <> Some(1)) |> List.takeWhile (fun i -> getElementStyle i <> Some("SHHeader"))
        let result = parseElementListToTree rootElement remainder sequenceOfStyleLevels (fun i -> 0)
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
    [<TestCategory("Integration")>]
    member this.TestOdataPaging() =
        let testQueryUrl = @"https://api.prod.legislation.gov.au/v1/titles/search(criteria='and(text(%22Statement%20of%20Principles%22,name,contains),pointintime(Latest),type(Principal,Amending),collection(LegislativeInstrument),administeringdepartments(%22O-000944%22))')"//?=administeringDepartments%2Ccollection%2ChasCommencedUnincorporatedAmendments%2Cid%2CisInForce%2CisPrincipal%2Cname%2Cnumber%2CoptionalSeriesNumber%2CsearchContexts%2CseriesType%2CsubCollection%2Cyear&=administeringDepartments%2CsearchContexts%28%3DfullTextVersion%2Ctext%29&=searchcontexts%2Ftext%2Frelevance%20desc"

        let fetcher = FrlApiClient.createApiFetcher(new HttpClient())
        let result = FrlApiClient.runOdataQuery fetcher (new Uri(testQueryUrl)) |> Async.RunSynchronously
        match result with
        | Ok(r) -> 
            Assert.IsTrue(r.Length > 0)
            // make a jarray of the results and print
            let jArray = new JArray(r)
            printf "%s" (jArray.ToString())
        | Error(e) ->
            Assert.Fail()
    
    [<TestMethod>]
    [<TestCategory("Integration")>]
    member this.TestGetLatestComplationWhereThereAreSome() = 
        let fetcher = FrlApiClient.createApiFetcher(new HttpClient())
        let result = FrlApiClient.getLatestVersion "F2019L01198" fetcher |> Async.RunSynchronously
        match result with
        | Ok(r) -> 
            match r with
            | Some(v) -> printf "%s" (v.ToString())
            | None -> Assert.Fail()
        | Error(e) ->
            Assert.Fail()
    
    [<TestMethod>]

    [<TestCategory("Integration")>]
    member this.TestGetLatestComplationWhereNone() = 
        let fetcher = FrlApiClient.createApiFetcher(new HttpClient())
        let result = FrlApiClient.getLatestVersion "goat" fetcher |> Async.RunSynchronously
        match result with
        | Ok(r) -> 
            Assert.IsTrue((r = Option.None))
        | Error(e) ->
            Assert.Fail()
        

    [<TestMethod>]
    member this.TestVersionDeserialisation() =
        let testJson = """{
            "titleId": "F2019L01198",
            "start": "2022-04-02T00:00:00",
            "retrospectiveStart": "2022-04-02T00:00:00",
            "end": null,
            "isLatest": true,
            "name": "Military Rehabilitation and Compensation (Warlike Service) Determination 2019",
            "status": "InForce",
            "registerId": "F2022C00414",
            "compilationNumber": "3",
            "publishComments": null,
            "hasUnincorporatedAmendments": false,
            "reasons": [
                {
                    "affect": "Amend",
                    "markdown": "sch 1 (item 1) of the [Military Rehabilitation and Compensation (Warlike Service) Amendment Determination 2022 (No. 1)](/F2022L00495)",
                    "affectedByTitle": {
                        "titleId": "F2022L00495",
                        "name": "Military Rehabilitation and Compensation (Warlike Service) Amendment Determination 2022 (No. 1)",
                        "provisions": "sch 1 (item 1)"
                    },
                    "amendedByTitle": null,
                    "dateChanged": null
                }
            ]
        }"""
        let jObject = JObject.Parse(testJson)
        let result = deserializeVersion jObject
        match result with
        | Ok(r) -> printf "%s" (r.ToString())
        | _ -> Assert.Fail()


    [<TestMethod>]
    member this.TestTitleDeserialisation() =
        let testJson = """{
    "id": "F2019L01091",
    "name": "Amendment Statement of Principles concerning hypertension No. 89 of 2019",
    "makingDate": "2019-08-23T00:00:00",
    "collection": "LegislativeInstrument",
    "subCollection": null,
    "isPrincipal": false,
    "isInForce": false,
    "publishComments": null,
    "status": "Repealed",
    "hasCommencedUnincorporatedAmendments": false,
    "originatingBillUri": null,
    "asMadeRegisteredAt": "2019-08-26T10:17:23.613",
    "optionalSeriesNumber": "No. 89 of 2019",
    "year": null,
    "number": null,
    "seriesType": null,
    "nameHistory": [
      {
        "name": "Amendment Statement of Principles concerning hypertension No. 89 of 2019",
        "start": "2019-08-26T00:00:00"
      }
    ],
    "namePossibleFuture": [],
    "statusHistory": [
      {
        "status": "InForce",
        "start": "2019-08-26T00:00:00",
        "reasons": []
      },
      {
        "status": "Repealed",
        "start": "2019-11-15T00:00:00",
        "reasons": [
          {
            "affect": "Repeal",
            "markdown": "Division 1 of Part 3 of Chapter 3 of the [Legislation Act 2003](/C2004A01224)",
            "affectedByTitle": null,
            "amendedByTitle": null,
            "dateChanged": null
          }
        ]
      }
    ],
    "statusPossibleFuture": []
  }"""
        
        
        let jObject = JObject.Parse(testJson)
        let result = FrlApiClient.deserializeTitle jObject
        match result with
        | Ok(r) -> printf "%s" (r.ToString())
        | _ -> Assert.Fail()

    [<TestMethod>]
    member this.TestGetSectionParas() =
        let testDoc = IO.File.ReadAllBytes("TestData/F2025L00149.docx")
        let wordDoc, np = FrlUtils.DocParsing.getWordDocWithParaTextProvider testDoc
        let factorSectionName = "Factors that must exist"
        let sequenceOfStyleLevelsNewStyle = ["Plainheader"; "LV1"; "LV2"; "LV3"; "LV4"; "LV5"; "LV6"; "LV7"; "LV8"; "LV9"; "LV10"]
        let factorSectionStyleLevel = 1
        let factorsSection =  getSectionParagraphs (factorSectionName, sequenceOfStyleLevelsNewStyle, factorSectionStyleLevel , wordDoc)
        let parsedToTree = FrlUtils.DocParsing.parseElementListToTree (factorsSection.Head :> OpenXmlElement) (factorsSection.Tail |> List.map (fun p -> p :> OpenXmlElement)) sequenceOfStyleLevelsNewStyle (fun i -> 0)
        printfn "%s"  (parsedToTree.PrettyPrintWithParaNumbering np)
        printfn "Children count: %d" (List.length parsedToTree.Children)
        // LVL2 style
        let firstLevelChildren = parsedToTree.Children |> List.filter (fun n -> getNodeLevel n sequenceOfStyleLevelsNewStyle = Some(2)) |> List.length
        printfn "First level children: %d" firstLevelChildren
        let noteChildren = parsedToTree.Children |> List.filter (fun n -> n.Element.InnerText.StartsWith("Note")) |> List.length
        printfn "Note children: %d" noteChildren
        for n in parsedToTree.Children do
            let level = getNodeLevel n sequenceOfStyleLevelsNewStyle
            let text = n.Element.InnerText
            let paraId = np (n.Element :?> Paragraph)
            let paraIdText = 
                match paraId with
                | Some(t) -> t
                | None -> "No numbering"
            printfn "Level: %A, ParaId: %s, Text: %s" level paraIdText text
        Assert.IsTrue((firstLevelChildren = 66)) // includes paaras where there is a tail in LVL2
         
    [<TestMethod>]
    member this.TestParseSectionWithHeadAndTail() =
        let testDoc = IO.File.ReadAllBytes("TestData/sectionWithHeadAndTail.docx")
        let wordDoc, np = FrlUtils.DocParsing.getWordDocWithParaTextProvider testDoc
        let sequenceOfStyleLevelsNewStyle = ["LV1"; "LV2"; "LV3"; "LV4"; "LV5"; "LV6"; "LV7"; "LV8"; "LV9"; "LV10"]
        let testSectionParas = getSectionParagraphs ("Section Head", sequenceOfStyleLevelsNewStyle, 0, wordDoc)
        
        let offsetProvider = fun (e : OpenXmlElement) ->
            // if has number, then zero, else -1
            match np (e :?> Paragraph) with
            | Some(_) -> 0
            | None -> -1
        
        
        let parsedToTree = FrlUtils.DocParsing.parseSectionParaToTree testSectionParas sequenceOfStyleLevelsNewStyle offsetProvider
        match parsedToTree with
        | Error(e) ->
            printfn "%s" (e.ToString())
            Assert.Fail()
        | Ok(t) ->
            printfn "%s"  (t.PrettyPrintWithParaNumbering np)
        
        
 
        
        

        