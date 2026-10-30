using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("Mafia II Azerbaijani Patch Installer")]
[assembly: AssemblyDescription("Offline installer for the Azerbaijani base-game text patch")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace MafiaAz {
    static class Program {
        [STAThread] static int Main(string[] args) {
            if (args.Length > 0) return Command(args);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new InstallerForm()); return 0;
        }
        static int Command(string[] args) {
            string report = null; for(int i=0;i<args.Length-1;i++)if(args[i]=="--report")report=args[i+1];
            try { using(var package=new Package()) {
                var engine=new PatchEngine(package,null);
                if(args.Length<2)throw new ArgumentException("A game path is required.");
                if(args[0]=="--install")engine.Install(args[1]);
                else if(args[0]=="--restore")engine.Restore(args[1]);
                else if(args[0]=="--verify")engine.Verify(args[1]);
                else if(args[0]=="--rebuild") {
                    if(args.Length<3)throw new ArgumentException("An output directory is required.");
                    string source=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
                    if(source.Equals(output,StringComparison.OrdinalIgnoreCase))throw new IOException("Test output must differ from the input directory.");
                    var results=package.Manifest.files.Select(f=>{string target=Util.Inside(output,f.path);byte[] rebuilt=package.Rebuild(File.ReadAllBytes(Util.Inside(source,f.path)),f);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,rebuilt);return new {path=f.path,sha256=Util.Hash(rebuilt)};}).ToArray();
                    if(report!=null)File.WriteAllText(report,Package.Json.Serialize(new {ok=true,files=results}));return 0;
                } else throw new ArgumentException("Unknown command.");
                if(report!=null)File.WriteAllText(report,Package.Json.Serialize(new {ok=true}));return 0;
            }} catch(Exception ex) { if(report!=null)File.WriteAllText(report,Package.Json.Serialize(new {ok=false,error=ex.Message}));return 1; }
        }
    }
    public sealed class InstallerForm : Form {
        readonly TextBox folder=new TextBox(),log=new TextBox(); readonly Button file=new Button(),browse=new Button(),install=new Button(),restore=new Button();
        readonly ProgressBar progress=new ProgressBar(); readonly Label status=new Label(); readonly BackgroundWorker worker=new BackgroundWorker();
        public InstallerForm() {
            Text="Mafia II - Azərbaycan dili yaması"; ClientSize=new Size(720,510); MinimumSize=new Size(736,549); MaximumSize=new Size(1100,750); StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(247,248,250); AutoScaleMode=AutoScaleMode.Dpi;
            var title=new Label {Text="Mafia II  |  Azərbaycan dili yaması",Location=new Point(24,22),AutoSize=true,Font=new Font("Segoe UI",20,FontStyle.Bold)}; Controls.Add(title);
            Controls.Add(new Label {Text="Classic PC  •  Base-game text  •  Original voices  •  v1.0.0",Location=new Point(27,66),AutoSize=true,ForeColor=Color.DimGray});
            Controls.Add(new Label {Text="Select mafia2.exe or the Mafia II game folder. The installer checks compatibility,\nbacks up the original files, and installs the Azerbaijani text patch offline.",Location=new Point(27,103),Size=new Size(665,52)});
            folder.SetBounds(27,174,438,28); folder.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;
            file.Text="Select mafia2.exe..."; file.SetBounds(477,171,214,34); file.Anchor=AnchorStyles.Top|AnchorStyles.Right; file.Click+=(s,e)=>{using(var d=new OpenFileDialog {Title="Select Mafia II Classic's mafia2.exe",Filter="Mafia II game executable (mafia2.exe)|mafia2.exe",CheckFileExists=true})if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.FileName;};
            browse.Text="Select folder..."; browse.SetBounds(477,211,214,32); browse.Anchor=AnchorStyles.Top|AnchorStyles.Right; browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog {Description="Select the Mafia II Classic game folder",ShowNewFolderButton=false})if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;};
            Controls.Add(new Label {Text="Keep the game language set to English. DLC and artwork are not changed.",Location=new Point(27,214),Size=new Size(440,40),ForeColor=Color.DimGray});
            install.Text="Install Azerbaijani patch"; install.SetBounds(27,267,310,44); install.BackColor=Color.FromArgb(28,92,72); install.ForeColor=Color.White; install.FlatStyle=FlatStyle.Flat; install.Anchor=AnchorStyles.Top|AnchorStyles.Left; install.Click+=(s,e)=>Start(false);
            restore.Text="Restore original English"; restore.SetBounds(353,267,338,44); restore.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right; restore.Click+=(s,e)=>Start(true);
            progress.SetBounds(27,330,664,18); progress.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            status.Text="Ready. Close the game before continuing.";status.SetBounds(27,357,664,38);status.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            log.SetBounds(27,400,664,86);log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.BackColor=Color.White;log.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
            Controls.AddRange(new Control[]{folder,file,browse,install,restore,progress,status,log});
            worker.WorkerReportsProgress=true;worker.ProgressChanged+=(s,e)=>{progress.Value=Math.Max(0,Math.Min(100,e.ProgressPercentage));status.Text=(string)e.UserState;log.AppendText(e.UserState+Environment.NewLine);};
            worker.DoWork+=(s,e)=>{var job=(Job)e.Argument;using(var p=new Package()){var engine=new PatchEngine(p,(n,t)=>worker.ReportProgress(n,t));if(job.Restore)engine.Restore(job.Path);else engine.Install(job.Path);}};
            worker.RunWorkerCompleted+=(s,e)=>{Busy(false);if(e.Error!=null){status.Text="Not completed. See details below.";log.AppendText(e.Error.Message+Environment.NewLine);MessageBox.Show(this,e.Error.Message,"Patch could not be completed",MessageBoxButtons.OK,MessageBoxIcon.Warning);}else MessageBox.Show(this,status.Text,"Mafia II Azerbaijani patch",MessageBoxButtons.OK,MessageBoxIcon.Information);};
            FormClosing+=(s,e)=>{if(worker.IsBusy){e.Cancel=true;MessageBox.Show(this,"Please wait for the current operation to finish. Original files are backed up before installation.","Operation in progress");}};
        }
        sealed class Job { public string Path;public bool Restore; }
        void Busy(bool value){folder.Enabled=file.Enabled=browse.Enabled=install.Enabled=restore.Enabled=!value;UseWaitCursor=value;}
        void Start(bool undo){try{string root=Util.Root(folder.Text);folder.Text=root;if(undo&&MessageBox.Show(this,"Restore the original English game files from this installer's verified backups?","Restore English",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;log.Clear();progress.Value=0;Busy(true);worker.RunWorkerAsync(new Job{Path=root,Restore=undo});}catch(Exception ex){MessageBox.Show(this,ex.Message,"Select Mafia II Classic",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
    }
}
