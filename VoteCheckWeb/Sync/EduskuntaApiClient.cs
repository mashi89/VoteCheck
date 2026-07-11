using System.Text.Json;

namespace VoteCheckWeb.Sync;

public sealed record TablePage( bool HasMore, IReadOnlyList<Dictionary<string, string>> Rows );

// Thin async client for the avoindata.eduskunta.fi tables/rows endpoint.
// Returns rows as column-name → value maps so callers never index by ordinal
// (the API may add or reorder columns).
public sealed class EduskuntaApiClient {

    private const string BaseUrl = "https://avoindata.eduskunta.fi/api/v1/tables/";
    private readonly HttpClient _http;

    public EduskuntaApiClient( HttpClient http ) {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd( "VoteCheckWeb/1.0" );
    }

    public async Task<TablePage> GetPageAsync(
        string table, int page, int perPage,
        string? columnName = null, string? columnValue = null,
        CancellationToken ct = default ) {

        var url = $"{BaseUrl}{Uri.EscapeDataString( table )}/rows?perPage={perPage}&page={page}";
        if ( columnName != null )
            url += $"&columnName={Uri.EscapeDataString( columnName )}&columnValue={Uri.EscapeDataString( columnValue ?? "" )}";

        using var stream = await _http.GetStreamAsync( url, ct );
        using var doc = await JsonDocument.ParseAsync( stream, cancellationToken: ct );
        var root = doc.RootElement;

        if ( root.TryGetProperty( "message", out var message ) )
            throw new InvalidOperationException( "API error: " + message.GetString() );

        var columns = root.GetProperty( "columnNames" )
            .EnumerateArray().Select( c => c.GetString()! ).ToArray();

        var rows = new List<Dictionary<string, string>>();
        foreach ( var rowData in root.GetProperty( "rowData" ).EnumerateArray() ) {
            var row = new Dictionary<string, string>( columns.Length );
            var i = 0;
            foreach ( var cell in rowData.EnumerateArray() )
                row[columns[i++]] = cell.GetString() ?? "";
            rows.Add( row );
        }

        return new TablePage( root.GetProperty( "hasMore" ).GetBoolean(), rows );
    }
}
