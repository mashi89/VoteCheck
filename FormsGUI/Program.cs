using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaSHi;

namespace FormsGUI {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// 

        [STAThread]
        static void Main() {

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault( false );
            Application.Run( new GUI_MPVotes() );
            //Application.Run( new GUI_VoteDistribution() );
            //Application.Run( new GUI_FindSubject() );
        }
    }
}
