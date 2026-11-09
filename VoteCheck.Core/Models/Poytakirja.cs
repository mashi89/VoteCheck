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
        // että lakiehdotukset hyväksytään vastalauseen 1 mukaisena." Several can share one
        // paragraph, so each runs to its own full stop — and only to one that ends a sentence.
        // A full stop inside an abbreviation would otherwise cut the proposal short, and a
        // truncated proposal is a different proposal; such a sentence is skipped instead.
        private static readonly Regex DescribedSentence = new(
            @"\b(?:Yleis)?[Kk]eskustelussa\s+(?:edustaja\s+)?(?<owner>[^,.]+?)\s+on\s+[^.]*?\bkannattamana\s+ehdottanut,\s+että\s+(?<text>[^.]+\.)(?=\s+\p{Lu}|\s*\(|\s*$)",
            RegexOptions.CultureInvariant, RegexTimeout);

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

                var numbered = NumberedProposals(paragraphs);
                var described = DescribedProposals(paragraphs);

                foreach (var element in votesHere)
                {
                    var vote = ReadVote(element, numbered, described);
                    if (vote != null && seen.Add((vote.Valtiopaivavuosi, vote.Istuntonumero, vote.Aanestysnumero)))
                        votes.Add(vote);
                }
            }

            minutes.Aanestykset = votes;
            return minutes;
        }

        private static PoytakirjaAanestys? ReadVote(
            XElement element,
            IReadOnlyDictionary<int, NumberedProposal> numbered,
            IReadOnlyList<(string Owner, string Text)> described)
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
                vote.Jaa = Option(match.Groups["yes"].Value, numbered, described);
                vote.Ei = Option(match.Groups["no"].Value, numbered, described);
            }

            return vote;
        }

        // One side of a vote, with the proposal's text when the same item states it and names
        // the same member. A number alone is not enough to attach a text: the name has to agree
        // too, because a wrong text beside a vote is worse than none.
        private static PoytakirjaVaihtoehto Option(
            string side,
            IReadOnlyDictionary<int, NumberedProposal> numbered,
            IReadOnlyList<(string Owner, string Text)> described)
        {
            var label = Collapse(side);
            var option = new PoytakirjaVaihtoehto { Nimi = label };

            Match match;
            try
            {
                match = OwnedSide.Match(label);
            }
            catch (RegexMatchTimeoutException)
            {
                return option;
            }
            if (!match.Success)
                return option;

            var owner = match.Groups["owner"].Value;

            if (match.Groups["n"].Success)
            {
                if (int.TryParse(match.Groups["n"].Value, out var n)
                    && numbered.TryGetValue(n, out var proposal)
                    && SameMember(proposal.Owner, owner))
                {
                    option.Kohta = proposal.Context;
                    option.Teksti = proposal.Text;
                }
                return option;
            }

            var candidates = described.Where(d => SameMember(d.Owner, owner)).ToList();
            if (candidates.Count == 1)
            {
                option.Teksti = candidates[0].Text;
                return option;
            }

            // "Saara Hyrkön ehdotus" with no number, in an item whose list holds exactly one
            // proposal of hers. More than one and the vote does not say which, so none is used.
            var listed = numbered.Values.Where(p => p.Owner.Length > 0 && SameMember(p.Owner, owner)).ToList();
            if (candidates.Count == 0 && listed.Count == 1)
            {
                option.Kohta = listed[0].Context;
                option.Teksti = listed[0].Text;
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

        private static IReadOnlyList<(string Owner, string Text)> DescribedProposals(IEnumerable<string> paragraphs)
        {
            var proposals = new List<(string, string)>();
            foreach (var paragraph in paragraphs)
            {
                try
                {
                    foreach (Match match in DescribedSentence.Matches(paragraph))
                    {
                        // "että lakiehdotus hylätään." — the clause itself, without the "että"
                        // that only made it a subordinate clause in the minutes' sentence.
                        var text = match.Groups["text"].Value.Trim();
                        proposals.Add((match.Groups["owner"].Value.Trim(), Capitalise(text)));
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }
            }
            return proposals;
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
    }
}
