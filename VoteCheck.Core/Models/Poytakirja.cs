using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace VoteCheck.Core.Models
{
    // A plenary session's minutes (Pöytäkirja, "PTK 108/2026 vp"), read for what a division
    // payload does not say: what each side of a vote stood for, in the parliament's own words.
    //
    // The division payload names the options as a procedural title — "mietintö JAA / 2 EI" —
    // and stops there. The minutes state the setup ("Mietintö ”jaa”, Hanna Sarkkisen ehdotus 2
    // ”ei”."), the outcome ("Eduskunta hyväksyi mietinnön."), and in the same item the text of
    // the proposals made in the debate. Every vote is tagged with its session and vote number,
    // so it joins to a division exactly rather than by matching titles.
    //
    // Only the full session document carries every vote. The per-matter items
    // (Pöytäkirjan asiakohta) leave out the annual budget's per-chapter votes, which are two
    // thirds of the archive. See docs/eduskunta-api.md.
    public sealed class Poytakirja
    {
        public string Edktunnus { get; set; } = "";

        public IReadOnlyList<PoytakirjaAanestys> Aanestykset { get; set; } = new List<PoytakirjaAanestys>();

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

        // "1) Hanna Sarkkisen ehdotus 2 ”jaa”, Saara Hyrkön ehdotus 7 ”ei”." The numbered prefix
        // appears when one question takes several votes. Only a setup that marks both sides in
        // so many words is read; anything else keeps its wording and resolves no options.
        private static readonly Regex Setup = new(
            @"^(?:\d+\)\s*)?(?<yes>.+?)\s*[”""“]jaa[”""“]\s*[,;]\s*(?<no>.+?)\s*[”""“]ei[”""“]\s*\.?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

        // "2. Hanna Sarkkinen (vas) Minja Koskelan (vas) kannattamana 30.20.20: Lausumaehdotus: …"
        // — a proposal from the numbered list a budget item states before its votes. The party
        // in brackets is required: it is the only thing that separates the proposer's name
        // from their seconder's.
        private static readonly Regex NumberedLine = new(
            @"^(?<n>\d+)\.\s+(?<owner>[^()]+?)\s*\([^)]*\)\s+.+?\bkannattamana\s*(?<context>[^:]*?)\s*:\s*(?<text>.+)$",
            RegexOptions.CultureInvariant, RegexTimeout);

        // "Keskustelussa edustaja Lauri Lyly on edustaja Tiina Elon kannattamana ehdottanut,
        // että lakiehdotukset hyväksytään vastalauseen 1 mukaisena." — a sentence recording
        // proposals made in the debate. Several can share one paragraph, so each runs to its own
        // full stop, and only to one that ends a sentence: what follows is a capital, a bracket,
        // a dash ("— Selonteko ruotsiksi.") or nothing. A full stop inside an abbreviation would
        // otherwise cut the proposal short, and a truncated proposal is a different proposal;
        // such a sentence is skipped instead.
        private static readonly Regex ProposalSentence = new(
            @"\b(?:Yleis)?[Kk]eskustelussa\s+(?<body>[^.]*?\bkannattamana\s+(?:ehdottanut|tehnyt)\b[^.]*\.)(?=\s+\p{Lu}|\s*[(—–-]|\s*$)",
            RegexOptions.CultureInvariant, RegexTimeout);

        // One sentence can carry two members' proposals: "…vastalauseen 1 mukaisena, ja edustaja
        // Hilkka Kemppi on edustaja … kannattamana ehdottanut, että …" — with the comma or
        // without it ("…neljä lausumaehdotusta ja edustaja Johannes Yrttiaho on …"). This is
        // where they part.
        private static readonly Regex ClauseBreak = new(
            @",?\s+ja\s+(?=(?:edustaja\s+)?\p{Lu}[\p{L}\-]*(?:\s+\p{Lu}[\p{L}\-]*)+\s+on\s)",
            RegexOptions.CultureInvariant, RegexTimeout);

        // "edustaja Tiina Elo on edustaja Krista Mikkosen kannattamana ehdottanut, että …"
        private static readonly Regex DescribedClause = new(
            @"^(?:edustaja\s+)?(?<owner>[^,]+?)\s+on\s+.*?\bkannattamana\s+ehdottanut,\s+että\s+(?<text>.+?)\.?$",
            RegexOptions.CultureInvariant, RegexTimeout);

        // "edustaja Tiina Elo on … kannattamana tehnyt vastalauseen 1 mukaiset neljä
        // lausumaehdotusta" — statements taken over from a dissenting opinion, whose words are in
        // the committee report. The member's "ehdotus 2" is then the dissent's statement 2.
        private static readonly Regex DissentClause = new(
            @"^(?:edustaja\s+)?(?<owner>[^,]+?)\s+on\s+.*?\bkannattamana\s+tehnyt\s+vastalauseen\s+(?:(?<v>\d+)\s+)?mukais(?:en\s+lausumaehdotuksen|et\s+(?<k>\S+)\s+lausumaehdotusta)\.?$",
            RegexOptions.CultureInvariant, RegexTimeout);

        // A described proposal to adopt the laws as a dissent has them: "Lakiehdotukset
        // hyväksytään vastalauseen 1 mukaisina." On a section vote its words for that section
        // are in the dissent.
        private static readonly Regex AdoptDissent = new(
            @"^Lakiehdotu\w*\s+hyväksytään\s+vastalauseen\s+(?:(?<v>\d+)\s+)?mukais",
            RegexOptions.CultureInvariant, RegexTimeout);

        // "Mietinnön lausumaehdotus 1" — the committee's own statement, in its report.
        private static readonly Regex CommitteeStatement = new(
            @"^Mietinnön\s+lausumaehdotus\s+(?<n>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

        // The headings a section vote stands under: "1. lakiehdotus", then "24 §".
        private static readonly Regex LawHeading = new(
            @"^(?<n>\d+)\.\s*lakiehdotus$", RegexOptions.CultureInvariant, RegexTimeout);
        private static readonly Regex SectionHeading = new(
            @"^\d+\s*[a-z]?\s*§$", RegexOptions.CultureInvariant, RegexTimeout);

        // Finnish counts up to fifteen, as the minutes write them.
        private static readonly string[] Counts =
        {
            "nolla", "yksi", "kaksi", "kolme", "neljä", "viisi", "kuusi", "seitsemän", "kahdeksan",
            "yhdeksän", "kymmenen", "yksitoista", "kaksitoista", "kolmetoista", "neljätoista", "viisitoista",
        };

        // A side naming one member's proposal: "Lauri Lylyn ehdotus", "Hanna Sarkkisen ehdotus 2",
        // "Kim Bergin lausumaehdotus 3".
        private static readonly Regex OwnedSide = new(
            @"^(?:edustaja\s+|ed\.\s+)?(?<owner>\p{Lu}[\p{L}\-]*(?:\s+\p{Lu}[\p{L}\-]*)+)\s+(?:lausuma)?ehdotus(?:\s+(?<n>\d+))?$",
            RegexOptions.CultureInvariant, RegexTimeout);

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant, RegexTimeout);

        // Reads every vote out of a session's minutes. Malformed XML is an upstream fault this
        // caller cannot fix, so it reads as "nothing here" rather than an exception.
        public static Poytakirja Parse(string edktunnus, string xml)
        {
            var minutes = new Poytakirja { Edktunnus = edktunnus };
            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (XmlException)
            {
                return minutes;
            }

            var votes = new List<PoytakirjaAanestys>();
            var seen = new HashSet<(int, int, int)>();

            // An item is the scope a proposal's number means anything in: "ehdotus 2" in one item
            // is not "ehdotus 2" in the next. Matters are Asiakohta; the annual budget's chapters
            // are MuuAsiakohta, each with its own numbered list — and they hold most of the
            // archive's votes, so missing them would miss two thirds of it.
            //
            // Each vote and each paragraph belongs to its nearest item, so an item that ever held
            // another would not borrow the inner one's numbered list. A document with no items at
            // all is read as one.
            XElement? ItemOf(XElement e) =>
                e.Ancestors().FirstOrDefault(a => a.Name.LocalName.EndsWith("Asiakohta", StringComparison.Ordinal))
                ?? document.Root;

            var votesByItem = document.Descendants()
                .Where(e => e.Name.LocalName == "AanestysKappaleKooste")
                .GroupBy(ItemOf);

            foreach (var votesHere in votesByItem)
            {
                var item = votesHere.Key!;
                var paragraphs = item.Descendants()
                    .Where(e => e.Name.LocalName == "KappaleKooste"
                                && ItemOf(e) == item
                                && !e.Ancestors().Any(a => a.Name.LocalName == "AanestysKappaleKooste"))
                    .Select(Text)
                    .ToList();

                var (described, dissents) = DebateProposals(paragraphs);
                var context = new ItemContext(NumberedProposals(paragraphs), described, dissents);

                // The committee report the item is decided on, which holds the texts the minutes
                // only refer to. One or none: an item naming two is not one to guess within.
                var reports = item.Descendants()
                    .Where(e => e.Name.LocalName == "KohtaAsiakirja"
                                && Text(e.Descendants().FirstOrDefault(d => d.Name.LocalName == "AsiakirjatyyppiNimi")) == "Valiokunnan mietintö")
                    .Select(e => Text(e.Descendants().FirstOrDefault(d => d.Name.LocalName == "EduskuntaTunnus")))
                    .Where(id => id.Length > 0)
                    .Distinct()
                    .ToList();
                var report = reports.Count == 1 ? reports[0] : "";

                foreach (var element in votesHere)
                {
                    var vote = ReadVote(element, context);
                    if (vote != null)
                        vote.Mietinto = report;
                    if (vote != null && seen.Add((vote.Valtiopaivavuosi, vote.Istuntonumero, vote.Aanestysnumero)))
                        votes.Add(vote);
                }
            }

            minutes.Aanestykset = votes;
            return minutes;
        }

        private static PoytakirjaAanestys? ReadVote(XElement element, ItemContext context)
        {
            var reference = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "AanestysViite");
            if (reference == null)
                return null;

            if (!int.TryParse(Attribute(reference, "valtiopaivavuosiNroTeksti"), out var year)
                || !int.TryParse(Attribute(reference, "kokousNroTeksti"), out var session)
                || !int.TryParse(Attribute(reference, "aanestysNroTeksti"), out var number))
                return null;

            var result = element.Elements().FirstOrDefault(e => e.Name.LocalName == "AanestysTulos");

            // The setup is what comes before the result, the outcome what follows it:
            //   Mietintö ”jaa”, Lauri Lylyn ehdotus ”ei”. <AanestysTulos/> Eduskunta hyväksyi mietinnön.
            var setup = Collapse(string.Concat(element.Nodes()
                .TakeWhile(n => n != result)
                .Select(NodeText)));
            var outcome = result == null
                ? ""
                : Collapse(string.Concat(result.NodesAfterSelf().Select(NodeText)));

            // After an accept-or-reject vote the decision often stands as the next paragraph
            // instead: "Eduskunta hyväksyi … lakiehdotukset."
            if (outcome.Length == 0
                && element.ElementsAfterSelf().FirstOrDefault() is { } next
                && next.Name.LocalName == "KappaleKooste")
            {
                var text = Text(next);
                if (text.StartsWith("Eduskunta ", StringComparison.Ordinal))
                    outcome = text;
            }

            var vote = new PoytakirjaAanestys
            {
                Valtiopaivavuosi = year,
                Istuntonumero = session,
                Aanestysnumero = number,
                Asettelu = setup,
                Tulos = outcome,
            };

            Match match;
            try
            {
                match = Setup.Match(setup);
            }
            catch (RegexMatchTimeoutException)
            {
                return vote;
            }

            // "…valinta pääministeriksi ”jaa”; joka ei kannata valintaa, äänestää ”ei”." names
            // no Ei option, it explains how to vote against. Left as the setup alone.
            if (match.Success && !match.Groups["no"].Value.StartsWith("joka ", StringComparison.OrdinalIgnoreCase))
            {
                var section = SectionOf(element);
                vote.Jaa = Option(match.Groups["yes"].Value, context, section);
                vote.Ei = Option(match.Groups["no"].Value, context, section);
            }

            return vote;
        }

        // The law and section a vote decides, when it is a section vote: the paragraph straight
        // before it is the section ("24 §"), and the law is the nearest "1. lakiehdotus" above
        // that within the same stretch of proceedings. Anything less certain is not a section
        // vote as far as this is concerned.
        private static (int? Law, string? Section)? SectionOf(XElement vote)
        {
            var before = vote.ElementsBeforeSelf().Reverse().ToList();
            if (before.Count == 0 || before[0].Name.LocalName != "KappaleKooste")
                return null;

            var heading = Text(before[0]);
            if (!SectionHeading.IsMatch(heading))
                return null;

            int? law = null;
            foreach (var e in before.Skip(1))
            {
                // The Speaker opening a new question ends the stretch the heading applies to.
                if (e.Name.LocalName == "PuheenjohtajaRepliikki")
                    break;
                if (e.Name.LocalName == "KappaleKooste" && LawHeading.Match(Text(e)) is { Success: true } m)
                {
                    law = int.Parse(m.Groups["n"].Value);
                    break;
                }
            }

            return (law, Mietinto.Normalise(heading));
        }

        // One side of a vote, with the proposal's text when the same item states it and names
        // the same member, or a pointer into the committee report when the minutes only refer
        // to it there. A number alone is not enough to attach a text: the name has to agree
        // too, because a wrong text beside a vote is worse than none.
        private static PoytakirjaVaihtoehto Option(string side, ItemContext context, (int? Law, string? Section)? section)
        {
            var label = Collapse(side);
            var option = new PoytakirjaVaihtoehto { Nimi = label };

            Match match;
            try
            {
                if (CommitteeStatement.Match(label) is { Success: true } statement)
                {
                    option.Viite = new MietintoViite { Lausuma = int.Parse(statement.Groups["n"].Value) };
                    return option;
                }

                if (section is { } s
                    && (label.Equals("Mietintö", StringComparison.OrdinalIgnoreCase)
                        || label.Equals("Valiokunnan ehdotus", StringComparison.OrdinalIgnoreCase)))
                {
                    option.Viite = new MietintoViite { Laki = s.Law, Pykala = s.Section };
                    return option;
                }

                match = OwnedSide.Match(label);
            }
            catch (RegexMatchTimeoutException)
            {
                return option;
            }
            if (!match.Success)
                return option;

            var owner = match.Groups["owner"].Value;
            var fromDissent = context.Dissents.Where(d => SameMember(d.Owner, owner)).ToList();

            if (match.Groups["n"].Success)
            {
                if (!int.TryParse(match.Groups["n"].Value, out var n))
                    return option;

                if (context.Numbered.TryGetValue(n, out var proposal) && SameMember(proposal.Owner, owner))
                {
                    option.Kohta = proposal.Context;
                    option.Teksti = proposal.Text;
                }
                // "Tiina Elon ehdotus 2", where she took over a dissent's statements: its
                // statement 2. Only when that is the one thing she took over, so the numbering
                // cannot be hers across two sources.
                else if (fromDissent.Count == 1 && n <= fromDissent[0].Count)
                {
                    option.Viite = new MietintoViite
                    {
                        Vastalauseesta = true,
                        Vastalause = fromDissent[0].Dissent,
                        Lausuma = n,
                        Maara = fromDissent[0].Count,
                    };
                }
                return option;
            }

            var candidates = context.Described.Where(d => SameMember(d.Owner, owner)).ToList();
            if (candidates.Count == 1)
            {
                option.Teksti = candidates[0].Text;

                // "Lakiehdotukset hyväksytään vastalauseen 1 mukaisina", on a vote about one
                // section: the dissent's wording of that section is what Ei would have adopted.
                // The sentence stays as the text until the report confirms the section.
                if (section is { } s && AdoptDissent.Match(candidates[0].Text) is { Success: true } adopt)
                {
                    option.Viite = new MietintoViite
                    {
                        Vastalauseesta = true,
                        Vastalause = adopt.Groups["v"].Success ? int.Parse(adopt.Groups["v"].Value) : null,
                        Laki = s.Law,
                        Pykala = s.Section,
                    };
                }
                return option;
            }

            // "Saara Hyrkön ehdotus" with no number, in an item whose list holds exactly one
            // proposal of hers. More than one and the vote does not say which, so none is used.
            var listed = context.Numbered.Values.Where(p => p.Owner.Length > 0 && SameMember(p.Owner, owner)).ToList();
            if (candidates.Count == 0 && listed.Count == 1)
            {
                option.Kohta = listed[0].Context;
                option.Teksti = listed[0].Text;
            }
            // "Hannu Hoskosen ehdotus", who took over a dissent's one statement.
            else if (candidates.Count == 0 && listed.Count == 0 && fromDissent.Count == 1 && fromDissent[0].Count == 1)
            {
                option.Viite = new MietintoViite
                {
                    Vastalauseesta = true,
                    Vastalause = fromDissent[0].Dissent,
                    Lausuma = 1,
                    Maara = 1,
                };
            }

            return option;
        }

        private static IReadOnlyDictionary<int, NumberedProposal> NumberedProposals(IEnumerable<string> paragraphs)
        {
            var proposals = new Dictionary<int, NumberedProposal>();
            foreach (var paragraph in paragraphs)
            {
                if (!paragraph.Contains("kannattamana"))
                    continue;

                Match match;
                try
                {
                    match = NumberedLine.Match(paragraph);
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }
                if (!match.Success || !int.TryParse(match.Groups["n"].Value, out var n))
                    continue;

                // A number stated twice in one item is a list this parser does not understand.
                // Neither text is attached rather than guessing which one the vote meant.
                if (proposals.ContainsKey(n))
                    proposals[n] = new NumberedProposal("", "", "");
                else
                    proposals[n] = new NumberedProposal(
                        match.Groups["owner"].Value.Trim(),
                        match.Groups["context"].Value.Trim(),
                        match.Groups["text"].Value.Trim());
            }
            return proposals;
        }

        // The proposals the debate produced: those described in words, and statements taken
        // over from a dissenting opinion.
        private static (IReadOnlyList<(string Owner, string Text)>, IReadOnlyList<DissentProposal>) DebateProposals(
            IEnumerable<string> paragraphs)
        {
            var described = new List<(string, string)>();
            var dissents = new List<DissentProposal>();
            foreach (var paragraph in paragraphs)
            {
                try
                {
                    foreach (Match sentence in ProposalSentence.Matches(paragraph))
                    {
                        foreach (var clause in ClauseBreak.Split(sentence.Groups["body"].Value))
                        {
                            // One member's clause has one seconder. Two means the split missed
                            // a join this does not know, and reading on would hand one member's
                            // proposal to the other — so the clause is not read at all.
                            if (Regex.Matches(clause, @"\bkannattamana\b").Count != 1)
                                continue;

                            if (DescribedClause.Match(clause.Trim()) is { Success: true } d)
                            {
                                // "että lakiehdotus hylätään." — the clause itself, without the
                                // "että" that only made it subordinate in the minutes' sentence.
                                var text = d.Groups["text"].Value.Trim().TrimEnd('.') + ".";
                                described.Add((d.Groups["owner"].Value.Trim(), Capitalise(text)));
                            }
                            else if (DissentClause.Match(clause.Trim()) is { Success: true } v
                                     && Count(v.Groups["k"]) is int count)
                            {
                                dissents.Add(new DissentProposal(
                                    v.Groups["owner"].Value.Trim(),
                                    v.Groups["v"].Success ? int.Parse(v.Groups["v"].Value) : null,
                                    count));
                            }
                        }
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }
            }
            return (described, dissents);
        }

        // "neljä" → 4; an absent count is the singular form, one statement. Anything else is a
        // count this does not read, and the clause is left alone.
        private static int? Count(Group count)
        {
            if (!count.Success)
                return 1;
            if (int.TryParse(count.Value, out var n))
                return n;
            var index = Array.IndexOf(Counts, count.Value.ToLowerInvariant());
            return index > 0 ? index : null;
        }

        // Whether "Hanna Sarkkinen" (as listed) is the member in "Hanna Sarkkisen" (as the vote
        // names them). Finnish inflects the surname's stem — Sarkkinen, Sarkkisen; Hyrkkö,
        // Hyrkön — so the first names must agree exactly and the surname must share all but its
        // last few letters with the inflected form.
        internal static bool SameMember(string nominative, string genitive)
        {
            var a = nominative.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var b = genitive.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length < 2 || a.Length != b.Length)
                return false;

            for (var i = 0; i < a.Length - 1; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            var surname = a[^1];
            var inflected = b[^1];
            var stem = Math.Max(3, surname.Length - 3);
            return inflected.Length >= stem
                && string.Compare(surname, 0, inflected, 0, stem, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static string? Attribute(XElement element, string localName) =>
            element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

        private static string NodeText(XNode node) => node switch
        {
            XText text => text.Value,
            XElement element => element.Value,
            _ => "",
        };

        private static string Text(XElement element) => Collapse(element.Value);

        private static string Collapse(string text) => Whitespace.Replace(text, " ").Trim();

        private static string Capitalise(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        private sealed record NumberedProposal(string Owner, string Context, string Text);

        // A member's statements taken over from dissent Dissent (null: "vastalauseen mukaiset",
        // the report's only one), Count of them.
        private sealed record DissentProposal(string Owner, int? Dissent, int Count);

        // What one item of the minutes says about the proposals voted on in it.
        private sealed record ItemContext(
            IReadOnlyDictionary<int, NumberedProposal> Numbered,
            IReadOnlyList<(string Owner, string Text)> Described,
            IReadOnlyList<DissentProposal> Dissents);
    }

    // One vote as the minutes record it.
    public sealed class PoytakirjaAanestys
    {
        public int Valtiopaivavuosi { get; set; }
        public int Istuntonumero { get; set; }
        public int Aanestysnumero { get; set; }

        // "Mietintö ”jaa”, Hanna Sarkkisen ehdotus 2 ”ei”." — verbatim.
        public string Asettelu { get; set; } = "";

        // "Eduskunta hyväksyi mietinnön." or "Tässä äänestyksessä voitti …". Empty when the
        // minutes state no decision beside the vote, which is usual after an accept-or-reject
        // vote: the decision is then the vote itself.
        public string Tulos { get; set; } = "";

        // Null when the setup does not mark both sides in so many words.
        public PoytakirjaVaihtoehto? Jaa { get; set; }
        public PoytakirjaVaihtoehto? Ei { get; set; }

        // The committee report the vote's item is decided on, "HaVM 28/2025 vp"; empty when the
        // item names none. Where the texts behind a side's Viite are.
        public string Mietinto { get; set; } = "";

        // "2026-108-5", the identifier the division payloads carry.
        public string AanestysTunnus => $"{Valtiopaivavuosi}-{Istuntonumero}-{Aanestysnumero}";
    }

    // One side of a vote.
    public sealed class PoytakirjaVaihtoehto
    {
        // As the setup names it: "Mietintö", "Hanna Sarkkisen ehdotus 2".
        public string Nimi { get; set; } = "";

        // Where in the matter the proposal applies, when the minutes say: a budget item
        // ("30.20.20") or "yleisperustelut".
        public string Kohta { get; set; } = "";

        // The proposal's own text, when the same item of the minutes states it. Empty otherwise
        // — a committee's side, or a proposal made in an attachment or a dissenting opinion.
        public string Teksti { get; set; } = "";

        // Where in the committee report the side's text is, when the minutes only refer to it:
        // "Mietinnön lausumaehdotus 1", a dissent's statements, or a section. Mietinto.Resolve
        // turns it into Teksti.
        public MietintoViite? Viite { get; set; }
    }
}
