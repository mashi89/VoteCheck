using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Data;
using System.IO;

namespace MaSHi {

    #region NotInUse
    //public class SaliDBAanestysEdustaja {
    //    public int EdustajaId {
    //        get; set;
    //    }
    //    public int AanestysId {
    //        get; set;
    //    }
    //    public string EdustajaEtunimi {
    //        get; set;
    //    }
    //    public string EdustajaSukunimi {
    //        get; set;
    //    }
    //    public int EdustajaHenkiloNumero {
    //        get; set;
    //    }
    //    public string EdustajaRyhmaLyhenne {
    //        get; set;
    //    }
    //    public string EdustajaAanestys {
    //        get; set;
    //    }
    //    public int Imported {
    //        get; set;
    //    }
    //}
    #endregion NotInUse
    
    // Retrieve open data
    public static class OpenDataRetriever {

        // Maps the full party group name (as stored in Ryhmalyhenne in the SaliDBAanestysJakauma API
        // response) to the abbreviated party code used in EdustajaRyhmaLyhenne.
        // Based on Parties.txt in the repository root.
        public static readonly Dictionary<string, string> PartyNameToAbbreviation =
            new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
            {
                { "Keskustan eduskuntaryhmä",                   "kesk" },
                { "Kansallisen kokoomuksen eduskuntaryhmä",     "kok"  },
                { "Perussuomalaisten eduskuntaryhmä",           "ps"   },
                { "Sosialidemokraattinen eduskuntaryhmä",       "sd"   },
                { "Vihreä eduskuntaryhmä",                      "vihr" },
                { "Vasemmistoliiton eduskuntaryhmä",            "vas"  },
                { "Ruotsalainen eduskuntaryhmä",                "r"    },
                { "Kristillisdemokraattinen eduskuntaryhmä",    "kd"   },
            };

        #region Variables
        // Initialize variables
        public static bool hasMore = false;

        // Override with VOTECHECK_API_BASE_URL env var to point at a local mock server.
        private static readonly string _apiBase =
            System.Environment.GetEnvironmentVariable("VOTECHECK_API_BASE_URL")
            ?? "https://avoindata.eduskunta.fi";

        private static string baseUrl = "";
        private static string json = "";
        private static ManualResetEvent _quitEvent = new ManualResetEvent( false );
        private static DataTable finalTable;
        private static int counter = 0;
        private static int saveCounter = 0;
        private static JObject o;
        // Not readonly so that unit tests can substitute a mock HttpClient via reflection.
        private static HttpClient _httpClient = CreateHttpClient();
        private const int MaxPerPage = 100;
        #endregion Variables

        private static HttpClient CreateHttpClient() {
            var handler = new LoggingHandler { InnerHandler = new HttpClientHandler() };
            var client  = new HttpClient( handler );
            client.DefaultRequestHeaders.Add( "User-Agent", "VoteCheck/1.0" );
            return client;
        }

        private sealed class LoggingHandler : DelegatingHandler {
            protected override async Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request, CancellationToken cancellationToken ) {
                Logger.Info( $"GET {request.RequestUri}" );
                var sw = Stopwatch.StartNew();
                try {
                    var response = await base.SendAsync( request, cancellationToken ).ConfigureAwait( false );
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait( false );
                    response.Content = new StringContent( body, Encoding.UTF8,
                        response.Content.Headers.ContentType?.MediaType ?? "application/json" );
                    if ( response.IsSuccessStatusCode )
                        Logger.Info( $"HTTP {(int)response.StatusCode} OK ({sw.ElapsedMilliseconds}ms)" );
                    else
                        Logger.Warn( $"HTTP {(int)response.StatusCode} {response.StatusCode} ({sw.ElapsedMilliseconds}ms) — body: {body}" );
                    return response;
                } catch ( Exception ex ) {
                    Logger.Error( $"HTTP request failed after {sw.ElapsedMilliseconds}ms: {request.RequestUri}", ex );
                    throw;
                }
            }
        }

        public static void Main() {

        }

        #region Public methods
        // GetPartyDistData method should seek the party vote distribution for a voting id
        public static DataTable GetVotingData(string year, bool skipEven, int count, string type)
        {
            Logger.Info( $"GetVotingData year={year} type={type} count={count} skipEven={skipEven}" );
            var sw = Stopwatch.StartNew();
            DataTable votingTable = null;
            string dbName = "SaliDBAanestys";

            try
            {
                votingTable = FetchAllPages( dbName, type, year, MaxPerPage, skipEven, false );
            }
            catch (Exception ex) when (ex.Message == "No rows found.")
            {
                // Legitimate empty result — votingTable remains null
            }
            catch (Exception ex)
            {
                Logger.Error( "GetVotingData failed", ex );
                throw;
            }

            if ( votingTable != null )
            {                
                votingTable.Columns.Remove("KieliId");
                votingTable.Columns.Remove("KohtaTunniste");
                votingTable.Columns.Remove("KohtaJarjestys");
                votingTable.Columns.Remove("IstuntoVPVuosi");
                votingTable.Columns.Remove("IstuntoIlmoitettuAlkuaika");
                votingTable.Columns.Remove("IstuntoAlkuaika");
                votingTable.Columns.Remove("AanestysLoppuaika");
                votingTable.Columns.Remove("IstuntoNumero");
                votingTable.Columns.Remove("AanestysNumero");
                votingTable.Columns.Remove("PaaKohtaTunniste");
                votingTable.Columns.Remove("PaaKohtaOtsikko");
                votingTable.Columns.Remove("PaaKohtaHuomautus");
                votingTable.Columns.Remove("KohtaKasittelyOtsikko");
                votingTable.Columns.Remove("KohtaKasittelyVaihe");
                votingTable.Columns.Remove("KohtaHuomautus");
                votingTable.Columns.Remove("Url");
                votingTable.Columns.Remove("AanestysPoytakirja");
                votingTable.Columns.Remove("AanestysPoytakirjaUrl");
                votingTable.Columns.Remove("AanestysValtiopaivaasiaUrl");
                votingTable.Columns.Remove("AanestysTulosYhteensa");
                votingTable.Columns.Remove("AlikohtaTunniste");
                votingTable.Columns.Remove("Imported");
                votingTable.Columns.Remove("AanestysLisaOtsikko");
                votingTable.Columns[votingTable.Columns.IndexOf("PJOtsikko")].SetOrdinal(votingTable.Columns.Count - 1);
                votingTable.Columns[votingTable.Columns.IndexOf("AanestysId")].SetOrdinal(votingTable.Columns.Count - 1);
                votingTable.Columns[votingTable.Columns.IndexOf("AanestysMitatoity")].SetOrdinal(votingTable.Columns.Count - 1);
                votingTable.Columns[votingTable.Columns.IndexOf("KohtaOtsikko")].SetOrdinal(2);
            }

            Logger.Info( $"GetVotingData complete in {sw.ElapsedMilliseconds}ms, rows={votingTable?.Rows.Count}" );
            return votingTable;
        }

        // GetVotingDataByDate retrieves voting records matching a date prefix.
        // The date parameter may be a year ("yyyy"), year+month ("yyyy-MM"), or
        // full date ("yyyy-MM-dd").
        // IstuntoPvm is a date/datetime column whose SQL type is reported as 'OTHER' by the API,
        // so it cannot be used as a filter column directly.  Instead we query by the integer
        // IstuntoVPVuosi (parliamentary year) which the API supports, then filter client-side
        // to rows whose IstuntoPvm value starts with the requested date prefix.
        public static DataTable GetVotingDataByDate(string date, bool skipEven, int count) =>
            Timed( $"GetVotingDataByDate date={date} skipEven={skipEven} count={count}", () => {
                string year = date.Length >= 4 ? date.Substring( 0, 4 ) : date;
                DataTable yearTable = GetVotingData( year, skipEven, count, "IstuntoVPVuosi" );
                if ( yearTable == null ) return null;
                if ( !yearTable.Columns.Contains( "IstuntoPvm" ) ) return yearTable;
                var matching = yearTable.AsEnumerable()
                    .Where( r => r["IstuntoPvm"]?.ToString()?.StartsWith( date ) == true )
                    .ToList();
                return matching.Count > 0 ? matching.CopyToDataTable() : yearTable.Clone();
            } );

        // GetSubjectData method should seek for certain phrases
        public static DataTable GetSubjectData(string inputName, string type)
        {
            DataRow votingDataRow = GetVotingDataOfOne(inputName);
            finalTable = null;

            

            return finalTable;
        }

        // GetPartyDistData method should seek the party vote distribution for a voting id
        public static DataTable GetPartyDistData(string votingId, bool skipEven, string type) =>
            Timed( $"GetPartyDistData votingId={votingId} skipEven={skipEven}", () => {
                try {
                    finalTable = GetVotingDistData( votingId, skipEven );
                } catch ( Exception ex ) when ( ex.Message == "No rows found." ) {
                    Logger.Info( $"GetPartyDistData: no rows for votingId={votingId}" );
                    return null;
                }
                finalTable.Columns.Remove( "Imported" );
                finalTable.Columns.Remove( "Tyyppi" );
                return finalTable;
            } );

        // GetCurrentMPs fetches all current MPs from the SeatingOfParliament table,
        // paginating through all pages until hasMore = false.
        public static DataTable GetCurrentMPs() =>
            Timed( "GetCurrentMPs", () => {
                const int perPage = MaxPerPage;
                string dbName = "SeatingOfParliament";
                DataTable result = null;
                int page = 0;
                bool more = true;

                while ( more ) {
                    string url = _apiBase + "/api/v1/tables/" + dbName +
                                 "/rows?perPage=" + perPage + "&page=" + page;

                    string pageJson;
                    try {
                        pageJson = GetDataAsync( url ).GetAwaiter().GetResult();
                    } catch ( Exception ex ) {
                        Logger.Error( $"GetCurrentMPs failed fetching page {page}", ex );
                        throw;
                    }

                    if ( string.IsNullOrEmpty( pageJson ) )
                        throw new Exception( "JSON data is empty." );

                    var pageObj = JObject.Parse( pageJson );

                    var errorToken = pageObj.SelectToken( "message" );
                    if ( errorToken != null )
                        throw new Exception( "API error: " + errorToken.ToString() );

                    var check = pageObj.SelectToken( "hasMore" );
                    if ( check == null )
                        throw new Exception( "Unexpected response format: 'hasMore' field missing." );

                    more = check.Value<bool>();

                    if ( pageObj.SelectToken( "rowCount" )?.ToString() == "0" ) {
                        if ( page == 0 ) throw new Exception( "No rows found." );
                        break;
                    }

                    if ( result == null ) result = InitTable( pageObj );
                    result = AppendTable( pageObj, result, skipEven: false, voting: true );
                    Logger.Info( $"GetCurrentMPs page {page} fetched, rows so far: {result?.Rows.Count}" );
                    page++;
                }

                hasMore = false;
                return result;
            } );

        // GetEdustajaData fetches individual MP votes from SaliDBAanestysEdustaja for a given AanestysId.
        // When partyFilter is provided, only rows whose EdustajaRyhmaLyhenne matches (case-insensitive,
        // whitespace-trimmed) are returned.  Matches the abbreviations used in Parties.txt.
        public static DataTable GetEdustajaData(string votingId, bool skipEven, string partyFilter = null) =>
            Timed( $"GetEdustajaData votingId={votingId} partyFilter={partyFilter ?? "(none)"} skipEven={skipEven}", () => {
                string dbName = "SaliDBAanestysEdustaja";
                baseUrl = _apiBase + "/api/v1/tables/" + dbName +
                          "/rows?perPage=" + MaxPerPage + "&page=0&columnName=" +
                          Uri.EscapeDataString( "AanestysId" ) + "&columnValue=" + Uri.EscapeDataString( votingId );

                DataTable edustajaTable = ReadData( baseUrl, skipEven, true );
                edustajaTable.Columns.Remove( "Imported" );

                if ( !string.IsNullOrWhiteSpace( partyFilter ) && edustajaTable.Columns.Contains( "EdustajaRyhmaLyhenne" ) ) {
                    string trimmed = partyFilter.Trim();
                    var matching = edustajaTable.AsEnumerable()
                        .Where( r => string.Equals(
                            r["EdustajaRyhmaLyhenne"]?.ToString()?.Trim(),
                            trimmed, StringComparison.OrdinalIgnoreCase ) )
                        .ToList();
                    edustajaTable = matching.Count > 0 ? matching.CopyToDataTable() : edustajaTable.Clone();
                }

                return edustajaTable;
            } );

        // GetCombined data should seek for MP votes with vote subjects visible
        // This method makes two different queries and combines them in one table
        public static DataTable GetCombinedData(string inputName, bool skipEven, int count, string type) =>
            Timed( $"GetCombinedData inputName={inputName} type={type} count={count} skipEven={skipEven}", () => {
                // Use a local reference — the static finalTable field gets overwritten by
                // GetVotingDataOfOne calls inside the loop, which would invalidate row lookups.
                DataTable nameTable = GetNameData( inputName, skipEven, count.ToString(), type );

                // Add enrichment columns and capture DataColumn refs before the loop so
                // that row[DataColumn] lookups are immune to any static-field side effects.
                DataColumn colAanestysOtsikko       = nameTable.Columns.Add( "AanestysOtsikko" );
                DataColumn colKohtaOtsikko          = nameTable.Columns.Add( "KohtaOtsikko" );
                DataColumn colPaaKohtaOtsikko       = nameTable.Columns.Add( "PaaKohtaOtsikko" );
                DataColumn colKohtaKasittelyOtsikko = nameTable.Columns.Add( "KohtaKasittelyOtsikko" );
                DataColumn colAanestysAlkuaika      = nameTable.Columns.Add( "AanestysAlkuaika" );

                foreach ( DataRow row in nameTable.Rows ) {
                    string aanestysId  = row["AanestysId"]?.ToString();
                    DataRow votingRow  = GetVotingDataOfOne( aanestysId );
                    if ( votingRow == null ) {
                        Logger.Warn( $"GetCombinedData: no voting row for AanestysId={aanestysId}, skipping enrichment" );
                        continue;
                    }
                    row[colAanestysOtsikko]       = ColOrEmpty( votingRow, "AanestysOtsikko" );
                    row[colKohtaOtsikko]          = ColOrEmpty( votingRow, "KohtaOtsikko" );
                    row[colPaaKohtaOtsikko]       = ColOrEmpty( votingRow, "PaaKohtaOtsikko" );
                    row[colKohtaKasittelyOtsikko] = ColOrEmpty( votingRow, "KohtaKasittelyOtsikko" );
                    row[colAanestysAlkuaika]      = ColOrEmpty( votingRow, "AanestysAlkuaika" );
                }

                finalTable = nameTable;
                finalTable.Columns.Remove( "EdustajaId" );
                finalTable.Columns.Remove( "Imported" );
                finalTable.Columns[finalTable.Columns.IndexOf( "AanestysId" )].SetOrdinal( 10 );
                finalTable.Columns[finalTable.Columns.IndexOf( "EdustajaHenkiloNumero" )].SetOrdinal( 10 );
                return finalTable;
            } );
        // GetCombinedDataWithDateFilter fetches all pages of surname results and all pages of
        // voting sessions for the given year, then joins them client-side by AanestysId and
        // applies the date prefix filter.  Avoids N+1 enrichment calls.
        //
        // dateFilter: prefix string — "yyyy", "yyyy-MM", or "yyyy-MM-dd".
        public static DataTable GetCombinedDataWithDateFilter(
                string inputName, string dateFilter, bool skipEven, int perPage) =>
            Timed( $"GetCombinedDataWithDateFilter inputName={inputName} dateFilter={dateFilter}", () => {
                string year = dateFilter.Length >= 4 ? dateFilter.Substring( 0, 4 ) : dateFilter;

                // ── Step 1: all surname rows from SaliDBAanestysEdustaja ──────────
                DataTable mpTable = FetchAllPages( "SaliDBAanestysEdustaja", "EdustajaSukunimi",
                                                   inputName, perPage, skipEven: false, voting: true );

                // ── Step 2: all session rows for the year from SaliDBAanestys ────
                // Raw rows (not cleaned up by GetVotingData) so all enrichment columns are present.
                // skipEven keeps only Finnish rows (KieliId=1), which carry the Finnish AanestysIds
                // that SaliDBAanestysEdustaja also uses.
                DataTable sessionTable = FetchAllPages( "SaliDBAanestys", "IstuntoVPVuosi",
                                                        year, MaxPerPage, skipEven, voting: false );

                // ── Step 3: build AanestysId → session row lookup, date-prefix filtered ──
                var sessionMap = new Dictionary<string, DataRow>( StringComparer.OrdinalIgnoreCase );
                foreach ( DataRow row in sessionTable.Rows ) {
                    string id       = row["AanestysId"]?.ToString()?.Trim();
                    string alkuaika = row["AanestysAlkuaika"]?.ToString() ?? "";
                    if ( string.IsNullOrEmpty( id ) ) continue;
                    if ( !alkuaika.StartsWith( dateFilter ) ) continue;
                    if ( !sessionMap.ContainsKey( id ) ) sessionMap[id] = row;
                }
                Logger.Info( $"GetCombinedDataWithDateFilter: {sessionMap.Count} sessions match date filter" );

                // ── Step 4: join and build enriched result table ──────────────────
                DataTable result = mpTable.Clone();
                DataColumn colAanestysOtsikko       = result.Columns.Add( "AanestysOtsikko" );
                DataColumn colKohtaOtsikko          = result.Columns.Add( "KohtaOtsikko" );
                DataColumn colPaaKohtaOtsikko       = result.Columns.Add( "PaaKohtaOtsikko" );
                DataColumn colKohtaKasittelyOtsikko = result.Columns.Add( "KohtaKasittelyOtsikko" );
                DataColumn colAanestysAlkuaika      = result.Columns.Add( "AanestysAlkuaika" );

                foreach ( DataRow mpRow in mpTable.Rows ) {
                    string id = mpRow["AanestysId"]?.ToString()?.Trim();
                    if ( string.IsNullOrEmpty( id ) || !sessionMap.TryGetValue( id, out DataRow sessionRow ) )
                        continue;
                    DataRow newRow = result.NewRow();
                    foreach ( DataColumn col in mpTable.Columns )
                        newRow[col.ColumnName] = mpRow[col];
                    newRow[colAanestysOtsikko]       = ColOrEmpty( sessionRow, "AanestysOtsikko" );
                    newRow[colKohtaOtsikko]          = ColOrEmpty( sessionRow, "KohtaOtsikko" );
                    newRow[colPaaKohtaOtsikko]       = ColOrEmpty( sessionRow, "PaaKohtaOtsikko" );
                    newRow[colKohtaKasittelyOtsikko] = ColOrEmpty( sessionRow, "KohtaKasittelyOtsikko" );
                    newRow[colAanestysAlkuaika]      = sessionRow["AanestysAlkuaika"]?.ToString() ?? "";
                    result.Rows.Add( newRow );
                }
                Logger.Info( $"GetCombinedDataWithDateFilter: {result.Rows.Count} joined rows" );

                if ( result.Rows.Count == 0 )
                    throw new Exception( "No rows found matching the date filter." );

                // ── Cleanup (same column order as GetCombinedData) ────────────────
                result.Columns.Remove( "EdustajaId" );
                result.Columns.Remove( "Imported" );
                result.Columns[result.Columns.IndexOf( "AanestysId" )].SetOrdinal( result.Columns.Count - 1 );
                result.Columns[result.Columns.IndexOf( "EdustajaHenkiloNumero" )].SetOrdinal( result.Columns.Count - 1 );

                hasMore = false;
                return result;
            } );
        #endregion Public methods

        #region Private methods

        // Paginates through all pages of a filtered table query and returns a single DataTable.
        // Throws "No rows found." when page 0 returns rowCount=0.
        private static DataTable FetchAllPages( string dbName, string columnName, string columnValue,
                                                int perPage, bool skipEven, bool voting )
        {
            DataTable result = null;
            int page = 0;
            bool more = true;

            while (more)
            {
                string url = _apiBase + "/api/v1/tables/" + dbName +
                             "/rows?perPage=" + perPage + "&page=" + page +
                             "&columnName=" + Uri.EscapeDataString( columnName ) +
                             "&columnValue=" + Uri.EscapeDataString( columnValue );

                string pageJson;
                try
                {
                    pageJson = GetDataAsync( url ).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.Error( $"FetchAllPages {dbName} page {page} HTTP failed", ex );
                    throw;
                }

                if ( string.IsNullOrEmpty( pageJson ) ) throw new Exception( "JSON data is empty." );

                var pageObj = JObject.Parse( pageJson );

                var errorToken = pageObj.SelectToken( "message" );
                if ( errorToken != null ) throw new Exception( "API error: " + errorToken.ToString() );

                var check = pageObj.SelectToken( "hasMore" );
                if ( check == null ) throw new Exception( "Unexpected response format: 'hasMore' field missing." );

                more = check.Value<bool>();

                if ( pageObj.SelectToken( "rowCount" )?.ToString() == "0" )
                {
                    if ( page == 0 ) throw new Exception( "No rows found." );
                    break;
                }

                if ( result == null ) result = InitTable( pageObj );
                result = AppendTable( pageObj, result, skipEven, voting );
                Logger.Info( $"FetchAllPages {dbName} page {page}: {result?.Rows.Count} rows so far" );
                page++;
            }

            Logger.Info( $"FetchAllPages {dbName} complete: pages={page}, total rows={result?.Rows.Count}" );
            return result;
        }

        private static DataTable GetVotingDistData(string votingId, bool skipEven)
        {
            DataTable distTable = null;
            string dbName = "SaliDBAanestysJakauma";

            // Create url structure
            baseUrl = _apiBase + "/api/v1/tables/" + dbName + "/rows?perPage=10&page=0&columnName=" + Uri.EscapeDataString("AanestysId") + "&columnValue=" + Uri.EscapeDataString(votingId);

            // Read data and form finalTable
            try
            {

                distTable = ReadData(baseUrl, skipEven, true);

            }
            catch (Exception ex)
            {

                Logger.Error( $"GetVotingDistData failed for votingId={votingId}", ex );
                throw;

            }

            return distTable;
        }

        // GetNameData forms the url and makes the call to ReadData
        private static DataTable GetNameData( string inputName, bool skipEven, string count, string type) {

            DataTable nameTable = null;
            string dbName = "SaliDBAanestysEdustaja";

            // Create url structure
            baseUrl = _apiBase + "/api/v1/tables/" + dbName + "/rows?perPage=" + count + "&page=0&columnName=" + Uri.EscapeDataString(type) + "&columnValue=" + Uri.EscapeDataString(inputName);

            // Read data and form finalTable
            try {

                nameTable = ReadData( baseUrl, skipEven, true );

            } catch ( Exception ex ) {

                Logger.Error( $"GetNameData failed for inputName={inputName} type={type}", ex );
                throw;

            }

            return nameTable;
        }

        private static object ColOrEmpty(DataRow row, string colName) =>
            row.Table.Columns.Contains(colName) ? (row[colName] ?? "") : (object)"";

        private static DataTable Timed( string label, Func<DataTable> body ) {
            Logger.Info( label );
            var sw = Stopwatch.StartNew();
            try {
                var result = body();
                Logger.Info( $"{label} → {( result != null ? result.Rows.Count + " rows" : "null" )} in {sw.ElapsedMilliseconds}ms" );
                return result;
            } catch ( Exception ex ) {
                Logger.Error( $"{label} failed after {sw.ElapsedMilliseconds}ms", ex );
                throw;
            }
        }

        // GetVotingData forms the url to seek for data concerning a certain voting id
        private static DataRow GetVotingDataOfOne(string votingNbr)
        {

            DataTable votingTable = null;

            // Create url structure
            baseUrl = _apiBase + "/api/v1/tables/SaliDBAanestys/rows?perPage=1&page=0&columnName=" + Uri.EscapeDataString("AanestysId") + "&columnValue=" + Uri.EscapeDataString(votingNbr);

            // Read data and form finalTable
            try
            {

                votingTable = ReadData(baseUrl, false, true);

            }
            catch (Exception ex)
            {

                Logger.Error( $"GetVotingDataOfOne failed for votingNbr={votingNbr}", ex );
                return null;

            }

            return votingTable?.Rows.Count > 0 ? votingTable.Rows[0] : null;
        }       
        
        // Read JSON data from target
        private static DataTable ReadData(string dataUrl, bool skipEven, bool voting) {

            // Initialize
            counter = 0;
            saveCounter = 0;
            DataTable tempTable = null;

            // Get async data
            try {
                json = GetDataAsync( dataUrl ).GetAwaiter().GetResult();
            } catch ( Exception ex ) {
                Logger.Error( "ReadData HTTP request failed", ex );
                throw;
            }

            // Check for null or empty
            if ( !string.IsNullOrEmpty( json ) ) {

                o = JObject.Parse( json );

                // Check for API-level error response
                var errorToken = o.SelectToken("message");
                if (errorToken != null) {
                    throw new Exception("API error: " + errorToken.ToString());
                }

                var check = o.SelectToken("hasMore");
                // Check if there is more available
                if (check == null) {
                    throw new Exception("Unexpected response format: 'hasMore' field missing.");
                }
                if (check.ToString() == "True")
                {
                    hasMore = true;
                } else {
                    hasMore = false;
                }

                if ( !( o.SelectToken( "rowCount" ).ToString() == "0" ) ) {

                    int rowCount = (int)o.SelectToken( "rowCount" );
                    Logger.Info( $"ReadData page {counter}: {rowCount} rows, hasMore={hasMore}" );

                    // Initialize or not?
                    if ( saveCounter == 0 ) {
                        tempTable = InitTable( o );
                        tempTable = AppendTable( o, tempTable, skipEven, voting );
                    } else {
                        tempTable = AppendTable( o, tempTable, skipEven, voting );
                    }

                } else {
                    Logger.Warn( "ReadData: API returned rowCount=0" );
                    throw new Exception("No rows found.");
                }

                counter++;

                json = "";

            } else {
                throw new Exception( "JSON data is empty." );        
            };

            return tempTable;
        }

        // Initialize table
        private static DataTable InitTable(JObject input) {

            DataTable table = new DataTable();
            counter = 0;

            // Get column count
            int columnCount = (int)input.SelectToken( "columnCount" );

            // Insert column names to string array
            for ( int i = 1; i < ( columnCount + 1 ); i++ ) {
                JToken columnTokens = input.SelectToken( "columnNames" );
                table.Columns.Add( columnTokens[i - 1].ToString() );
            }
            Logger.Info( $"InitTable: {columnCount} columns" );

            return table;
        }

        // Handle the json object to add data to a existing table
        private static DataTable AppendTable( JObject input, DataTable tempTable, bool skipEven, bool voting ) {
            
            // Get rowData
            JToken rowData = input.SelectToken( "rowData" );            

            if ( rowData != null ) {

                // Add data to table
                foreach ( JToken token in rowData ) {

                    if (!skipEven)
                    {
                        if (!(!voting && ((int)token[1] % 2 != 0)))
                        {
                            var row = tempTable.NewRow(); // Initialize new temporary row

                            // Cyclically add row data to each column
                            foreach (DataColumn column in tempTable.Columns)
                            {

                                row[column.ColumnName] = token[column.Ordinal];
                            };

                            tempTable.Rows.Add(row); // Add full temporary row to actual table

                        }
                    }
                    else
                    {
                        if (!(!voting && ((int)token[1] % 2 == 0)))
                        {
                            var row = tempTable.NewRow(); // Initialize new temporary row

                            // Cyclically add row data to each column
                            foreach (DataColumn column in tempTable.Columns)
                            {

                                row[column.ColumnName] = token[column.Ordinal];
                            };

                            tempTable.Rows.Add(row); // Add full temporary row to actual table

                        }
                    }                 
                         
                }

            } else {
                Logger.Warn( "AppendTable: rowData token not found in response" );
            }
            return tempTable;
        }

        // Http get string from url
        private static async Task<string> GetDataAsync(string url) {
            var response = await _httpClient.GetAsync( url ).ConfigureAwait( false );
            return await response.Content.ReadAsStringAsync().ConfigureAwait( false );
        }
        #endregion Private methods

        #region LegacyMain
        //// Start program from here
        //public static void Main() {

        //    // Get data from opendata
        //    //baseUrl = "http://avoindata.eduskunta.fi/api/v1/tables/SaliDBAanestys/rows?perPage=100&page=" + counter.ToString();
        //    //baseUrl = "http://avoindata.eduskunta.fi/api/v1/tables/SaliDBAanestysEdustaja/rows?perPage=100&page=" + counter.ToString();

        //    // If target has more data left to read
        //    while ( !done ) {

        //        baseUrl = "http://avoindata.eduskunta.fi/api/v1/tables/SaliDBAanestysEdustaja/rows?perPage=100&page=" + counter.ToString() + "&columnName=AanestysId&columnValue=" + idCounter.ToString();

        //        // Read data
        //        ReadData(baseUrl);                

        //        // If more tha 1000 reads have passed, write an excel file
        //        if ( counter - lastSave > 1000 ) {
        //            finalTable.ExportToExcel( @"C:\temp\prokkikset\data_"+idCounter+"_"+counter+".xlsx" );
        //            lastSave = counter;
        //        }

        //        // Make an excel file if there is no more data
        //        if (!hasMore)
        //        {
        //            hasMore = true;
        //            idCounter++;

        //            counter = 0;
        //            saveCounter = 0;

        //            if (saveCounter > 1500) 
        //            {
        //                try {
        //                    finalTable.ExportToExcel( @"C:\temp\prokkikset\data_" + idCounter + "_no_more.xlsx" );
        //                } catch ( Exception ex) {
        //                    System.Console.WriteLine( ex.Message );
        //                }

        //                saveCounter = 0;
        //            };
        //        };
        //    }

        //    // Write the quit file
        //    string dateString = System.DateTime.Today.ToShortDateString();
        //    dateString = dateString.Replace( '/', '_' );
        //    dateString = dateString.Replace( '\\', '_' );

        //    try {
        //        finalTable.ExportToExcel( @"C:\temp\prokkikset\dataquit" + dateString + ".xlsx" );
        //    } catch ( Exception ex) {
        //        System.Console.WriteLine( ex.Message );
        //    }

        //    // Wait for asynchronous stuff 
        //    _quitEvent.WaitOne();

        //    // cleanup/shutdown and quit           

        //}        
        #endregion LegacyMain

    }

    #region Legacy
    // ExportToExcel extension removed: requires Microsoft.Office.Interop.Excel (Windows/Office only)
#endregion Legacy

}
