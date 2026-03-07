using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FormsGUI {
    public partial class GUI_VoteDistribution : Form {
                
        public GUI_VoteDistribution() {
            InitializeComponent();
        }

        private void label1_Click( object sender, EventArgs e ) {

        }

        private void button1_Click( object sender, EventArgs e ) {
            // Initialization. empty grid and handle lowercase
            dataGridView1.DataSource = null;
            bool searchFailure = false;

            string inputName = textBox1.Text.First().ToString().ToUpper() + textBox1.Text.Substring(1);

            // Run threaded task
            var t = Task.Run(() => MaSHi.OpenDataRetriever.GetPartyDistData( inputName, !checkBox1.Checked, "EdustajaRyhmaLyhenne" ));

            try
            {
                t.Wait();
            }
            catch (Exception ex)
            {
                searchFailure = true;
                MessageBox.Show(ex.Message, "Error during search", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            
            if( !searchFailure )
            {
                // Bring results to dataGridView
                dataGridView1.DataSource = t.Result;
                dataGridView1.AutoResizeColumns();
                dataGridView1.Sort(dataGridView1.Columns[1], ListSortDirection.Descending);
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {

        }
    }
}
