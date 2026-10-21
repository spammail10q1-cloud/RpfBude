using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace RpfStudio
{
    /// <summary>Baut und installiert .oiv-Pakete (OpenIV-kompatibles Format).</summary>
    public static class OivPackage
    {
        public static string Build(string sourceFolder, string targetRelative, string packageName, string author, string outPath, string dlcName = null)
        {
            var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<package version=\"2.1\" id=\"{{{Guid.NewGuid()}}}\" target=\"Five\">");
            sb.AppendLine("<metadata>");
            sb.AppendLine($"<name>{SecurityElement.Escape(packageName)}</name>");
            sb.AppendLine("<version><major>1</major><minor>0</minor><tag>Public</tag></version>");
            sb.AppendLine($"<author><displayName>{SecurityElement.Escape(author)}</displayName><actionLink></actionLink><web></web></author>");
            sb.AppendLine($"<description footerLink=\"\" footerLinkTitle=\"\"><![CDATA[{packageName} - erstellt mit RPF Studio.]]></description>");
            sb.AppendLine("<largeDescription displayName=\"\" footerLink=\"\" footerLinkTitle=\"\"><![CDATA[]]></largeDescription>");
            sb.AppendLine("<licence footerLink=\"\" footerLinkTitle=\"\"><![CDATA[]]></licence>");
            sb.AppendLine("</metadata>");
            sb.AppendLine("<colors><headerBackground useBlackTextColor=\"false\">$FF4286F4</headerBackground><iconBackground>$FF4286F4</iconBackground></colors>");
            sb.AppendLine("<content>");

            var zipEntries = new List<(string entry, string file)>();
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(sourceFolder, f).Replace('\\', '/');
                var target = (targetRelative.Trim('\\', '/') + "/" + rel).Replace('\\', '/').Replace('/', '\\');
                var entry = "content/" + rel;
                sb.AppendLine($"<add source=\"{entry}\">{SecurityElement.Escape(target)}</add>");
                zipEntries.Add((entry, f));
            }

            if (!string.IsNullOrWhiteSpace(dlcName))
            {
                sb.AppendLine("<archive path=\"update\\update.rpf\" createIfNotExist=\"false\" type=\"RPF7\">");
                sb.AppendLine($"<xml path=\"common\\data\\dlclist.xml\"><add xpath=\"/SMandatoryPacksData/Paths\"><Item>dlcpacks:/{dlcName}/</Item></add></xml>");
                sb.AppendLine("</archive>");
            }

            sb.AppendLine("</content>");
            sb.AppendLine("</package>");

            if (File.Exists(outPath)) File.Delete(outPath);
            using var fs = File.Create(outPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var asmEntry = zip.CreateEntry("assembly.xml", CompressionLevel.Optimal);
            var xmlBytes = new UTF8Encoding(false).GetBytes(sb.ToString());
            using (var s = asmEntry.Open()) s.Write(xmlBytes, 0, xmlBytes.Length);
            foreach (var (entry, file) in zipEntries)
            {
                var ze = zip.CreateEntry(entry, CompressionLevel.Optimal);
                using var s = ze.Open();
                using var src = File.OpenRead(file);
                src.CopyTo(s);
            }
            return outPath;
        }

        public static string Install(string oivPath, string gameDir, Action<string> log)
        {
            using var zip = ZipFile.OpenRead(oivPath);
            var asm = zip.GetEntry("assembly.xml");
            if (asm == null) throw new InvalidDataException("assembly.xml fehlt im Paket.");
            XDocument doc;
            using (var s = asm.Open()) doc = XDocument.Load(s);
            var content = doc.Root?.Element("content");
            if (content == null) throw new InvalidDataException("<content> fehlt.");

            int copied = 0, patched = 0;
            foreach (var add in content.Elements("add"))
            {
                var srcName = (string)add.Attribute("source");
                var target = add.Value.Trim().Replace('/', '\\');
                if (string.IsNullOrWhiteSpace(srcName) || string.IsNullOrWhiteSpace(target)) continue;
                var ze = zip.GetEntry(srcName.Replace('\\', '/'));
                if (ze == null) { log($"  ! fehlt im Paket: {srcName}"); continue; }
                var dest = Path.Combine(gameDir, target);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (var s = ze.Open())
                using (var f = File.Create(dest)) s.CopyTo(f);
                copied++;
            }

            foreach (var arch in content.Elements("archive"))
            {
                var archPath = ((string)arch.Attribute("path") ?? "").Replace('/', '\\');
                if (!archPath.Equals(@"update\update.rpf", StringComparison.OrdinalIgnoreCase)) { log($"  ! Archiv {archPath} nicht unterstützt - übersprungen"); continue; }
                var rpfPath = Path.Combine(gameDir, archPath);
                if (!File.Exists(rpfPath)) { log($"  ! {archPath} nicht gefunden"); continue; }

                var rpf = new RpfArchive { FilePath = rpfPath };
                rpf.Load();
                var dl = rpf.Find("common", "data", "dlclist.xml");
                if (dl == null) { log("  ! dlclist.xml nicht gefunden"); continue; }
                var xml = Encoding.UTF8.GetString(rpf.Extract(dl));
                bool changed = false;
                foreach (var x in arch.Elements("xml"))
                {
                    foreach (var item in x.Descendants().Where(e => e.Name.LocalName == "Item"))
                    {
                        var tag = item.Value.Trim();
                        if (string.IsNullOrWhiteSpace(tag) || xml.Contains(tag)) continue;
                        int idx = xml.IndexOf("</Paths>", StringComparison.OrdinalIgnoreCase);
                        if (idx < 0) continue;
                        xml = xml.Insert(idx, "\t\t<Item>" + tag + "</Item>\r\n\t");
                        changed = true;
                    }
                }
                if (changed)
                {
                    new System.Xml.XmlDocument().LoadXml(xml);
                    rpf.Backup("before-oiv");
                    rpf.Replace(dl, Encoding.UTF8.GetBytes(xml));
                    patched++;
                }
            }
            return $"{copied} Datei(en) kopiert, {patched} Archiv(e) gepatcht.";
        }
    }
}
