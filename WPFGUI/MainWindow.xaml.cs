using System;
using System.ComponentModel;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

#nullable enable

namespace WPFGUI {
    public partial class MainWindow : Window {

        private DataTable? oldDataTable = null;
        private DataTable? newDataTable = null;
        private string? dgStatus = null;
        private string? oldDgStatus = null;

        // Column names that contain bold-winner info (hidden helper columns)
        private const string ColJaaBold = "_JaaBold";
        private const string ColEiBold  = "_EiBold";

        public MainWindow() {
            InitializeComponent();

            // Restrict tbQueryCount to digits only
            tbQueryCount.AddHandler(
                InputElement.TextInputEvent,
                new EventHandler<TextInputEventArgs>( tbQueryCount_TextInput ),
                handledEventsToo: false );
        }

        // ── Surname search ──────────────────────────────────────────────────

        private async void btnFindSurname_Click( object? sender, RoutedEventArgs e ) {
            await FindBySurnameAsync();
        }

        private async void tbSurname_KeyDown( object? sender, KeyEventArgs e ) {
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
                await ShowAlert( "Error during search", ex.Message );
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

        private async void btnFindDate_Click( object? sender, RoutedEventArgs e ) {
            await FindByDateAsync();
        }

        private async void tbDate_KeyDown( object? sender, KeyEventArgs e ) {
            if ( e.Key == Key.Return ) await FindByDateAsync();
        }

        private void btnToday_Click( object? sender, RoutedEventArgs e ) {
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
                await ShowAlert( "Invalid date",
                    "Enter a year (e.g. 2024), year and month (e.g. 2024-03), or full date (e.g. 2024-03-10)." );
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
                await ShowAlert( "Error during search", ex.Message );
                return;
            }

            if ( result == null ) return;

            RenameColumn( result, "AanestysTulosJaa",     "Jaa" );
            RenameColumn( result, "AanestysTulosEi",      "Ei" );
            RenameColumn( result, "AanestysTulosTyhjiä",  "Tyhjä" );
            RenameColumn( result, "AanestysTulosTyhjia",  "Tyhjä" );
            RenameColumn( result, "AanestysTulosPoissa",  "Poissa" );
            RenameColumn( result, "KohtaOtsikko",         "Kohta" );
            RenameColumn( result, "AanestysOtsikko",      "Äänestysaihe" );

            MarkWinningVotes( result );

            ShowData( result, "Päivähaku",
                // After column cleanup in GetVotingData, index 1 = AanestysAlkuaika (used as query and sort column).
                sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
        }

        // Add hidden boolean helper columns so template columns can bold the winning vote.
        // Ties do not bold either column.
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

        private async void btnCurrentMPs_Click( object? sender, RoutedEventArgs e ) {
            await FindCurrentMPsAsync();
        }

        private async Task FindCurrentMPsAsync() {
            dataGrid.ItemsSource = null;

            DataTable? result = null;
            try {
                result = await Task.Run( () => MaSHi.OpenDataRetriever.GetCurrentMPs() );
            } catch ( Exception ex ) {
                await ShowAlert( "Error during search", ex.Message );
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

        private async void dataGrid_DoubleTapped( object? sender, TappedEventArgs e ) {
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
                    await ShowAlert( "Error during search", ex.Message );
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
                    await ShowAlert( "Error during search", ex.Message );
                    return;
                }

                RenameColumn( result, "Ryhma", "Ryhmä" );

                ShowData( result, "Puoluejakaumahaku", sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
            }
        }

        // ── Back button ─────────────────────────────────────────────────────

        private void btnBack_Click( object? sender, RoutedEventArgs e ) {
            if ( oldDataTable == null ) return;

            var temp = oldDataTable;
            oldDataTable = newDataTable;
            newDataTable = temp;

            var tempStatus = oldDgStatus;
            oldDgStatus = dgStatus;
            dgStatus = tempStatus;

            Title = "VoteCheck (with Avalonia) - " + dgStatus;

            // After the swap, newDataTable == original oldDataTable which was verified non-null above.
            if ( newDataTable == null ) return;
            ApplyDataSource( newDataTable, sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private void ShowData( DataTable table, string status, int sortColumnIndex, ListSortDirection sortDirection ) {
            oldDataTable = newDataTable;
            newDataTable = table;
            oldDgStatus = dgStatus;
            dgStatus = status;

            Title = "VoteCheck (with Avalonia) - " + dgStatus;
            lblHasMore.IsVisible = MaSHi.OpenDataRetriever.hasMore;

            ApplyDataSource( table, sortColumnIndex, sortDirection );
        }

        private void ApplyDataSource( DataTable table, int sortColumnIndex, ListSortDirection sortDirection ) {
            // Sort the DataView directly
            if ( sortColumnIndex < table.Columns.Count ) {
                string colName = table.Columns[sortColumnIndex].ColumnName;
                string dir = sortDirection == ListSortDirection.Ascending ? "ASC" : "DESC";
                table.DefaultView.Sort = $"{colName} {dir}";
            }

            dataGrid.ItemsSource = null;
            dataGrid.Columns.Clear();
            dataGrid.ItemsSource = table.DefaultView;

            // Replace the auto-generated Jaa/Ei text columns with template columns that bold the winner.
            ReplaceWithBoldColumn( "Jaa", ColJaaBold, table );
            ReplaceWithBoldColumn( "Ei",  ColEiBold,  table );
        }

        // Find the auto-generated column by header, remove it, and insert a DataGridTemplateColumn
        // at the same position that renders the winning-vote value in bold.
        private void ReplaceWithBoldColumn( string colName, string helperCol, DataTable table ) {
            if ( !table.Columns.Contains( colName ) || !table.Columns.Contains( helperCol ) ) return;

            int idx = -1;
            for ( int i = 0; i < dataGrid.Columns.Count; i++ ) {
                if ( dataGrid.Columns[i].Header?.ToString() == colName ) { idx = i; break; }
            }
            if ( idx < 0 ) return;

            dataGrid.Columns.RemoveAt( idx );

            var templateCol = new DataGridTemplateColumn {
                Header         = colName,
                Width          = new DataGridLength( 50 ),
                SortMemberPath = colName,
                CellTemplate   = CreateBoldTemplate( colName, helperCol )
            };

            dataGrid.Columns.Insert( idx, templateCol );
        }

        // Creates a cell template that renders the value in bold when the helper boolean column is true.
        private static FuncDataTemplate<DataRowView?> CreateBoldTemplate( string colName, string helperCol ) {
            return new FuncDataTemplate<DataRowView?>( ( row, _ ) => {
                bool bold = row != null
                            && row.Row.Table.Columns.Contains( helperCol )
                            && row[helperCol] is bool b && b;
                return new TextBlock {
                    Text              = row?[colName]?.ToString() ?? "",
                    FontWeight        = bold ? FontWeight.Bold : FontWeight.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness( 4, 0, 4, 0 )
                };
            } );
        }

        // Auto-generating column: hide helper columns and set column widths.
        private void dataGrid_AutoGeneratingColumn( object? sender, DataGridAutoGeneratingColumnEventArgs e ) {
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
                    e.Column.Width    = new DataGridLength( 1, DataGridLengthUnitType.SizeToCells );
                    e.Column.MaxWidth = 500;
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
        }

        private int GetQueryCount() {
            if ( int.TryParse( tbQueryCount.Text, out int val ) && val > 0 ) return val;
            return 50;
        }

        private static void RenameColumn( DataTable table, string oldName, string newName ) {
            if ( table.Columns.Contains( oldName ) )
                table.Columns[oldName]!.ColumnName = newName;
        }

        private async Task ShowAlert( string title, string message ) {
            var dialog = new Window {
                Title                   = title,
                SizeToContent           = SizeToContent.WidthAndHeight,
                MinWidth                = 350,
                CanResize               = false,
                WindowStartupLocation   = WindowStartupLocation.CenterOwner
            };

            var okBtn = new Button {
                Content             = "OK",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding             = new Thickness( 20, 4 )
            };

            dialog.Content = new StackPanel {
                Margin   = new Thickness( 24 ),
                Spacing  = 16,
                Children = {
                    new TextBlock {
                        Text         = message,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth     = 400
                    },
                    okBtn
                }
            };

            okBtn.Click += ( _, _ ) => dialog.Close();
            await dialog.ShowDialog( this );
        }

        // Allow only digits in the query-count textbox
        private static readonly Regex _digitsOnly = new Regex( @"^\d+$", RegexOptions.Compiled );
        private void tbQueryCount_TextInput( object? sender, TextInputEventArgs e ) {
            e.Handled = e.Text != null && !_digitsOnly.IsMatch( e.Text );
        }
    }
}

