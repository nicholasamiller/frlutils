namespace FrlUtils
open System
open System.Text
open System.Text.RegularExpressions
open DocumentFormat.OpenXml.Wordprocessing

module ParagraphText =
 
    /// Remove soft hyphens, zero-width chars, etc.
    let private stripInvisible (s: string) =
        s
            .Replace("\u00AD", "")   // soft hyphen
            .Replace("\u200B", "")   // zero-width space
            .Replace("\uFEFF", "")   // zero-width no-break
            .Replace("\u00A0", " ")  // NBSP -> space

    /// Collapse repeated whitespace and trim
    let private normaliseWhitespace (s: string) =
        Regex.Replace(s.Trim(), @"\s+", " ")

    /// Canonical Unicode + whitespace normalisation
    let normalize (s: string) =
        if isNull s then ""
        else
            s
            |> stripInvisible
            |> fun x -> x.Normalize(NormalizationForm.FormC)
            |> normaliseWhitespace

    /// Extract safe text from an OpenXML paragraph
    let extract (p: Paragraph) =
        if isNull p then ""
        else
            p.InnerText
            |> normalize

    /// Compare a paragraph to an expected string
    let equals (p: Paragraph) (expected: string) =
        String.Equals(
            extract p,
            normalize expected,
            StringComparison.OrdinalIgnoreCase
        )


