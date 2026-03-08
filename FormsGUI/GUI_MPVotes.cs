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
    public partial class GUI_MPVotes : Form {

        DataTable oldDataTable = null;
        DataTable newDataTable = null;
        string dgStatus = null;

        public GUI_MPVotes() {
            InitializeComponent();
        }

        private void label1_Click( object sender, EventArgs e ) {

        }

        private void btnFind_Click( object sender, EventArgs e ) {
            // Initialization. empty grid and handle lowercase
            dataGridView1.DataSource = null;
            bool searchFailure = false;

            // Get name from textbox
            string inputName = tbSurname.Text.First().ToString().ToUpper() + tbSurname.Text.Substring(1);

            // Prepare threaded task
            var t = Task.Run(() => MaSHi.OpenDataRetriever.GetCombinedData( inputName, !cbSwedish.Checked, (int)numericUpDown1.Value*2, "EdustajaSukunimi") );

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
            
            // If getting the data was not a failure, show it on the dataGridView
            if( !searchFailure )
            {
                // Beautify
                t.Result.Columns["EdustajaEtunimi"].ColumnName = "Etunimi";
                t.Result.Columns["EdustajaSukunimi"].ColumnName = "Sukunimi";
                t.Result.Columns["EdustajaRyhmaLyhenne"].ColumnName = "Puolue";
                t.Result.Columns["EdustajaAanestys"].ColumnName = "Ääni";
                t.Result.Columns["KohtaKasittelyOtsikko"].ColumnName = "Käsittely";
                t.Result.Columns["PaaKohtaOtsikko"].ColumnName = "Pääkohta";
                t.Result.Columns["KohtaOtsikko"].ColumnName = "Kohta";
                t.Result.Columns["AanestysOtsikko"].ColumnName = "Äänestysaihe";

                oldDataTable = newDataTable;
                newDataTable = t.Result;

                // Bring results to dataGridView
                dgStatus = "Sukunimihaku";
                dataGridView1.DataSource = newDataTable;
                dataGridView1.AutoResizeColumns();
                dataGridView1.Sort(dataGridView1.Columns[1], ListSortDirection.Descending);
            }
        }

        private void cbSwedish_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            bool searchFailure = false;
            // Fetch selected cell
            var selectedCell = dataGridView1.SelectedCells[0];

            // Get the column number of "AanestysId" column
            int columnNbr = newDataTable.Columns.IndexOf("AanestysId");

            // Fetch the voting ID
            var votingId = dataGridView1[columnNbr, selectedCell.RowIndex].FormattedValue;
            string inputName = votingId.ToString();

            // Prepare threaded task
            var t = Task.Run(() => MaSHi.OpenDataRetriever.GetPartyDistData(inputName, !cbSwedish.Checked, "AanestysId"));

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

            if (!searchFailure)
            {
                oldDataTable = newDataTable;
                newDataTable = t.Result;

                // Bring results to dataGridView
                dgStatus = "Puoluejakaumahaku";
                dataGridView1.DataSource = newDataTable;
                dataGridView1.AutoResizeColumns();
                dataGridView1.Sort(dataGridView1.Columns[1], ListSortDirection.Descending);
            }
        }

        private void dataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (dgStatus != "Puoluejakaumahaku") return;
            if (dataGridView1.SelectedCells.Count == 0) return;

            bool searchFailure = false;
            var selectedCell = dataGridView1.SelectedCells[0];

            int columnNbr = newDataTable.Columns.IndexOf("AanestysId");
            var votingId = dataGridView1[columnNbr, selectedCell.RowIndex].FormattedValue;
            string inputName = votingId.ToString();

            var t = Task.Run(() => MaSHi.OpenDataRetriever.GetEdustajaData(inputName, !cbSwedish.Checked));

            try
            {
                t.Wait();
            }
            catch (Exception ex)
            {
                searchFailure = true;
                MessageBox.Show(ex.Message, "Error during search", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (!searchFailure)
            {
                oldDataTable = newDataTable;
                newDataTable = t.Result;

                dgStatus = "Edustajahaku";
                dataGridView1.DataSource = newDataTable;
                dataGridView1.AutoResizeColumns();
                dataGridView1.Sort(dataGridView1.Columns[3], ListSortDirection.Ascending);
            }
        }

        private void btnBack_MouseClick(object sender, MouseEventArgs e)        {
            if ( oldDataTable != null )
            {
                var tempTable = oldDataTable;
                oldDataTable = newDataTable;
                newDataTable = tempTable;

                // Bring results to dataGridView
                dataGridView1.DataSource = newDataTable;
                dataGridView1.AutoResizeColumns();
                dataGridView1.Sort(dataGridView1.Columns[1], ListSortDirection.Descending);
            }            
        }

        private void btnFindYear_Click(object sender, EventArgs e)
        {
            // Initialization. empty grid and handle lowercase
            dataGridView1.DataSource = null;
            bool searchFailure = false;

            // Get name from textbox
            string inputName = tbYear.Text;

            // Prepare threaded task
            var t = Task.Run(() => MaSHi.OpenDataRetriever.GetVotingData(inputName, !cbSwedish.Checked, (int)numericUpDown1.Value * 2, "IstuntoVPVuosi"));

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

            // If getting the data was not a failure, show it on the dataGridView
            if (!searchFailure && t.Result != null)
            {
                // Beautify
                t.Result.Columns["AanestysTulosJaa"].ColumnName = "Jaa";
                t.Result.Columns["AanestysTulosEi"].ColumnName = "Ei";
                t.Result.Columns["AanestysTulosTyhjia"].ColumnName = "Tyhjiä";
                t.Result.Columns["AanestysTulosPoissa"].ColumnName = "Poissa";
                //t.Result.Columns["EdustajaRyhmaLyhenne"].ColumnName = "Puolue";
                //t.Result.Columns["EdustajaAanestys"].ColumnName = "Ääni";
                //t.Result.Columns["KohtaKasittelyOtsikko"].ColumnName = "Käsittely";
                //t.Result.Columns["PaaKohtaOtsikko"].ColumnName = "Pääkohta";
                t.Result.Columns["KohtaOtsikko"].ColumnName = "Kohta";
                t.Result.Columns["AanestysOtsikko"].ColumnName = "Äänestysaihe";

                oldDataTable = newDataTable;
                newDataTable = t.Result;

                // Bring results to dataGridView
                dgStatus = "Vuosihaku";
                dataGridView1.DataSource = newDataTable;
                dataGridView1.AutoResizeColumns();
                dataGridView1.Columns["Kohta"].Width = 500;
                dataGridView1.Columns["Äänestysaihe"].Width = 200;
                dataGridView1.Sort(dataGridView1.Columns[0], ListSortDirection.Descending);

                // Bold winning vote
                var jaaIndex = dataGridView1.Columns["Jaa"].Index;
                var eiIndex = dataGridView1.Columns["Ei"].Index;
                var styleBold = new DataGridViewCellStyle();
                styleBold.Font = new Font(DataGridView.DefaultFont, FontStyle.Bold);
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    if (row.DataBoundItem != null)
                    {
                        int jaaVotes = 0;
                        int eiVotes = 0;

                        Int32.TryParse(row.Cells[jaaIndex].Value.ToString().Trim(), out jaaVotes);
                        Int32.TryParse(row.Cells[eiIndex].Value.ToString().Trim(), out eiVotes);


                        if (jaaVotes > eiVotes)
                        {
                            row.Cells[jaaIndex].Style = styleBold;
                        }
                        else
                        {
                            row.Cells[eiIndex].Style = styleBold;
                        }
                    }                
                        
                }
            }
        }

        private void GUI_MPVotes_Load(object sender, EventArgs e)
        {

        }

        private void dataGridView1_DataSourceChanged(object sender, EventArgs e)
        {
            this.Text = "VoteCheck (with Forms) - " + dgStatus;
            if (MaSHi.OpenDataRetriever.hasMore)
            {
                lblHasMore.Visible = true;
            } else {
                lblHasMore.Visible = false;
            }
        }

        private void GUI_MPVotes_Validated(object sender, EventArgs e)
        {
            
        }
    }
}
