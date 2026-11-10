using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace VoteCheck.Core.Models
{
    // A committee report (Valiokunnan mietintö, "HaVM 28/2025 vp"), read for the texts a vote
    // in the plenary refers to without stating: the committee's own statements and sections,
    // and those of the dissenting opinions appended to it.
    //
    // The minutes say "Mietinnön lausumaehdotus 1 ”jaa”" or "Tiina Elo on … tehnyt vastalauseen
    // 1 mukaiset neljä lausumaehdotusta", and the words those name are here.
    public sealed class Mietinto
    {
        // The committee's statements by number: "Eduskunta edellyttää, että …".
        public IReadOnlyDictionary<int, string> Lausumat { get; set; } = new Dictionary<int, string>();

        // The committee's wording of each section, by law and section: (1, "24 §").
        public IReadOnlyDictionary<(int Laki, string Pykala), string> Pykalat { get; set; } =
            new Dictionary<(int, string), string>();

        // How many laws the report proposes. A vote that names a section but not its law can
        // only be placed when there is one.
        public int Lakeja { get; set; }

        public IReadOnlyList<Vastalause> Vastalauseet { get; set; } = new List<Vastalause>();

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
        private static readonly Regex LeadingNumber = new(@"^(?<n>\d+)\.\s*", RegexOptions.CultureInvariant);
        private static readonly Regex DissentTitle = new(@"^Vastalause(?:\s+(?<n>\d+))?$", RegexOptions.CultureInvariant);

        public static Mietinto Parse(string xml)
        {
            var report = new Mietinto();
            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (XmlException)
            {
                return report;
            }

            // Everything inside a dissenting opinion belongs to it and not to the committee,
            // even where the element names are the same.
            bool InDissent(XElement e) => e.Ancestors().Any(a => a.Name.LocalName == "JasenMielipideOsa");

            var statements = new Dictionary<int, string>();
            foreach (var statement in document.Descendants().Where(e => e.Name.LocalName == "LausumaKannanotto" && !InDissent(e)))
            {
                var number = statement.Elements().FirstOrDefault(e => e.Name.LocalName == "LausumaKannanottoNro");
                var n = LeadingNumber.Match(Text(number));
                if (!n.Success)
                    continue;
                var text = Collapse(string.Concat(statement.Elements()
                    .Where(e => e.Name.LocalName != "LausumaKannanottoNro")
                    .Select(e => Wording(e) + " ")));
                statements[int.Parse(n.Groups["n"].Value)] = text;
            }
            report.Lausumat = statements;

            var laws = document.Descendants()
                .Where(e => e.Name.LocalName == "Saados" && !InDissent(e))
                .ToList();
            report.Lakeja = laws.Count;
            report.Pykalat = Sections(laws);

            var dissents = new List<Vastalause>();
            foreach (var dissent in document.Descendants().Where(e => e.Name.LocalName == "JasenMielipideOsa"))
            {
                var title = Text(dissent.Elements().FirstOrDefault(e => e.Name.LocalName == "OtsikkoTeksti"));
                var match = DissentTitle.Match(title);
                if (!match.Success)
                    continue;

                // "1.\tEduskunta edellyttää, että …" — numbered in the text, not in an element.
                var dissentStatements = dissent.Descendants()
                    .Where(e => e.Name.LocalName == "SuppeaLausumaKannanottoOsa")
                    .SelectMany(e => e.Elements().Where(c => c.Name.LocalName == "SisennettyKappaleKooste"))
                    .Select(e => Collapse(Wording(e)))
                    .Select(t => LeadingNumber.Match(t) is { Success: true } m ? (Number: int.Parse(m.Groups["n"].Value), Text: t[m.Length..]) : (Number: 0, Text: ""))
                    .ToList();

                // Statements numbered 1, 2, 3 … in order, or none are used: a list that skips or
                // repeats a number cannot be matched to "ehdotus 2" with any confidence.
                var ordered = dissentStatements.Select((s, i) => s.Number == i + 1).All(ok => ok);

                dissents.Add(new Vastalause
                {
                    Numero = match.Groups["n"].Success ? int.Parse(match.Groups["n"].Value) : null,
                    Lausumat = ordered ? dissentStatements.Select(s => s.Text).ToList() : new List<string>(),
                    Pykalat = Sections(dissent.Descendants().Where(e => e.Name.LocalName == "Saados")),
                });
            }
            report.Vastalauseet = dissents;

            return report;
        }

        // Fills in the text a reference from the minutes points to, when this report has it.
        // Returns whether it did. Anything that does not line up — a dissent number the report
        // does not have, a different number of statements than the minutes counted — leaves
        // the option as it was.
        public bool Resolve(PoytakirjaVaihtoehto option)
        {
            var reference = option.Viite;
            if (reference == null)
                return false;

            string? text = null;
            string context = "";

            if (!reference.Vastalauseesta)
            {
                if (reference.Lausuma is int n)
                    text = Lausumat.TryGetValue(n, out var s) ? s : null;
                else if (reference.Pykala != null && Law(reference.Laki, Lakeja) is int law)
                {
                    text = Pykalat.TryGetValue((law, reference.Pykala), out var p) ? p : null;
                    context = $"{law}. lakiehdotus, {reference.Pykala}";
                }
            }
            else
            {
                var dissent = reference.Vastalause is int v
                    ? Vastalauseet.SingleOrDefault(d => d.Numero == v)
                    : Vastalauseet.Count == 1 ? Vastalauseet[0] : null;
                if (dissent == null)
                    return false;

                var name = dissent.Numero is int dn ? $"Vastalause {dn}" : "Vastalause";
                if (reference.Lausuma is int n)
                {
                    if (reference.Maara is int count && count != dissent.Lausumat.Count)
                        return false;
                    if (n >= 1 && n <= dissent.Lausumat.Count)
                        text = dissent.Lausumat[n - 1];
                    context = name;
                }
                else if (reference.Pykala != null && Law(reference.Laki, Lakeja) is int law)
                {
                    text = dissent.Pykalat.TryGetValue((law, reference.Pykala), out var p) ? p : null;
                    context = $"{name}: {law}. lakiehdotus, {reference.Pykala}";
                }
            }

            if (string.IsNullOrEmpty(text))
                return false;

            option.Teksti = text;
            option.Kohta = context;
            return true;
        }

        private static int? Law(int? stated, int laws) => stated ?? (laws == 1 ? 1 : null);

        // Sections by law number and "24 §", read from each section's own heading: a dissent's
        // sections do not always carry the identifier attribute the committee's do.
        private static IReadOnlyDictionary<(int, string), string> Sections(IEnumerable<XElement> laws)
        {
            var sections = new Dictionary<(int, string), string>();
            foreach (var law in laws)
            {
                var number = LeadingNumber.Match(Text(law.Descendants().FirstOrDefault(e => e.Name.LocalName == "LakiehdotusNumeroKooste")));
                // A report proposing one law may leave it unnumbered.
                var lawNumber = number.Success ? int.Parse(number.Groups["n"].Value) : 1;

                foreach (var section in law.Descendants().Where(e => e.Name.LocalName == "Pykala"))
                {
                    var heading = Text(section.Elements().FirstOrDefault(e => e.Name.LocalName == "PykalaTunnusKooste"));
                    if (heading.Length == 0)
                        continue;
                    var key = (lawNumber, Normalise(heading));
                    // A section stated twice is not one this can pick between.
                    sections[key] = sections.ContainsKey(key) ? "" : Collapse(string.Join(" ", section.Elements().Select(Wording)));
                }
            }
            return sections;
        }

        // "24 §", "4 a §": one space between the parts, so the minutes' "24 §" finds the
        // report's "24§" or "24  §".
        internal static string Normalise(string section) =>
            Collapse(Regex.Replace(section, @"\s*§\s*$", " §", RegexOptions.CultureInvariant));

        // What an element says once its marked changes are applied. A report marks how its
        // wording differs from what it amends — <PoistoTeksti>18 </PoistoTeksti><MuutosTeksti>20
        // </MuutosTeksti> — and the struck words are not part of it. Read with them, a dissent's
        // "alle 20-vuotiaan" came out as "alle 18 20-vuotiaan".
        private static string Wording(XElement element) =>
            string.Concat(element.DescendantNodes()
                .OfType<XText>()
                .Where(t => !t.Ancestors().TakeWhile(a => a != element).Any(a => a.Name.LocalName == "PoistoTeksti")));

        private static string Text(XElement? element) => element == null ? "" : Collapse(element.Value);

        private static string Collapse(string text) => Whitespace.Replace(text, " ").Trim();
    }

    // A dissenting opinion appended to a committee report.
    public sealed class Vastalause
    {
        // "Vastalause 1" → 1. Null when the report has a single, unnumbered one.
        public int? Numero { get; set; }

        // Its statements in order; statement n is at index n − 1.
        public IReadOnlyList<string> Lausumat { get; set; } = new List<string>();

        public IReadOnlyDictionary<(int Laki, string Pykala), string> Pykalat { get; set; } =
            new Dictionary<(int, string), string>();
    }

    // Where in a committee report the text behind one side of a vote is, as the minutes point
    // to it. Either a statement (Lausuma) or a section (Laki, Pykala), of the committee or of
    // one of the dissenting opinions.
    public sealed class MietintoViite
    {
        public bool Vastalauseesta { get; set; }

        // The dissent's number; null for the committee, or for "vastalauseen mukaiset" when
        // the report has only one.
        public int? Vastalause { get; set; }

        public int? Lausuma { get; set; }

        // How many statements the minutes said the member made from this dissent. Checked
        // against the dissent's own count before any is used.
        public int? Maara { get; set; }

        public int? Laki { get; set; }
        public string? Pykala { get; set; }
    }
}
