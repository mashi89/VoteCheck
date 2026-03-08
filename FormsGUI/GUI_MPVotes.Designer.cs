namespace FormsGUI {
    partial class GUI_MPVotes {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose( bool disposing ) {
            if ( disposing && ( components != null ) ) {
                components.Dispose();
            }
            base.Dispose( disposing );
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.btnFindSurname = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.tbSurname = new System.Windows.Forms.TextBox();
            this.cbSwedish = new System.Windows.Forms.CheckBox();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.btnBack = new System.Windows.Forms.Button();
            this.tbYear = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.btnFindYear = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.lblHasMore = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            this.SuspendLayout();
            // 
            // dataGridView1
            // 
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Location = new System.Drawing.Point(271, 12);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.Size = new System.Drawing.Size(1125, 587);
            this.dataGridView1.TabIndex = 0;
            this.dataGridView1.DataSourceChanged += new System.EventHandler(this.dataGridView1_DataSourceChanged);
            this.dataGridView1.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView1_CellContentClick);
            this.dataGridView1.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView1_CellDoubleClick);
            // 
            // btnFindSurname
            // 
            this.btnFindSurname.Location = new System.Drawing.Point(12, 302);
            this.btnFindSurname.Name = "btnFindSurname";
            this.btnFindSurname.Size = new System.Drawing.Size(75, 23);
            this.btnFindSurname.TabIndex = 1;
            this.btnFindSurname.Text = "Find";
            this.btnFindSurname.UseVisualStyleBackColor = true;
            this.btnFindSurname.Click += new System.EventHandler(this.btnFind_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(9, 250);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(84, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Find by surname";
            this.label1.Click += new System.EventHandler(this.label1_Click);
            // 
            // tbSurname
            // 
            this.tbSurname.AcceptsReturn = true;
            this.tbSurname.Location = new System.Drawing.Point(12, 276);
            this.tbSurname.Name = "tbSurname";
            this.tbSurname.Size = new System.Drawing.Size(160, 20);
            this.tbSurname.TabIndex = 3;
            // 
            // cbSwedish
            // 
            this.cbSwedish.AutoSize = true;
            this.cbSwedish.Location = new System.Drawing.Point(12, 565);
            this.cbSwedish.Name = "cbSwedish";
            this.cbSwedish.Size = new System.Drawing.Size(72, 17);
            this.cbSwedish.TabIndex = 4;
            this.cbSwedish.Text = "Swedish?";
            this.cbSwedish.UseVisualStyleBackColor = true;
            this.cbSwedish.CheckedChanged += new System.EventHandler(this.cbSwedish_CheckedChanged);
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.Location = new System.Drawing.Point(110, 527);
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(43, 20);
            this.numericUpDown1.TabIndex = 5;
            this.numericUpDown1.Value = new decimal(new int[] {
            50,
            0,
            0,
            0});
            // 
            // btnBack
            // 
            this.btnBack.Location = new System.Drawing.Point(12, 12);
            this.btnBack.Name = "btnBack";
            this.btnBack.Size = new System.Drawing.Size(52, 23);
            this.btnBack.TabIndex = 6;
            this.btnBack.Text = "Back";
            this.btnBack.UseVisualStyleBackColor = true;
            this.btnBack.MouseClick += new System.Windows.Forms.MouseEventHandler(this.btnBack_MouseClick);
            // 
            // tbYear
            // 
            this.tbYear.Location = new System.Drawing.Point(12, 121);
            this.tbYear.Name = "tbYear";
            this.tbYear.Size = new System.Drawing.Size(160, 20);
            this.tbYear.TabIndex = 9;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(9, 95);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(64, 13);
            this.label2.TabIndex = 8;
            this.label2.Text = "Find by year";
            // 
            // btnFindYear
            // 
            this.btnFindYear.Location = new System.Drawing.Point(12, 147);
            this.btnFindYear.Name = "btnFindYear";
            this.btnFindYear.Size = new System.Drawing.Size(75, 23);
            this.btnFindYear.TabIndex = 7;
            this.btnFindYear.Text = "Find";
            this.btnFindYear.UseVisualStyleBackColor = true;
            this.btnFindYear.Click += new System.EventHandler(this.btnFindYear_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(9, 529);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(95, 13);
            this.label3.TabIndex = 11;
            this.label3.Text = "Amount of queries:";
            // 
            // lblHasMore
            // 
            this.lblHasMore.AutoSize = true;
            this.lblHasMore.Location = new System.Drawing.Point(145, 586);
            this.lblHasMore.Name = "lblHasMore";
            this.lblHasMore.Size = new System.Drawing.Size(120, 13);
            this.lblHasMore.TabIndex = 12;
            this.lblHasMore.Text = "Scroll down to find more";
            this.lblHasMore.Visible = false;
            // 
            // GUI_MPVotes
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1408, 611);
            this.Controls.Add(this.lblHasMore);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.tbYear);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.btnFindYear);
            this.Controls.Add(this.btnBack);
            this.Controls.Add(this.numericUpDown1);
            this.Controls.Add(this.cbSwedish);
            this.Controls.Add(this.tbSurname);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnFindSurname);
            this.Controls.Add(this.dataGridView1);
            this.Name = "GUI_MPVotes";
            this.Text = "VoteCheck (with Forms)";
            this.Load += new System.EventHandler(this.GUI_MPVotes_Load);
            this.Validated += new System.EventHandler(this.GUI_MPVotes_Validated);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Button btnFindSurname;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox tbSurname;
        private System.Windows.Forms.CheckBox cbSwedish;
        private System.Windows.Forms.NumericUpDown numericUpDown1;
        private System.Windows.Forms.Button btnBack;
        private System.Windows.Forms.TextBox tbYear;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button btnFindYear;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label lblHasMore;
    }
}

