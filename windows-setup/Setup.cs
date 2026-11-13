// I-Team CRM single-file Windows installer.
// Installs for the current Windows user (no admin rights): unpacks the app
// carried inside this .exe to %LOCALAPPDATA%\Programs\I-Team CRM, adds Desktop
// and Start menu shortcuts, registers it in Settings > Apps and the
// iteamcrm:// link handler, then opens it. Run with /S for a silent install.
//
// Written on Dorsa's Windows PC (13 Nov 2026) and built there with the .NET
// Framework compiler that ships with Windows (no NSIS). @VERSION@ is filled
// in by build-setup.ps1. Same install folder as install.ps1, so the app's
// own signed updater works on top of either.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("I-Team CRM Setup")]
[assembly: AssemblyProduct("I-Team CRM")]
[assembly: AssemblyCompany("I-Team Collage")]
[assembly: AssemblyVersion("@VERSION@.0")]
[assembly: AssemblyFileVersion("@VERSION@.0")]

static class Setup
{
    const string AppName = "I-Team CRM";
    const string ExeName = "I-Team CRM.exe";
    const string Publisher = "I-Team Collage";
    public const string Version = "@VERSION@";
    const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ITeamCRM";
    const string ProtocolKey = @"Software\Classes\iteamcrm";

    static string Target
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = Array.Exists(args, a => a.Equals("/S", StringComparison.OrdinalIgnoreCase));
        if (silent)
        {
            try { Install(null); Launch(); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        Application.EnableVisualStyles();
        Application.Run(new SetupForm());
        return 0;
    }

    public static void Install(Action<int, string> progress)
    {
        Action<int, string> report = progress ?? ((p, s) => { });
        string target = Target;

        report(0, "Closing I-Team CRM if it is open...");
        foreach (Process p in Process.GetProcessesByName(AppName))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { }
        }
        Thread.Sleep(500);

        // Sign-in and settings live in %APPDATA%\I-Team CRM and are kept.
        if (Directory.Exists(target))
        {
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Delete(target, true); break; }
                catch { if (attempt >= 10) throw; Thread.Sleep(500); }
            }
        }
        Directory.CreateDirectory(target);

        string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.zip"))
        using (ZipArchive zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            long total = 0, done = 0;
            foreach (ZipArchiveEntry e in zip.Entries) total += e.Length;
            byte[] buf = new byte[1 << 20];
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                string dest = Path.GetFullPath(Path.Combine(target, rel));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.EndsWith(Path.DirectorySeparatorChar.ToString())) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (Stream src = e.Open())
                using (FileStream dst = File.Create(dest))
                {
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        done += n;
                        report((int)(done * 95 / Math.Max(1, total)), "Copying files...");
                    }
                }
            }
        }

        report(96, "Creating shortcuts...");
        string exe = Path.Combine(target, ExeName);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        foreach (string dir in new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
        {
            dynamic lnk = shell.CreateShortcut(Path.Combine(dir, AppName + ".lnk"));
            lnk.TargetPath = exe;
            lnk.WorkingDirectory = target;
            lnk.IconLocation = exe + ",0";
            lnk.Description = AppName + " by " + Publisher;
            lnk.Save();
        }

        report(98, "Registering the app...");
        long size = 0;
        foreach (string f in Directory.GetFiles(target, "*", SearchOption.AllDirectories)) size += new FileInfo(f).Length;
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstKey))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", Publisher);
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", target);
            k.SetValue("URLInfoAbout", "https://iteamcollage.com");
            k.SetValue("UninstallString", "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(target, "uninstall.ps1") + "\"");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
        }
        // iteamcrm:// links (browser sign-in hands the session back to the app).
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ProtocolKey))
        {
            k.SetValue("", "URL:" + AppName);
            k.SetValue("URL Protocol", "");
            using (RegistryKey icon = k.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
            using (RegistryKey cmd = k.CreateSubKey(@"shell\open\command")) cmd.SetValue("", "\"" + exe + "\" \"%1\"");
        }
        report(100, "Done.");
    }

    public static void Launch()
    {
        Process.Start(new ProcessStartInfo(Path.Combine(Target, ExeName)) { WorkingDirectory = Target, UseShellExecute = true });
    }
}

class SetupForm : Form
{
    readonly Label status = new Label();
    readonly ProgressBar bar = new ProgressBar();
    readonly Button go = new Button();
    readonly Button cancel = new Button();

    public SetupForm()
    {
        Text = "I-Team CRM Setup";
        Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(460, 190);

        Label title = new Label { Text = "Install I-Team CRM " + Setup.Version, Font = new Font("Segoe UI", 14f, FontStyle.Bold), Location = new Point(20, 16), AutoSize = true };
        status.Text = "Installs for your Windows user only. No admin rights needed.\nA shortcut is added to your Desktop and Start menu.";
        status.Location = new Point(22, 56);
        status.Size = new Size(420, 44);
        bar.Location = new Point(22, 106);
        bar.Size = new Size(416, 18);
        bar.Visible = false;
        go.Text = "Install";
        go.Size = new Size(100, 30);
        go.Location = new Point(232, 144);
        go.Click += delegate { Start(); };
        cancel.Text = "Cancel";
        cancel.Size = new Size(100, 30);
        cancel.Location = new Point(338, 144);
        cancel.Click += delegate { Close(); };
        AcceptButton = go;
        Controls.AddRange(new Control[] { title, status, bar, go, cancel });
    }

    void Start()
    {
        go.Enabled = false;
        cancel.Enabled = false;
        bar.Visible = true;
        Thread t = new Thread(() =>
        {
            try
            {
                Setup.Install((p, s) => BeginInvoke((Action)(() => { bar.Value = p; status.Text = s; })));
                BeginInvoke((Action)(() => { Setup.Launch(); Close(); }));
            }
            catch (Exception e)
            {
                BeginInvoke((Action)(() =>
                {
                    MessageBox.Show(this, "The install did not finish:\n\n" + e.Message + "\n\nPlease send a screenshot of this to contact@iteamcollage.com", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                }));
            }
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!cancel.Enabled && bar.Visible && bar.Value < 100) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
