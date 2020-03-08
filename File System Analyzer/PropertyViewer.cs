using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace File_System_Analyzer
{
    public partial class PropertyViewer : Form
    {
        public string Tittle { get; set; }
        public object DisplayObject { get; set; }

        public PropertyViewer()
        {
            InitializeComponent();
        }

        private void PropertyViewer_Load(object sender, EventArgs e)
        {

        }

        private void PropertyViewer_Shown(object sender, EventArgs e)
        {
            try
            {
                label1.Text = Tittle;
                propertyGrid1.SelectedObject = DisplayObject;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PropertyViewer_FormClosing(object sender, FormClosingEventArgs e)
        {
            
        }

        private void PropertyViewer_FormClosed(object sender, FormClosedEventArgs e)
        {

        }
    }
}
