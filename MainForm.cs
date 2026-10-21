using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace RpfStudio
{
    public class MainForm : Form
    {
        RpfArchive _rpf;
        readonly TreeView _tree = new TreeView();
        readonly ListView _list = new ListView();
        readonly StatusStrip _status = new StatusStrip();
        readonly ToolStripStatusLabel _lbl = new ToolStripStatusLabel("Bereit.");
        readonly TextBox _search = new TextBox();
        RpfEntry _curDir;
        string _gameDir;

        public MainForm()
        {
            Text = "RpfBude";
            Width = 1180; Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            _gameDir = FindGameDir();

            var menu = new MenuStrip();
            var mFile = new ToolStripMenuItem("Datei");
            mFile.DropDownItems.Add("Archiv öffnen...", null, (s, e) => OpenArchive());
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Beenden", null, (s, e) => Close());
            var mTools = new ToolStripMenuItem("Werkzeuge");
            mTools.DropDownItems.Add("DLC-Pack in dlclist eintragen...", null, (s, e) => AddDlcEntry());
            mTools.DropDownItems.Add("DLC-Pack wieder entfernen...", null, (s, e) => RemoveDlcEntry());
            mTools.DropDownItems.Add(new ToolStripSeparator());
            mTools.DropDownItems.Add("Integrität prüfen", null, (s, e) => Verify());
            mTools.DropDownItems.Add(new ToolStripSeparator());
            mTools.DropDownItems.Add("Mod als .oiv-Paket bauen...", null, (s, e) => BuildOiv());
            mTools.DropDownItems.Add("Paket installieren (.oiv)...", null, (s, e) => InstallOiv());
            var mHelp = new ToolStripMenuItem("Hilfe");
            mHelp.DropDownItems.Add("Info", null, (s, e) => MessageBox.Show(this,
                "RPF Studio\n\nEigenständiges Werkzeug für GTA V RPF7-Archive.\n" +
                "Unterstützt OPEN, NG (lesen) und das sichere Patchen von dlclist.xml.\n\n" +
                "NG-Keys: " + (RpfArchive.Ng != null ? "geladen" : "nicht geladen") + "\nSpielordner: " + (_gameDir ?? "nicht gefunden"),
                "Info", MessageBoxButtons.OK, MessageBoxIcon.Information));
            menu.Items.AddRange(new[] { mFile, mTools, mHelp });

            var bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.Items.Add(new ToolStripButton("Archiv öffnen", null, (s, e) => OpenArchive()));
            bar.Items.Add(new ToolStripButton("Extrahieren", null, (s, e) => ExtractSelected()));
            bar.Items.Add(new ToolStripButton("Ersetzen", null, (s, e) => ImportFile()));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(new ToolStripButton("DLC eintragen", null, (s, e) => AddDlcEntry()));
            bar.Items.Add(new ToolStripButton("DLC-Ordner installieren", null, (s, e) => InstallDlcFolder()));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(new ToolStripButton("Integrität", null, (s, e) => Verify()));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(new ToolStripLabel("Suche:"));
            _search.Width = 180;
            _search.TextChanged += (s, e) => FillList(_curDir);
            bar.Items.Add(new ToolStripControlHost(_search));

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 300 };
            _tree.Dock = DockStyle.Fill;
            _tree.HideSelection = false;
            _tree.AfterSelect += (s, e) => FillList(e.Node?.Tag as RpfEntry);
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.Columns.Add("Name", 320);
            _list.Columns.Add("Größe", 110, HorizontalAlignment.Right);
            _list.Columns.Add("Gepackt", 110, HorizontalAlignment.Right);
            _list.DoubleClick += (s, e) => ExtractSelected();
            split.Panel1.Controls.Add(_tree);
            split.Panel2.Controls.Add(_list);

            _status.Items.Add(_lbl);
            Controls.Add(split);
            Controls.Add(bar);
            Controls.Add(menu);
            MainMenuStrip = menu;

            Shown += async (s, e) => { await LoadKeysAsync(); await BuildTablesAsync(); };
        }

        async System.Threading.Tasks.Task BuildTablesAsync()
        {
            if (RpfArchive.Ng == null) return;
            if (RpfArchive.AEnc != null && RpfArchive.BLut != null) { _lbl.Text = "NG-Verschlüsselung bereit."; return; }
            _lbl.Text = "Baue NG-Verschlüsselungstabellen (einmalig, ca. 3 Minuten)...";
            try
            {
                await System.Threading.Tasks.Task.Run(() => RpfArchive.EnsureTables(null));
                _lbl.Text = "NG-Verschlüsselung bereit.";
            }
            catch (Exception ex) { _lbl.Text = "Tabellen fehlgeschlagen: " + ex.Message; }
        }

        static string FindGameDir()
        {
            foreach (var p in new[]
            {
                @"D:\SteamLibrary\steamapps\common\Grand Theft Auto V",
                @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V",
                @"C:\Program Files\Rockstar Games\Grand Theft Auto V"
            })
                if (File.Exists(Path.Combine(p, "GTA5.exe"))) return p;
            return null;
        }

        async System.Threading.Tasks.Task LoadKeysAsync()
        {
            if (_gameDir == null) { _lbl.Text = "Spielordner nicht gefunden - nur OPEN-Archive lesbar."; return; }
            var magic = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "magic.dat");
            if (!File.Exists(magic)) { _lbl.Text = "magic.dat fehlt - nur OPEN-Archive lesbar."; return; }
            _lbl.Text = "Lade NG-Schlüssel aus GTA5.exe ...";
            try
            {
                var aes = await System.Threading.Tasks.Task.Run(() => Crypto.FindAesKey(Path.Combine(_gameDir, "GTA5.exe")));
                if (aes == null) { _lbl.Text = "AES-Key nicht gefunden."; return; }
                var ng = await System.Threading.Tasks.Task.Run(() => Crypto.LoadNgKeys(magic, aes));
                RpfArchive.Ng = ng;
                _lbl.Text = "NG-Schlüssel geladen - verschlüsselte Archive lesbar.";
            }
            catch (Exception ex) { _lbl.Text = "NG-Init fehlgeschlagen: " + ex.Message; }
        }

        void OpenArchive()
        {
            using var ofd = new OpenFileDialog { Filter = "RPF-Archiv (*.rpf)|*.rpf|Alle Dateien (*.*)|*.*" };
            if (_gameDir != null) ofd.InitialDirectory = Path.Combine(_gameDir, "update");
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var a = new RpfArchive { FilePath = ofd.FileName };
                a.Load();
                _rpf = a;
                BuildTree();
                Text = $"RPF Studio - {Path.GetFileName(a.FilePath)} [{a.Entries.Count} Einträge, {a.Encryption}]";
                _lbl.Text = $"Geladen: {a.Entries.Count} Einträge, Verschlüsselung {a.Encryption}, {a.FileLength:N0} Bytes.";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void BuildTree()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            var root = new TreeNode(Path.GetFileName(_rpf.FilePath)) { Tag = _rpf.Root };
            AddNodes(root, _rpf.Root);
            _tree.Nodes.Add(root);
            root.Expand();
            _tree.EndUpdate();
        }

        void AddNodes(TreeNode tn, RpfEntry dir)
        {
            foreach (var c in dir.Children.Where(c => c.Kind == EntryKind.Directory).OrderBy(c => c.Name))
            {
                var n = new TreeNode(c.Name) { Tag = c };
                AddNodes(n, c);
                tn.Nodes.Add(n);
            }
        }

        void FillList(RpfEntry dir)
        {
            _curDir = dir;
            var filter = _search.Text.Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            if (dir != null)
            {
                foreach (var c in dir.Children.Where(c => c.Kind != EntryKind.Directory)
                                              .Where(c => filter.Length == 0 || c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                                              .OrderBy(c => c.Name))
                {
                    var it = new ListViewItem(c.Name) { Tag = c };
                    it.SubItems.Add(c.StoredSize.ToString("N0"));
                    it.SubItems.Add(c.FileSize > 0 ? "deflate" : "-");
                    _list.Items.Add(it);
                }
            }
            _list.EndUpdate();
        }

        void ExtractSelected()
        {
            if (_rpf == null || _list.SelectedItems.Count == 0) return;
            var e = (RpfEntry)_list.SelectedItems[0].Tag;
            byte[] data;
            try { data = _rpf.Extract(e); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler"); return; }
            if (data == null) { MessageBox.Show(this, "Konnte nicht gelesen werden (Verschlüsselung/Fragment)."); return; }
            using var sfd = new SaveFileDialog { FileName = e.Name };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(sfd.FileName, data);
            _lbl.Text = $"Extrahiert: {e.Name} ({data.Length:N0} Bytes)";
        }

        RpfEntry FindDlclist() => _rpf?.Find("common", "data", "dlclist.xml");

        void AddDlcEntry() => ModifyDlcList(true);
        void RemoveDlcEntry() => ModifyDlcList(false);

        void ModifyDlcList(bool add)
        {
            if (_rpf == null) { MessageBox.Show(this, "Bitte zuerst ein Archiv öffnen.", "Hinweis"); return; }
            var e = FindDlclist();
            if (e == null) { MessageBox.Show(this, "dlclist.xml nicht gefunden.", "Hinweis"); return; }

            string name;
            using (var dlg = new DlclistDialog(add)) { if (dlg.ShowDialog(this) != DialogResult.OK) { _lbl.Text = "Abgebrochen."; return; } name = dlg.DlcName; }
            if (string.IsNullOrWhiteSpace(name)) return;

            var xml = Encoding.UTF8.GetString(_rpf.Extract(e));
            var tag = $"<Item>dlcpacks:/{name}/</Item>";
            bool changed;
            if (add)
            {
                if (xml.Contains($"dlcpacks:/{name}/")) { MessageBox.Show(this, "Ist bereits eingetragen."); return; }
                int idx = xml.IndexOf("</Paths>", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) { MessageBox.Show(this, "</Paths> nicht gefunden - unerwartetes Format."); return; }
                xml = xml.Insert(idx, "\t\t" + tag + "\r\n\t");
                changed = true;
            }
            else
            {
                var rx = new System.Text.RegularExpressions.Regex("<Item>\\s*dlcpacks:/" + System.Text.RegularExpressions.Regex.Escape(name) + "/[^<]*</Item>\\s*");
                if (!rx.IsMatch(xml)) { MessageBox.Show(this, "Eintrag nicht vorhanden."); return; }
                xml = rx.Replace(xml, "");
                changed = true;
            }
            if (!changed) return;
            try { new System.Xml.XmlDocument().LoadXml(xml); }
            catch (Exception ex) { MessageBox.Show(this, "Ergebnis wäre kein gültiges XML:\n" + ex.Message); return; }

            try
            {
                _rpf.Backup();
                _rpf.Replace(e, Encoding.UTF8.GetBytes(xml));
                _lbl.Text = $"dlclist.xml aktualisiert ({name}). Backup angelegt.";
                MessageBox.Show(this, "dlclist.xml wurde aktualisiert.\n\nBackup neben der Originaldatei angelegt.", "Fertig",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void BuildOiv()
        {
            using var fbd = new FolderBrowserDialog { Description = "Mod-Ordner wählen (Inhalt wird ins Paket gepackt)" };
            if (fbd.ShowDialog(this) != DialogResult.OK) return;
            var target = Microsoft.VisualBasic.Interaction.InputBox("Zielpfad im Spielordner (z.B. scripts oder plugins\\LSPDFR):", "Ziel im Spiel", "scripts");
            if (string.IsNullOrWhiteSpace(target)) return;
            var name = Microsoft.VisualBasic.Interaction.InputBox("Paketname:", "Name", Path.GetFileName(fbd.SelectedPath));
            var dlc = Microsoft.VisualBasic.Interaction.InputBox("dlcpack-Name für dlclist (leer = keiner):", "DLC-Eintrag", "");
            using var sfd = new SaveFileDialog { Filter = "OpenIV Paket (*.oiv)|*.oiv", FileName = (string.IsNullOrWhiteSpace(name) ? "package" : name) + ".oiv" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                OivPackage.Build(fbd.SelectedPath, target, name, Environment.UserName, sfd.FileName,
                    string.IsNullOrWhiteSpace(dlc) ? null : dlc.Trim());
                _lbl.Text = "Paket erstellt: " + Path.GetFileName(sfd.FileName);
                MessageBox.Show(this, "Paket erstellt:\n" + sfd.FileName, "Fertig", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void InstallOiv()
        {
            var gameDir = _gameDir ?? Microsoft.VisualBasic.Interaction.InputBox("GTA V Ordner:", "Spielordner", "");
            if (string.IsNullOrWhiteSpace(gameDir)) return;
            using var ofd = new OpenFileDialog { Filter = "OpenIV Paket (*.oiv)|*.oiv|Alle Dateien (*.*)|*.*" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var log = OivPackage.Install(ofd.FileName, gameDir, m => _lbl.Text = m);
                MessageBox.Show(this, "Installation abgeschlossen:\n\n" + log, "Fertig", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (_rpf != null)
                {
                    var p = _rpf.FilePath;
                    _rpf = new RpfArchive { FilePath = p };
                    _rpf.Load();
                    BuildTree();
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void ImportFile()
        {
            if (_rpf == null || _list.SelectedItems.Count == 0) { MessageBox.Show(this, "Bitte zuerst eine Datei im Archiv auswählen.", "Hinweis"); return; }
            var e = (RpfEntry)_list.SelectedItems[0].Tag;
            using var ofd = new OpenFileDialog { Filter = "Alle Dateien (*.*)|*.*", FileName = e.Name };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            var data = File.ReadAllBytes(ofd.FileName);
            if (MessageBox.Show(this, "Eintrag '" + e.Name + "' durch '" + Path.GetFileName(ofd.FileName) + "' ersetzen?\n\nEs wird automatisch ein Backup angelegt.",
                "Bestätigen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                _lbl.Text = "Schreibe... (erster NG-Schreibvorgang baut ggf. Tabellen, ca. 3 min)";
                Application.DoEvents();
                _rpf.Backup();
                _rpf.Replace(e, data);
                _lbl.Text = "Ersetzt: " + e.Name;
                FillList(_curDir);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void InstallDlcFolder()
        {
            var gameDir = _gameDir;
            if (string.IsNullOrEmpty(gameDir)) { MessageBox.Show(this, "Spielordner nicht gefunden.", "Hinweis"); return; }
            using var fbd = new FolderBrowserDialog { Description = "dlcpack-Ordner waehlen (muss dlc.rpf enthalten)" };
            if (fbd.ShowDialog(this) != DialogResult.OK) return;
            var src = fbd.SelectedPath;
            if (!File.Exists(Path.Combine(src, "dlc.rpf"))) { MessageBox.Show(this, "In diesem Ordner liegt keine dlc.rpf.", "Hinweis"); return; }
            var name = Path.GetFileName(src.TrimEnd('\\'));
            var dest = Path.Combine(gameDir, "update", "x64", "dlcpacks", name);
            try
            {
                _lbl.Text = "Installiere DLC '" + name + "' ...";
                Application.DoEvents();
                if (!Directory.Exists(dest)) CopyDir(src, dest);
                var rpfPath = Path.Combine(gameDir, "update", "update.rpf");
                var rpf = new RpfArchive { FilePath = rpfPath };
                rpf.Load();
                var dl = rpf.Find("common", "data", "dlclist.xml");
                if (dl == null) throw new InvalidOperationException("dlclist.xml nicht gefunden.");
                var xml = Encoding.UTF8.GetString(rpf.Extract(dl));
                bool added = false;
                if (!xml.Contains("dlcpacks:/" + name + "/"))
                {
                    int i = xml.IndexOf("</Paths>", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) throw new InvalidOperationException("</Paths> nicht gefunden.");
                    xml = xml.Insert(i, "\t\t<Item>dlcpacks:/" + name + "/</Item>\r\n\t");
                    new System.Xml.XmlDocument().LoadXml(xml);
                    rpf.Backup();
                    rpf.Replace(dl, Encoding.UTF8.GetBytes(xml));
                    added = true;
                }
                _lbl.Text = "DLC '" + name + "' " + (added ? "installiert und eingetragen." : "war bereits eingetragen.");
                MessageBox.Show(this, "DLC '" + name + "' ist installiert.\n\nOrdner: " + dest + (added ? "\nEintrag: dlclist.xml aktualisiert (NG-verschluesselt)." : "\nEintrag war bereits vorhanden."),
                    "Fertig", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        void Verify()
        {
            if (_rpf == null) return;
            int dirs = _rpf.Entries.Count(x => x.Kind == EntryKind.Directory);
            int bins = _rpf.Entries.Count(x => x.Kind == EntryKind.Binary);
            int res = _rpf.Entries.Count(x => x.Kind == EntryKind.Resource);
            long maxEnd = 0;
            foreach (var f in _rpf.Entries.Where(x => x.Kind != EntryKind.Directory))
            {
                long end = (long)f.FileOffset * 512 + ((f.StoredSize + 511) / 512) * 512;
                if (end > maxEnd) maxEnd = end;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"Datei      : {_rpf.FilePath}");
            sb.AppendLine($"Größe      : {_rpf.FileLength:N0} Bytes");
            sb.AppendLine($"Format     : {_rpf.Encryption}");
            sb.AppendLine($"Einträge   : {_rpf.Entries.Count}  (Ordner {dirs}, Dateien {bins}, Resources {res})");
            sb.AppendLine($"Datenende  : {maxEnd:N0} Bytes ({(double)maxEnd / _rpf.FileLength:P1} der Datei)");
            sb.AppendLine();
            sb.AppendLine(maxEnd <= _rpf.FileLength
                ? "OK: alle Daten liegen innerhalb der Datei."
                : "WARNUNG: Daten ragen über das Dateiende hinaus!");
            MessageBox.Show(this, sb.ToString(), "Integritätsprüfung", MessageBoxButtons.OK,
                maxEnd <= _rpf.FileLength ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }

    public class DlclistDialog : Form
    {
        readonly TextBox _tb = new TextBox();
        public string DlcName => _tb.Text.Trim();

        public DlclistDialog(bool add)
        {
            Text = add ? "DLC-Pack eintragen" : "DLC-Pack entfernen";
            Width = 460; Height = 170; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            Controls.Add(new Label { Text = "dlcpack-Name (Ordner unter update\\x64\\dlcpacks):", Left = 14, Top = 16, Width = 420 });
            _tb.Left = 14; _tb.Top = 42; _tb.Width = 420; _tb.Text = "pbpdkeene";
            var ok = new Button { Text = "OK", Left = 262, Top = 80, Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Abbrechen", Left = 348, Top = 80, Width = 86, DialogResult = DialogResult.Cancel };
            Controls.Add(_tb); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
