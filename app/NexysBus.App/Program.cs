namespace NexysBus.App;

public static class Program
{
    /// <summary>--fresh: behave like a first start (ignore and do not write settings.json, separate WebView2 profile).</summary>
    public static bool Fresh { get; private set; }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--stacks") return Diag.WriteStacks(args);   // watchdog helper process
        if (args.Length >= 3 && args[0] == "--heap") return Diag.WriteHeap(args);
        SelfTest.Args = args;
        Fresh = args.Contains("--fresh");
        AppPaths.AppName = "NexysBus"; AppPaths.Ephemeral = () => Fresh || SelfTest.Enabled; AppPaths.Automation = () => SelfTest.Enabled;
        AppInfo.Name = "NexysBus"; AppInfo.Repo = "https://github.com/Kim-Hakseong/nexys-bus";
        AppId.Apply();
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
