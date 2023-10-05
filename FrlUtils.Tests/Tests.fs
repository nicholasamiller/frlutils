namespace FrlUtilsTests
open Microsoft.VisualStudio.TestTools.UnitTesting
open System.Net.Http
open FrlUtils.WebScraping
open FrlUtils.DocParsing
open FrlUtils.EmailParsing
open System.IO
open System.Collections.Generic
open DocumentFormat.OpenXml.Wordprocessing
open DocumentFormat.OpenXml


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
          let result = getInstrumentDocX "F2021C00349" mockFetcher |> Async.RunSynchronously
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
        printfn "%s" result.InnerText
        


    
       
      
        
   
