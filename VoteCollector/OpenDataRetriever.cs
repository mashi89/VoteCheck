using System;
using System.Collections.Generic;
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

        #region Variables
        // Initialize variables
        public static bool hasMore = false;

        private static string baseUrl = "";        
        private static string json = "";
        private static ManualResetEvent _quitEvent = new ManualResetEvent( false );
        private static DataTable finalTable;
        private static int counter = 0;
        private static int saveCounter = 0;
        private static JObject o;
        #endregion Variables

        public static void Main() {

        }

        #region Public methods
        // GetPartyDistData method should seek the party vote distribution for a voting id
        public static DataTable GetVotingData(string year, bool skipEven, int count, string type)
        {
            DataTable votingTable = null;
            string dbName = "SaliDBAanestys";

            // Create url structure
            baseUrl = "http://avoindata.eduskunta.fi/api/v1/tables/" + dbName + "/rows?perPage=" + count + "&page=0&columnName="+ type + "&columnValue=" + year;

            // Read data and form finalTable
            try
            {

                votingTable = ReadData(baseUrl, skipEven, false);

            }
            catch (Exception ex)
            {

                System.Console.WriteLine(ex.Message);

            }

            if ( votingTable != null )
            {                
                votingTable.Columns.Remove("KieliId");
                votingTable.Columns.Remove("KohtaTunniste");
                votingTable.Columns.Remove("KohtaJarjestys");
                votingTable.Columns.Remove("IstuntoPvm");
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

            return votingTable;
        }

        // GetSubjectData method should seek for certain phrases
        public static DataTable GetSubjectData(string inputName, string type)
        {
            DataRow votingDataRow = GetVotingDataOfOne(inputName);
            finalTable = null;

            

            return finalTable;
        }

        // GetPartyDistData method should seek the party vote distribution for a voting id
        public static DataTable GetPartyDistData(string votingId, bool skipEven, string type)
        {
            // Get voting data
            try
            {
                finalTable = GetVotingDistData(votingId, skipEven);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            // Cleaning
            finalTable.Columns.Remove("Imported");
            finalTable.Columns.Remove("Tyyppi");

            return finalTable;
        }

        // GetCombined data should seek for MP votes with vote subjects visible
        // This method makes two different queries and combines them in one table
        public static DataTable GetCombinedData(string inputName, bool skipEven, int count, string type)
        {
            DataRow votingDataRow = null;

            // Initial data query
            try
            {
                finalTable = GetNameData(inputName, skipEven, count.ToString(), type);
            }
            catch (Exception ex)
            {
                throw ex;
            }            

            // Add new columns for voting data
            finalTable.Columns.Add("AanestysOtsikko").SetOrdinal(7);
            finalTable.Columns.Add("KohtaOtsikko").SetOrdinal(8);
            finalTable.Columns.Add("PaaKohtaOtsikko").SetOrdinal(9);
            finalTable.Columns.Add("KohtaKasittelyOtsikko").SetOrdinal(10);            
            finalTable.Columns.Add("AanestysAlkuaika").SetOrdinal(11);

            // Insert voting data for each row
            foreach (DataRow row in finalTable.Rows)
            {
                // Make new queries
                votingDataRow = GetVotingDataOfOne(row.ItemArray[1].ToString());
                row[7] = votingDataRow[12];
                row[8] = votingDataRow[21];
                row[9] = votingDataRow[15];
                row[10] = votingDataRow[17];
                row[11] = votingDataRow[9];

            }

            // Cleaning
            finalTable.Columns.Remove("EdustajaId");
            finalTable.Columns.Remove("Imported");
            finalTable.Columns[finalTable.Columns.IndexOf("AanestysId")].SetOrdinal(10);
            finalTable.Columns[finalTable.Columns.IndexOf("EdustajaHenkiloNumero")].SetOrdinal(10);
            return finalTable;
        }
        #endregion Public methods

        #region Private methods
        private static DataTable GetVotingDistData(string votingId, bool skipEven)
        {
            DataTable distTable = null;
            string dbName = "SaliDBAanestysJakauma";

            // Create url structure
            baseUrl = "http://avoindata.eduskunta.fi/api/v1/tables/" + dbName + "/rows?perPage=10&page=0&columnName=AanestysId&columnValue=" + votingId;

            // Read data and form finalTable
            try
            {

                distTable = ReadData(baseUrl, skipEven, false);

            }
            catch (Exception ex)
            {

                System.Console.WriteLine(ex.Message);
                throw ex;

            }

            return distTable;
        }

        // GetNameData forms the url and makes the call to ReadData
        private static DataTable GetNameData( string inputName, bool skipEven, string count, string type) {

            DataTable nameTable = null;
            string dbName = "SaliDBAanestysEdustaja";

            // Create url structure
            baseUrl = "http://avoindata.eduskunta.fi/api/v1/tables/" + dbName + "/rows?perPage=" + count + "&page=0&columnName=" + type + "&columnValue=" + inputName;
   
            // Read data and form finalTable
            try {

                nameTable = ReadData( baseUrl, skipEven, false );

            } catch ( Exception ex ) {

                System.Console.WriteLine( ex.Message );
                throw ex;

            }

            return nameTable;
        }

        // GetVotingData forms the url to seek for data concerning a certain voting id
        private static DataRow GetVotingDataOfOne(string votingNbr)
        {

            DataTable votingTable = null;

            // Create url structure
            baseUrl = "http://avoindata.eduskunta.fi/api/v1/tables/SaliDBAanestys/rows?perPage=1&page=0&columnName=AanestysId&columnValue=" + votingNbr;

            // Read data and form finalTable
            try
            {

                votingTable = ReadData(baseUrl, false, true);

            }
            catch (Exception ex)
            {

                System.Console.WriteLine(ex.Message);

            }

            return votingTable.Rows[0];
        }       
        
        // Read JSON data from target
        private static DataTable ReadData(string dataUrl, bool skipEven, bool voting) {

            // Initialize
            DataTable tempTable = null;

            // Get async data
            try {
                json = GetDataAsync( dataUrl ).Result;
            } catch ( Exception ex ) {
                System.Console.WriteLine(ex.Message);
            }

            // Check for null
            if ( json != "" ) {

                o = JObject.Parse( json );
                var check = o.SelectToken("hasMore");
                // Check if there is more available
                if (check.ToString() == "True")
                {
                    hasMore = true;
                } else {
                    hasMore = false;
                }

                if ( !( o.SelectToken( "rowCount" ).ToString() == "0" ) ) {

                    System.Console.WriteLine( "Commencing handling of page " + counter );

                    // Initialize or not?
                    if ( saveCounter == 0 ) {
                        tempTable = InitTable( o );
                        tempTable = AppendTable( o, tempTable, skipEven, voting );
                    } else {
                        tempTable = AppendTable( o, tempTable, skipEven, voting );
                    }

                } else {
                    // Handle row count zero
                    System.Console.WriteLine( "No rows found." );
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

            // Initialize
            System.Console.Write("Initializing table");
            DataTable table = new DataTable();
            counter = 0;

            // Get column count
            int columnCount = (int)input.SelectToken( "columnCount" );
           
            // Insert column names to string array
            for ( int i = 1; i < ( columnCount + 1 ); i++ ) {
                JToken columnTokens = input.SelectToken( "columnNames" );
                table.Columns.Add( columnTokens[i - 1].ToString() );
                System.Console.Write( "." );
            }
            System.Console.WriteLine( "done." );
            
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
                System.Console.WriteLine( "Row data not found." );
            }
            return tempTable;
        }

        // Http get string from url
        private static async Task<string> GetDataAsync(string url) {

            var result = "";

            using ( var httpClient = new HttpClient() ) {

                result = await httpClient.GetStringAsync( url );   

            }
            return result;
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
