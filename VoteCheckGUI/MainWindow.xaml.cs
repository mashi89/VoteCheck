using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

#nullable enable

namespace VoteCheckGUI {
    public partial class MainWindow : Window {

        private DataTable? oldDataTable = null;
        private DataTable? newDataTable = null;
        private string? dgStatus = null;
        private string? oldDgStatus = null;

        private readonly List<string> _breadcrumb = new();
        private List<string>? _oldBreadcrumb = null;

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

            SetBusy( true );
            try { await FindBySurnameInternalAsync(); } finally { SetBusy( false ); }
        }

        private async Task FindBySurnameInternalAsync() {
            string inputName = tbSurname.Text.Trim();
            if ( inputName.Length == 0 ) return;
            inputName = char.ToUpper( inputName[0] ) + inputName.Substring( 1 );

            string dateFilter = tbDate.Text?.Trim() ?? "";
            MaSHi.Logger.Info( $"[UI] FindBySurname: {inputName} dateFilter={( dateFilter.Length > 0 ? dateFilter : "(none)" )}" );
            dataGrid.ItemsSource = null;

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

            // Filter by date prefix if the date field is filled in.
            // AanestysAlkuaika is enriched from SaliDBAanestys and starts with yyyy-MM-dd.
            if ( dateFilter.Length > 0 && result.Columns.Contains( "AanestysAlkuaika" ) ) {
                var matching = result.AsEnumerable()
                    .Where( r => r["AanestysAlkuaika"]?.ToString()?.StartsWith( dateFilter ) == true )
                    .ToList();
                result = matching.Count > 0 ? matching.CopyToDataTable() : result.Clone();
                MaSHi.Logger.Info( $"[UI] Date filter '{dateFilter}' applied, {matching.Count} rows remain" );
            }

            SetBreadcrumb( $"Sukunimihaku: {inputName}" + ( dateFilter.Length > 0 ? $" / {dateFilter}" : "" ) );
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

            SetBusy( true );
            try { await FindByDateInternalAsync(); } finally { SetBusy( false ); }
        }

        private async Task FindByDateInternalAsync() {
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

            MaSHi.Logger.Info( $"[UI] FindByDate: {inputDate}" );
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

            SetBreadcrumb( $"Päivähaku: {inputDate}" );
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
            SetBusy( true );
            try { await FindCurrentMPsInternalAsync(); } finally { SetBusy( false ); }
        }

        private async Task FindCurrentMPsInternalAsync() {
            MaSHi.Logger.Info( "[UI] FindCurrentMPs" );
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

            SetBreadcrumb( "Kansanedustajat" );
            ShowData( result, "Kansanedustajat", sortColumnIndex: 2, sortDirection: ListSortDirection.Ascending );
        }

        // ── Party distribution (drill-down on row double-click) ─────────────

        private async void dataGrid_DoubleTapped( object? sender, TappedEventArgs e ) {
            if ( newDataTable == null ) return;
            SetBusy( true );
            try { await DrillDownAsync(); } finally { SetBusy( false ); }
        }

        private async Task DrillDownAsync() {
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
                string partyLabel = "";
                if ( newDataTable.Columns.Contains( "Ryhmä" ) ) {
                    string ryhma = row["Ryhmä"]?.ToString()?.Trim() ?? "";
                    if ( !MaSHi.OpenDataRetriever.PartyNameToAbbreviation.TryGetValue( ryhma, out string? mapped ) )
                        partyAbbrev = ryhma; // fallback: value is already an abbreviation
                    else
                        partyAbbrev = mapped;
                    partyLabel = partyAbbrev?.ToUpper() ?? ryhma;
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

                MaSHi.Logger.Info( $"[UI] DrillDown → Edustajahaku votingId={votingId} party={partyLabel}" );
                PushBreadcrumb( partyLabel );
                ShowData( result, "Edustajahaku", sortColumnIndex: 3, sortDirection: ListSortDirection.Ascending );
            } else {
                string votingLabel = RowLabel( row );

                try {
                    result = await Task.Run( () => MaSHi.OpenDataRetriever.GetPartyDistData(
                        votingId, !isSwedish, "AanestysId" ) );
                } catch ( Exception ex ) {
                    await ShowAlert( "Error during search", ex.Message );
                    return;
                }

                RenameColumn( result, "Ryhma", "Ryhmä" );

                MaSHi.Logger.Info( $"[UI] DrillDown → Puoluejakaumahaku votingId={votingId} topic=\"{votingLabel}\"" );
                PushBreadcrumb( votingLabel );
                ShowData( result, "Puoluejakaumahaku", sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
            }
        }

        // ── Column header sorting ────────────────────────────────────────────

        private void dataGrid_Sorting( object? sender, DataGridColumnEventArgs e ) {
            if ( newDataTable == null || string.IsNullOrEmpty( e.Column.SortMemberPath ) ) return;

            string colName = e.Column.SortMemberPath;

            // Toggle: if DataView is already sorted ASC on this column, switch to DESC.
            string currentSort = newDataTable.DefaultView.Sort;
            bool ascending = !string.Equals( currentSort, $"[{colName}] ASC",
                                 StringComparison.OrdinalIgnoreCase );

            MaSHi.Logger.Info( $"[UI] Sorting column={colName} ascending={ascending}" );

            newDataTable.DefaultView.Sort = $"[{colName}] {( ascending ? "ASC" : "DESC" )}";

            // Avalonia doesn't react to DataView.ListChanged for sort updates on DataRowView
            // sources, so explicitly re-bind to make the reordered rows appear.
            dataGrid.ItemsSource = null;
            dataGrid.ItemsSource = newDataTable.DefaultView;

            // Don't set e.Handled — let Avalonia update the column header sort indicator.
        }

        // ── Back button ─────────────────────────────────────────────────────

        private void btnBack_Click( object? sender, RoutedEventArgs e ) {
            MaSHi.Logger.Info( $"[UI] Back: {dgStatus} → {oldDgStatus}" );
            if ( oldDataTable == null ) return;

            var temp = oldDataTable;
            oldDataTable = newDataTable;
            newDataTable = temp;

            var tempStatus = oldDgStatus;
            oldDgStatus = dgStatus;
            dgStatus = tempStatus;

            Title = "VoteCheck (with Avalonia) - " + dgStatus;
            PopBreadcrumb();

            // newDataTable is the original oldDataTable, which was verified non-null at the start of this method.
            ApplyDataSource( newDataTable!, sortColumnIndex: 1, sortDirection: ListSortDirection.Descending );
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

            // Manually create a column for each DataTable column using template columns.
            // Avalonia's AutoGenerateColumns reflects on DataRowView's own CLR properties
            // (DataView, Item, Row, RowVersion…) instead of the actual DataTable columns, and
            // DataGridTextColumn bindings do not resolve DataRowView indexers in Avalonia,
            // so we use DataGridTemplateColumn with FuncDataTemplate for all columns.
            foreach ( DataColumn col in table.Columns ) {
                string name = col.ColumnName;

                // Skip hidden helper columns
                if ( name == ColJaaBold || name == ColEiBold ) continue;

                // Skip redundant date column when the more specific timestamp column is present
                if ( name == "IstuntoPvm" && table.Columns.Contains( "AanestysAlkuaika" ) ) continue;

                // Skip parliamentary journal reference (not useful in the grid)
                if ( name == "PJOtsikko" ) continue;

                DataGridColumn column;

                // Winning-vote columns use a template that bolds the larger value
                if ( ( name == "Jaa" && table.Columns.Contains( ColJaaBold ) ) ||
                     ( name == "Ei"  && table.Columns.Contains( ColEiBold  ) ) ) {
                    string helperCol = name == "Jaa" ? ColJaaBold : ColEiBold;
                    column = new DataGridTemplateColumn {
                        Header             = name,
                        Width              = new DataGridLength( 50 ),
                        SortMemberPath     = name,
                        CustomSortComparer = new DataRowViewComparer( name ),
                        CellTemplate       = CreateBoldTemplate( name, helperCol )
                    };
                } else {
                    var templateCol = new DataGridTemplateColumn {
                        Header             = name,
                        SortMemberPath     = name,
                        CustomSortComparer = new DataRowViewComparer( name ),
                        CellTemplate       = CreateTextTemplate( name )
                    };
                    ApplyColumnWidth( templateCol, name );
                    column = templateCol;
                }

                dataGrid.Columns.Add( column );
            }

            dataGrid.ItemsSource = table.DefaultView;
        }

        // Applies per-column widths sized to fit all columns inside the window without horizontal scrolling.
        // Grid area ≈ 1185 px (window 1450 − left panel 265).
        private static void ApplyColumnWidth( DataGridColumn col, string name ) {
            if ( ColumnSizingHelper.GetSizing( name ) == ColumnSizing.Star ) {
                col.Width    = new DataGridLength( 1, DataGridLengthUnitType.Star );
                col.MinWidth = name switch {
                    "Kohta"     => 100,
                    "Ryhmä"     => 150,
                    _           => 110,
                };
                return;
            }

            switch ( name ) {

                // ── date / time columns ──
                case "IstuntoPvm":
                case "AanestysAlkuaika":
                    col.Width = new DataGridLength( 135 ); break;

                // ── vote-count columns (date view & party dist view) ──
                case "Jaa":
                case "Ei":
                case "Tyhjä":
                case "Poissa":
                case "JaaLkm":
                case "EiLkm":
                case "TyhjaLkm":
                case "PoissaLkm":
                    col.Width = new DataGridLength( 50 ); break;

                // ── short identifier / code columns ──
                case "AanestysId":
                case "EdustajaId":
                    col.Width = new DataGridLength( 65 ); break;

                case "EdustajaHenkiloNumero":
                    col.Width = new DataGridLength( 60 ); break;

                case "AanestysMitatoity":
                    col.Width = new DataGridLength( 80 ); break;

                case "Puolue":
                case "Ryhmalyhenne":
                    col.Width = new DataGridLength( 55 ); break;

                case "Ministeri":
                    col.Width = new DataGridLength( 65 ); break;

                case "Paikka":
                    col.Width = new DataGridLength( 55 ); break;

                case "Ääni":
                    col.Width = new DataGridLength( 55 ); break;

                // ── name columns (surname view & MP view) ──
                case "Etunimi":
                    col.Width = new DataGridLength( 90 ); break;

                case "Sukunimi":
                    col.Width = new DataGridLength( 100 ); break;
            }
        }

        // Creates a plain cell template that renders the column value as text.
        private static FuncDataTemplate<DataRowView?> CreateTextTemplate( string colName ) {
            return new FuncDataTemplate<DataRowView?>( ( row, _ ) =>
                new TextBlock {
                    Text              = row?[colName]?.ToString() ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness( 4, 0, 4, 0 )
                } );
        }

        // Creates a cell template that renders the value in bold when the helper boolean column is true.
        // <param name="colName">The data column whose value is displayed in the cell.</param>
        // <param name="helperCol">The hidden boolean column that controls bold formatting (true = bold).</param>
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

        private void SetBusy( bool busy ) {
            progressBar.IsVisible = busy;
            btnFindDate.IsEnabled    = !busy;
            btnFindSurname.IsEnabled = !busy;
            btnCurrentMPs.IsEnabled  = !busy;
            btnBack.IsEnabled        = !busy;
        }

        private static string RowLabel( DataRow row ) {
            foreach ( string col in new[] { "Äänestysaihe", "Kohta", "Pääkohta", "Käsittely" } ) {
                string val = row.Table.Columns.Contains( col )
                    ? row[col]?.ToString()?.Trim() ?? ""
                    : "";
                if ( val.Length > 0 ) {
                    return val.Length > 60 ? val[..57] + "…" : val;
                }
            }
            return row["AanestysId"]?.ToString() ?? "?";
        }

        private void SetBreadcrumb( params string[] items ) {
            _oldBreadcrumb = new List<string>( _breadcrumb );
            _breadcrumb.Clear();
            _breadcrumb.AddRange( items );
            lblBreadcrumb.Text = string.Join( " › ", _breadcrumb );
        }

        private void PushBreadcrumb( string item ) {
            _oldBreadcrumb = new List<string>( _breadcrumb );
            _breadcrumb.Add( item );
            lblBreadcrumb.Text = string.Join( " › ", _breadcrumb );
        }

        private void PopBreadcrumb() {
            if ( _oldBreadcrumb != null ) {
                _breadcrumb.Clear();
                _breadcrumb.AddRange( _oldBreadcrumb );
                _oldBreadcrumb = null;
            }
            lblBreadcrumb.Text = _breadcrumb.Count > 0 ? string.Join( " › ", _breadcrumb ) : "—";
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
            MaSHi.Logger.Error( $"[UI] Alert shown — {title}: {message}" );
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

        // Compares two DataRowView objects by a named column value.
        // Direction-agnostic (always ascending); Avalonia's DataGrid inverts the result
        // automatically when the user has selected descending sort.
        //
        // This is required because DataGridTemplateColumn has no CLR binding path from
        // which Avalonia can derive a PropertyDescriptor for DataRowView.  Setting
        // CustomSortComparer on the column causes DataGridColumn.GetSortDescription() to
        // produce a DataGridSortDescription.FromComparer(...) instead of FromPath(...).
        // FromPath silently fails for DataRowView items (no type-level CLR properties),
        // whereas FromComparer works correctly.
        private sealed class DataRowViewComparer : IComparer {
            private readonly string _columnName;
            public DataRowViewComparer( string columnName ) => _columnName = columnName;
            public int Compare( object? x, object? y ) {
                object? a = (x as DataRowView)?[_columnName];
                object? b = (y as DataRowView)?[_columnName];
                // Normalize database-null to null so Comparer.DefaultInvariant doesn't throw
                if ( a is DBNull ) a = null;
                if ( b is DBNull ) b = null;
                if ( a == null && b == null ) return 0;
                if ( a == null ) return -1;
                if ( b == null ) return  1;
                return Comparer.DefaultInvariant.Compare( a, b );
            }
        }
    }
}

