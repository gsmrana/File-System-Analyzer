using File_System_Analyzer.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Design;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Megamind.IO.FileSystem;
using System.Reflection;

namespace File_System_Analyzer
{
    public partial class MainForm : Form
    {
        #region Const

        readonly string TestImagesDir = @"..\..\..\ImageFiles";

        #endregion

        #region Data

        bool _showdebuglog = true;
        bool _showdelentry = false;
        int _selpartition = 0;
        string _dirpathstr = "";

        string _selectedfile;
        FileManager _fileManager;
        List<FatEntry> _files = new List<FatEntry>();
        readonly List<string> DiskImageFileNames = new List<string>();
        readonly Dictionary<int, int> EntryIconMap = new Dictionary<int, int>()
        {
            { (int)EntryAttributes.Archive, 1},
            { (int)EntryAttributes.Directory, 2},
            { (int)EntryAttributes.HiddenSystemDir, 3},
            { (int)EntryAttributes.VolumeLabel, 4},
            { (int)EntryAttributes.Device, 4},
        };

        #endregion

        #region ctor

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                imageList1.Images.Add(Resources.FileIcon1);
                imageList1.Images.Add(Resources.FileIcon2);
                imageList1.Images.Add(Resources.DirIcon1);
                imageList1.Images.Add(Resources.DirIcon2);
                imageList1.Images.Add(Resources.DiskIcon1);
                imageList1.Images.Add(Resources.HardDisk);

                listView1.Columns.Add("Filename", 290);
                listView1.Columns.Add("Attribute", 100);
                listView1.Columns.Add("Start Cluster", 100);
                listView1.Columns.Add("Size", 110);
                listView1.Columns.Add("Entry", 40);

                richTextBoxEventLog.Font = new Font("Consolas", 9);

                var dispheight = Screen.PrimaryScreen.Bounds.Height;
                if (Height > (dispheight - 35)) Height = dispheight - 35;
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            try
            {
                ReloadDiskNames();
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    TryOpenFileSystem(args[1]);
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region UI Update Methods

        void AppendEventLog(string text, Color? color = null, bool appendNewLine = true)
        {
            var str = text;
            var clr = color ?? Color.Blue;
            if (str.StartsWith("Error")) clr = Color.Magenta;
            if (appendNewLine) str += Environment.NewLine;
            Invoke(new MethodInvoker(() =>
            {
                richTextBoxEventLog.SelectionStart = richTextBoxEventLog.TextLength;
                richTextBoxEventLog.SelectionLength = 0;
                richTextBoxEventLog.SelectionColor = clr;
                richTextBoxEventLog.AppendText(str);
                if (!richTextBoxEventLog.Focused) richTextBoxEventLog.ScrollToCaret();
            }));
        }

        void DisplayDirectoryPath(string path)
        {
            Invoke(new MethodInvoker(() =>
            {
                toolStripTextBoxDirPath.Text = path;
            }));
        }

        void DisplayPartitionTreeView(List<FileSystemBase> partitions)
        {
            Invoke(new MethodInvoker(() =>
            {
                treeView1.Nodes.Clear();
                var disk = new TreeNode()
                {
                    Text = "Disk",
                    ImageIndex = 5,
                    SelectedImageIndex = 5
                };
                treeView1.Nodes.Add(disk);
                for (int i = 0; i < partitions.Count; i++)
                {
                    var node1 = new TreeNode()
                    {
                        Text = "Partition" + i,
                        Tag = i,
                        ImageIndex = 4,
                        SelectedImageIndex = 4
                    };
                    disk.Nodes.Add(node1);
                }
                treeView1.ExpandAll();
                if (partitions.Count > 0) treeView1.SelectedNode = disk.Nodes[0];
            }));
        }

        void DisplayFileListView(List<FatEntry> files)
        {
            var lvis = new List<ListViewItem>();
            foreach (var file in files)
            {
                var lvi = new ListViewItem(file.FullName);
                lvi.SubItems.Add(((EntryAttributes)file.Attribute).ToString());
                lvi.SubItems.Add("0x" + file.StartCluster.ToString("X8"));
                lvi.SubItems.Add(file.FileSizeString);
                lvi.SubItems.Add(file.EntryIndex.ToString());
                lvi.ImageIndex = 0;
                if (EntryIconMap.ContainsKey(file.Attribute))
                    lvi.ImageIndex = EntryIconMap[file.Attribute];
                lvis.Add(lvi);
            }

            Invoke(new MethodInvoker(() =>
            {
                listView1.Items.Clear();
                listView1.Items.AddRange(lvis.ToArray());
            }));
        }

        void DisplayProgress(int percent)
        {
            Invoke(new MethodInvoker(() => { toolStripProgressBar1.Value = percent; }));
        }

        void DisplayStorageName(string name)
        {
            Invoke(new MethodInvoker(() =>
            {
                Text = string.Format("{0} - {1}", name, Assembly.GetEntryAssembly().GetName().Name);
                toolStripStatusStorage.Text = string.Format("Storage: {0}", name);
            }));
        }

        void DisplayPartitionStatus(FileSystemBase partition)
        {
            var bs = partition.BootSector;
            var fsstr = string.Format("File System: {0}  |  {1}", (FsTypesMbr)bs.MbrPartitionTable.First().FileSystemMbr, bs.FsType);
            var psize = string.Format("Partition Size: {0}", bs.PartitionSizeString);
            Invoke(new MethodInvoker(() =>
            {
                toolStripStatusFSType.Text = fsstr;
                toolStripStatusSize.Text = psize;
            }));
        }

        void PopupException(string message, string caption = "Error")
        {
            Invoke(new MethodInvoker(() =>
            {
                MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }

        #endregion

        #region Internal Methods

        static IEnumerable<DiskInfo> GetPhysicalDiskList()
        {
            var drives = new List<DiskInfo>();
            var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject mobj in searcher.Get())
            {
                var di = new DiskInfo();
                try
                {
                    di.FileName = mobj["Name"].ToString();
                    di.Model = mobj["Model"].ToString();
                    di.PartitionCount = mobj["Partitions"].ToString();
                    di.BytesPerSector = mobj["BytesPerSector"].ToString();
                    di.TotalSectors = mobj["TotalSectors"].ToString();
                    di.TotalSize = mobj["Size"].ToString();
                    di.Description = mobj["Description"].ToString();
                    di.InterfaceType = mobj["InterfaceType"].ToString();
                    di.MediaType = mobj["MediaType"].ToString();
                    di.SerialNumber = mobj["SerialNumber"].ToString();
                }
                catch { }
                drives.Add(di);
            }
            return drives.ToArray();
        }

        void ReloadDiskNames()
        {
            DiskImageFileNames.Clear();
            comboBoxStorageName.Items.Clear();

            var disklist = GetPhysicalDiskList();
            foreach (var item in disklist)
            {
                DiskImageFileNames.Add(item.FileName);
                comboBoxStorageName.Items.Add(item.FileName);
            }
            if (comboBoxStorageName.Items.Count > 0)
                comboBoxStorageName.SelectedIndex = comboBoxStorageName.Items.Count - 1;

            var drivelist = DriveInfo.GetDrives();
            foreach (var item in drivelist)
            {
                if (!item.IsReady) continue;
                var name = @"\\.\" + item.Name.Substring(0, 2);
                DiskImageFileNames.Add(name);
                comboBoxStorageName.Items.Add(name);
            }

            var files = Directory.GetFiles(Environment.CurrentDirectory);
            foreach (var item in files)
            {
                if (Path.GetExtension(item) == ".img")
                {
                    DiskImageFileNames.Add(item);
                    comboBoxStorageName.Items.Add(Path.GetFileName(item));
                }
            }

            if (Directory.Exists(TestImagesDir))
            {
                files = Directory.GetFiles(TestImagesDir);
                foreach (var item in files)
                {
                    if (Path.GetExtension(item) == ".img")
                    {
                        DiskImageFileNames.Add(item);
                        comboBoxStorageName.Items.Add(Path.GetFileName(item));
                    }
                }
            }
        }

        void TryOpenFileSystem(string filename)
        {
            try
            {
                treeView1.Nodes.Clear();
                listView1.Items.Clear();
                _selectedfile = filename;
                if (_fileManager != null)
                    _fileManager.Close();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }

            Task.Factory.StartNew(new Action(() =>
            {
                try
                {
                    IStorageIO storage;
                    string displayname;
                    if (_selectedfile.StartsWith(@"\\.\"))
                    {
                        displayname = _selectedfile;
                        AppendEventLog("\r\nReading Physical Drive: " + displayname);
                        storage = new DiskIO(_selectedfile);
                    }
                    else
                    {
                        displayname = Path.GetFileName(filename);
                        AppendEventLog("\r\nReading Disk Image: " + displayname);
                        storage = new DiskImageIO(_selectedfile);
                    }

                    DisplayStorageName(displayname);
                    _fileManager = new FileManager(storage);
                    _fileManager.OnEventLog += FileSystem_OnEventLog;
                    _fileManager.OnProgress += FileSystem_OnProgress;
                    _fileManager.Open();
                    foreach (var item in _fileManager.Partitions)
                        item.IncludeDeletedEntry = _showdelentry;
                    DisplayPartitionTreeView(_fileManager.Partitions);
                    if (_fileManager.Partitions.Count > 0)
                        ReadPartitionRoot(0);
                    else throw new Exception("Error: No Partition Found!");
                }
                catch (Exception ex)
                {
                    var info = ex.Message;
                    if (ex.Message.Contains("denied"))
                        info = ex.Message + "\r\rRun as Administrator for physical drive access!";
                    PopupException(info);
                }
            }));
        }

        public void ReadPartitionRoot(int partition)
        {
            _selpartition = partition;
            DisplayPartitionStatus(_fileManager.Partitions[_selpartition]);
            GetDisplayDirEntries(_selpartition, FileSystemBase.ROOT_DIR_FAT_12_16);
            _dirpathstr = _selpartition + ":";
            DisplayDirectoryPath(_dirpathstr);
        }

        public void GetDisplayDirEntries(int partition, int startcluster)
        {
            if (_fileManager.Partitions[partition].BootSector.FsType == FsType.Unknown)
                throw new Exception("File System Unknown!");

            _files = _fileManager.Partitions[partition].GetDirEntries(startcluster).ToList();
            DisplayFileListView(_files);
        }

        public string CopyFatFileToLocal(FatEntry srcfile, string destdir)
        {
            if (srcfile.FileSize <= 0)
            {
                AppendEventLog("FileCopy => " + srcfile.FullName + " - Invalid File Size!");
                MessageBox.Show("Invalid File Size!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return string.Empty;
            }
            var dest = Path.Combine(destdir, srcfile.FullName);
            AppendEventLog(string.Format("\r\nCopying File: {0}\\{1}, Dest: {2}", _dirpathstr, srcfile.FullName, destdir));
            using (var destfile = File.Create(dest))
            {
                _fileManager.Partitions[_selpartition].ReadFile(srcfile, destfile);
            }
            return dest;
        }

        int _prevpercent;

        private void FileSystem_OnProgress(object sender, ProgressUpdateEventArgs e)
        {
            try
            {
                if (e.Percent == _prevpercent)
                    return;
                DisplayProgress(e.Percent);
                _prevpercent = e.Percent;
            }
            catch (Exception ex)
            {
                AppendEventLog("FileSystem_OnProgress Error: " + ex.Message);
            }
        }

        private void FileSystem_OnEventLog(object sender, LogEventArgs e)
        {
            try
            {
                if (!_showdebuglog) return;
                AppendEventLog(e.Message);
            }
            catch (Exception ex)
            {
                AppendEventLog("FileSystem_OnEventLog Error: " + ex.Message);
            }
        }

        #endregion

        #region MenuStrip Events

        private void NewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                NewToolStripButton_Click(sender, e);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                OpenToolStripButton_Click(sender, e);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void SaveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                SaveToolStripButton_Click(sender, e);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void CloseHandleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (_fileManager != null)
                    _fileManager.Close();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void DebugLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _showdebuglog = debugLogToolStripMenuItem.Checked;
        }

        private void ShowDeletedEntryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _showdelentry = showDeletedEntryToolStripMenuItem.Checked;
            if (_fileManager != null) _fileManager.Partitions[_selpartition].IncludeDeletedEntry = _showdelentry;
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            HelpToolStripButtonAbout_Click(sender, e);
        }

        #endregion

        #region Toolbar1 Events

        private void NewToolStripButton_Click(object sender, EventArgs e)
        {
            try
            {
                _fileManager.Partitions[_selpartition].CreateFile(new FatEntry());
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void OpenToolStripButton_Click(object sender, EventArgs e)
        {
            try
            {
                var ofd = new OpenFileDialog();
                ofd.Filter = "DiskImage Files|*.img|All Files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    TryOpenFileSystem(ofd.FileName);
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void SaveToolStripButton_Click(object sender, EventArgs e)
        {
            SaveAsToolStripMenuItem_Click(sender, e);
        }

        private void ToolStripButtonDiskList_Click(object sender, EventArgs e)
        {
            try
            {
                var disklist = GetPhysicalDiskList();
                var drivelist = DriveInfo.GetDrives();

                var tableview = new TableViwForm();
                tableview.Tittle = "Physical Disk and Drive List";
                tableview.ColumnHeaders.Add(new ColumnHeader("Name", 150));
                tableview.ColumnHeaders.Add(new ColumnHeader("Capacity", 100));
                tableview.ColumnHeaders.Add(new ColumnHeader("Description", 200));
                tableview.ColumnHeaders.Add(new ColumnHeader("Model", 280));
                foreach (var item in disklist)
                {
                    var sizestr = FileSystemBase.GetFormatedSizeString(long.Parse(item.TotalSize));
                    var descstr = string.Format("{0}, {1}, {2}", item.InterfaceType, item.Description, item.MediaType);
                    var modelstr = string.Format("{0}, {1}", item.Model, item.SerialNumber);
                    tableview.DataRows.Add(new[] { item.FileName, sizestr, descstr, modelstr });
                }
                foreach (var item in drivelist)
                {
                    if (!item.IsReady) continue;
                    var namestr = @"\\.\" + item.Name.Substring(0, 2);
                    var sizestr = FileSystemBase.GetFormatedSizeString(item.TotalSize);
                    var descstr = string.Format("{0}, {1}, {2}", item.VolumeLabel, item.DriveFormat, item.DriveType);
                    tableview.DataRows.Add(new[] { namestr, sizestr, descstr });
                }
                tableview.ShowDialog();
                var idx = tableview.SelectedIndex;
                if (idx >= 0)
                {
                    ReloadDiskNames();
                    comboBoxStorageName.SelectedIndex = idx;
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }
        private void ComboBoxStorageName_DropDown(object sender, EventArgs e)
        {
            try
            {
                ReloadDiskNames();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ToolStripButtonOpen_Click(object sender, EventArgs e)
        {
            try
            {
                string filename;
                var idx = comboBoxStorageName.SelectedIndex;
                if (idx >= 0) filename = DiskImageFileNames[idx];
                else filename = comboBoxStorageName.Text;
                TryOpenFileSystem(filename);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ToolStripButtonInfo_Click(object sender, EventArgs e)
        {
            try
            {
                var selname = comboBoxStorageName.Text;
                var pviewer = new PropertyViewer();
                var disk = GetPhysicalDiskList().FirstOrDefault(p => p.FileName.Contains(selname));
                if (disk != null)
                {
                    pviewer.Tittle = "Physical Disk: " + selname;
                    pviewer.DisplayObject = disk;
                    pviewer.Show();
                    return;
                }

                var drive = DriveInfo.GetDrives().FirstOrDefault(p => p.Name.Contains(selname.Substring(4, 2)));
                if (drive != null)
                {
                    pviewer.Tittle = "Drive: " + selname;
                    pviewer.DisplayObject = drive;
                    pviewer.Show();
                    return;
                }

                var file = DiskImageFileNames.FirstOrDefault(p => p.Contains(selname));
                if (file != null)
                {
                    pviewer.Tittle = "File: " + selname;
                    pviewer.DisplayObject = new FileInfo(file);
                    pviewer.Show();
                    return;
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ToolStripButtonClear_Click(object sender, EventArgs e)
        {
            try
            {
                richTextBoxEventLog.Clear();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void HelpToolStripButtonAbout_Click(object sender, EventArgs e)
        {
            try
            {
                var info = Application.ProductName;
                info += "\rVersion " + Application.ProductVersion;
                info += "\rDeveloper GSM Rana";
                info += "\rhttps://github.com/gsmrana";
                MessageBox.Show(info, "Credit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ComboBoxStorageName_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void ComboBoxPartitions_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        #endregion

        #region Toolbar2 Events

        private void ToolStripButtonUpDir_Click(object sender, EventArgs e)
        {
            try
            {
                if (_files.Count >= 2)
                {
                    if (_files[1].Attribute == (byte)EntryAttributes.Directory &&
                        _files[1].FullName.StartsWith(".."))
                    {
                        GetDisplayDirEntries(_selpartition, _files[1].StartCluster);
                        var idx = _dirpathstr.LastIndexOf('\\');
                        if (idx > 0) _dirpathstr = _dirpathstr.Substring(0, idx);
                        DisplayDirectoryPath(_dirpathstr);
                    }
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ToolStripButtonGoDir_Click(object sender, EventArgs e)
        {
            try
            {
                if (_files.Count >= 1)
                {
                    if (_files[0].Attribute == (byte)EntryAttributes.Directory &&
                        _files[0].FullName.StartsWith("."))
                    {
                        GetDisplayDirEntries(_selpartition, _files[0].StartCluster);
                    }
                    else GetDisplayDirEntries(_selpartition, FileSystemBase.ROOT_DIR_FAT_12_16);
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region TreeView Events

        TreeNode _rightclicknode;

        private void TreeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            _rightclicknode = e.Node;
            if (e.Button != MouseButtons.Left) return;

            Task.Factory.StartNew(new Action(() =>
            {
                try
                {
                    if (e.Node.Text.Contains("Partition"))
                    {
                        ReadPartitionRoot((int)e.Node.Tag);
                    }
                }
                catch (Exception ex)
                {
                    PopupException(ex.Message);
                }
            }));
        }

        #endregion

        #region TreeView Context Menu

        private void FormatTreeViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (_rightclicknode == null) return;
                if (_rightclicknode.Text.Contains("Disk"))
                {
                    _fileManager.Partitions[0].Format();
                }
                else if (_rightclicknode.Text.Contains("Partition"))
                {
                    var partition = (int)_rightclicknode.Tag;
                    _fileManager.Partitions[_selpartition].Format();
                }
                _rightclicknode = null;
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void FATDumpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (_rightclicknode == null) return;
                if (!(_fileManager.Partitions[_selpartition] is FileSystemFAT partition)) return;

                if (_rightclicknode.Text.Contains("Partition"))
                {
                    var index = (int)_rightclicknode.Tag;
                    var objviewer1 = new PropertyViewer();
                    objviewer1.Tittle = "FAT Dump of Partition " + index;
                    objviewer1.DisplayObject = partition.FatDump(index).ToArray();
                    objviewer1.Show();
                }
                _rightclicknode = null;
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void PropertiesTreeViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (_rightclicknode == null) return;
                if (_rightclicknode.Text.Contains("Disk"))
                {
                    var objviewer1 = new PropertyViewer();
                    objviewer1.Tittle = "Disk MBR";
                    objviewer1.DisplayObject = _fileManager.Mbr;
                    objviewer1.Show();
                }
                else if (_rightclicknode.Text.Contains("Partition"))
                {
                    var index = (int)_rightclicknode.Tag;
                    var objviewer1 = new PropertyViewer();
                    objviewer1.Tittle = "Boot Sector of Partition " + index;
                    objviewer1.DisplayObject = _fileManager.BootSectors[index];
                    objviewer1.Show();
                }
                _rightclicknode = null;
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region ListView Events

        private void ListView1_DoubleClick(object sender, EventArgs e)
        {
            OpenToolStripMenuItem1_Click(sender, e);
        }

        #endregion

        #region ListView Context Menu

        private void OpenToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedIndices.Count <= 0) return;
            var filesToRead = new List<FatEntry>();
            foreach (int index in listView1.SelectedIndices)
                filesToRead.Add(_files[index]);

            Task.Factory.StartNew(new Action(() =>
            {
                try
                {
                    foreach (var fentry in filesToRead)
                    {
                        if (fentry.Attribute == (byte)EntryAttributes.Directory ||
                            fentry.Attribute == (byte)EntryAttributes.HiddenSystemDir)
                        {
                            GetDisplayDirEntries(_selpartition, fentry.StartCluster);
                            if (fentry.FullName.StartsWith(".."))
                            {
                                var idx = _dirpathstr.LastIndexOf('\\');
                                if (idx > 0) _dirpathstr = _dirpathstr.Substring(0, idx);
                            }
                            else if (!fentry.FullName.StartsWith("."))
                            {
                                _dirpathstr += string.Format(@"\{0}", fentry.FullName);
                            }
                            DisplayDirectoryPath(_dirpathstr);
                            break;
                        }
                        else if (fentry.Attribute == (byte)EntryAttributes.VolumeLabel)
                        {
                            AppendEventLog("VolumeLabel: " + fentry.FullName);
                        }
                        else //file
                        {
                            var destdir = Path.GetTempPath();
                            var destfile = CopyFatFileToLocal(fentry, destdir);
                            try
                            {
                                if (!string.IsNullOrEmpty(destfile))
                                {
                                    AppendEventLog("Opening File: " + destfile);
                                    Process.Start(destfile);
                                }
                            }
                            catch (Exception ex)
                            {
                                AppendEventLog("Error: " + ex.Message);
                                Process.Start(destdir);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    PopupException(ex.Message);
                }
            }));
        }

        private void SaveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedIndices.Count <= 0) return;
            var filesToRead = new List<FatEntry>();
            foreach (int index in listView1.SelectedIndices)
                filesToRead.Add(_files[index]);

            var sfd = new SaveFileDialog();
            sfd.Filter = "All Files (*.*)|*.*";
            sfd.FileName = filesToRead.First().FullName;
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                var destdir = Path.GetDirectoryName(sfd.FileName);
                Task.Factory.StartNew(new Action(() =>
                {
                    try
                    {
                        foreach (var fentry in filesToRead)
                        {
                            if (fentry.Attribute != (byte)EntryAttributes.Directory &&
                                fentry.Attribute != (byte)EntryAttributes.HiddenSystemDir &&
                                fentry.Attribute != (byte)EntryAttributes.VolumeLabel)
                            {
                                CopyFatFileToLocal(fentry, destdir);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        PopupException(ex.Message);
                    }
                }));
            }
        }

        private void ViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedIndices.Count <= 0) return;
            var file = _files[listView1.SelectedIndices[0]];
            if (file.FileSize <= 0)
            {
                AppendEventLog("Error: FileView => " + file.FullName + " - Invalid File Size!");
                return;
            }

            Task.Factory.StartNew(new Action(() =>
            {
                try
                {
                    var objviewer = new PropertyViewer();
                    objviewer.Tittle = file.FullName;
                    if (_fileManager.Partitions[_selpartition] is FileSystemNTFS ntfsfs && file.FullName.StartsWith("$"))
                    {
                        AppendEventLog(string.Format("\r\nNTFS SystemFile: {0}\\{1}", _dirpathstr, file.FullName));
                        var sysfile = ntfsfs.SystemFiles.FirstOrDefault(p => p.FileName == file.FullName);
                        objviewer.DisplayObject = sysfile.MftEntry;
                    }
                    else
                    {
                        var filecontent = new FileContent();
                        AppendEventLog(string.Format("\r\nReading File: {0}\\{1}", _dirpathstr, file.FullName));
                        using (var dest = new MemoryStream())
                        {
                            _fileManager.Partitions[_selpartition].ReadFile(file, dest);
                            filecontent.CopyData(dest);
                        }
                        objviewer.DisplayObject = filecontent;
                    }
                    Invoke(new Action(() => { objviewer.Show(); }));
                }
                catch (Exception ex)
                {
                    PopupException(ex.Message);
                }
            }));
        }

        private void NewToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            try
            {
                _fileManager.Partitions[_selpartition].CreateFile(new FatEntry());
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void DeleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (listView1.SelectedIndices.Count <= 0) return;
                foreach (int index in listView1.SelectedIndices)
                {
                    _fileManager.Partitions[_selpartition].DeleteFile(_files[index]);
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void PropertiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (listView1.SelectedIndices.Count <= 0) return;
                foreach (int index in listView1.SelectedIndices)
                {
                    var file = _files[index];
                    var objviewer1 = new PropertyViewer();
                    objviewer1.Tittle = file.FullName;
                    objviewer1.DisplayObject = file;
                    objviewer1.Show();
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }


        #endregion

        #region File Drag and Drop

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                TryOpenFileSystem(files[0]);
            }
        }

        #endregion

    }

    #region Internal Class

    public class FileContent
    {
        public byte[] RawBuffer { get; set; }
        public string[] HexString { get; set; }

        [Editor(typeof(MultilineStringEditor), typeof(UITypeEditor))]
        public string AsciiString { get; set; }

        public void CopyData(MemoryStream stream)
        {
            RawBuffer = stream.ToArray();

            HexString = new string[RawBuffer.Length];
            for (int i = 0; i < RawBuffer.Length; i++)
            {
                HexString[i] = RawBuffer[i].ToString("X2");
            }

            AsciiString = "<BinaryContent>";
            if (RawBuffer[0] == 10 || RawBuffer[0] == 13 ||
                (RawBuffer[0] > 31 && RawBuffer[0] < 127))
            {
                AsciiString = Encoding.ASCII.GetString(RawBuffer);
            }
        }
    }

    #endregion

}
