using System.Text;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace BarsiG.Presentation;

/// <summary>
/// Builds the presentation of BarsiG as a real PPTX file.
/// The package is written with the Open XML SDK, the master, the layout and the theme
/// are written as XML, so the file opens in PowerPoint, in Keynote and in LibreOffice.
/// </summary>
public static class Program
{
    private const string SlideWidth = "12192000";
    private const string SlideHeight = "6858000";

    /// <summary>Builds the presentation and writes it to the given path.</summary>
    public static int Main(string[] args)
    {
        string output = args.Length > 0
            ? args[0]
            : Path.Combine("docs", "BarsiG-Presentation.pptx");

        string? directory = Path.GetDirectoryName(Path.GetFullPath(output));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        IReadOnlyList<Slide> slides = Slides.Build();

        using (PresentationDocument document = PresentationDocument.Create(output, PresentationDocumentType.Presentation))
        {
            PresentationPart presentationPart = document.AddPresentationPart();

            SlideMasterPart masterPart = presentationPart.AddNewPart<SlideMasterPart>();
            SlideLayoutPart layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
            ThemePart themePart = masterPart.AddNewPart<ThemePart>();

            Feed(themePart, ThemeXml);
            Feed(layoutPart, LayoutXml);
            Feed(masterPart, MasterXml(masterPart.GetIdOfPart(layoutPart)));

            List<string> slideIds = [];
            uint counter = 256;

            foreach (Slide slide in slides)
            {
                SlidePart slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.AddPart(layoutPart);
                string layoutId = slidePart.GetIdOfPart(layoutPart);

                string imageId = string.Empty;

                if (slide == slides[0])
                {
                    imageId = AddLogo(slidePart);
                }

                Feed(slidePart, SlideXml(slide, layoutId, imageId));

                slideIds.Add($"<p:sldId id=\"{counter}\" r:id=\"{presentationPart.GetIdOfPart(slidePart)}\"/>");
                counter++;
            }

            Feed(
                presentationPart,
                PresentationXml(
                    presentationPart.GetIdOfPart(masterPart),
                    string.Concat(slideIds)));
        }

        Console.WriteLine($"The presentation was written to {Path.GetFullPath(output)} ({slides.Count} slides).");

        Validate(output);

        return 0;
    }

    /// <summary>Checks that the file is a valid presentation.</summary>
    private static void Validate(string path)
    {
        DocumentFormat.OpenXml.Validation.OpenXmlValidator validator = new();

        using PresentationDocument document = PresentationDocument.Open(path, false);

        List<DocumentFormat.OpenXml.Validation.ValidationErrorInfo> errors =
            validator.Validate(document).ToList();

        if (errors.Count == 0)
        {
            Console.WriteLine("The file was checked: it is a valid presentation.");
            return;
        }

        Console.WriteLine($"The file was checked: {errors.Count} problems were found.");

        foreach (DocumentFormat.OpenXml.Validation.ValidationErrorInfo error in errors.Take(5))
        {
            Console.WriteLine($"  {error.Description}");
        }
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(xml));

        part.FeedData(stream);
    }

    /// <summary>Adds the logo of BarsiG to a slide and returns the identifier of the picture.</summary>
    private static string AddLogo(SlidePart slidePart)
    {
        string? logo = FindLogo();

        if (logo is null)
        {
            return string.Empty;
        }

        ImagePart imagePart = slidePart.AddImagePart(ImagePartType.Png);

        using (FileStream file = File.OpenRead(logo))
        {
            imagePart.FeedData(file);
        }

        return slidePart.GetIdOfPart(imagePart);
    }

    /// <summary>Finds the logo of BarsiG in the repository.</summary>
    private static string? FindLogo()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "BarsiG_Logo.png");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string PresentationXml(string masterId, string slideIds) =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" saveSubsetFonts="1">
          <p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="{masterId}"/></p:sldMasterIdLst>
          <p:sldIdLst>{slideIds}</p:sldIdLst>
          <p:sldSz cx="{SlideWidth}" cy="{SlideHeight}"/>
          <p:notesSz cx="{SlideHeight}" cy="{SlideWidth}"/>
          <p:defaultTextStyle>
            <a:defPPr><a:defRPr lang="en-US"/></a:defPPr>
          </p:defaultTextStyle>
        </p:presentation>
        """;

    private static string MasterXml(string layoutId) =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:cSld>
            <p:bg><p:bgPr><a:solidFill><a:srgbClr val="{Slides.OffWhite}"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>
            <p:spTree>
              <p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
              <p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>
            </p:spTree>
          </p:cSld>
          <p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/>
          <p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="{layoutId}"/></p:sldLayoutIdLst>
        </p:sldMaster>
        """;

    private static string LayoutXml =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" type="blank" preserve="1">
          <p:cSld name="Blank">
            <p:spTree>
              <p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
              <p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sldLayout>
        """;

    private static string ThemeXml =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="BarsiG">
          <a:themeElements>
            <a:clrScheme name="BarsiG">
              <a:dk1><a:srgbClr val="{Slides.Midnight}"/></a:dk1>
              <a:lt1><a:srgbClr val="FFFFFF"/></a:lt1>
              <a:dk2><a:srgbClr val="{Slides.Slate}"/></a:dk2>
              <a:lt2><a:srgbClr val="{Slides.OffWhite}"/></a:lt2>
              <a:accent1><a:srgbClr val="{Slides.Sage}"/></a:accent1>
              <a:accent2><a:srgbClr val="{Slides.Mist}"/></a:accent2>
              <a:accent3><a:srgbClr val="5D6B70"/></a:accent3>
              <a:accent4><a:srgbClr val="3F8F6B"/></a:accent4>
              <a:accent5><a:srgbClr val="C88A2E"/></a:accent5>
              <a:accent6><a:srgbClr val="B4463F"/></a:accent6>
              <a:hlink><a:srgbClr val="{Slides.Sage}"/></a:hlink>
              <a:folHlink><a:srgbClr val="5D6B70"/></a:folHlink>
            </a:clrScheme>
            <a:fontScheme name="BarsiG">
              <a:majorFont><a:latin typeface="Segoe UI"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont>
              <a:minorFont><a:latin typeface="Segoe UI"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont>
            </a:fontScheme>
            <a:fmtScheme name="BarsiG">
              <a:fillStyleLst>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
              </a:fillStyleLst>
              <a:lnStyleLst>
                <a:ln w="6350" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
                <a:ln w="12700" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
                <a:ln w="19050" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
              </a:lnStyleLst>
              <a:effectStyleLst>
                <a:effectStyle><a:effectLst/></a:effectStyle>
                <a:effectStyle><a:effectLst/></a:effectStyle>
                <a:effectStyle><a:effectLst/></a:effectStyle>
              </a:effectStyleLst>
              <a:bgFillStyleLst>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
              </a:bgFillStyleLst>
            </a:fmtScheme>
          </a:themeElements>
        </a:theme>
        """;

    /// <summary>Builds the XML of one slide: a bar, the title, the line, the text and the footer.</summary>
    private static string SlideXml(Slide slide, string layoutId, string imageId)
    {
        bool isTitle = string.Equals(slide.Title, "BarsiG", StringComparison.Ordinal);

        string picture = string.IsNullOrEmpty(imageId)
            ? string.Empty
            : $"""
               <p:pic>
                 <p:nvPicPr><p:cNvPr id="9" name="Logo"/><p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>
                 <p:blipFill><a:blip r:embed="{imageId}"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>
                 <p:spPr><a:xfrm><a:off x="685800" y="457200"/><a:ext cx="4572000" cy="1371600"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr>
               </p:pic>
               """;

        string titleTop = isTitle ? "2286000" : "548640";
        string textTop = isTitle ? "3657600" : "1828800";
        string titleSize = isTitle ? "4800" : "3200";
        string textSize = isTitle ? "2200" : "1700";

        StringBuilder bullets = new();

        foreach (string line in slide.Bullets)
        {
            string size = isTitle && line == slide.Bullets[0] ? "2600" : textSize;

            string color = isTitle && line == slide.Bullets[0] ? Slides.Sage : Slides.Slate;

            bool bold = isTitle && line == slide.Bullets[0];

            bullets.Append(
                $"""
                 <a:p>
                   <a:pPr marL="285750" indent="-285750"><a:buFont typeface="Arial" pitchFamily="34" charset="0"/><a:buChar char="•"/></a:pPr>
                   <a:r><a:rPr lang="en-US" sz="{size}"{(bold ? " b=\"1\"" : string.Empty)}><a:solidFill><a:srgbClr val="{color}"/></a:solidFill><a:latin typeface="Segoe UI"/></a:rPr><a:t>{Escape(line)}</a:t></a:r>
                 </a:p>
                 """);
        }

        string footer = string.IsNullOrEmpty(slide.Footer)
            ? string.Empty
            : $"""
               <p:sp>
                 <p:nvSpPr><p:cNvPr id="8" name="Footer"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
                 <p:spPr><a:xfrm><a:off x="685800" y="6019800"/><a:ext cx="10820400" cy="400000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>
                 <p:txBody><a:bodyPr wrap="square"/><a:lstStyle/><a:p><a:r><a:rPr lang="en-US" sz="1200"><a:solidFill><a:srgbClr val="5D6B70"/></a:solidFill><a:latin typeface="Segoe UI"/></a:rPr><a:t>{Escape(slide.Footer)}</a:t></a:r></a:p></p:txBody>
               </p:sp>
               """;

        return $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
              <p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>
              <p:sp>
                <p:nvSpPr><p:cNvPr id="2" name="Bar"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
                <p:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="228600" cy="{SlideHeight}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="{Slides.Sage}"/></a:solidFill><a:ln><a:noFill/></a:ln></p:spPr>
                <p:txBody><a:bodyPr/><a:lstStyle/><a:p/></p:txBody>
              </p:sp>
              {picture}
              <p:sp>
                <p:nvSpPr><p:cNvPr id="3" name="Title"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
                <p:spPr><a:xfrm><a:off x="685800" y="{titleTop}"/><a:ext cx="10820400" cy="1000000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>
                <p:txBody><a:bodyPr wrap="square"><a:normAutofit/></a:bodyPr><a:lstStyle/><a:p><a:pPr algn="l"/><a:r><a:rPr lang="en-US" sz="{titleSize}" b="1"><a:solidFill><a:srgbClr val="{Slides.Midnight}"/></a:solidFill><a:latin typeface="Segoe UI"/></a:rPr><a:t>{Escape(slide.Title)}</a:t></a:r></a:p></p:txBody>
              </p:sp>
              <p:sp>
                <p:nvSpPr><p:cNvPr id="4" name="Line"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
                <p:spPr><a:xfrm><a:off x="685800" y="{titleTop + 900000}"/><a:ext cx="10820400" cy="19050"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="{Slides.Sage}"/></a:solidFill><a:ln><a:noFill/></a:ln></p:spPr>
                <p:txBody><a:bodyPr/><a:lstStyle/><a:p/></p:txBody>
              </p:sp>
              <p:sp>
                <p:nvSpPr><p:cNvPr id="5" name="Text"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
                <p:spPr><a:xfrm><a:off x="685800" y="{textTop}"/><a:ext cx="10820400" cy="4114800"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>
                <p:txBody><a:bodyPr wrap="square"><a:normAutofit/></a:bodyPr><a:lstStyle/>{bullets}</p:txBody>
              </p:sp>
              {footer}
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """;
    }
}
