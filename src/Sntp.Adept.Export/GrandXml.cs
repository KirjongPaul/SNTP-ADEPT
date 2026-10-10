using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Export;

public static class GrandXml
{
    public const string ProjectEstimateType = GrandGuids.ProjectEstimate;

    static GrandXml()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Win1251 => Encoding.GetEncoding(1251);

    public static PirDocument LoadFile(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".gsfx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return LoadGsfx(path);
        return LoadXml(File.ReadAllBytes(path), Path.GetFileName(path));
    }

    public static PirDocument LoadGsfx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("Data.xml")
                    ?? zip.Entries.FirstOrDefault(e => e.Name.Equals("Data.xml", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("В GSFX нет Data.xml");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return LoadXml(ms.ToArray(), Path.GetFileName(path));
    }

    public static PirDocument LoadXml(byte[] bytes, string? fileName = null)
    {
        var xml = DecodeXml(bytes);
        var doc = new XmlDocument { PreserveWhitespace = false };
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        doc.Load(reader);

        var root = doc.DocumentElement ?? throw new InvalidOperationException("Пустой XML");
        if (fileName?.EndsWith(".gge", StringComparison.OrdinalIgnoreCase) == true
            || root.LocalName.Equals("Construction", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("gge-not-pir: XML Минстроя (корень Construction, файл .gge) не является сметой на ПИР.");

        if (!string.Equals(root.GetAttribute("DocumentType"), ProjectEstimateType, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(root.GetAttribute("DocumentType")))
        {
            var dt = root.GetAttribute("DocumentType");
            if (string.Equals(dt, GrandGuids.SmrLocal, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Это XML локальной сметы СМР ({2B0470FD-…}), нужен тип проектной сметы {39200715-…}.");
        }

        var pir = new PirDocument
        {
            Source = new SourceInfo { File = fileName, Kind = "grand-xml" },
            Project = new ProjectHeader
            {
                ObjectName = fileName ?? "Проектная смета",
                Stage = StageHelper.FromGrandPhase(root.GetAttribute("ProjectPhase")),
                StageLabel = StageHelper.ToLabel(StageHelper.FromGrandPhase(root.GetAttribute("ProjectPhase"))),
                RegionName = root.GetAttribute("RegionName"),
                ZoneName = root.GetAttribute("ZoneName"),
                PriceLevel = "2001",
                RoundMode = root.GetAttribute("RoundMode") == "Round10" ? RoundMode.Round10 : RoundMode.None,
                KponPolicy = KponPolicy.AutoKpon
            },
            Grand = new GrandDocumentExtras
            {
                DocumentType = string.IsNullOrEmpty(root.GetAttribute("DocumentType"))
                    ? ProjectEstimateType
                    : root.GetAttribute("DocumentType"),
                ProgramVersion = root.GetAttribute("ProgramVersion"),
                Generator = AttrOr(root, "Generator", "GrandSmeta"),
                RegionCode = root.GetAttribute("RegionCode"),
                MaxSysId = root.GetAttribute("MaxSysID"),
                ItogExpandTo = root.GetAttribute("ItogExpandTo"),
                InternalCounter = IntAttr(root.SelectSingleNode("IdsGenerator") as XmlElement, "InternalCounter")
            }
        };

        if (root.SelectSingleNode("GsDocSignatures") is XmlElement sig)
            pir.Grand.RawSignaturesXml = sig.OuterXml;
        if (root.SelectSingleNode("VidRab_Catalog") is XmlElement vr)
            pir.Grand.RawVidRabXml = vr.OuterXml;
        if (root.SelectSingleNode("OsInfo") is XmlElement os)
            pir.Grand.RawOsInfoXml = os.OuterXml;
        if (root.SelectSingleNode("RZDCalcOptions") is XmlElement rzd)
            pir.Grand.RawRzdXml = rzd.OuterXml;
        if (root.SelectSingleNode("AddZatrats") is XmlElement az)
            pir.Grand.RawAddZatratsXml = az.OuterXml;
        if (root.SelectSingleNode("Parameters") is XmlElement pr)
            pir.Grand.RawParametersXml = pr.OuterXml;

        var est = new Estimate
        {
            Id = "1",
            Number = "1",
            Title = pir.Project.ObjectName,
            Form = EstimateForm.Form2PSbc,
            Sheet = fileName ?? "XML",
            PriceLevel = "2001"
        };

        foreach (XmlElement fe in root.SelectNodes("Factors/Factor")!)
            est.Factors.Add(ReadFactor(fe));

        var chapterNodes = root.SelectNodes("Chapters/Chapter");
        var firstChapter = chapterNodes is { Count: > 0 } ? chapterNodes[0] as XmlElement : null;
        est.ChapterCaption = firstChapter?.GetAttribute("Caption") ?? "Новый раздел";
        var manyChapters = chapterNodes is { Count: > 1 };
        if (chapterNodes is not null)
        {
            foreach (XmlElement chapter in chapterNodes)
            {
                var caption = chapter.GetAttribute("Caption");
                if (manyChapters)
                {
                    est.Positions.Add(new Position
                    {
                        Kind = PositionKind.Section,
                        Name = string.IsNullOrWhiteSpace(caption) ? "Раздел" : caption,
                        Origin = OriginKind.GrandXml,
                        EstimateNumber = est.Number,
                        SheetName = est.Sheet
                    });
                }

                foreach (XmlElement pe in chapter.SelectNodes("Position")!)
                {
                    var pos = ReadPosition(pe, est);
                    pos.Section = string.IsNullOrWhiteSpace(caption) ? est.ChapterCaption : caption;
                    est.Positions.Add(pos);
                }
            }
        }

        pir.Estimates.Add(est);

        foreach (XmlElement it in root.SelectNodes("RelativeFactorsSummary/Item")!)
        {
            pir.Grand.RelativeSummary.Add(new RelativeSummaryItem
            {
                Caption = it.GetAttribute("Caption"),
                Value = NumberParse.TryParse(it.GetAttribute("Value"))
            });
        }

        CalcEngine.Recalculate(pir);
        return pir;
    }

    public static byte[] WriteUnsignedXml(PirDocument pir, string? estimateId = null)
    {
        var est = SelectEstimate(pir, estimateId);
        var settings = new XmlWriterSettings
        {
            Encoding = Win1251,
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace
        };

        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, settings))
        {
            w.WriteStartDocument();
            w.WriteStartElement("Document");
            w.WriteAttributeString("ProgramVersion", pir.Grand?.ProgramVersion ?? "16.2.1.59411");
            w.WriteAttributeString("Generator", "GrandSmeta");
            w.WriteAttributeString("DocumentType", ProjectEstimateType);
            var maxSys = est.Positions.Select(p => int.TryParse(p.SysId, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            w.WriteAttributeString("MaxSysID", maxSys.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("ProjectPhase", StageHelper.ToGrandPhase(pir.Project.Stage));
            w.WriteAttributeString("RegionCode", pir.Grand?.RegionCode ?? "10010");
            w.WriteAttributeString("RegionName", pir.Project.RegionName ?? "Проектно-изыскательские работы");
            w.WriteAttributeString("ZoneName", pir.Project.ZoneName ?? "Базовый федеральный район");
            w.WriteAttributeString("RoundMode", pir.Project.RoundMode == RoundMode.Round10 ? "Round10" : "Default");
            w.WriteAttributeString("ItogExpandTo", pir.Grand?.ItogExpandTo ?? "Default");

            w.WriteStartElement("IdsGenerator");
            w.WriteAttributeString("InternalCounter", (pir.Grand?.InternalCounter ?? maxSys).ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();

            WriteRawOrDefault(w, pir.Grand?.RawSignaturesXml, "GsDocSignatures", () => DefaultSignatures(w));
            WriteRawOrDefault(w, pir.Grand?.RawParametersXml, "Parameters", () => { w.WriteStartElement("Parameters"); w.WriteEndElement(); });
            WriteRawOrDefault(w, pir.Grand?.RawVidRabXml, "VidRab_Catalog", () => { w.WriteStartElement("VidRab_Catalog"); w.WriteStartElement("Vids_Rab"); w.WriteEndElement(); w.WriteEndElement(); });
            WriteRawOrDefault(w, pir.Grand?.RawAddZatratsXml, "AddZatrats", () => { w.WriteStartElement("AddZatrats"); w.WriteEndElement(); });

            w.WriteStartElement("Factors");
            foreach (var f in est.Factors)
                WriteFactor(w, f);
            w.WriteEndElement();

            w.WriteStartElement("Indexes");
            w.WriteEndElement();

            w.WriteStartElement("Chapters");
            foreach (var (caption, works) in ChaptersOf(est))
            {
                w.WriteStartElement("Chapter");
                w.WriteAttributeString("Caption", caption);
                foreach (var pos in works)
                    WritePosition(w, pos, pir.Project.Stage);
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("RelativeFactorsSummary");
            w.WriteAttributeString("Caption", "Распределение стоимости");
            foreach (var pos in est.Positions)
            foreach (var rf in pos.RelativeFactors.Where(r => r.Enabled))
            {
                w.WriteStartElement("Item");
                w.WriteAttributeString("Caption", rf.Caption);
                if (rf.RelativePrice is decimal rp)
                    w.WriteAttributeString("Value", FormatDec(rp));
                w.WriteEndElement();
            }
            w.WriteEndElement();

            WriteRawOrDefault(w, pir.Grand?.RawOsInfoXml, "OsInfo", () =>
            {
                w.WriteStartElement("OsInfo");
                w.WriteAttributeString("OSChapter", "3");
                w.WriteAttributeString("LinkType", "SB");
                w.WriteStartElement("CCChapter");
                w.WriteAttributeString("Cons", "9");
                w.WriteAttributeString("Rec", "7");
                w.WriteAttributeString("Road", "10");
                w.WriteEndElement();
                w.WriteEndElement();
            });
            if (!string.IsNullOrEmpty(pir.Grand?.RawRzdXml))
                WriteRaw(w, pir.Grand!.RawRzdXml!);

            w.WriteEndElement();
            w.WriteEndDocument();
        }

        return ms.ToArray();
    }

    public static void SaveUnsignedXml(string path, PirDocument pir, string? estimateId = null)
        => File.WriteAllBytes(path, WriteUnsignedXml(pir, estimateId));

    private static Estimate SelectEstimate(PirDocument pir, string? estimateId)
    {
        if (pir.Estimates.Count == 0)
            throw new InvalidOperationException("Нет локальных смет для экспорта. Создайте локальную смету.");
        if (!string.IsNullOrWhiteSpace(estimateId))
            return pir.Estimates.FirstOrDefault(e => e.Id == estimateId || e.Number == estimateId)
                   ?? throw new InvalidOperationException($"Смета {estimateId} не найдена.");
        return pir.Estimates[0];
    }

    private static List<(string Caption, List<Position> Works)> ChaptersOf(Estimate est)
    {
        if (!est.Positions.Any(p => p.Kind == PositionKind.Section))
        {
            return [(est.ChapterCaption ?? "Новый раздел", est.Positions.Where(p => p.Kind == PositionKind.Work).ToList())];
        }

        var result = new List<(string Caption, List<Position> Works)>();
        string? caption = null;
        var bucket = new List<Position>();
        void Flush()
        {
            if (caption is null && bucket.Count == 0) return;
            result.Add((caption ?? est.ChapterCaption ?? "Новый раздел", bucket.ToList()));
            bucket = [];
        }

        foreach (var pos in est.Positions)
        {
            if (pos.Kind == PositionKind.Section)
            {
                Flush();
                caption = string.IsNullOrWhiteSpace(pos.Name) ? "Раздел" : pos.Name;
                continue;
            }

            if (pos.Kind != PositionKind.Work) continue;
            caption ??= est.ChapterCaption ?? "Новый раздел";
            bucket.Add(pos);
        }

        Flush();
        if (result.Count == 0)
            result.Add((est.ChapterCaption ?? "Новый раздел", []));
        return result;
    }

    private static Basis ReadBasis(XmlElement pe)
    {
        var esn = pe.GetAttribute("ESNTable");
        var code = pe.GetAttribute("Code");
        var basis = BasisParser.Parse(string.IsNullOrWhiteSpace(esn) ? code : esn);
        if (basis.Kind == "Unknown" && !string.IsNullOrWhiteSpace(code) && !string.Equals(code, esn, StringComparison.Ordinal))
            basis = BasisParser.Parse(code);
        return basis;
    }

    private static Position ReadPosition(XmlElement pe, Estimate est)
    {
        var pos = new Position
        {
            SysId = pe.GetAttribute("SysID"),
            Number = pe.GetAttribute("Number"),
            Name = pe.GetAttribute("Caption"),
            Code = pe.GetAttribute("Code"),
            Units = pe.GetAttribute("Units"),
            EsnTable = pe.GetAttribute("ESNTable"),
            PriceA = NumberParse.TryParse(pe.GetAttribute("PriceA")),
            PriceB = NumberParse.TryParse(pe.GetAttribute("PriceB")),
            Formula = pe.GetAttribute("Formula"),
            ExpandedFormula = pe.GetAttribute("ExpandedFormula"),
            ExcelAmount = NumberParse.TryParse(pe.GetAttribute("Total")),
            PriceLevel = pe.GetAttribute("PriceLevel"),
            Vr = pe.GetAttribute("Vr"),
            HasDbFactors = pe.GetAttribute("HasDBFactors") == "Yes",
            Origin = OriginKind.GrandXml,
            EstimateNumber = est.Number,
            SheetName = est.Sheet,
            Kind = PositionKind.Work,
            Basis = ReadBasis(pe)
        };
        pos.CatalogCode = AprXml.RateCode(pos.Basis);

        if (pe.SelectSingleNode("Quantity") is XmlElement q)
            pos.Quantity = NumberParse.TryParse(q.GetAttribute("Fx")) ?? NumberParse.TryParse(q.GetAttribute("Result"));

        foreach (XmlElement fe in pe.SelectNodes("Factors/Factor")!)
            pos.Factors.Add(ReadFactor(fe));

        if (pe.SelectSingleNode("RelativeFactors") is XmlElement rfs)
        {
            pos.RelativeFactorsProjectDoc = NumberParse.TryParse(rfs.GetAttribute("ProjectDoc"));
            pos.RelativeFactorsWorkDoc = NumberParse.TryParse(rfs.GetAttribute("WorkDoc"));
            foreach (XmlElement rf in rfs.SelectNodes("RelativeFactor")!)
            {
                pos.RelativeFactors.Add(new RelativeFactor
                {
                    Caption = rf.GetAttribute("Caption"),
                    ProjectDoc = NumberParse.TryParse(rf.GetAttribute("ProjectDoc")),
                    WorkDoc = NumberParse.TryParse(rf.GetAttribute("WorkDoc")),
                    RelativePrice = NumberParse.TryParse(rf.GetAttribute("RelativePrice"))
                });
            }
        }

        if (pe.SelectSingleNode("Items") is XmlElement items)
        {
            foreach (XmlElement it in items.SelectNodes("Item")!)
            {
                pos.Items.Add(new IntervalItem
                {
                    Caption = it.GetAttribute("Caption"),
                    Code = it.GetAttribute("Code"),
                    EsnTable = it.GetAttribute("ESNTable"),
                    MinQuantity = NumberParse.TryParse(it.GetAttribute("MinQuantity")),
                    MaxQuantity = NumberParse.TryParse(it.GetAttribute("MaxQuantity")),
                    PriceA = NumberParse.TryParse(it.GetAttribute("PriceA")),
                    PriceB = NumberParse.TryParse(it.GetAttribute("PriceB"))
                });
            }
            if (pos.Items.Count == 1)
            {
                pos.Xmin = pos.Items[0].MinQuantity;
                pos.Xmax = pos.Items[0].MaxQuantity;
            }
        }

        return pos;
    }

    private static Factor ReadFactor(XmlElement fe) => new()
    {
        Caption = fe.GetAttribute("Caption"),
        Identifier = fe.GetAttribute("Identifier"),
        Code = EmptyToNull(fe.GetAttribute("Code")),
        ProjectDoc = NumberParse.TryParse(fe.GetAttribute("ProjectDoc")),
        WorkDoc = NumberParse.TryParse(fe.GetAttribute("WorkDoc")),
        WorkProject = NumberParse.TryParse(fe.GetAttribute("WorkProject")),
        Level = int.TryParse(fe.GetAttribute("Level"), out var lv) ? lv : 0,
        Options = EmptyToNull(fe.GetAttribute("Options")),
        Role = fe.GetAttribute("Identifier").Contains("Ки", StringComparison.OrdinalIgnoreCase) ? "stage" : "other",
        CombineType = FactorCombineType.Pricing
    };

    private static void WritePosition(XmlWriter w, Position pos, DesignStage stage)
    {
        w.WriteStartElement("Position");
        w.WriteAttributeString("Caption", pos.Name);
        if (!string.IsNullOrEmpty(pos.SysId)) w.WriteAttributeString("SysID", pos.SysId);
        w.WriteAttributeString("Number", pos.Number);
        if (!string.IsNullOrEmpty(pos.Code)) w.WriteAttributeString("Code", pos.Code);
        if (!string.IsNullOrEmpty(pos.Units)) w.WriteAttributeString("Units", pos.Units);
        if (!string.IsNullOrEmpty(pos.EsnTable)) w.WriteAttributeString("ESNTable", pos.EsnTable);
        if (pos.PriceA is decimal a) w.WriteAttributeString("PriceA", FormatIntish(a));
        if (pos.PriceB is decimal b) w.WriteAttributeString("PriceB", FormatIntish(b));
        if (!string.IsNullOrEmpty(pos.Formula)) w.WriteAttributeString("Formula", pos.Formula);
        var exp = pos.EngineExpandedFormula ?? pos.ExpandedFormula;
        if (!string.IsNullOrEmpty(exp)) w.WriteAttributeString("ExpandedFormula", exp);
        if (pos.HasDbFactors) w.WriteAttributeString("HasDBFactors", "Yes");
        if (!string.IsNullOrEmpty(pos.Vr)) w.WriteAttributeString("Vr", pos.Vr);
        if (pos.EngineAmount is decimal tot) w.WriteAttributeString("Total", FormatIntish(tot));
        if (!string.IsNullOrEmpty(pos.PriceLevel)) w.WriteAttributeString("PriceLevel", pos.PriceLevel);

        w.WriteStartElement("Quantity");
        w.WriteAttributeString("Fx", FormatDec(pos.Quantity ?? 0));
        w.WriteAttributeString("Result", FormatDec(pos.Quantity ?? 0));
        w.WriteEndElement();

        if (pos.Factors.Count > 0)
        {
            w.WriteStartElement("Factors");
            foreach (var f in pos.Factors) WriteFactor(w, f);
            w.WriteEndElement();
        }

        if (pos.RelativeFactors.Count > 0)
        {
            w.WriteStartElement("RelativeFactors");
            if (pos.RelativeFactorsProjectDoc is decimal pd) w.WriteAttributeString("ProjectDoc", FormatDec(pd));
            if (pos.RelativeFactorsWorkDoc is decimal wd) w.WriteAttributeString("WorkDoc", FormatDec(wd));
            foreach (var rf in pos.RelativeFactors)
            {
                w.WriteStartElement("RelativeFactor");
                w.WriteAttributeString("Caption", rf.Caption);
                if (rf.ProjectDoc is decimal rpd) w.WriteAttributeString("ProjectDoc", FormatDec(rpd));
                if (rf.WorkDoc is decimal rwd) w.WriteAttributeString("WorkDoc", FormatDec(rwd));
                if (rf.RelativePrice is decimal rp) w.WriteAttributeString("RelativePrice", FormatDec(rp));
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        if (pos.Items.Count > 0)
        {
            w.WriteStartElement("Items");
            w.WriteAttributeString("Caption", pos.Name);
            foreach (var it in pos.Items)
            {
                w.WriteStartElement("Item");
                if (!string.IsNullOrEmpty(it.Caption)) w.WriteAttributeString("Caption", it.Caption);
                if (!string.IsNullOrEmpty(it.Code)) w.WriteAttributeString("Code", it.Code);
                if (!string.IsNullOrEmpty(it.EsnTable)) w.WriteAttributeString("ESNTable", it.EsnTable);
                if (it.MinQuantity is decimal mn) w.WriteAttributeString("MinQuantity", FormatDec(mn));
                if (it.MaxQuantity is decimal mx) w.WriteAttributeString("MaxQuantity", FormatDec(mx));
                if (it.PriceA is decimal ia) w.WriteAttributeString("PriceA", FormatIntish(ia));
                if (it.PriceB is decimal ib) w.WriteAttributeString("PriceB", FormatIntish(ib));
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static void WriteFactor(XmlWriter w, Factor f)
    {
        w.WriteStartElement("Factor");
        w.WriteAttributeString("Caption", f.Caption);
        w.WriteAttributeString("Identifier", f.Identifier);
        if (!string.IsNullOrEmpty(f.Code)) w.WriteAttributeString("Code", f.Code);
        if (f.ProjectDoc is decimal pd) w.WriteAttributeString("ProjectDoc", FormatDec(pd));
        if (f.WorkDoc is decimal wd) w.WriteAttributeString("WorkDoc", FormatDec(wd));
        if (f.WorkProject is decimal wp) w.WriteAttributeString("WorkProject", FormatDec(wp));
        if (f.Level != 0) w.WriteAttributeString("Level", f.Level.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(f.Options)) w.WriteAttributeString("Options", f.Options);
        w.WriteEndElement();
    }

    private static void DefaultSignatures(XmlWriter w)
    {
        w.WriteStartElement("GsDocSignatures");
        foreach (var (cap, id) in new[] { ("Главный инженер проекта", "360"), ("Составил", "300"), ("Проверил", "310") })
        {
            w.WriteStartElement("Item");
            w.WriteAttributeString("Caption", cap);
            w.WriteAttributeString("ID", id);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void WriteRawOrDefault(XmlWriter w, string? raw, string name, Action fallback)
    {
        if (!string.IsNullOrWhiteSpace(raw)) WriteRaw(w, raw);
        else fallback();
    }

    private static void WriteRaw(XmlWriter w, string raw)
    {
        using var r = XmlReader.Create(new StringReader(raw), new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment });
        w.WriteNode(r, false);
    }

    private static string DecodeXml(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes);
        var text = Win1251.GetString(bytes);
        if (text.Contains("encoding=\"utf-8\"", StringComparison.OrdinalIgnoreCase)
            || text.Contains("encoding='utf-8'", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetString(bytes);
        return text;
    }

    private static string FormatDec(decimal v)
    {
        var s = v.ToString(CultureInfo.GetCultureInfo("ru-RU"));
        return s;
    }

    private static string FormatIntish(decimal v)
    {
        if (v == decimal.Truncate(v))
            return decimal.Truncate(v).ToString(CultureInfo.InvariantCulture);
        return FormatDec(v);
    }

    private static string AttrOr(XmlElement el, string name, string fallback)
    {
        var v = el.GetAttribute(name);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    private static int? IntAttr(XmlElement? el, string name)
    {
        if (el is null) return null;
        return int.TryParse(el.GetAttribute(name), out var n) ? n : null;
    }

    private static string? EmptyToNull(string s) => string.IsNullOrEmpty(s) ? null : s;
}
