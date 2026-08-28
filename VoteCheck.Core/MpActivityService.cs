using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoteCheck.Core.Models;

namespace VoteCheck.Core
{
    // Derives per-MP voting history and activity summaries.
    //
    // This is only cheap because every vote payload embeds the full ballot list (one entry per
    // seat), so an MP's record is a filter over data we already fetched rather than a separate
    // per-MP query — see design.md §3.1. The scope of any answer here is therefore exactly the
    // window of votes handed in: "recent" means the ~10 divisions uusimmat-aanestykset returns,
    // not the MP's whole term. Deep history would mean walking past sessions and is out of
    // scope for v1.
    public sealed class MpActivityService
    {
        private readonly IEduskuntaClient _client;

        public MpActivityService(IEduskuntaClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        // An MP's ballots across the recent-votes window, in whatever order upstream returned
        // them — uusimmat-aanestykset is NOT chronologically sorted (the captured sample runs
        // session 60, 65, 69, 71, …, 58, 69, 71), so callers that want date order must sort.
        public async Task<IReadOnlyList<MpVote>> GetRecentVotesForMpAsync(
            int henkilonumero, CancellationToken cancellationToken = default)
        {
            var votes = await _client.GetRecentVotesAsync(cancellationToken).ConfigureAwait(false);
            return ExtractVotesFor(henkilonumero, votes);
        }

        // An MP's ballots within one plenary session.
        public async Task<IReadOnlyList<MpVote>> GetSessionVotesForMpAsync(
            int henkilonumero, string istuntotunnus, CancellationToken cancellationToken = default)
        {
            var votes = await _client
                .GetVotesInSessionAsync(istuntotunnus, cancellationToken)
                .ConfigureAwait(false);
            return ExtractVotesFor(henkilonumero, votes);
        }

        // Activity summary over the recent-votes window.
        public async Task<MpActivitySummary> GetRecentActivityAsync(
            int henkilonumero, CancellationToken cancellationToken = default)
        {
            var votes = await _client.GetRecentVotesAsync(cancellationToken).ConfigureAwait(false);
            return Summarize(henkilonumero, votes);
        }

        // Pulls one MP's ballot out of each division they appear in.
        public static IReadOnlyList<MpVote> ExtractVotesFor(
            int henkilonumero, IEnumerable<Aanestys> votes)
        {
            if (votes is null) return Array.Empty<MpVote>();

            var result = new List<MpVote>();

            foreach (var vote in votes)
            {
                var ballot = FindBallot(vote, henkilonumero);
                if (ballot is null)
                    continue;

                result.Add(new MpVote
                {
                    VoteId = vote.Id,
                    SessionId = vote.IstunnonTunniste,
                    Date = vote.Istuntopvm,
                    Subject = vote.Kohta?.Otsikko,
                    BallotTitle = vote.Aanestysotsikko,
                    DocumentId = vote.Kohta?.Asiakirjat?.PaaasiakirjaEduskuntatunnus,
                    Choice = ParseChoice(ballot.Kayttaytyminen),
                    PartyAbbreviation = ballot.Edkryhmalyhenne,
                    Annulled = vote.Aanestysmitatoity,
                });
            }

            return result;
        }

        // Counts an MP's ballots across a window of divisions.
        //
        // Annulled divisions (aanestysmitatoity) are excluded: they carry no decision, so
        // counting them would distort both the totals and the attendance rate.
        public static MpActivitySummary Summarize(int henkilonumero, IEnumerable<Aanestys> votes)
        {
            var counted = (votes ?? Enumerable.Empty<Aanestys>())
                .Where(v => !v.Aanestysmitatoity)
                .Select(v => new { Vote = v, Ballot = FindBallot(v, henkilonumero) })
                .Where(x => x.Ballot is not null)
                .ToList();

            int jaa = 0, ei = 0, tyhja = 0, poissa = 0, unknown = 0;

            foreach (var entry in counted)
            {
                switch (ParseChoice(entry.Ballot!.Kayttaytyminen))
                {
                    case VoteChoice.Jaa: jaa++; break;
                    case VoteChoice.Ei: ei++; break;
                    case VoteChoice.Tyhja: tyhja++; break;
                    case VoteChoice.Poissa: poissa++; break;
                    default: unknown++; break;
                }
            }

            // Identity is taken from any ballot in the window — upstream doesn't order these
            // chronologically, so this is not necessarily the most recent one. That only
            // matters if an MP changed parliamentary group mid-window, in which case the
            // reported party is whichever the window happened to end on; callers needing the
            // MP's current group should read it from Mp.ViimeisinEduskuntaryhma instead.
            var identity = counted.LastOrDefault()?.Ballot;

            return new MpActivitySummary
            {
                Henkilonumero = henkilonumero,
                Name = identity is null ? null : $"{identity.Etunimi} {identity.Sukunimi}".Trim(),
                Party = identity?.Edkryhmalyhenne,
                TotalVotes = counted.Count,
                Jaa = jaa,
                Ei = ei,
                Tyhja = tyhja,
                Poissa = poissa,
                Unknown = unknown,
            };
        }

        // Maps the Finnish vocabulary term to a VoteChoice. Accepts "Tyhja" alongside "Tyhjä"
        // so a de-accented value doesn't silently fall through to Unknown.
        public static VoteChoice ParseChoice(LocalizedText? kayttaytyminen)
        {
            string? value = kayttaytyminen?.Fi?.Trim();
            if (string.IsNullOrEmpty(value))
                return VoteChoice.Unknown;

            if (value.Equals("Jaa", StringComparison.OrdinalIgnoreCase)) return VoteChoice.Jaa;
            if (value.Equals("Ei", StringComparison.OrdinalIgnoreCase)) return VoteChoice.Ei;
            if (value.Equals("Poissa", StringComparison.OrdinalIgnoreCase)) return VoteChoice.Poissa;
            if (value.Equals("Tyhjä", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Tyhja", StringComparison.OrdinalIgnoreCase)) return VoteChoice.Tyhja;

            return VoteChoice.Unknown;
        }

        private static EdustajanAanestys? FindBallot(Aanestys vote, int henkilonumero) =>
            vote?.Aanestystapahtumat?.FirstOrDefault(b => b.Henkilonumero == henkilonumero);
    }
}
