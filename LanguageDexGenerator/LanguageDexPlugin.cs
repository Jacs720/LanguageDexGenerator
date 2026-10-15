using System.Windows.Forms;
using PKHeX.Core;

namespace LanguageDexGenerator;

public sealed class LanguageDexPlugin : IPlugin
{
    public string Name => "Language Dex Generator (Gen 8-9)";
    public int Priority => 200;
    public ISaveFileProvider SaveFileEditor { get; private set; } = null!;

    public void Initialize(params object[] args)
    {
        var expected = typeof(LanguageDexPlugin).Assembly.GetReferencedAssemblies().Single(a => a.Name == "PKHeX.Core").Version;
        var actual = typeof(PKM).Assembly.GetName().Version;
        if (expected != actual)
            throw new InvalidOperationException($"Language Dex Generator requiere PKHeX {expected}; se encontró {actual}. Usa el DLL compilado para esa versión.");
        SaveFileEditor = args.OfType<ISaveFileProvider>().FirstOrDefault()
            ?? throw new InvalidOperationException("PKHeX did not provide ISaveFileProvider.");
        var menu = args.OfType<ToolStrip>().FirstOrDefault()
            ?? throw new InvalidOperationException("PKHeX did not provide ToolStrip.");
        var tools = menu.Items.Find("Menu_Tools", false).OfType<ToolStripDropDownItem>().FirstOrDefault()
            ?? throw new InvalidOperationException("Tools menu not found.");
        var entry = new ToolStripMenuItem(Name) { Name = "Menu_LanguageDexGen" };
        entry.Click += (_, _) =>
        {
            using var form = new ExportForm(SaveFileEditor);
            form.ShowDialog(menu.FindForm());
        };
        tools.DropDownItems.Add(entry);
    }

    public void NotifySaveLoaded() { /* Active save is queried on each import; no cached save pointers. */ }
    public bool TryLoadFile(string filePath) => false;
}
