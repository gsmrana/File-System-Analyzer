using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace File_System_Analyzer
{
    public partial class TableViwForm : Form
    {
        #region Properties

        public string Tittle { get; set; }

        public int SelectedIndex { get; set; } = -1;

        public List<ColumnHeader> ColumnHeaders = new List<ColumnHeader>();

        public List<string[]> DataRows = new List<string[]>();

        #endregion

        #region Internal Methods

        private void PopUpException(string message, string caption = "Error")
        {
            MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        #endregion

        #region ctor

        public TableViwForm()
        {
            InitializeComponent();
        }

        private void TableViwForm_Load(object sender, EventArgs e)
        {
            try
            {
                labelHeader.Text = Tittle;
            }
            catch (Exception ex)
            {
                PopUpException(ex.Message);
            }
        }

        private void TableViwForm_Shown(object sender, EventArgs e)
        {
            try
            {
                foreach (var item in ColumnHeaders)
                {
                    listView1.Columns.Add(item.Text, item.Width);
                }
                Application.DoEvents();
                foreach (var item in DataRows)
                {
                    listView1.Items.Add(new ListViewItem(item));
                }
            }
            catch (Exception ex)
            {
                PopUpException(ex.Message);
            }
        }

        #endregion

        #region Control Events

        private void ListView1_KeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    var sb = new StringBuilder();
                    foreach (ListViewItem lvi in listView1.SelectedItems)
                    {
                        for (int i = 0; i < lvi.SubItems.Count; i++)
                        {
                            sb.Append(lvi.SubItems[i].Text + " ");
                        }
                        sb.AppendLine();
                    }
                    Clipboard.SetText(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                PopUpException(ex.Message);
            }
        }

        private void ButtonSelect_Click(object sender, EventArgs e)
        {
            try
            {
                if (listView1.SelectedItems.Count > 0)
                {
                    SelectedIndex = listView1.Items.IndexOf(listView1.SelectedItems[0]);
                    Close();
                }
            }
            catch (Exception ex)
            {
                PopUpException(ex.Message);
            }
        }

        private void ListView1_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (listView1.SelectedItems.Count > 0)
                {
                    SelectedIndex = listView1.Items.IndexOf(listView1.SelectedItems[0]);
                    Close();
                }
            }
            catch (Exception ex)
            {
                PopUpException(ex.Message);
            }
        }

        #endregion
    }


    public class ColumnHeader
    {
        public int Width { get; set; }
        public string Text { get; set; }

        public ColumnHeader(string text, int width)
        {
            Text = text;
            Width = width;
        }
    }
}
