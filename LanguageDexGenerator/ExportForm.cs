using PKHeX.Core;

namespace LanguageDexGenerator;

public sealed class ExportForm : Form
{
    private readonly ISaveFileProvider _saveProvider;
    private readonly CheckedListBox _games = new() { Name = "Games", CheckOnClick = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly CheckedListBox _langs = new() { Name = "Languages", CheckOnClick = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly TextBox _ot = new() { Name = "OriginalTrainer", Text = "LANG", Width = 90, MaxLength = 6 };
    private readonly NumericUpDown _tid = new() { Name = "TID16", Maximum = ushort.MaxValue, Value = 12345, Width = 95 };
    private readonly NumericUpDown _sid = new() { Name = "SID16", Maximum = ushort.MaxValue, Value = 54321, Width = 95 };
    private readonly TextBox _path = new() { Name = "OutputDirectory", Dock = DockStyle.Fill };
    private readonly Label _counts = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Name = "Status", Text = "Listo para generar.", Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button _export = new() { Name = "Export", Text = "Generar y exportar", AutoSize = true };
    private readonly Button _import = new() { Name = "Import", Text = "Generar e importar a partida", AutoSize = true };
    private readonly Button _preview = new() { Name = "Preview", Text = "Vista previa", AutoSize = true };
    private readonly Button _cancel = new() { Name = "CancelGeneration", Text = "Cancelar", AutoSize = true, Enabled = false };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel _settings = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
    private CancellationTokenSource? _cancellation;

    public ExportForm(ISaveFileProvider saveProvider)
    {
        _saveProvider = saveProvider;
        Text = "Language Dex Generator · SwSh / Arceus / SV";
        Name = "LanguageDexForm";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 665);
        MinimumSize = new Size(760, 690);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        Controls.Add(root);
        root.Controls.Add(_settings, 0, 0);
        _settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 242));
        _settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        _settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        _settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var selectors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        var gameGroup = new GroupBox { Text = "Juegos de origen", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var languageGroup = new GroupBox { Text = "Idiomas originales", Dock = DockStyle.Fill, Padding = new Padding(10) };
        foreach (var g in DexCatalog.Games) _games.Items.Add($"{g.Label} · {g.SpeciesCount} especies", true);
        foreach (var l in LegacyLanguages.All) _langs.Items.Add($"{l.Label} ({l.Code})", l.Id != 7);
        gameGroup.Controls.Add(_games);
        languageGroup.Controls.Add(_langs);
        selectors.Controls.Add(gameGroup, 0, 0);
        selectors.Controls.Add(languageGroup, 1, 0);
        _settings.Controls.Add(selectors, 0, 0);

        var trainerGroup = new GroupBox { Text = "Entrenador · OT de 1 a 6 caracteres ASCII", Dock = DockStyle.Fill };
        var trainer = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), WrapContents = false };
        foreach (var pair in new (string Label, Control Field)[] { ("OT", _ot), ("TID16", _tid), ("SID16", _sid) })
        {
            trainer.Controls.Add(new Label { Text = pair.Label, AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            trainer.Controls.Add(pair.Field);
        }
        trainerGroup.Controls.Add(trainer);
        _settings.Controls.Add(trainerGroup, 0, 1);
        _path.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LanguageDexOutput");
        var folderGroup = new GroupBox { Text = "Carpeta para los archivos y el informe", Dock = DockStyle.Fill };
        var folder = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(5) };
        folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        folder.Controls.Add(_path, 0, 0);
        var browse = new Button { Text = "Examinar…", Dock = DockStyle.Fill };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.SelectedPath;
        };
        folder.Controls.Add(browse, 1, 0);
        folderGroup.Controls.Add(folder);
        _settings.Controls.Add(folderGroup, 0, 2);
        _settings.Controls.Add(new CheckBox { Text = "Omitir eventos y Mystery Gifts", Checked = true, Enabled = false, AutoSize = true }, 0, 3);
        root.Controls.Add(_counts, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.AddRange([_preview, _export, _import, _cancel]);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(_bar, 0, 3);
        root.Controls.Add(_status, 0, 4);

        _games.ItemCheck += (_, _) => BeginInvoke((Action)UpdateCounts);
        _langs.ItemCheck += (_, _) => BeginInvoke((Action)UpdateCounts);
        _preview.Click += (_, _) => Preview();
        _export.Click += async (_, _) => await GenerateAsync(import: false);
        _import.Click += async (_, _) => await GenerateAsync(import: true);
        _cancel.Click += (_, _) => { _cancellation?.Cancel(); _cancel.Enabled = false; _status.Text = "Cancelando…"; };
        FormClosing += (_, e) =>
        {
            if (_cancellation is null) return;
            e.Cancel = true;
            _cancellation.Cancel();
            _status.Text = "Cancelando; espera antes de cerrar.";
        };
        UpdateCounts();
    }

    private GenerationRequest BuildRequest() => new()
    {
        GameKeys = _games.CheckedIndices.Cast<int>().Select(i => DexCatalog.Games[i].Key).ToList(),
        LanguageIds = _langs.CheckedIndices.Cast<int>().Select(i => LegacyLanguages.All[i].Id).ToList(),
        OriginalTrainer = _ot.Text.Trim(), TID16 = (ushort)_tid.Value, SID16 = (ushort)_sid.Value,
        OutputDirectory = _path.Text.Trim(), ExcludeMysteryGifts = true,
    };

    private void UpdateCounts()
    {
        var req = BuildRequest();
        var counts = DexCatalog.Games.Where(g => req.GameKeys.Contains(g.Key)).Select(g =>
        {
            int count = g.SpeciesCount * req.LanguageIds.Count;
            return $"{g.Key.ToUpperInvariant()}: {count} ({SaveBoxImporter.RequiredBoxes(count)} cajas)";
        });
        _counts.Text = $"{req.PlannedCount:N0} combinaciones previstas · {req.LanguageIds.Count} idiomas\n" + string.Join("     ", counts);
    }

    private void Preview()
    {
        try
        {
            var req = BuildRequest(); req.Validate();
            MessageBox.Show(this, _counts.Text + "\n\nSe omiten las combinaciones sin encuentro nativo válido.\n" +
                "Exportar crea archivos. Importar requiere un solo juego y suficientes espacios vacíos en las cajas; pide confirmación después de generar.", "Vista previa");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Revisa la configuración"); }
    }

    private async Task GenerateAsync(bool import)
    {
        GenerationRequest request;
        SaveFile? save = null;
        string? fingerprint = null;
        SimpleTrainerInfo? handlingTrainer;
        try
        {
            request = BuildRequest(); request.Validate();
            var loadedSave = _saveProvider.SAV;
            handlingTrainer = loadedSave.State.Exportable ? new SimpleTrainerInfo(loadedSave) : null;
            if (import)
            {
                if (request.GameKeys.Count != 1) throw new InvalidOperationException("Para importar, selecciona solo el juego de la partida abierta.");
                save = _saveProvider.SAV;
                SaveBoxImporter.Preflight(save, DexCatalog.Games.Single(g => g.Key == request.GameKeys[0]), request.PlannedCount);
                fingerprint = SaveBoxImporter.Fingerprint(save);
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se puede iniciar"); return; }

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _settings.Enabled = _export.Enabled = _import.Enabled = _preview.Enabled = false;
        _cancel.Enabled = true;
        _bar.Value = 0;
        _status.Text = "Buscando encuentros nativos…";
        try
        {
            var staged = new List<PKM>();
            var progress = new Progress<(int Current, int Total, string Label)>(p =>
            {
                if (_cancellation is null || cancellation.IsCancellationRequested) return;
                _bar.Value = Math.Clamp((int)(100L * p.Current / Math.Max(1, p.Total)), 0, 100);
                _status.Text = $"{p.Current}/{p.Total} · {p.Label}";
            });
            var summary = await Task.Run(() => GenerationEngine.Run(request, progress, cancellation.Token,
                onGenerated: import ? (_, pk) => staged.Add(pk) : null, handlingTrainer: handlingTrainer));
            _cancel.Enabled = false;
            string report = Path.Combine(summary.Folder, "report.csv");
            if (summary.Cancelled || cancellation.IsCancellationRequested)
            {
                _status.Text = $"Cancelado · {summary.Exported} archivos conservados. No se importó nada.";
                MessageBox.Show(this, _status.Text + "\nInforme: " + report, "Generación cancelada");
                return;
            }
            if (import && staged.Count > 0)
            {
                if (!ReferenceEquals(save, _saveProvider.SAV)) throw new InvalidOperationException("Cambió la partida abierta; no se importó nada.");
                string message = $"Generados y verificados: {staged.Count}\nOmitidos: {summary.Skipped}\nPartida: {save!.Version}\n\n" +
                    "Se creará una copia de seguridad de la partida original y se insertarán estos Pokémon en los espacios vacíos disponibles dentro de PKHeX. Los Pokémon existentes se conservarán.\n" +
                    "Después podrás guardar la partida desde PKHeX.\n\n¿Importar ahora?";
                if (MessageBox.Show(this, message, "Confirmar importación", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    string backup = SaveBoxImporter.Commit(save, request, staged, summary.Folder, fingerprint!);
                    _saveProvider.ReloadSlots();
                    _status.Text = $"Importados: {staged.Count} · Omitidos: {summary.Skipped}";
                    MessageBox.Show(this, _status.Text + "\nCopia original: " + backup + "\nInforme: " + report +
                        "\n\nGuarda la partida en PKHeX para conservar los cambios.", "Importación terminada");
                    return;
                }
            }
            _status.Text = $"Exportados: {summary.Exported} · Omitidos: {summary.Skipped}";
            MessageBox.Show(this, _status.Text + "\nInforme: " + report, "Archivos listos");
        }
        catch (Exception ex) { _status.Text = "No se completó la operación."; MessageBox.Show(this, ex.Message, "Error"); }
        finally
        {
            _cancellation = null;
            _settings.Enabled = _export.Enabled = _import.Enabled = _preview.Enabled = true;
            _cancel.Enabled = false;
        }
    }
}
