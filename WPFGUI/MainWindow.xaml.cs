using System;
using System.ComponentModel;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;

#nullable enable

namespace WPFGUI {
    public partial class MainWindow : Window {

        private DataTable? oldDataTable = null;
        private DataTable? newDataTable = null;
        private string? dgStatus = null;
        private string? oldDgStatus = null;

        // Column names that contain bold-winner info (hidden helper columns)
        private const string ColJaaBold = "_JaaBold";
        private const string ColEiBold = "_EiBold";

        public MainWindow() {
            InitializeComponent();
        }

        // ── Surname search ──────────────────────────────────────────────────

        private async void btnFindSurname_Click( object sender, RoutedEventArgs e ) {
            await FindBySurnameAsync();
        }

        private async void tbSurname_KeyDown( object sender, KeyEventArgs e ) {
            if ( e.Key == Key.Return ) await FindBySurnameAsync();
        }

        private async Task FindBySurnameAsync() {
            if ( string.IsNullOrWhiteSpace( tbSurname.Text ) ) return;

            dataGrid.ItemsSource = null;

            string inputName = tbSurname.Text.Trim();
            if ( inputName.Length == 0 ) return;
            inputName = char.ToUpper( inputName[0] ) + inputName.Substring( 1 );

            int queryCount = GetQueryCount();
            bool isSwedish = cbSwedish.IsChecked.GetValueOrDefault();
            DataTable? result = null;
            try {
                result = await Task.Run( () => MaSHi.OpenDataRetriever.GetCombinedData(
                    inputName, !isSwedish, queryCount * 2, "EdustajaSukunimi" ) );
            } catch ( Exception ex ) {
                MessageBox.Show( ex.Message, "Error during search", MessageBoxButton.OK, MessageBoxImage.Error );
                return;
            }

            RenameColumn( result, "EdustajaEtunimi",        "Etunimi" );
            RenameColumn( result, "EdustajaSukunimi",       "Sukunimi" );
            RenameColumn( result, "EdustajaRyhmaLyhenne",   "Puolue" );
            RenameColumn( result, "EdustajaAanestys",       "Ääni" );
            RenameColumn( result, "KohtaKasittelyOtsikko",  "Käsittely" );
            RenameColumn( result, "PaaKohtaOtsikko",        "Pääkohta" );
            RenameColumn( result, "KohtaOtsikko",           "Kohta" );
            RenameColumn( result, "AanestysOtsikko",        "Äänestysaihe" );

            ShowData( result, "Sukunimihaku", sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
        }

        // ── Date search ─────────────────────────────────────────────────────

        private async void btnFindDate_Click( object sender, RoutedEventArgs e ) {
            await FindByDateAsync();
        }

        private async void tbDate_KeyDown( object sender, KeyEventArgs e ) {
            if ( e.Key == Key.Return ) await FindByDateAsync();
        }

        private void btnToday_Click( object sender, RoutedEventArgs e ) {
            tbDate.Text = DateTime.Today.ToString( "yyyy-MM-dd" );
        }

        private async Task FindByDateAsync() {
            if ( string.IsNullOrWhiteSpace( tbDate.Text ) ) return;

            string inputDate = tbDate.Text.Trim();

            // Validate that the input is a parseable date prefix: yyyy, yyyy-MM, or yyyy-MM-dd.
            if ( !DateTime.TryParseExact( inputDate,
                     new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy" },
                     System.Globalization.CultureInfo.InvariantCulture,
                     System.Globalization.DateTimeStyles.None, out _ ) ) {
                MessageBox.Show( "Enter a year (e.g. 2024), year and month (e.g. 2024-03), or full date (e.g. 2024-03-10).",
                    "Invalid date", MessageBoxButton.OK, MessageBoxImage.Warning );
                return;
            }

            dataGrid.ItemsSource = null;

            int queryCount = GetQueryCount();
            bool isSwedish = cbSwedish.IsChecked.GetValueOrDefault();

            DataTable? result = null;
            try {
                result = await Task.Run( () => MaSHi.OpenDataRetriever.GetVotingDataByDate(
                    inputDate, !isSwedish, queryCount * 2 ) );
            } catch ( Exception ex ) {
                MessageBox.Show( ex.Message, "Error during search", MessageBoxButton.OK, MessageBoxImage.Error );
                return;
            }

            if ( result == null ) return;

            RenameColumn( result, "AanestysTulosJaa",     "Jaa" );
            RenameColumn( result, "AanestysTulosEi",      "Ei" );
            RenameColumn( result, "AanestysTulosTyhjiä",  "Tyhjä" );
            RenameColumn( result, "AanestysTulosTyhjia",  "Tyhjä" );
            RenameColumn( result, "AanestysTulosPoissa",  "Poissa" );
            RenameColumn( result, "KohtaOtsikko",        "Kohta" );
            RenameColumn( result, "AanestysOtsikko",     "Äänestysaihe" );

            MarkWinningVotes( result );

            ShowData( result, "Päivähaku",
                // After column cleanup in GetVotingData, index 1 = AanestysAlkuaika (used as query and sort column).
                sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
        }

        // Add hidden boolean helper columns so CellStyle DataTriggers can make
        // the winning vote column bold.  Ties do not bold either column.
        private static void MarkWinningVotes( DataTable table ) {
            if ( !table.Columns.Contains( "Jaa" ) || !table.Columns.Contains( "Ei" ) ) return;

            table.Columns.Add( ColJaaBold, typeof( bool ) );
            table.Columns.Add( ColEiBold,  typeof( bool ) );

            foreach ( DataRow row in table.Rows ) {
                int jaa = 0, ei = 0;
                int.TryParse( row["Jaa"]?.ToString()?.Trim(), out jaa );
                int.TryParse( row["Ei"]?.ToString()?.Trim(),  out ei );
                row[ColJaaBold] = ( jaa > ei );
                row[ColEiBold]  = ( ei > jaa );
            }
        }

        // ── Current MPs ─────────────────────────────────────────────────────

        private async void btnCurrentMPs_Click( object sender, RoutedEventArgs e ) {
            await FindCurrentMPsAsync();
        }

        private async Task FindCurrentMPsAsync() {
            dataGrid.ItemsSource = null;

            DataTable? result = null;
            try {
                result = await Task.Run( () => MaSHi.OpenDataRetriever.GetCurrentMPs() );
            } catch ( Exception ex ) {
                MessageBox.Show( ex.Message, "Error during search", MessageBoxButton.OK, MessageBoxImage.Error );
                return;
            }

            RenameColumn( result, "hetekaId",    "HetekaId" );
            RenameColumn( result, "seatNumber",  "Paikka" );
            RenameColumn( result, "lastname",    "Sukunimi" );
            RenameColumn( result, "firstname",   "Etunimi" );
            RenameColumn( result, "party",       "Puolue" );
            RenameColumn( result, "minister",    "Ministeri" );

            ShowData( result, "Kansanedustajat", sortColumnIndex: 2, sortDirection: ListSortDirection.Ascending );
        }

        // ── Party distribution (drill-down on row double-click) ─────────────

        private async void dataGrid_MouseDoubleClick( object sender, MouseButtonEventArgs e ) {
            if ( newDataTable == null ) return;

            var row = ( dataGrid.SelectedItem as DataRowView )?.Row;
            if ( row == null ) return;

            if ( !newDataTable.Columns.Contains( "AanestysId" ) ) return;

            string? votingId = row["AanestysId"]?.ToString();
            if ( string.IsNullOrEmpty( votingId ) ) return;

            bool isSwedish = cbSwedish.IsChecked.GetValueOrDefault();
            DataTable? result = null;

            if ( dgStatus == "Puoluejakaumahaku" ) {
                string? partyAbbrev = null;
                if ( newDataTable.Columns.Contains( "Ryhmä" ) ) {
                    string ryhma = row["Ryhmä"]?.ToString()?.Trim() ?? "";
                    if ( !MaSHi.OpenDataRetriever.PartyNameToAbbreviation.TryGetValue( ryhma, out string? mapped ) )
                        partyAbbrev = ryhma; // fallback: value is already an abbreviation
                    else
                        partyAbbrev = mapped;
                }

                try {
                    result = await Task.Run( () => MaSHi.OpenDataRetriever.GetEdustajaData(
                        votingId, !isSwedish, partyAbbrev ) );
                } catch ( Exception ex ) {
                    MessageBox.Show( ex.Message, "Error during search", MessageBoxButton.OK, MessageBoxImage.Error );
                    return;
                }

                RenameColumn( result, "EdustajaEtunimi",      "Etunimi" );
                RenameColumn( result, "EdustajaSukunimi",     "Sukunimi" );
                RenameColumn( result, "EdustajaRyhmaLyhenne", "Puolue" );
                RenameColumn( result, "EdustajaAanestys",     "Ääni" );

                ShowData( result, "Edustajahaku", sortColumnIndex: 3, sortDirection: ListSortDirection.Ascending );
            } else {
                try {
                    result = await Task.Run( () => MaSHi.OpenDataRetriever.GetPartyDistData(
                        votingId, !isSwedish, "AanestysId" ) );
                } catch ( Exception ex ) {
                    MessageBox.Show( ex.Message, "Error during search", MessageBoxButton.OK, MessageBoxImage.Error );
                    return;
                }

                RenameColumn( result, "Ryhma", "Ryhmä" );

                ShowData( result, "Puoluejakaumahaku", sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
            }
        }

        // ── Back button ─────────────────────────────────────────────────────

        private void btnBack_Click( object sender, RoutedEventArgs e ) {
            if ( oldDataTable == null ) return;

            var temp = oldDataTable;
            oldDataTable = newDataTable;
            newDataTable = temp;

            var tempStatus = oldDgStatus;
            oldDgStatus = dgStatus;
            dgStatus = tempStatus;

            Title = "VoteCheck (with WPF) - " + dgStatus;

            ApplyDataSource( newDataTable, sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private void ShowData( DataTable table, string status, int sortColumnIndex, ListSortDirection sortDirection ) {
            oldDataTable = newDataTable;
            newDataTable = table;
            oldDgStatus = dgStatus;
            dgStatus = status;

            Title = "VoteCheck (with WPF) - " + dgStatus;
            lblHasMore.Visibility = MaSHi.OpenDataRetriever.hasMore ? Visibility.Visible : Visibility.Collapsed;

            ApplyDataSource( table, sortColumnIndex, sortDirection );
        }

        private void ApplyDataSource( DataTable table, int sortColumnIndex, ListSortDirection sortDirection ) {
            var view = table.DefaultView;
            view.Sort = "";

            dataGrid.ItemsSource = null;
            dataGrid.Columns.Clear();
            dataGrid.ItemsSource = view;

            // Apply sort if column index is valid
            if ( sortColumnIndex < table.Columns.Count ) {
                string colName = table.Columns[sortColumnIndex].ColumnName;
                var cv = CollectionViewSource.GetDefaultView( dataGrid.ItemsSource );
                cv.SortDescriptions.Clear();
                cv.SortDescriptions.Add( new SortDescription( colName,
                    sortDirection == ListSortDirection.Ascending
                        ? System.ComponentModel.ListSortDirection.Ascending
                        : System.ComponentModel.ListSortDirection.Descending ) );
            }
        }

        // Auto-generating column: hide helper columns, apply bold CellStyle to Jaa/Ei
        private void dataGrid_AutoGeneratingColumn( object sender, DataGridAutoGeneratingColumnEventArgs e ) {
            // Hide helper columns
            if ( e.PropertyName == ColJaaBold || e.PropertyName == ColEiBold ) {
                e.Cancel = true;
                return;
            }

            // Hide IstuntoPvm when AanestysAlkuaika is present (redundant date column)
            if ( e.PropertyName == "IstuntoPvm" &&
                 newDataTable != null && newDataTable.Columns.Contains( "AanestysAlkuaika" ) ) {
                e.Cancel = true;
                return;
            }

            // Hide PJOtsikko (parliamentary journal reference, not useful in the grid)
            if ( e.PropertyName == "PJOtsikko" ) {
                e.Cancel = true;
                return;
            }

            // Per-column widths sized to fit all columns inside the window without horizontal scrolling.
            // Grid area ≈ 1185 px (window 1450 − left panel 265).
            switch ( e.PropertyName ) {
                // ── long text columns (all views) ──
                case "Kohta":
                    e.Column.Width = new DataGridLength( 1, DataGridLengthUnitType.SizeToCells );
                    e.Column.MaxWidth = 500;
                    e.Column.CellStyle = new Style( typeof( DataGridCell ) ) {
                        Setters = {
                            new Setter( TextBlock.TextWrappingProperty, TextWrapping.Wrap )
                        }
                    };
                    break;

                case "Äänestysaihe":
                    e.Column.Width = new DataGridLength( 120 ); break;

                case "Ryhmä":           // party dist view full party name
                    e.Column.Width = new DataGridLength( 1, DataGridLengthUnitType.SizeToCells ); break;

                case "Käsittely":
                case "Pääkohta":
                    e.Column.Width = new DataGridLength( 110 ); break;

                // ── date / time columns ──
                case "IstuntoPvm":
                case "AanestysAlkuaika":
                    e.Column.Width = new DataGridLength( 135 ); break;

                // ── vote-count columns (date view & party dist view) ──
                case "Jaa":
                case "Ei":
                case "Tyhjä":
                case "Poissa":
                case "JaaLkm":
                case "EiLkm":
                case "TyhjaLkm":
                case "PoissaLkm":
                    e.Column.Width = new DataGridLength( 50 ); break;

                // ── short identifier / code columns ──
                case "AanestysId":
                case "EdustajaId":
                    e.Column.Width = new DataGridLength( 65 ); break;

                case "EdustajaHenkiloNumero":
                    e.Column.Width = new DataGridLength( 60 ); break;

                case "AanestysMitatoity":
                    e.Column.Width = new DataGridLength( 80 ); break;

                case "Puolue":
                case "Ryhmalyhenne":
                    e.Column.Width = new DataGridLength( 55 ); break;

                case "Ministeri":
                    e.Column.Width = new DataGridLength( 65 ); break;

                case "Paikka":
                    e.Column.Width = new DataGridLength( 55 ); break;

                case "Ääni":
                    e.Column.Width = new DataGridLength( 55 ); break;

                // ── name columns (surname view & MP view) ──
                case "Etunimi":
                    e.Column.Width = new DataGridLength( 90 ); break;

                case "Sukunimi":
                    e.Column.Width = new DataGridLength( 100 ); break;
            }

            // Bold winning-vote columns only when the helper columns exist in the table
            if ( e.PropertyName == "Jaa" || e.PropertyName == "Ei" ) {
                string helperCol = ( e.PropertyName == "Jaa" ) ? ColJaaBold : ColEiBold;

                if ( newDataTable != null && newDataTable.Columns.Contains( helperCol ) ) {
                    var style = new Style( typeof( DataGridCell ) );
                    var trigger = new DataTrigger {
                        Binding = new Binding( "[" + helperCol + "]" ),
                        Value   = true
                    };
                    trigger.Setters.Add( new Setter( FontWeightProperty, FontWeights.Bold ) );
                    style.Triggers.Add( trigger );
                    e.Column.CellStyle = style;
                }
            }
        }

        private int GetQueryCount() {
            if ( int.TryParse( tbQueryCount.Text, out int val ) && val > 0 ) return val;
            return 50;
        }

        private static void RenameColumn( DataTable table, string oldName, string newName ) {
            if ( table.Columns.Contains( oldName ) )
                table.Columns[oldName]!.ColumnName = newName;
        }

        // Allow only digits in the query-count textbox
        private static readonly Regex _digitsOnly = new Regex( @"^\d+$", RegexOptions.Compiled );
        private void tbQueryCount_PreviewTextInput( object sender, TextCompositionEventArgs e ) {
            e.Handled = !_digitsOnly.IsMatch( e.Text );
        }
    }
}
