using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MaSHi;
using Newtonsoft.Json.Linq;

namespace VoteCollectorTests
{
    // ── Shared mock HTTP infrastructure ──────────────────────────────────────

    internal sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            });
        }
    }

    // ── Shared reflection helpers ─────────────────────────────────────────────

    internal static class TestHelpers
    {
        // Replaces the static HttpClient with a mock returning the given response body.
        public static void SetMockHttpClient(
            string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var field = typeof(OpenDataRetriever).GetField(
                "_httpClient", BindingFlags.NonPublic | BindingFlags.Static)!;
            field.SetValue(null, new HttpClient(new MockHttpMessageHandler(responseBody, statusCode)));
        }

        // Calls the private static InitTable method.
        public static DataTable InvokeInitTable(JObject input)
        {
            var method = typeof(OpenDataRetriever).GetMethod(
                "InitTable", BindingFlags.NonPublic | BindingFlags.Static)!;
            return (DataTable)method.Invoke(null, new object[] { input })!;
        }

        // Calls the private static AppendTable method.
        public static DataTable InvokeAppendTable(
            JObject input, DataTable table, bool skipEven, bool voting)
        {
            var method = typeof(OpenDataRetriever).GetMethod(
                "AppendTable", BindingFlags.NonPublic | BindingFlags.Static)!;
            return (DataTable)method.Invoke(null, new object[] { input, table, skipEven, voting })!;
        }

        // Calls the private static ReadData method, unwrapping TargetInvocationException.
        public static DataTable InvokeReadData(string url, bool skipEven, bool voting)
        {
            var method = typeof(OpenDataRetriever).GetMethod(
                "ReadData", BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                return (DataTable)method.Invoke(null, new object[] { url, skipEven, voting })!;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Re-throw the original exception, preserving its type and stack trace.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(tie.InnerException).Throw();
                throw; // unreachable – satisfies compiler
            }
        }
    }

    // ── Realistic sample JSON payloads drawn from the real API ────────────────
    //
    // Source: https://avoindata.eduskunta.fi/api/v1/tables/SaliDBAanestys/rows?perPage=10&page=0
    //
    // Key column facts confirmed by the live API:
    //   Index 1 = KieliId  (1 = Finnish/odd, 2 = Swedish/even)  ← skipEven filter column
    //   "AanestysTulosTyhjia"  – no ä  (real API spelling)
    //   "AliKohtaTunniste"     – capital K (DataColumnCollection.Remove is case-insensitive)

    internal static class SampleJson
    {
        // ── SaliDBAanestys ────────────────────────────────────────────────────

        // Two rows: Finnish (AanestysId=13259, KieliId=1, odd) and
        //           Swedish (AanestysId=13260, KieliId=2, even).  hasMore=false.
        public const string SaliDBAanestys_TwoRows_HasMoreFalse = @"{
  ""page"": 0, ""perPage"": 10, ""hasMore"": false,
  ""tableName"": ""SaliDBAanestys"",
  ""columnNames"": [""AanestysId"",""KieliId"",""IstuntoVPVuosi"",""IstuntoNumero"",""IstuntoPvm"",
    ""IstuntoIlmoitettuAlkuaika"",""IstuntoAlkuaika"",""PJOtsikko"",""AanestysNumero"",
    ""AanestysAlkuaika"",""AanestysLoppuaika"",""AanestysMitatoity"",""AanestysOtsikko"",
    ""AanestysLisaOtsikko"",""PaaKohtaTunniste"",""PaaKohtaOtsikko"",""PaaKohtaHuomautus"",
    ""KohtaKasittelyOtsikko"",""KohtaKasittelyVaihe"",""KohtaJarjestys"",""KohtaTunniste"",
    ""KohtaOtsikko"",""KohtaHuomautus"",""AanestysTulosJaa"",""AanestysTulosEi"",
    ""AanestysTulosTyhjia"",""AanestysTulosPoissa"",""AanestysTulosYhteensa"",""Url"",
    ""AanestysPoytakirja"",""AanestysPoytakirjaUrl"",""AanestysValtiopaivaasia"",
    ""AanestysValtiopaivaasiaUrl"",""AliKohtaTunniste"",""Imported""],
  ""rowData"": [
    [""13259"",""1"",""1996"",""112"",""1996-10-01 00:00:00"",""1996-10-01 13:30:00"",
     ""1996-10-01 13:33:21"",null,""1"",""1996-10-01 13:38:30"",""1996-10-01 13:38:30"",
     ""0"",""P\u00e4ät\u00f6s 1 (Finnish)"",null,null,null,null,
     ""Ensimm\u00e4inen k\u00e4sittely"",""Ensimm\u00e4inen k\u00e4sittely"",
     ""1"",""1"",""Lakialoite laiksi X"",null,
     ""134"",""33"",""4"",""28"",""199"",
     ""/aanestystulos/1/112/1996"",""PTK 112/1996 vp"",
     ""/valtiopaivaasiakirjat/PTK+112/1996"","" / vp"",""/valtiopaivaasiat/+/"",
     null,""2018-06-02 10:14:00""],
    [""13260"",""2"",""1996"",""112"",""1996-10-01 00:00:00"",""1996-10-01 13:30:00"",
     ""1996-10-01 13:33:21"",null,""1"",""1996-10-01 13:38:30"",""1996-10-01 13:38:30"",
     ""0"",""Beslut 1 (Swedish)"",null,null,null,null,
     ""F\u00f6rsta behandling"",""F\u00f6rsta behandling"",
     ""1"",""1"",""Lagmotion om \u00e4ndring av lagen X"",null,
     ""134"",""33"",""4"",""28"",""199"",
     ""/omrostningsresultat/1/112/1996"",""PR 112/1996 rd"",
     ""/riksdagshandlingar/PR+112/1996"","" / rd"",""/riksdagsarenden/+/"",
     null,""2018-06-02 10:14:00""]
  ],
  ""columnCount"": 35, ""rowCount"": 2,
  ""pkName"": ""AanestysId"", ""pkStartValue"": null, ""pkLastValue"": null
}";

        // One Finnish row, hasMore=true (pagination not exhausted).
        public const string SaliDBAanestys_OneRow_HasMoreTrue = @"{
  ""page"": 0, ""perPage"": 10, ""hasMore"": true,
  ""tableName"": ""SaliDBAanestys"",
  ""columnNames"": [""AanestysId"",""KieliId"",""IstuntoVPVuosi"",""IstuntoNumero"",""IstuntoPvm"",
    ""IstuntoIlmoitettuAlkuaika"",""IstuntoAlkuaika"",""PJOtsikko"",""AanestysNumero"",
    ""AanestysAlkuaika"",""AanestysLoppuaika"",""AanestysMitatoity"",""AanestysOtsikko"",
    ""AanestysLisaOtsikko"",""PaaKohtaTunniste"",""PaaKohtaOtsikko"",""PaaKohtaHuomautus"",
    ""KohtaKasittelyOtsikko"",""KohtaKasittelyVaihe"",""KohtaJarjestys"",""KohtaTunniste"",
    ""KohtaOtsikko"",""KohtaHuomautus"",""AanestysTulosJaa"",""AanestysTulosEi"",
    ""AanestysTulosTyhjia"",""AanestysTulosPoissa"",""AanestysTulosYhteensa"",""Url"",
    ""AanestysPoytakirja"",""AanestysPoytakirjaUrl"",""AanestysValtiopaivaasia"",
    ""AanestysValtiopaivaasiaUrl"",""AliKohtaTunniste"",""Imported""],
  ""rowData"": [
    [""13259"",""1"",""1996"",""112"",""1996-10-01 00:00:00"",""1996-10-01 13:30:00"",
     ""1996-10-01 13:33:21"",null,""1"",""1996-10-01 13:38:30"",""1996-10-01 13:38:30"",
     ""0"",""P\u00e4ät\u00f6s 1"",null,null,null,null,
     ""Ensimm\u00e4inen k\u00e4sittely"",""Ensimm\u00e4inen k\u00e4sittely"",
     ""1"",""1"",""Lakialoite laiksi X"",null,
     ""134"",""33"",""4"",""28"",""199"",
     ""/aanestystulos/1/112/1996"",""PTK 112/1996 vp"",
     ""/valtiopaivaasiakirjat/PTK+112/1996"","" / vp"",""/valtiopaivaasiat/+/"",
     null,""2018-06-02 10:14:00""]
  ],
  ""columnCount"": 35, ""rowCount"": 1,
  ""pkName"": ""AanestysId"", ""pkStartValue"": null, ""pkLastValue"": null
}";

        // Empty result – triggers the "No rows found" exception path.
        public const string AnyTable_ZeroRows = @"{
  ""hasMore"": false, ""rowCount"": 0, ""columnCount"": 0,
  ""columnNames"": [], ""rowData"": []
}";

        // ── SaliDBAanestysEdustaja (MP votes per voting) ──────────────────────
        //
        // AanestysId is at index 1 (value "13301", odd).
        // skipEven=true keeps odd AanestysId rows → both rows included.
        public const string SaliDBAanestysEdustaja_TwoRows = @"{
  ""page"": 0, ""perPage"": 200, ""hasMore"": false,
  ""tableName"": ""SaliDBAanestysEdustaja"",
  ""columnNames"": [""EdustajaId"",""AanestysId"",""EdustajaEtunimi"",""EdustajaSukunimi"",
                    ""EdustajaHenkiloNumero"",""EdustajaRyhmaLyhenne"",""EdustajaAanestys"",""Imported""],
  ""rowData"": [
    [""2745050"",""13301"",""Markus"",""Aaltonen"",""102"",""sd"",""Poissa"",""2018-02-05 11:49:36""],
    [""2745051"",""13301"",""Esko"",""Aho"",""104"",""kesk"",""Ei"",""2018-02-05 11:49:36""]
  ],
  ""columnCount"": 8, ""rowCount"": 2,
  ""pkName"": ""EdustajaId"", ""pkStartValue"": null, ""pkLastValue"": null
}";

        // ── SaliDBAanestysJakauma (party vote distribution) ───────────────────
        //
        // AanestysId is at index 1 (value "13260", even).
        // skipEven=false keeps even AanestysId rows → both rows included.
        public const string SaliDBAanestysJakauma_TwoRows = @"{
  ""page"": 0, ""perPage"": 10, ""hasMore"": false,
  ""tableName"": ""SaliDBAanestysJakauma"",
  ""columnNames"": [""JakaumaId"",""AanestysId"",""Tyyppi"",""Ryhmalyhenne"",
                    ""Jaa"",""Ei"",""Tyhja"",""Poissa"",""YhteensaAanestaneet"",""Imported""],
  ""rowData"": [
    [""1"",""13260"",""A"",""kesk"",""50"",""10"",""2"",""5"",""67"",""2018-06-02 10:14:00""],
    [""2"",""13260"",""A"",""kok"", ""40"",""20"",""3"",""4"",""67"",""2018-06-02 10:14:00""]
  ],
  ""columnCount"": 10, ""rowCount"": 2,
  ""pkName"": ""JakaumaId"", ""pkStartValue"": null, ""pkLastValue"": null
}";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // InitTable tests
    // ═══════════════════════════════════════════════════════════════════════════

    [TestClass]
    public class InitTableTests
    {
        [TestMethod]
        public void InitTable_CreatesColumnsMatchingRealApiColumnNames()
        {
            var input = JObject.Parse(SampleJson.SaliDBAanestys_OneRow_HasMoreTrue);
            var table = TestHelpers.InvokeInitTable(input);

            Assert.AreEqual(35, table.Columns.Count);
            Assert.AreEqual("AanestysId",       table.Columns[0].ColumnName);
            Assert.AreEqual("KieliId",           table.Columns[1].ColumnName);
            Assert.AreEqual("IstuntoVPVuosi",    table.Columns[2].ColumnName);
            Assert.AreEqual("KohtaOtsikko",      table.Columns[21].ColumnName);
            Assert.AreEqual("AliKohtaTunniste",  table.Columns[33].ColumnName);
            Assert.AreEqual("Imported",          table.Columns[34].ColumnName);
        }

        [TestMethod]
        public void InitTable_ReturnsEmptyDataTable_NoRowsPreloaded()
        {
            var input = JObject.Parse(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);
            var table = TestHelpers.InvokeInitTable(input);

            Assert.AreEqual(0, table.Rows.Count);
        }

        [TestMethod]
        public void InitTable_ColumnCountMatchesApiColumnCount()
        {
            var input = JObject.Parse(SampleJson.SaliDBAanestysJakauma_TwoRows);
            var table = TestHelpers.InvokeInitTable(input);

            Assert.AreEqual(10, table.Columns.Count);
            Assert.AreEqual("JakaumaId",  table.Columns[0].ColumnName);
            Assert.AreEqual("AanestysId", table.Columns[1].ColumnName);
            Assert.AreEqual("Imported",   table.Columns[9].ColumnName);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AppendTable tests  (language filter is based on token[1] = KieliId)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestClass]
    public class AppendTableTests
    {
        // Helper: create an empty DataTable whose schema matches SaliDBAanestys.
        private static DataTable BuildSaliDBAanestysTable() =>
            TestHelpers.InvokeInitTable(
                JObject.Parse(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse));

        // ── voting=true bypasses the KieliId filter completely ─────────────────

        [TestMethod]
        public void AppendTable_VotingTrue_SkipEvenFalse_IncludesBothFinnishAndSwedishRows()
        {
            var table = BuildSaliDBAanestysTable();
            var input = JObject.Parse(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = TestHelpers.InvokeAppendTable(input, table, skipEven: false, voting: true);

            Assert.AreEqual(2, result.Rows.Count);
        }

        [TestMethod]
        public void AppendTable_VotingTrue_SkipEvenTrue_IncludesBothFinnishAndSwedishRows()
        {
            var table = BuildSaliDBAanestysTable();
            var input = JObject.Parse(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = TestHelpers.InvokeAppendTable(input, table, skipEven: true, voting: true);

            Assert.AreEqual(2, result.Rows.Count);
        }

        // ── skipEven=true (Finnish / default mode): odd KieliId kept ──────────

        [TestMethod]
        public void AppendTable_SkipEvenTrue_VotingFalse_KeepsFinnishRow_DropsSwedishRow()
        {
            // KieliId=1 (odd) → kept;  KieliId=2 (even) → dropped
            var table = BuildSaliDBAanestysTable();
            var input = JObject.Parse(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = TestHelpers.InvokeAppendTable(input, table, skipEven: true, voting: false);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual("1", result.Rows[0]["KieliId"].ToString(), "Only Finnish row (KieliId=1) should be kept");
        }

        // ── skipEven=false (Swedish mode): even KieliId kept ──────────────────

        [TestMethod]
        public void AppendTable_SkipEvenFalse_VotingFalse_KeepsSwedishRow_DropsFinnishRow()
        {
            // KieliId=2 (even) → kept;  KieliId=1 (odd) → dropped
            var table = BuildSaliDBAanestysTable();
            var input = JObject.Parse(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = TestHelpers.InvokeAppendTable(input, table, skipEven: false, voting: false);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual("2", result.Rows[0]["KieliId"].ToString(), "Only Swedish row (KieliId=2) should be kept");
        }

        // ── row data mapping ───────────────────────────────────────────────────

        [TestMethod]
        public void AppendTable_RowValuesAreMappedToCorrectColumns()
        {
            // voting=true to include both rows; check that Finnish row maps correctly.
            var table = BuildSaliDBAanestysTable();
            var input = JObject.Parse(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = TestHelpers.InvokeAppendTable(input, table, skipEven: false, voting: true);

            var finnishRow = result.Rows[0];
            Assert.AreEqual("13259",   finnishRow["AanestysId"].ToString());
            Assert.AreEqual("1",       finnishRow["KieliId"].ToString());
            Assert.AreEqual("1996",    finnishRow["IstuntoVPVuosi"].ToString());
            Assert.AreEqual("134",     finnishRow["AanestysTulosJaa"].ToString().Trim());
            Assert.AreEqual("33",      finnishRow["AanestysTulosEi"].ToString().Trim());
        }

        // ── missing rowData key ────────────────────────────────────────────────

        [TestMethod]
        public void AppendTable_MissingRowData_ReturnsUnchangedEmptyTable()
        {
            var table = BuildSaliDBAanestysTable();
            var inputWithNoRowData = JObject.Parse(@"{""rowCount"": 0}");

            var result = TestHelpers.InvokeAppendTable(inputWithNoRowData, table, skipEven: false, voting: false);

            Assert.AreEqual(0, result.Rows.Count);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ReadData tests  (private method accessed via reflection + mock HTTP)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestClass]
    public class ReadDataTests
    {
        [TestMethod]
        public void ReadData_SetsHasMoreFalse_WhenResponseIndicatesNoMoreData()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            TestHelpers.InvokeReadData("http://test/", skipEven: true, voting: true);

            Assert.IsFalse(OpenDataRetriever.hasMore);
        }

        [TestMethod]
        public void ReadData_SetsHasMoreTrue_WhenResponseIndicatesMoreDataAvailable()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_OneRow_HasMoreTrue);

            TestHelpers.InvokeReadData("http://test/", skipEven: true, voting: true);

            Assert.IsTrue(OpenDataRetriever.hasMore);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void ReadData_ZeroRowCount_ThrowsException()
        {
            TestHelpers.SetMockHttpClient(SampleJson.AnyTable_ZeroRows);

            TestHelpers.InvokeReadData("http://test/", skipEven: false, voting: false);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void ReadData_EmptyResponseBody_ThrowsException()
        {
            TestHelpers.SetMockHttpClient("");

            TestHelpers.InvokeReadData("http://test/", skipEven: false, voting: false);
        }

        [TestMethod]
        [ExpectedException(typeof(Newtonsoft.Json.JsonReaderException))]
        public void ReadData_MalformedJson_ThrowsException()
        {
            TestHelpers.SetMockHttpClient("not valid json");

            TestHelpers.InvokeReadData("http://test/", skipEven: false, voting: false);
        }

        [TestMethod]
        public void ReadData_ReturnsTableWithCorrectColumnSchema()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_OneRow_HasMoreTrue);

            var result = TestHelpers.InvokeReadData("http://test/", skipEven: true, voting: true);

            Assert.IsNotNull(result);
            Assert.AreEqual(35, result.Columns.Count);
            Assert.AreEqual("AanestysId", result.Columns[0].ColumnName);
            Assert.AreEqual("KieliId",    result.Columns[1].ColumnName);
            Assert.AreEqual("Imported",   result.Columns[34].ColumnName);
        }

        [TestMethod]
        public void ReadData_VotingTrue_ReturnsAllRows_IgnoresKieliIdFilter()
        {
            // Both Finnish (KieliId=1) and Swedish (KieliId=2) rows must survive.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = TestHelpers.InvokeReadData("http://test/", skipEven: false, voting: true);

            Assert.AreEqual(2, result.Rows.Count);
        }

        [TestMethod]
        public void ReadData_SkipEvenTrue_ReturnsOnlyFinnishRows()
        {
            // skipEven=true → keeps odd KieliId=1 (Finnish), drops even KieliId=2 (Swedish).
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = TestHelpers.InvokeReadData("http://test/", skipEven: true, voting: false);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual("1", result.Rows[0]["KieliId"].ToString());
        }

        [TestMethod]
        public void ReadData_SkipEvenFalse_ReturnsOnlySwedishRows()
        {
            // skipEven=false → keeps even KieliId=2 (Swedish), drops odd KieliId=1 (Finnish).
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = TestHelpers.InvokeReadData("http://test/", skipEven: false, voting: false);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual("2", result.Rows[0]["KieliId"].ToString());
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GetVotingData tests  (public API)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestClass]
    public class GetVotingDataTests
    {
        [TestMethod]
        public void GetVotingData_ReturnsNull_WhenApiReportsZeroRows()
        {
            TestHelpers.SetMockHttpClient(SampleJson.AnyTable_ZeroRows);

            var result = OpenDataRetriever.GetVotingData(
                "1996", skipEven: true, count: 10, type: "IstuntoVPVuosi");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetVotingData_RemovesSessionAndMetadataColumns()
        {
            // skipEven=true → Finnish row (KieliId=1) survives the filter.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = OpenDataRetriever.GetVotingData(
                "1996", skipEven: true, count: 10, type: "IstuntoVPVuosi");

            Assert.IsNotNull(result);
            Assert.IsFalse(result!.Columns.Contains("KieliId"),                   "KieliId");
            Assert.IsFalse(result.Columns.Contains("IstuntoVPVuosi"),             "IstuntoVPVuosi");
            Assert.IsFalse(result.Columns.Contains("IstuntoPvm"),                 "IstuntoPvm");
            Assert.IsFalse(result.Columns.Contains("IstuntoNumero"),              "IstuntoNumero");
            Assert.IsFalse(result.Columns.Contains("IstuntoAlkuaika"),            "IstuntoAlkuaika");
            Assert.IsFalse(result.Columns.Contains("IstuntoIlmoitettuAlkuaika"),  "IstuntoIlmoitettuAlkuaika");
            Assert.IsFalse(result.Columns.Contains("AanestysLoppuaika"),          "AanestysLoppuaika");
            Assert.IsFalse(result.Columns.Contains("AanestysNumero"),             "AanestysNumero");
            Assert.IsFalse(result.Columns.Contains("AanestysLisaOtsikko"),        "AanestysLisaOtsikko");
            Assert.IsFalse(result.Columns.Contains("AanestysTulosYhteensa"),      "AanestysTulosYhteensa");
            Assert.IsFalse(result.Columns.Contains("AanestysValtiopaivaasiaUrl"), "AanestysValtiopaivaasiaUrl");
            Assert.IsFalse(result.Columns.Contains("AliKohtaTunniste"),           "AliKohtaTunniste");
            Assert.IsFalse(result.Columns.Contains("Imported"),                   "Imported");
            Assert.IsFalse(result.Columns.Contains("Url"),                        "Url");
            Assert.IsFalse(result.Columns.Contains("PaaKohtaTunniste"),           "PaaKohtaTunniste");
            Assert.IsFalse(result.Columns.Contains("PaaKohtaOtsikko"),            "PaaKohtaOtsikko");
            Assert.IsFalse(result.Columns.Contains("PaaKohtaHuomautus"),          "PaaKohtaHuomautus");
            Assert.IsFalse(result.Columns.Contains("KohtaKasittelyOtsikko"),      "KohtaKasittelyOtsikko");
            Assert.IsFalse(result.Columns.Contains("KohtaKasittelyVaihe"),        "KohtaKasittelyVaihe");
            Assert.IsFalse(result.Columns.Contains("KohtaJarjestys"),             "KohtaJarjestys");
            Assert.IsFalse(result.Columns.Contains("KohtaTunniste"),              "KohtaTunniste");
            Assert.IsFalse(result.Columns.Contains("KohtaHuomautus"),             "KohtaHuomautus");
            Assert.IsFalse(result.Columns.Contains("AanestysPoytakirja"),         "AanestysPoytakirja");
            Assert.IsFalse(result.Columns.Contains("AanestysPoytakirjaUrl"),      "AanestysPoytakirjaUrl");
        }

        [TestMethod]
        public void GetVotingData_RetainsVotingResultAndSubjectColumns()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = OpenDataRetriever.GetVotingData(
                "1996", skipEven: true, count: 10, type: "IstuntoVPVuosi");

            Assert.IsNotNull(result);
            Assert.IsTrue(result!.Columns.Contains("AanestysId"),          "AanestysId");
            Assert.IsTrue(result.Columns.Contains("AanestysOtsikko"),      "AanestysOtsikko");
            Assert.IsTrue(result.Columns.Contains("AanestysAlkuaika"),     "AanestysAlkuaika");
            Assert.IsTrue(result.Columns.Contains("AanestysMitatoity"),    "AanestysMitatoity");
            Assert.IsTrue(result.Columns.Contains("KohtaOtsikko"),         "KohtaOtsikko");
            Assert.IsTrue(result.Columns.Contains("PJOtsikko"),            "PJOtsikko");
            Assert.IsTrue(result.Columns.Contains("AanestysTulosJaa"),     "AanestysTulosJaa");
            Assert.IsTrue(result.Columns.Contains("AanestysTulosEi"),      "AanestysTulosEi");
            Assert.IsTrue(result.Columns.Contains("AanestysTulosTyhjia"),  "AanestysTulosTyhjia");
            Assert.IsTrue(result.Columns.Contains("AanestysTulosPoissa"),  "AanestysTulosPoissa");
            // AanestysValtiopaivaasia (without Url suffix) is NOT removed by the code.
            Assert.IsTrue(result.Columns.Contains("AanestysValtiopaivaasia"), "AanestysValtiopaivaasia");
        }

        [TestMethod]
        public void GetVotingData_KohtaOtsikkoReorderedToOrdinal2()
        {
            // The code calls SetOrdinal(2) on KohtaOtsikko after removing other columns.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = OpenDataRetriever.GetVotingData(
                "1996", skipEven: true, count: 10, type: "IstuntoVPVuosi");

            Assert.IsNotNull(result);
            Assert.AreEqual("KohtaOtsikko", result!.Columns[2].ColumnName,
                "KohtaOtsikko must be at ordinal 2 after reordering");
        }

        [TestMethod]
        public void GetVotingData_SetsHasMoreTrue_WhenApiIndicatesMorePages()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_OneRow_HasMoreTrue);

            OpenDataRetriever.GetVotingData("1996", skipEven: true, count: 10, type: "IstuntoVPVuosi");

            Assert.IsTrue(OpenDataRetriever.hasMore);
        }

        [TestMethod]
        public void GetVotingData_SetsHasMoreFalse_WhenApiIndicatesLastPage()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            OpenDataRetriever.GetVotingData("1996", skipEven: true, count: 10, type: "IstuntoVPVuosi");

            Assert.IsFalse(OpenDataRetriever.hasMore);
        }

        [TestMethod]
        public void GetVotingData_SkipEvenTrue_ReturnsOnlyFinnishRows()
        {
            // Two source rows (Finnish + Swedish); only Finnish (KieliId=1) survives.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = OpenDataRetriever.GetVotingData(
                "1996", skipEven: true, count: 10, type: "IstuntoVPVuosi");

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result!.Rows.Count);
        }

        [TestMethod]
        public void GetVotingData_SkipEvenFalse_ReturnsOnlySwedishRows()
        {
            // Two source rows; only Swedish (KieliId=2) survives.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestys_TwoRows_HasMoreFalse);

            var result = OpenDataRetriever.GetVotingData(
                "1996", skipEven: false, count: 10, type: "IstuntoVPVuosi");

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result!.Rows.Count);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GetPartyDistData tests  (public API)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestClass]
    public class GetPartyDistDataTests
    {
        // The sample has AanestysId=13260 (even) at index 1.
        // skipEven=false keeps even values, so both rows survive the filter.

        [TestMethod]
        public void GetPartyDistData_RemovesImportedColumn()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysJakauma_TwoRows);

            var result = OpenDataRetriever.GetPartyDistData(
                "13260", skipEven: false, type: "AanestysId");

            Assert.IsNotNull(result);
            Assert.IsFalse(result!.Columns.Contains("Imported"), "Imported column must be removed");
        }

        [TestMethod]
        public void GetPartyDistData_RemovesTyyppiColumn()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysJakauma_TwoRows);

            var result = OpenDataRetriever.GetPartyDistData(
                "13260", skipEven: false, type: "AanestysId");

            Assert.IsNotNull(result);
            Assert.IsFalse(result!.Columns.Contains("Tyyppi"), "Tyyppi column must be removed");
        }

        [TestMethod]
        public void GetPartyDistData_RetainsVoteCountAndPartyColumns()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysJakauma_TwoRows);

            var result = OpenDataRetriever.GetPartyDistData(
                "13260", skipEven: false, type: "AanestysId");

            Assert.IsNotNull(result);
            Assert.IsTrue(result!.Columns.Contains("Ryhmalyhenne"),       "Ryhmalyhenne");
            Assert.IsTrue(result.Columns.Contains("Jaa"),                 "Jaa");
            Assert.IsTrue(result.Columns.Contains("Ei"),                  "Ei");
            Assert.IsTrue(result.Columns.Contains("Tyhja"),               "Tyhja");
            Assert.IsTrue(result.Columns.Contains("Poissa"),              "Poissa");
            Assert.IsTrue(result.Columns.Contains("YhteensaAanestaneet"), "YhteensaAanestaneet");
            Assert.IsTrue(result.Columns.Contains("AanestysId"),          "AanestysId");
        }

        [TestMethod]
        public void GetPartyDistData_ReturnsBothPartyRows()
        {
            // Both rows have even AanestysId=13260; skipEven=false keeps even values.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysJakauma_TwoRows);

            var result = OpenDataRetriever.GetPartyDistData(
                "13260", skipEven: false, type: "AanestysId");

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result!.Rows.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(Newtonsoft.Json.JsonReaderException))]
        public void GetPartyDistData_RethrowsException_OnInvalidJson()
        {
            TestHelpers.SetMockHttpClient("not valid json");

            OpenDataRetriever.GetPartyDistData("13260", skipEven: false, type: "AanestysId");
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void GetPartyDistData_RethrowsException_OnZeroRows()
        {
            TestHelpers.SetMockHttpClient(SampleJson.AnyTable_ZeroRows);

            OpenDataRetriever.GetPartyDistData("13260", skipEven: false, type: "AanestysId");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GetEdustajaData tests  (public API)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestClass]
    public class GetEdustajaDataTests
    {
        [TestMethod]
        public void GetEdustajaData_RemovesImportedColumn()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true);

            Assert.IsNotNull(result);
            Assert.IsFalse(result!.Columns.Contains("Imported"), "Imported column must be removed");
        }

        [TestMethod]
        public void GetEdustajaData_RetainsMPAndVoteColumns()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true);

            Assert.IsNotNull(result);
            Assert.IsTrue(result!.Columns.Contains("EdustajaId"),          "EdustajaId");
            Assert.IsTrue(result.Columns.Contains("AanestysId"),           "AanestysId");
            Assert.IsTrue(result.Columns.Contains("EdustajaEtunimi"),      "EdustajaEtunimi");
            Assert.IsTrue(result.Columns.Contains("EdustajaSukunimi"),     "EdustajaSukunimi");
            Assert.IsTrue(result.Columns.Contains("EdustajaHenkiloNumero"),"EdustajaHenkiloNumero");
            Assert.IsTrue(result.Columns.Contains("EdustajaRyhmaLyhenne"), "EdustajaRyhmaLyhenne");
            Assert.IsTrue(result.Columns.Contains("EdustajaAanestys"),     "EdustajaAanestys");
        }

        [TestMethod]
        public void GetEdustajaData_ReturnsBothRows_WhenSkipEvenTrue_AndOddAanestysId()
        {
            // AanestysId=13301 is odd; skipEven=true keeps odd token[1] values → both rows kept.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result!.Rows.Count);
        }

        [TestMethod]
        public void GetEdustajaData_ReturnsNoRows_WhenSkipEvenFalse_AndOddAanestysId()
        {
            // AanestysId=13301 is odd; skipEven=false keeps even token[1] values → no rows added.
            // ReadData only throws when the API rowCount is 0, not when the filter removes all rows.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: false);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result!.Rows.Count, "All rows should be filtered out for skipEven=false with odd AanestysId");
        }

        [TestMethod]
        public void GetEdustajaData_SetsHasMoreFalse_WhenApiIndicatesLastPage()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            OpenDataRetriever.GetEdustajaData("13301", skipEven: true);

            Assert.IsFalse(OpenDataRetriever.hasMore);
        }

        [TestMethod]
        [ExpectedException(typeof(Newtonsoft.Json.JsonReaderException))]
        public void GetEdustajaData_RethrowsException_OnInvalidJson()
        {
            TestHelpers.SetMockHttpClient("not valid json");

            OpenDataRetriever.GetEdustajaData("13301", skipEven: true);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void GetEdustajaData_RethrowsException_OnZeroRows()
        {
            TestHelpers.SetMockHttpClient(SampleJson.AnyTable_ZeroRows);

            OpenDataRetriever.GetEdustajaData("13301", skipEven: true);
        }

        // ── Party-filter tests ─────────────────────────────────────────────────

        [TestMethod]
        public void GetEdustajaData_PartyFilter_ReturnsOnlyMatchingPartyRows()
        {
            // Sample has two rows: "sd" (Aaltonen) and "kesk" (Aho).
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true, partyFilter: "sd");

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result!.Rows.Count, "Only the sd row should be returned");
            Assert.AreEqual("Aaltonen", result.Rows[0]["EdustajaSukunimi"].ToString());
        }

        [TestMethod]
        public void GetEdustajaData_PartyFilter_IsCaseInsensitive()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true, partyFilter: "SD");

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result!.Rows.Count);
            Assert.AreEqual("Aaltonen", result.Rows[0]["EdustajaSukunimi"].ToString());
        }

        [TestMethod]
        public void GetEdustajaData_PartyFilter_TrimsWhitespaceBeforeComparing()
        {
            // EdustajaRyhmaLyhenne in real API data comes with trailing spaces (e.g. "sd        ").
            // The filter value from Ryhmalyhenne is typically already trimmed, but we handle both.
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true, partyFilter: "  kesk  ");

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result!.Rows.Count);
            Assert.AreEqual("Aho", result.Rows[0]["EdustajaSukunimi"].ToString());
        }

        [TestMethod]
        public void GetEdustajaData_PartyFilter_NoMatch_ReturnsEmptyTableWithCorrectSchema()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true, partyFilter: "vihr");

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result!.Rows.Count, "No rows should match party 'vihr'");
            // Schema must still be present
            Assert.IsTrue(result.Columns.Contains("EdustajaRyhmaLyhenne"), "Schema column must be present");
        }

        [TestMethod]
        public void GetEdustajaData_PartyFilter_NullFilter_ReturnsAllRows()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true, partyFilter: null);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result!.Rows.Count, "All rows returned when partyFilter is null");
        }

        [TestMethod]
        public void GetEdustajaData_PartyFilter_EmptyStringFilter_ReturnsAllRows()
        {
            TestHelpers.SetMockHttpClient(SampleJson.SaliDBAanestysEdustaja_TwoRows);

            var result = OpenDataRetriever.GetEdustajaData("13301", skipEven: true, partyFilter: "");

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result!.Rows.Count, "All rows returned when partyFilter is empty");
        }
    }
}
