namespace FrlUtils

open DocumentFormat.OpenXml.Packaging
open DocumentFormat.OpenXml.Wordprocessing
open System
open System.Collections.Generic
open System.Linq

module WordDocumentParser =

    let private buildNumberingStyles (numberingPart: NumberingDefinitionsPart) =
        let styles = Dictionary<_, _>()
        if numberingPart <> null then
            for num in numberingPart.Numbering.Elements<NumberingInstance>() do
                let numId = num.NumberID
                let abstractNumId = num.AbstractNumId.Val.Value

                let levels = 
                    numberingPart.Numbering.Elements<AbstractNum>()
                    |> Seq.tryFind (fun a -> a.AbstractNumberId = abstractNumId)
                    |> fun maybeAbstractNum -> maybeAbstractNum.Value.Descendants<Level>()

                if levels <> null then
                    let numFormatList = List< string >()
                    for level in levels do
                        let numberFormat = level.NumberingFormat.Val.Value.ToString()
                        numFormatList.Add(numberFormat)

                    styles.[numId] <- numFormatList
        styles


    let private extractText (body: Body, numberingStyles: Dictionary<_, _>) =
        let text = ref ""
        let currentNumbers = Dictionary<_, _>() // Level, CurrentNumber

        for para in body.Elements<Paragraph>() do

            let numIdElement =
                if para.ParagraphProperties <> null && para.ParagraphProperties.NumberingProperties <> null then para.ParagraphProperties.NumberingProperties.NumberingId else null

            let levelIdElement =
                if para.ParagraphProperties <> null && para.ParagraphProperties.NumberingProperties <> null then para.ParagraphProperties.NumberingProperties.NumberingLevelReference else null


            if numIdElement <> null && levelIdElement <> null then
                let numId = numIdElement.Val.Value
                let levelId = levelIdElement.Val.Value

                if not (currentNumbers.ContainsKey(levelId)) then
                    currentNumbers.[levelId] <- 1
                else
                    currentNumbers.[levelId] <- currentNumbers.[levelId] + 1

                for key in currentNumbers.Keys |> Seq.where (fun k -> k > levelId) |> Seq.toList do
                    currentNumbers.[key] <- 1

                if numberingStyles.TryGetValue(numId, &levels) && levelId < levels.Count then
                    let numberFormat = levels.[levelId]
                    text := !text + sprintf "%s%d. " numberFormat currentNumbers.[levelId]

            text := !text + para.InnerText + Environment.NewLine
        !text

    let extractTextWithNumbering (filePath: string) =
        use wordDoc = WordprocessingDocument.Open(filePath, false)
        let mainPart = wordDoc.MainDocumentPart
        if mainPart = null then
            ""
        else
            let numberingStyles = buildNumberingStyles (mainPart.NumberingDefinitionsPart)
            extractText (mainPart.Document.Body, numberingStyles)
