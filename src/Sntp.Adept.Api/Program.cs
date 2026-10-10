using Sntp.Adept.Api;

var content = FindContentRoot();
var app = AdeptWebApp.Build(args, content, 5179);
app.Run();

static string FindContentRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "wwwroot", "index.html")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return AppContext.BaseDirectory;
}
