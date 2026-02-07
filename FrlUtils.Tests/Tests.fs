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
        let testDocument = (System.IO.File.ReadAllBytes("TestData/F2025C00553.docx"))
        let result = getTablesBetweenParas "Schedule 1—Warlike service" "Endnotes" (getBodyParts (testDocument)) None
        printfn "%s" (result.ToString())
        Assert.IsTrue(result.headerRow.items.Length = 5)
        Assert.IsTrue(result.bodyRows.Length = 23)
 
    [<TestMethod>]
    member this.TestTableParser22() = 
        let testDocument = (System.IO.File.ReadAllBytes("TestData/F2025C00551.docx"))
        let result = getTablesBetweenParas "Schedule 1—Non-warlike service" "Endnotes" (getBodyParts (testDocument)) None
        printfn "%s" (result.ToString())
        Assert.IsTrue(result.headerRow.items.Length = 5)
        Assert.IsTrue(result.bodyRows.Length = 41)
         
     
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
        let bodyParts = getBodyParts testDoc |> List.ofSeq |> List.filter (fun e -> match e with | :? Paragraph -> true | _ -> false)
        let testStack = new Stack<DocNode>();

        let isParagraphNode (e : OpenXmlElement) =
            match e with
            | :? Paragraph  -> true
            | _ -> false
        
        let testDocNodes = bodyParts |> Seq.filter (fun e -> isParagraphNode e)
        
        testDocNodes |> Seq.iter (fun e -> testStack.Push {Element = e; Children = []; Parent = None})
        
        // print out the stack
        for n in testStack do
            let p = n.Element :?> Paragraph
            let style = getElementStyle p
            let text = p.InnerText
            printfn "Style: %A, Text: %s" style text
        
        
        Assert.IsTrue (testStack.Count = 12)
                        
        let testNode = testStack.Pop() // last node, let's texst if we can add it back to the stack
        printfn "Test node text: %s" (testNode.Element.InnerText)
        
        let testNodeLevel = getNodeLevel testNode sequenceOfStyleLevels
        Assert.IsTrue (testNodeLevel.Value = 3) // the last node is level 3
        let lp = buildStyleBasedParaLevelProvider sequenceOfStyleLevels
        unwindToNextAncestor testNode testStack lp 0
        // should remove the last lever 3: level 3A
        // last node should be level 2
        
        Assert.IsTrue (testStack.Count() = 10)
        
        let topOfStack = testStack.Peek()
        let topOfStackText = topOfStack.Element.InnerText
        Assert.AreEqual (topOfStackText, "Level 2 D")
        
        // now pop stack until there are only 3 items
        while testStack.Count() > 3 do
            let n = testStack.Pop()
            printfn "Popped node text: %s" (n.Element.InnerText)
        
        // node at top of stack should be level 2 A
        let topOfStack2 = testStack.Peek()
        let topOfStackText2 = topOfStack2.Element.InnerText
        Assert.AreEqual (topOfStackText2, "Level 2 A")
        
        // test unwind for a node that has no style
        // expected outcome is that the stack is unchanged
        
        let firstNote = bodyParts.[3]
        printfn "First note text: %s" (firstNote.InnerText)
        
        let testNode = {Element = firstNote; Children = []; Parent = None}
        
        unwindToNextAncestor testNode testStack lp 0
        // stack should be unchanged
        Assert.IsTrue (testStack.Count() = 3)
        
        // push note onto stack
        testStack.Push testNode
        // stack size should now be 4
        Assert.IsTrue (testStack.Count() = 4)
        
        // now test unwind again with another note - should wind back to the level 2A
        let secondNote = bodyParts.[4]
        printfn "Second note text: %s" (secondNote.InnerText)
        let testNote2 =  {Element = secondNote; Children = []; Parent = None}
        // stack size should now be 4
        
        unwindToNextAncestor testNote2 testStack lp 0
        // stack size should now be 3 - should have removed note 1
        Assert.IsTrue (testStack.Count() = 3)
        
        // top of stack should be Level 2 A
        let topOfStack3 = testStack.Peek()
        let topOfStackText3 = topOfStack3.Element.InnerText
        Assert.AreEqual (topOfStackText3, "Level 2 A")

        for n in testStack do
            let p = n.Element :?> Paragraph
            let style = getElementStyle p
            let text = p.InnerText
            printfn "Style: %A, Text: %s" style text  
       
    
    
    [<TestMethod>]
    member this.TestParseDocXToTree() = 
        let sequenceOfStyleLevels = ["Plainheader"; "LV1"; "LV2"; "LV3"; "LV4"; "LV5"; "LV6"; "LV7"; "LV8"; "LV9"; "LV10"]
        let testDoc = System.IO.File.ReadAllBytes("TestData/treeParseTest.docx")
        let bodyParagraphs = getBodyParts testDoc |>  List.ofSeq |> List.filter (fun e -> match e with | :? Paragraph -> true | _ -> false)
        let rootElement = bodyParagraphs |> List.find (fun e -> getElementStyle e = Some("Plainheader"))
        let rootElementIndex = bodyParagraphs |> List.findIndex (fun e -> e = rootElement)
        let remaineder = bodyParagraphs |> List.skip (rootElementIndex + 1)
        let cplp = buildStyleBasedParaLevelProvider sequenceOfStyleLevels
        
        let result = parseElementListToTree rootElement remaineder cplp (fun i -> 0)
        printfn "%s" (result.PrettyPrint())
        Assert.IsTrue (result.Children.Length = 2)
        let firstChild = result.Children.Head
        Assert.IsTrue (firstChild.Children.Length = 2)

   
        
    [<TestMethod>]
    [<TestCategory("Integration")>]
    [<Ignore>]
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
    [<Ignore>]
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
        let factorsSection =  getSectionParagraphs (factorSectionName, sequenceOfStyleLevelsNewStyle, factorSectionStyleLevel , wordDoc) (fun i -> false)
        let cplp = buildStyleBasedParaLevelProvider sequenceOfStyleLevelsNewStyle
        let parsedToTree = FrlUtils.DocParsing.parseElementListToTree (factorsSection.Head :> OpenXmlElement) (factorsSection.Tail |> List.map (fun p -> p :> OpenXmlElement)) cplp (fun i -> 0)
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
        let testSectionParas = getSectionParagraphs ("Section Head", sequenceOfStyleLevelsNewStyle, 0, wordDoc) (fun i -> false)
        
        let offsetProvider = fun (e : OpenXmlElement) ->
            // if has number, then zero, else -1
            match np (e :?> Paragraph) with
            | Some(_) -> 0
            | None -> -1
        
        let cplp = buildStyleBasedParaLevelProvider sequenceOfStyleLevelsNewStyle
        let parsedToTree = FrlUtils.DocParsing.parseSectionParaToTree testSectionParas cplp offsetProvider
        match parsedToTree with
        | Error(e) ->
            printfn "%s" (e.ToString())
            Assert.Fail()
        | Ok(t) ->
            printfn "%s"  (t.PrettyPrintWithParaNumbering np)
            // root should have two children only
            Assert.IsTrue((List.length t.Children) = 2)
            // first child should have 3 children
            let firstChild = t.Children.Head
            Assert.IsTrue((List.length firstChild.Children) = 3)
            let firstChildOfFirstChild = firstChild.Children.Head
            Assert.IsTrue((List.length firstChildOfFirstChild.Children) = 0)
            
            let fullParaNumberTextForRoot = getFullParaNumberText t np
            let fullParaNumberTextForFirstChild = getFullParaNumberText firstChild np
            let fullParaNumberTextForFirstChildOfFirstChild = getFullParaNumberText firstChildOfFirstChild np
            printfn "Full para number text for first child of first child: %s" (Option.defaultValue "No numbering" fullParaNumberTextForFirstChildOfFirstChild)
            printfn "Full para number text for first child: %s" (Option.defaultValue "No numbering" fullParaNumberTextForFirstChild)
            printfn "Full para number text for root: %s" (Option.defaultValue "No numbering" fullParaNumberTextForRoot)
            let sectiontail = firstChild.Children.[2]
            let fullParaNumberTextForSectionTail = getFullParaNumberText sectiontail np
            printfn "Full para number text for section tail: %s" (Option.defaultValue "No numbering" fullParaNumberTextForSectionTail)
            let sectionHead = firstChild
            let fullParaNumberTextForSectionHead = getFullParaNumberText sectionHead np
            printfn "Full para number text for section head: %s" (Option.defaultValue "No numbering" fullParaNumberTextForSectionHead)
            Assert.AreEqual(fullParaNumberTextForSectionHead, fullParaNumberTextForSectionHead)
            // test for structural equality
            Assert.IsTrue(fullParaNumberTextForFirstChildOfFirstChild.Value = "1(1)(a)")
 
        
        

        