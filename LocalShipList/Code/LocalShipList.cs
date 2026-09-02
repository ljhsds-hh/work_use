// Local 出荷 DLL 清单工具
// 用法:双击打开图形界面;或命令行 LocalShipList.exe <仓库路径> <最新SHA> [<更旧SHA> ...](按新到旧排列,须为连续提交)
// 依赖:电脑已安装 git(在 PATH 中)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace LocalShip
{
    public class ProjInfo
    {
        public string FullPath;
        public string AsmName = "";
        public string OutType = "Library";
        public string XapFilename = "";
        public bool IsSilverlightApp;
        public HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> ProjectRefs = new List<string>();   // ProjectReference 路径(小写全路径)
        public List<string> AssemblyRefs = new List<string>();  // Reference 的程序集名

        public string AsmOutput
        {
            get
            {
                bool lib = string.Equals(OutType == null ? "Library" : OutType.Trim(),
                                         "Library", StringComparison.OrdinalIgnoreCase);
                return AsmName + (lib ? ".dll" : ".exe");
            }
        }

        public string XapOutput
        {
            get
            {
                return XapFilename.Length > 0 ? Path.GetFileName(XapFilename) : AsmName + ".xap";
            }
        }
    }

    public static class Logic
    {
        public static string Git(string repo, string args)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "git";
            psi.Arguments = args;
            psi.WorkingDirectory = repo;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            Process p;
            try { p = Process.Start(psi); }
            catch { throw new Exception("找不到 git,请确认已安装 git 并且在 PATH 中。"); }
            string so = p.StandardOutput.ReadToEnd();
            string se = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception("git " + args + "\r\n" + se.Trim());
            return so;
        }

        static string Quote(string s) { return "\"" + s.Replace("\"", "") + "\""; }

        public static List<string> ListBranches(string repo, out string current)
        {
            current = null;
            var list = new List<string>();
            foreach (string raw in Git(repo, "branch -a").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Contains("HEAD ->")) continue;
                string b = line.Trim();
                if (b.StartsWith("* ")) { b = b.Substring(2); current = b; }
                if (b.Length > 0 && !list.Contains(b)) list.Add(b);
            }
            return list;
        }

        public class Commit { public string Sha, Short, Author, Time, Subject; }

        public static List<Commit> ListCommits(string repo, string branch, out string err)
        {
            err = null;
            var list = new List<Commit>();
            try
            {
                string outp = Git(repo, "log --pretty=format:%H|%h|%an|%ci|%s -n 300 " + Quote(branch));
                foreach (string line in outp.Split('\n'))
                {
                    string[] ps = line.TrimEnd('\r').Split(new[] { '|' }, 5);
                    if (ps.Length < 5) continue;
                    var c = new Commit();
                    c.Sha = ps[0]; c.Short = ps[1]; c.Author = ps[2]; c.Time = ps[3]; c.Subject = ps[4];
                    list.Add(c);
                }
            }
            catch (Exception ex) { err = ex.Message; }
            return list;
        }

        public static List<string> ParentsOf(string repo, string sha)
        {
            string outp = Git(repo, "rev-list --parents -n 1 " + sha).Trim();
            var parts = outp.Split(' ');
            var r = new List<string>();
            for (int i = 1; i < parts.Length; i++) r.Add(parts[i]);
            return r;
        }

        static List<string> ParseFiles(string outp, string repo)
        {
            var files = new List<string>();
            foreach (string line in outp.Split('\n'))
            {
                string f = line.TrimEnd('\r');
                if (f.Length == 0) continue;
                try
                {
                    files.Add(Path.GetFullPath(Path.Combine(repo, f.Replace('/', '\\'))).ToLower());
                }
                catch { }
            }
            return files;
        }

        // 单个提交的改动(相对其父提交;根提交用 --root)
        static List<string> DiffTreeFiles(string repo, string sha)
        {
            return ParseFiles(Git(repo, "diff-tree --root --no-commit-id --name-only -r " + sha), repo);
        }

        // 提交区间的全部改动文件:"每一笔碰过的"口径 = 各提交改动文件的并集
        public static List<string> ChangedFilesRange(string repo, List<string> shas)
        {
            // 校验连续性:每条提交的第一父提交必须就是下一条
            for (int i = 0; i + 1 < shas.Count; i++)
            {
                var ps = ParentsOf(repo, shas[i]);
                if (!ps.Contains(shas[i + 1]))
                    throw new Exception("选中的提交不连续:第 " + (i + 1) + " 条("
                        + shas[i].Substring(0, Math.Min(7, shas[i].Length))
                        + ")的父提交不是下一条所选提交。\r\n只支持连续的提交履历。");
            }
            var files = new List<string>();
            foreach (var s in shas)
                foreach (var f in DiffTreeFiles(repo, s))
                    if (!files.Contains(f)) files.Add(f);
            return files;
        }

        static IEnumerable<string> WalkFiles(string root, string pattern)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] sub = null, files = null;
                try { sub = Directory.GetDirectories(dir); files = Directory.GetFiles(dir, pattern); }
                catch { continue; }
                foreach (var f in files) yield return f;
                foreach (var d in sub)
                {
                    string low = Path.GetFileName(d).ToLower();
                    if (low == "bin" || low == "obj" || low == ".git" || low == "packages" || low == ".svn") continue;
                    stack.Push(d);
                }
            }
        }

        public static List<ProjInfo> LoadProjects(string repo)
        {
            var list = new List<ProjInfo>();
            foreach (string csproj in WalkFiles(repo, "*.csproj"))
            {
                try
                {
                    var pi = new ProjInfo();
                    pi.FullPath = Path.GetFullPath(csproj);
                    var doc = new XmlDocument();
                    doc.Load(csproj);
                    XmlNode n = doc.SelectSingleNode("//*[local-name()='AssemblyName']");
                    if (n != null) pi.AsmName = (n.InnerText ?? "").Trim();
                    n = doc.SelectSingleNode("//*[local-name()='OutputType']");
                    if (n != null) pi.OutType = n.InnerText.Trim();
                    n = doc.SelectSingleNode("//*[local-name()='XapFilename']");
                    if (n != null) pi.XapFilename = (n.InnerText ?? "").Trim();
                    // Silverlight application 工程 = 会被编译成 xap 的工程
                    n = doc.SelectSingleNode("//*[local-name()='SilverlightApplication']");
                    pi.IsSilverlightApp = (n != null && n.InnerText.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                                          || pi.XapFilename.Length > 0;
                    if (pi.AsmName.Length == 0) pi.AsmName = Path.GetFileNameWithoutExtension(csproj);

                    string dir = Path.GetDirectoryName(pi.FullPath);
                    const string items = "//*[local-name()='Compile' or local-name()='Page' or local-name()='Content'"
                                       + " or local-name()='EmbeddedResource' or local-name()='Resource' or local-name()='None'"
                                       + " or local-name()='ApplicationDefinition' or local-name()='SplashScreen'"
                                       + " or local-name()='DesignData' or local-name()='DesignDataWithDesignTimeCreatableTypes']";
                    foreach (XmlNode inc in doc.SelectNodes(items))
                    {
                        XmlAttribute a = inc.Attributes["Include"];
                        if (a == null || a.Value.Length == 0) continue;
                        try { pi.Files.Add(Path.GetFullPath(Path.Combine(dir, a.Value.Replace('/', '\\')))); }
                        catch { }
                    }
                    pi.Files.Add(pi.FullPath); // csproj 本身被改也算影响该工程

                    foreach (XmlNode pr in doc.SelectNodes("//*[local-name()='ProjectReference']"))
                    {
                        XmlAttribute a = pr.Attributes["Include"];
                        if (a == null) continue;
                        try { pi.ProjectRefs.Add(Path.GetFullPath(Path.Combine(dir, a.Value.Replace('/', '\\'))).ToLower()); }
                        catch { }
                    }
                    foreach (XmlNode rf in doc.SelectNodes("//*[local-name()='Reference']"))
                    {
                        XmlAttribute a = rf.Attributes["Include"];
                        if (a == null) continue;
                        string nm = a.Value.Split(',')[0].Trim();
                        if (nm.Length > 0) pi.AssemblyRefs.Add(nm);
                    }
                    list.Add(pi);
                }
                catch { }
            }
            return list;
        }

        public class AnalyzeResult
        {
            public List<string> Outputs = new List<string>();
            public List<ProjInfo> Affected = new List<ProjInfo>();
            public List<ProjInfo> XapDeps = new List<ProjInfo>(); // 因含受影响 dll 而连带输出的 xap
            public int UnmatchedFiles;
            public int ChangedCount;
        }

        public static AnalyzeResult Analyze(string repo, List<string> shas, List<ProjInfo> projects)
        {
            var res = new AnalyzeResult();
            var set = new HashSet<string>(ChangedFilesRange(repo, shas), StringComparer.OrdinalIgnoreCase);
            res.ChangedCount = set.Count;

            foreach (var p in projects)
                if (p.Files.Any(f => set.Contains(f))) res.Affected.Add(p);

            foreach (var p in res.Affected) if (!res.Outputs.Contains(p.AsmOutput)) res.Outputs.Add(p.AsmOutput);
            foreach (var p in res.Affected)
                if (p.IsSilverlightApp && !res.Outputs.Contains(p.XapOutput))
                    res.Outputs.Add(p.XapOutput);

            // xap 里装着的 dll 受影响时,对应 xap 也要输出:
            // 从每个 silverlight application 工程出发,沿 ProjectReference/Reference 引用链
            // 找它包含的所有程序集,与受影响工程求交集
            var byPath = new Dictionary<string, ProjInfo>(StringComparer.OrdinalIgnoreCase);
            var byAsm = new Dictionary<string, ProjInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in projects)
            {
                byPath[p.FullPath.ToLower()] = p;
                if (!byAsm.ContainsKey(p.AsmName)) byAsm[p.AsmName] = p;
            }
            var affectedSet = new HashSet<ProjInfo>(res.Affected);
            foreach (var app in projects)
            {
                if (!app.IsSilverlightApp || affectedSet.Contains(app)) continue;
                var seen = new HashSet<ProjInfo>();
                var queue = new Queue<ProjInfo>();
                foreach (var r in app.ProjectRefs) if (byPath.ContainsKey(r) && seen.Add(byPath[r])) queue.Enqueue(byPath[r]);
                foreach (var r in app.AssemblyRefs) if (byAsm.ContainsKey(r) && seen.Add(byAsm[r])) queue.Enqueue(byAsm[r]);
                bool touches = false;
                while (queue.Count > 0 && !touches)
                {
                    var cur = queue.Dequeue();
                    if (affectedSet.Contains(cur)) { touches = true; break; }
                    foreach (var r in cur.ProjectRefs) if (byPath.ContainsKey(r) && seen.Add(byPath[r])) queue.Enqueue(byPath[r]);
                    foreach (var r in cur.AssemblyRefs) if (byAsm.ContainsKey(r) && seen.Add(byAsm[r])) queue.Enqueue(byAsm[r]);
                }
                if (touches)
                {
                    res.XapDeps.Add(app);
                    if (!res.Outputs.Contains(app.XapOutput)) res.Outputs.Add(app.XapOutput);
                }
            }

            res.Outputs.Sort(StringComparer.OrdinalIgnoreCase);

            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in res.Affected) foreach (var f in p.Files) matched.Add(f);
            res.UnmatchedFiles = set.Count(f => !matched.Contains(f));
            return res;
        }
    }

    public class MainForm : Form
    {
        TextBox txtRepo;
        Button btnBrowse, btnLoad, btnCopy;
        ComboBox cboBranch;
        ListBox lstCommits;
        TextBox txtProjects, txtResult;
        Label lblStatus;
        List<ProjInfo> projects;
        List<Logic.Commit> commits;
        string repo;

        public MainForm()
        {
            Text = "Local 出荷 DLL 清单工具";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(830, 610);
            Font = new Font("Microsoft YaHei UI", 9F);

            var lblRepo = new Label { Text = "仓库目录:", Location = new Point(12, 16), AutoSize = true };
            txtRepo = new TextBox { Location = new Point(80, 12), Width = 570 };
            btnBrowse = new Button { Text = "浏览...", Location = new Point(660, 10), Width = 74 };
            btnLoad = new Button { Text = "载入", Location = new Point(742, 10), Width = 74 };

            var lblBranch = new Label { Text = "分支:", Location = new Point(12, 50), AutoSize = true };
            cboBranch = new ComboBox { Location = new Point(80, 46), Width = 370, DropDownStyle = ComboBoxStyle.DropDownList };

            lstCommits = new ListBox
            {
                Location = new Point(12, 80),
                Size = new Size(370, 430),
                SelectionMode = SelectionMode.MultiExtended
            };
            lstCommits.HorizontalScrollbar = true;

            var lblProj = new Label { Text = "受影响工程 → 输出", Location = new Point(396, 84), AutoSize = true };
            txtProjects = new TextBox
            {
                Location = new Point(396, 104),
                Size = new Size(420, 130),
                Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = Color.White
            };
            var lblResult = new Label { Text = "输出 DLL(每行一个,已自动复制)", Location = new Point(396, 244), AutoSize = true };
            txtResult = new TextBox
            {
                Location = new Point(396, 264),
                Size = new Size(420, 246),
                Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 10F),
                BackColor = Color.White
            };

            btnCopy = new Button { Text = "复制清单", Location = new Point(716, 516), Size = new Size(100, 28) };
            lblStatus = new Label
            {
                Location = new Point(12, 520),
                Size = new Size(690, 80),
                ForeColor = Color.DimGray
            };

            Controls.Add(lblRepo); Controls.Add(txtRepo); Controls.Add(btnBrowse); Controls.Add(btnLoad);
            Controls.Add(lblBranch); Controls.Add(cboBranch);
            Controls.Add(lstCommits);
            Controls.Add(lblProj); Controls.Add(txtProjects);
            Controls.Add(lblResult); Controls.Add(txtResult);
            Controls.Add(btnCopy); Controls.Add(lblStatus);

            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = txtRepo.Text;
                    if (dlg.ShowDialog() == DialogResult.OK) { txtRepo.Text = dlg.SelectedPath; LoadRepo(); }
                }
            };
            btnLoad.Click += (s, e) => LoadRepo();
            cboBranch.SelectedIndexChanged += OnBranchChanged;
            lstCommits.SelectedIndexChanged += OnCommitSelected;
            btnCopy.Click += (s, e) =>
            {
                try { Clipboard.SetText(txtResult.Text); lblStatus.Text = "已复制到剪贴板。"; }
                catch (Exception ex) { lblStatus.Text = "复制失败:" + ex.Message; }
            };

            // exe 放在仓库里时自动识别
            string here = AppDomain.CurrentDomain.BaseDirectory;
            if (Directory.Exists(Path.Combine(here, ".git"))) { txtRepo.Text = here; LoadRepo(); }
        }

        void OnBranchChanged(object sender, EventArgs e) { LoadCommits(); }
        void OnCommitSelected(object sender, EventArgs e) { Analyze(); }

        void LoadRepo()
        {
            repo = txtRepo.Text.Trim();
            if (repo.Length == 0) repo = AppDomain.CurrentDomain.BaseDirectory;
            txtRepo.Text = repo;
            try
            {
                Logic.Git(repo, "rev-parse --show-toplevel");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "这个目录不是 git 仓库。";
                MessageBox.Show(ex.Message, "载入失败");
                return;
            }
            lblStatus.Text = "正在读取工程文件...";
            Application.DoEvents();
            projects = Logic.LoadProjects(repo);
            string current;
            List<string> branches;
            try { branches = Logic.ListBranches(repo, out current); }
            catch (Exception ex) { lblStatus.Text = ex.Message; return; }
            cboBranch.SelectedIndexChanged -= OnBranchChanged;
            cboBranch.Items.Clear();
            foreach (var b in branches) cboBranch.Items.Add(b);
            if (current != null) cboBranch.SelectedItem = current;
            else if (branches.Count > 0) cboBranch.SelectedIndex = 0;
            cboBranch.SelectedIndexChanged += OnBranchChanged;
            LoadCommits();
        }

        void LoadCommits()
        {
            if (cboBranch.SelectedItem == null) return;
            string err;
            commits = Logic.ListCommits(repo, cboBranch.SelectedItem.ToString(), out err);
            lstCommits.SelectedIndexChanged -= OnCommitSelected;
            lstCommits.Items.Clear();
            foreach (var c in commits)
                lstCommits.Items.Add(string.Format("{0}  {1}  ({2})",
                    c.Short, c.Subject.Replace('\n', ' '), c.Author));
            lstCommits.SelectedIndexChanged += OnCommitSelected;
            if (err != null) lblStatus.Text = "读取提交失败:" + err;
            else lblStatus.Text = "共 " + commits.Count + " 条提交。点选一条,或按住 Shift/Ctrl 选择连续的多条提交(从新到旧)。";
        }

        void Analyze()
        {
            if (lstCommits.SelectedIndices.Count == 0 || commits == null) return;
            if (projects == null) { lblStatus.Text = "请先载入仓库。"; return; }
            var shas = lstCommits.SelectedIndices.Cast<int>()
                .OrderBy(i => i)               // 列表从新到旧排列,索引小 = 更新
                .Select(i => commits[i].Sha)
                .ToList();
            var names = lstCommits.SelectedIndices.Cast<int>()
                .Select(i => commits[i].Short).ToList();
            lblStatus.Text = "分析中...(" + shas.Count + " 条提交)";
            Application.DoEvents();
            try
            {
                var r = Logic.Analyze(repo, shas, projects);
                var lines = new List<string>();
                foreach (var p in r.Affected)
                {
                    lines.Add(Path.GetFileName(p.FullPath) + " → " + p.AsmOutput);
                    if (p.IsSilverlightApp)
                        lines.Add(Path.GetFileName(p.FullPath) + " → " + p.XapOutput + "  (application)");
                }
                foreach (var p in r.XapDeps)
                    lines.Add(Path.GetFileName(p.FullPath) + " → " + p.XapOutput + "  (xap 内含受影响 dll)");
                txtProjects.Lines = lines.ToArray();
                txtResult.Lines = r.Outputs.ToArray();
                lblStatus.Text = string.Format(
                    "提交 [{0}]:改动文件 {1} 个,受影响工程 {2} 个,连带 xap {3} 个;另有 {4} 个文件不属于任何工程(已忽略,如 .sln/.config 等)。",
                    string.Join(", ", names), r.ChangedCount, r.Affected.Count, r.XapDeps.Count, r.UnmatchedFiles);
                try { Clipboard.SetText(txtResult.Text); } catch { }
            }
            catch (Exception ex)
            {
                txtResult.Lines = new string[0];
                txtProjects.Lines = new string[0];
                lblStatus.Text = "出错:" + ex.Message.Replace("\r\n", " ");
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length >= 2)
            {
                ConsoleMode(args[0], args.Skip(1).ToList());
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        static void ConsoleMode(string repo, List<string> shas)
        {
            try
            {
                repo = Path.GetFullPath(repo);
                var projects = Logic.LoadProjects(repo);
                var r = Logic.Analyze(repo, shas, projects);
                foreach (var o in r.Outputs) Console.WriteLine(o);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message.Replace("\r\n", " "));
                Environment.ExitCode = 1;
            }
        }
    }
}
