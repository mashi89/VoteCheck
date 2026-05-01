using System;
using System.IO;

namespace MaSHi {

    public static class Logger {

        private static readonly string _logDir = Path.Combine(
            Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ),
            "VoteCheck", "logs" );

        // Disable file output during unit tests to keep the log directory clean.
        public static bool FileSinkEnabled = true;

        public static void Info( string message )  => Write( "INFO ", message, null );
        public static void Warn( string message )  => Write( "WARN ", message, null );
        public static void Error( string message, Exception? ex = null ) => Write( "ERROR", message, ex );

        private static void Write( string level, string message, Exception? ex ) {
            string timestamp = DateTime.Now.ToString( "yyyy-MM-dd HH:mm:ss.fff" );
            string line = $"[{timestamp}] [{level}] {message}";
            if ( ex != null )
                line += $"\n          {ex.GetType().Name}: {ex.Message}\n          {ex.StackTrace?.Replace( "\n", "\n          " )}";

            System.Diagnostics.Trace.WriteLine( line );

            if ( !FileSinkEnabled ) return;
            try {
                Directory.CreateDirectory( _logDir );
                string file = Path.Combine( _logDir, $"votecheck-{DateTime.Now:yyyy-MM-dd}.log" );
                File.AppendAllText( file, line + "\n" );
            } catch {
                // Never let logging failures crash the app.
            }
        }
    }
}
