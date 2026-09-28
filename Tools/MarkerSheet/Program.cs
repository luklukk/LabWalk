// Generates the printable Lab Walk calibration markers.
// Usage: dotnet run --project Tools/MarkerSheet -- <output folder> [LW1 LW2 LW3 ...]
// Writes LabWalk-markers.html, LabWalk-markers.pdf (all markers) and LabWalk-marker-<ID>.pdf (one each).
// Each QR code's text is its marker ID; the Rhino model needs a point named "LabWalk marker <ID>" at its center.
// The PDFs are vector (every module is an exact square) on US Letter, with the print-at-actual-size preference set;
// the code and its white quiet zone also fit on A4.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using QRCoder;

static class Program
{
    const double CodeCm=15; // printed black-square width; Horizon OS tracking favors large codes
    const int Quiet=4;      // QR quiet zone in modules, left as white paper around the code

    static void Main(string[] args)
    {
        var folder=Path.GetFullPath(args.Length>0 ? args[0] : ".");
        var ids=args.Length>1 ? args.Skip(1).ToArray() : new[]{"LW1","LW2","LW3"};
        Directory.CreateDirectory(folder);
        using var generator=new QRCodeGenerator();
        var codes=ids.Select(id=>(id,modules:Modules(generator,id))).ToList();
        File.WriteAllText(Path.Combine(folder,"LabWalk-markers.html"),Html(codes));
        File.WriteAllBytes(Path.Combine(folder,"LabWalk-markers.pdf"),Pdf(codes));
        foreach(var code in codes) File.WriteAllBytes(Path.Combine(folder,$"LabWalk-marker-{code.id}.pdf"),Pdf(new[]{code}));
        Console.WriteLine($"Wrote markers {string.Join(", ",ids)} to {folder}");
    }

    // The code's modules without QRCoder's quiet zone (the paper provides it).
    static bool[,] Modules(QRCodeGenerator generator,string id)
    {
        using var data=generator.CreateQrCode(id,QRCodeGenerator.ECCLevel.M);
        var m=data.ModuleMatrix; int n=m.Count-2*Quiet;
        var modules=new bool[n,n];
        for(int y=0;y<n;y++) for(int x=0;x<n;x++) modules[y,x]=m[y+Quiet][x+Quiet];
        return modules;
    }

    static string[] Instructions(string id) => new[]{
        "Print at Actual size (100%). Do not use \"Fit to page\" or \"Shrink\". The black square must measure",
        $"{CodeCm:0.0} cm: check with the 10 cm bar below. Keep all the white paper around the code (at least 3 cm).",
        "Tape it flat and level on the wall, in even light, away from glare. Matte paper works best.",
        $"Record its position: floor to the bottom edge of the black square + {CodeCm/2:0.0} cm = height of the center;",
        $"room corner to the nearer side edge + {CodeCm/2:0.0} cm = distance of the center along the wall.",
        $"The Rhino model needs a point named \"LabWalk marker {id}\" exactly at the center, on the wall surface.",
    };

    // ---- PDF (vector, US Letter) ----

    static byte[] Pdf(IEnumerable<(string id,bool[,] modules)> codes)
    {
        const double pageW=612, pageH=792, pt=72/2.54; // points per cm
        double size=CodeCm*pt;
        var objects=new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /ViewerPreferences << /PrintScaling /None >> >>",
            null, // pages, filled in below
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };
        var kids=new List<int>();
        foreach(var (id,modules) in codes)
        {
            int n=modules.GetLength(0);
            double module=size/n, x0=(pageW-size)/2, y0=235; // quiet zone: 4 modules (about 2.9 cm) of clear paper on every side
            var s=new StringBuilder();
            s.Append("0 g\n");
            for(int row=0;row<n;row++)
                for(int col=0;col<n;)
                {
                    if(!modules[row,col]) { col++; continue; }
                    int run=col; while(run<n && modules[row,run]) run++;
                    s.Append($"{F(x0+col*module)} {F(y0+size-(row+1)*module)} {F((run-col)*module)} {F(module)} re\n");
                    col=run;
                }
            s.Append("f\n");
            s.Append($"BT /F2 20 Tf 54 750 Td ({Esc("Lab Walk marker "+id)}) Tj ET\n");
            s.Append($"BT /F1 10 Tf 54 733 Td ({Esc($"QR code text: {id}   |   black square {CodeCm:0.0} cm x {CodeCm:0.0} cm")}) Tj ET\n");
            var lines=Instructions(id);
            for(int i=0;i<lines.Length;i++) s.Append($"BT /F1 9.5 Tf 54 {F(138-i*12.5)} Td ({Esc(lines[i])}) Tj ET\n");
            s.Append($"54 50 {F(10*pt)} 7 re f\n");
            s.Append($"BT /F1 9 Tf 54 38 Td ({Esc("10 cm (check with a ruler)")}) Tj ET\n");
            var content=s.ToString();
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
            int contentId=objects.Count;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageW)} {F(pageH)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
            kids.Add(objects.Count);
        }
        objects[1]=$"<< /Type /Pages /Kids [{string.Join(" ",kids.Select(k=>k+" 0 R"))}] /Count {kids.Count} >>";
        objects.Add("<< /Title (Lab Walk calibration markers) /Creator (Lab Walk Tools/MarkerSheet) >>");
        int info=objects.Count;

        using var output=new MemoryStream();
        void Write(string text) { var b=Encoding.ASCII.GetBytes(text); output.Write(b,0,b.Length); }
        Write("%PDF-1.4\n");
        output.Write(new byte[]{(byte)'%',0xE2,0xE3,0xCF,0xD3,(byte)'\n'},0,6); // binary marker comment
        var offsets=new List<long>();
        for(int i=0;i<objects.Count;i++) { offsets.Add(output.Position); Write($"{i+1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref=output.Position;
        Write($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n");
        foreach(var o in offsets) Write($"{o:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    static string F(double v) => v.ToString("0.###",CultureInfo.InvariantCulture);
    static string Esc(string s) => s.Replace("\\","\\\\").Replace("(","\\(").Replace(")","\\)");

    // ---- HTML (for viewing; print the PDFs) ----

    static string Html(IEnumerable<(string id,bool[,] modules)> codes)
    {
        var html=new StringBuilder();
        html.Append(@"<!doctype html><html lang=""en""><head><meta charset=""utf-8""><title>Lab Walk markers</title><style>
@page { size: auto; margin: 0.6cm; }
body { font-family: system-ui, sans-serif; margin: 0; color: #000; background: #fff; }
.page { page-break-after: always; break-after: page; text-align: center; padding-top: 0.4cm; }
.page:last-child { page-break-after: auto; break-after: auto; }
h1 { font-size: 28pt; margin: 0 0 0.3cm; }
.code { display: block; margin: 2.9cm auto; width: " + CodeCm + @"cm; height: " + CodeCm + @"cm; }
.note { font-size: 10pt; max-width: 17cm; margin: 0 auto; text-align: left; line-height: 1.35; }
.scale { margin: 0.4cm auto 0; width: 10cm; height: 0.3cm; background: #000; }
.scale-label { font-size: 9pt; }
@media screen { .page { border-bottom: 1px dashed #999; padding-bottom: 1cm; } }
</style></head><body>
");
        foreach(var (id,modules) in codes)
        {
            int n=modules.GetLength(0);
            var svg=new StringBuilder($@"<svg class=""code"" viewBox=""0 0 {n} {n}"" shape-rendering=""crispEdges"" xmlns=""http://www.w3.org/2000/svg""><rect width=""{n}"" height=""{n}"" fill=""#fff""/><path fill=""#000"" d=""");
            for(int y=0;y<n;y++) for(int x=0;x<n;x++) if(modules[y,x]) svg.Append($"M{x} {y}h1v1h-1z");
            svg.Append(@"""/></svg>");
            html.Append($@"<section class=""page""><h1>Lab Walk marker {id}</h1>{svg}
<div class=""note""><b>For printing, use the PDF files</b> (LabWalk-markers.pdf); browsers often rescale pages.<br>{string.Join("<br>",Instructions(id).Select(System.Net.WebUtility.HtmlEncode))}</div>
<div class=""scale""></div><div class=""scale-label"">10 cm</div></section>
");
        }
        html.Append("</body></html>\n");
        return html.ToString();
    }
}
