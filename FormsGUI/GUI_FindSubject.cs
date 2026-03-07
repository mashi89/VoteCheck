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
    public partial class GUI_FindSubject : Form {
                
        public GUI_FindSubject() {
            InitializeComponent();
        }

        private void label1_Click( object sender, EventArgs e ) {

        }

        private void button1_Click( object sender, EventArgs e ) {
            // Initialization. empty grid and handle lowercase
            dataGridView1.DataSource = null;
            bool searchFailure = false;

            // Get name from textbox
            string inputName = textBox1.Text.First().ToString().ToUpper() + textBox1.Text.Substring(1);

            // Prepare threaded task
            var t = Task.Run(() => MaSHi.OpenDataRetriever.GetSubjectData( inputName, "AanestysId" ));

            try
            {
                // Run threaded task
                t.Wait();
            }
            catch (Exception ex)
            {
                searchFailure = true;
                MessageBox.Show(ex.Message, "Error during search", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            
            // If getting data was not a failure, show it on the dataGridView
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
