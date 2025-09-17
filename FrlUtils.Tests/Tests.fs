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
        let result = parseElementListToTree rootElement remaineder sequenceOfStyleLevels
        printfn "%s" (result.PrettyPrint())
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
    member this.Test_ParseParagraphsFromHtml() =
        let html = """<html><body><div><p dir="ltr" data-w-style="LV2" data-w-paraId="48D97008" class="pt-000000"><span lang="en-AU" data-w-listItemRun="1.1" class="pt-000001">(1)</span><a id="_Ref409598124" class="pt-000002"></a><a id="_Ref402529683" class="pt-000002"></a><span lang="en-AU" class="pt-DefaultParagraphFont">For the purposes of this Statement of Principles, tardive dyskinesia:</span></p><p dir="ltr" data-w-style="LV3" data-w-paraId="58ABCA15" class="pt-000003"><span lang="en-AU" data-w-listItemRun="1.1.1" class="pt-000001">(a)</span><span lang="en-AU" class="pt-DefaultParagraphFont">means a movement disorder which meets the following criteria (derived from DSM-5-TR):</span></p><p dir="ltr" data-w-style="LV4" data-w-paraId="5174660A" class="pt-000004"><span lang="en-AU" data-w-listItemRun="1.1.1.1" class="pt-000001">(i)</span><span lang="en-AU" class="pt-DefaultParagraphFont">abnormal, involuntary movements of the tongue, jaw, trunk, or extremities that develop in association with the use of medications that block postsynaptic dopamine receptors;</span></p><p dir="ltr" data-w-style="LV4" data-w-paraId="797B6260" class="pt-000004"><span lang="en-AU" data-w-listItemRun="1.1.1.2" class="pt-000001">(ii)</span><span lang="en-AU" xml:space="preserve" class="pt-DefaultParagraphFont">the movements are present over a period of at least 4 weeks; </span></p><p dir="ltr" data-w-style="LV4" data-w-paraId="0D79E76D" class="pt-000004"><span lang="en-AU" data-w-listItemRun="1.1.1.3" class="pt-000001">(iii)</span><span lang="en-AU" class="pt-DefaultParagraphFont">there must be a history of use of the offending agent for at least 3 months in individuals &lt;60 years of age or at least 1 month in individuals age 60 years or older; and</span></p><p dir="ltr" data-w-style="LV4" data-w-paraId="18AFFF77" class="pt-000004"><span lang="en-AU" data-w-listItemRun="1.1.1.4" class="pt-000001">(iv)</span><span lang="en-AU" xml:space="preserve" class="pt-DefaultParagraphFont">symptoms and signs develop during medication use or within 4 weeks of withdrawal from an oral agent or within 8 weeks of withdrawal of a long-acting injectable agent; and  </span></p><p dir="ltr" class="pt-Normal"><span xml:space="preserve" class="pt-000005"> </span></p></div></body></html>"""
        let result = parseParagraphsFromHtml html
        Assert.AreEqual(7, result.Length)
        Assert.AreEqual("48D97008", result.[0].WordId)
        Assert.AreEqual("LV2", result.[0].WordStyle)
        Assert.AreEqual(Some "1.1", result.[0].WordNumberingLogical)
        Assert.AreEqual("58ABCA15", result.[1].WordId)
        Assert.AreEqual("LV3", result.[1].WordStyle)
        Assert.AreEqual(Some "1.1.1", result.[1].WordNumberingLogical)
        Assert.AreEqual("5174660A", result.[2].WordId)
        Assert.AreEqual("LV4", result.[2].WordStyle)
        Assert.AreEqual(Some "1.1.1.1", result.[2].WordNumberingLogical)
        // Last p has no attributes; should be empty strings and no numbering
        Assert.AreEqual("", result.[6].WordId)
        Assert.AreEqual("", result.[6].WordStyle)
        Assert.AreEqual(None, result.[6].WordNumberingLogical)
    
    [<TestMethod>]
    member this.Test_GetMapOfParasToNumbering() =
        let testDoc = System.IO.File.ReadAllBytes("TestData/Meaning of tardive dyskinesia.docx")
        let result = getMapOfParasToNumbering testDoc
        printf "%A" result
        