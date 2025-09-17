OpenXmlPowerTools Content
===========================
Update January 2025: Now using this with .NET 9.0 and it appears to work correctly.

This branch is created to support .net 8.0 and the latest version of DocumentFormat.OpenXml, 3.0.0
There are multiple breaking changes in DocumentFormat.OpenXml 3.0, which are explained on their package site, and seemed pretty easy to fix.
I haven't really tested this - but it seems to work for my little project.


# OpenXmlPowerTools.NetCore

Actual [Nuget Package](https://www.nuget.org/packages/OpenXmlPowerTools.NetCore)

As you know, Microsoft has archived this repository and does not maintain it. Current repo is created from the [LionelVallet/Open-Xml-PowerTools](https://github.com/LionelVallet/Open-Xml-PowerTools) project and configured to be used only in actual versions .NET

For compatibility with .NET 7.0, the System.Drawing.Common library was replaced with the cross-platform [SkiaSharp](https://github.com/mono/SkiaSharp) library.

---
About OpenXmlPowerTools

The Open XML PowerTools provides guidance and example code for programming with Open XML
Documents (DOCX, XLSX, and PPTX).  It is based on, and extends the functionality
of the [Open XML SDK](https://github.com/OfficeDev/Open-XML-SDK).

It supports scenarios such as:
- Splitting DOCX/PPTX files into multiple files.
- Combining multiple DOCX/PPTX files into a single file.
- Populating content in template DOCX files with data from XML.
- High-fidelity conversion of DOCX to HTML/CSS.
- High-fidelity conversion of HTML/CSS to DOCX.
- Searching and replacing content in DOCX/PPTX using regular expressions.
- Managing tracked-revisions, including detecting tracked revisions, and accepting tracked revisions.
- Updating Charts in DOCX/PPTX files, including updating cached data, as well as the embedded XLSX.
- Comparing two DOCX files, producing a DOCX with revision tracking markup, and enabling retrieving a list of revisions.
- Retrieving metrics from DOCX files, including the hierarchy of styles used, the languages used, and the fonts used.
- Writing XLSX files using far simpler code than directly writing the markup, including a streaming approach that
  enables writing XLSX files with millions of rows.
- Extracting data (along with formatting) from spreadsheets.

Copyright (c) Microsoft Corporation 2012-2017
Portions Copyright (c) Eric White Inc 2018-2019

Licensed under the MIT License.
See License in the project root for license information.

---

OpenXmlPowerTools Content
===========================

There is a lot of content about OpenXmlPowerTools at the [OpenXmlPowerTools Resource Center at OpenXmlDeveloper.org](http://openxmldeveloper.org/wiki/w/wiki/powertools-for-open-xml.aspx)

See:
- [DocumentBuilder Resource Center](http://www.ericwhite.com/blog/documentbuilder-developer-center/)
- [PresentationBuilder Resource Center](http://www.ericwhite.com/blog/presentationbuilder-developer-center/)
- [WmlToHtmlConverter Resource Center](http://www.ericwhite.com/blog/wmltohtmlconverter-developer-center/)
- [DocumentAssembler Resource Center](http://www.ericwhite.com/blog/documentassembler-developer-center/)

---

Build Instructions
==================

**Prerequisites:**

- Visual Studio 2022 or .NET CLI toolchain

**Build**
 
 With Visual Studio:

- Open `OpenXmlPowerTools.sln` in Visual Studio
- Rebuild the project
- Build the solution.  To validate the build, open the Test Explorer.  Click Run All.
- To run an example, set the example as the startup project, and press F5.

With .NET CLI toolchain:

- Run `dotnet build OpenXmlPowerTools.sln`

---

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information, see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
