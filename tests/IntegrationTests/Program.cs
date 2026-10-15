using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using LanguageDexGenerator;
using PKHeX.Core;
using PKHeX.WinForms;

internal static class Program
{
    private static readonly string Results = Path.GetFullPath(Path.Combine("test-results", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")));
    private static readonly List<object> Outcomes = [];
    private static readonly List<(GameProfile Game, PKM Pokemon)> Generated = [];
    private static int _failed;
    private static GenerationSummary? _fullRun;

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Results);
        Console.WriteLine("RESULTS " + Results);
        if (args.Contains("--regression"))
        {
            Test("Loaded PLA trainer does not prevent native generation", LoadedTrainer);
            Test("Occupied slots outside the requested space do not block PLA", OccupiedSlots);
        }
        else if (args.Contains("--ui"))
        {
            Test("PKHeX real window, isolated plugin loader, Tools entry and dialog", HostUi);
        }
        else
        {
            Test("Catalog, 1944 maximum and 1728 default", Catalog);
            Test("Complete nine-language export, reopen and fresh legality analysis", FullExport);
            Test("Native starters, Wyrdeer, Sprigatito, Hydrapple and event exclusions", RequiredSpecies);
            Test("Import preflight: full storage capacity regardless of unlocked count, occupied/protected slots", Preflights);
            Test("Loaded trainers: full export, PLA report request and partial-box import", LoadedTrainer);
            Test("Occupied slots outside the requested space do not block PLA", OccupiedSlots);
            Test("Partial import, byte-identical backup, only storage block changes", PartialImport);
            Test("Invalid, duplicate, unexpected-language and changed-save rejection", ImportFailures);
            Test("All eight-language batches import beyond one unlocked box and reopen in three saves", FullImports);
            Test("Cancellation preserves files and complete run accounting", Cancellation);
            Test("Concurrent job is rejected and lock is released", SingleJob);
            Test("Failure reports survive and reruns never overwrite", FailureAndRerun);
        }
        File.WriteAllText(Path.Combine(Results, "results.json"), JsonSerializer.Serialize(new
        {
            coreVersion = typeof(PKM).Assembly.GetName().Version?.ToString(),
            hostVersion = typeof(Main).Assembly.GetName().Version?.ToString(),
            failed = _failed, tests = Outcomes, fullRun = _fullRun,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"TESTS {Outcomes.Count - _failed} passed; {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action run)
    {
        var watch = Stopwatch.StartNew();
        try { run(); Outcomes.Add(new { name, passed = true, seconds = watch.Elapsed.TotalSeconds }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Outcomes.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + "\n" + ex); }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        catch (IOException) { return; }
        throw new InvalidOperationException("Did not reject: " + label);
    }

    private static GenerationRequest Request(string? game = null, params int[] languages) => new()
    {
        GameKeys = game is null ? ["swsh", "pla", "sv"] : [game],
        LanguageIds = languages.Length == 0 ? [1, 2, 3, 4, 5, 8, 9, 10] : [.. languages],
        OutputDirectory = Results,
    };

    private static SaveFile Blank(string key, int boxes = 32)
    {
        SaveFile save = key switch { "swsh" => new SAV8SWSH(), "pla" => new SAV8LA(), "sv" => new SAV9SV(), _ => throw new ArgumentException() };
        // PKHeX blank constructors use untyped placeholder blocks. Give only these
        // synthetic fixtures serializable types; this code never handles user saves.
        foreach (var block in ((ISCBlockArray)save).AllBlocks.Where(b => b.Type == SCTypeCode.None))
        {
            var type = block.Data.Length switch
            {
                0 => SCTypeCode.Bool1, 1 => SCTypeCode.Byte, 2 => SCTypeCode.UInt16,
                4 => SCTypeCode.UInt32, 8 => SCTypeCode.UInt64, _ => SCTypeCode.Object,
            };
            typeof(SCBlock).GetProperty(nameof(SCBlock.Type))!.SetValue(block, type);
        }
        if (save is SAV8SWSH s8) { s8.TeamIndexes.ClearBattleTeams(); s8.TeamIndexes.SaveBattleTeams(); }
        if (save is SAV9SV s9)
        {
            s9.TeamIndexes.ClearBattleTeams(); s9.TeamIndexes.SaveBattleTeams();
            s9.Blocks.GetBlock(SaveBlockAccessor9SV.KGameLanguage).ChangeStoredType(SCTypeCode.Int32);
        }
        save.Version = key switch { "swsh" => GameVersion.SW, "pla" => GameVersion.PLA, _ => GameVersion.SL };
        save.OT = "TEST"; save.Language = 2; save.ID32 = 123456;
        save.BoxesUnlocked = boxes;
        save.State.Edited = false;
        return save;
    }

    private static SaveFile ReopenFixture(byte[] data, string key)
    {
        Assert(SwishCrypto.GetIsHashValid(data), "Serialized save hash.");
        // Synthetic blank blocks don't have the exact retail save length used by
        // SaveUtil's autodetection. Exercise each game's actual parser explicitly.
        return key switch
        {
            "swsh" => new SAV8SWSH(data), "pla" => new SAV8LA(data), "sv" => new SAV9SV(data),
            _ => throw new ArgumentException(),
        };
    }

    private static void Catalog()
    {
        Assert(Request().PlannedCount == 1728, "Default total.");
        var all = Request(null, LegacyLanguages.All.Select(l => l.Id).ToArray());
        Assert(all.PlannedCount == 1944, "Maximum total.");
        Assert(DexCatalog.Games.Select(g => g.SpeciesCount).SequenceEqual([89, 7, 120]), "Ranges.");
        Assert(!LegacyLanguages.IsAllowed(11) && !LegacyLanguages.IsAllowed(6), "Forbidden language.");
        foreach (int forbidden in new[] { 0, 6, 11, 12 })
        {
            Reject(() => Request("swsh", forbidden).Validate(), "language");
            Reject(() => NativeGenerationAdapter.TryGenerate(DexCatalog.Games[0], 810, forbidden, "LANG", 1, 2), "adapter language");
        }
        Reject(() => Request("za", 1).Validate(), "ZA");
        Reject(() => Request("sv", 1, 1).Validate(), "duplicate language");
        Reject(() => (Request() with { ExcludeMysteryGifts = false }).Validate(), "events");
        Reject(() => NativeGenerationAdapter.TryGenerate(DexCatalog.Games[0], 809, 1, "LANG", 1, 2), "old species");
        Reject(() => (Request() with { OriginalTrainer = "TOOLONG" }).Validate(), "OT length");
        Reject(() => (Request() with { OriginalTrainer = "\n" }).Validate(), "OT control");
    }

    private static void LoadedTrainer()
    {
        var save = ReopenFixture(Blank("pla", 2).Write().ToArray(), "pla");
        Assert(save.State.Exportable, "Must reproduce a loaded, exportable save.");
        ParseSettings.InitFromSaveFileData(save);
        try
        {
            var (pk, reason) = NativeGenerationAdapter.TryGenerate(DexCatalog.Games[1], 899, 1, "LANG", 12345, 54321,
                handlingTrainer: new SimpleTrainerInfo(save));
            Assert(pk is not null, "Loaded trainer blocked Wyrdeer: " + reason);
            Assert(new LegalityAnalysis(pk!).Valid, "Must pass actual active-trainer legality.");
            Assert(pk!.OriginalTrainerName == "LANG" && pk.TID16 == 12345 && pk.SID16 == 54321 && pk.Language == 1,
                "Requested original trainer and language must survive handler update.");
            Assert(pk.CurrentHandler == 1 && pk.HandlingTrainerName == save.OT && pk is IHomeTrack { Tracker: 0 },
                "Use normal handling trainer without inventing HOME history.");

            // The user's exact eight-language selection; preserve occupied slots at
            // both ends, a gap within the first two boxes, and outside unlocked boxes.
            int[] occupiedSlots = [0, 17, 59, 959];
            foreach (int slot in occupiedSlots) save.SetBoxSlotAtIndex(pk.Clone(), slot, EntityImportSettings.None);
            byte[] before = save.Clone().Write().ToArray();
            var originalSlots = Enumerable.Range(0, 960).Select(i => SlotBytes(save.GetBoxSlotAtIndex(i))).ToArray();
            var batch = new List<PKM>();
            var request = Request("pla", 1, 3, 4, 5, 7, 8, 9, 10);
            var summary = GenerationEngine.Run(request, onGenerated: (_, p) => batch.Add(p), handlingTrainer: save);
            Assert(summary.Exported == 56 && summary.Skipped == 0 && batch.Count == 56, "User's PLA request must export all 56.");
            string backup = SaveBoxImporter.Commit(save, request, batch, summary.Folder, SaveBoxImporter.Fingerprint(save));
            Assert(File.ReadAllBytes(backup).SequenceEqual(before), "Partially occupied backup unchanged.");
            var reopened = ReopenFixture(save.Clone().Write().ToArray(), "pla");
            int[] inserted = Enumerable.Range(0, 60).Except(occupiedSlots).Take(56).ToArray();
            for (int i = 0; i < 960; i++)
            {
                var p = reopened.GetBoxSlotAtIndex(i);
                if (inserted.Contains(i)) Assert(new LegalityAnalysis(p).Valid, "Imported active-trainer legality.");
                else Assert(SlotBytes(p).SequenceEqual(originalSlots[i]), "Existing or unselected slot changed: " + i);
            }
            Console.WriteLine($"PLA REGRESSION: 56 exported/imported; 4 existing Pokémon preserved. Report: {summary.Folder}");

            foreach (string key in new[] { "swsh", "pla", "sv" })
            {
                var context = ReopenFixture(Blank(key).Write().ToArray(), key);
                ParseSettings.InitFromSaveFileData(context);
                var all = GenerationEngine.Run(Request(null, LegacyLanguages.All.Select(l => l.Id).ToArray()),
                    onGenerated: (_, p) => Assert(new LegalityAnalysis(p).Valid, "Active-trainer legality " + p.Species),
                    handlingTrainer: context);
                Assert(all.Exported == 1908 && all.Skipped == 36, "Full export under loaded " + key + " trainer.");
                Assert(ParseSettings.Settings.Handler.CheckActiveHandler, "Do not disable PKHeX handler checks.");
                Console.WriteLine($"LOADED {key}: {all.Exported} valid files; handler checks remain enabled.");
            }

            // The OT itself must remain the current handler when it owns the Pokémon.
            var own = ReopenFixture(Blank("pla").Write().ToArray(), "pla");
            own.OT = "LANG"; own.TID16 = 12345; own.SID16 = 54321;
            ParseSettings.InitFromSaveFileData(own);
            (pk, reason) = NativeGenerationAdapter.TryGenerate(DexCatalog.Games[1], 899, 1, "LANG", 12345, 54321, handlingTrainer: own);
            Assert(pk is not null && pk.CurrentHandler == 0 && new LegalityAnalysis(pk).Valid, "Matching OT handling: " + reason);
        }
        finally { ParseSettings.ClearActiveTrainer(); }
    }

    private static void OccupiedSlots()
    {
        var save = Blank("pla", 2);
        var occupied = new PA8 { Species = 25 };
        save.SetBoxSlotAtIndex(occupied, 59, EntityImportSettings.None);
        save.SetBoxSlotAtIndex(occupied, 959, EntityImportSettings.None);
        SaveBoxImporter.Preflight(save, DexCatalog.Games[1], 56);
    }

    private static byte[] SlotBytes(PKM pk)
    {
        var bytes = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(bytes);
        return bytes;
    }

    private static void FullExport()
    {
        var sentinel = Blank("sv");
        string fingerprint = SaveBoxImporter.Fingerprint(sentinel);
        var request = Request(null, LegacyLanguages.All.Select(l => l.Id).ToArray());
        _fullRun = GenerationEngine.Run(request, onGenerated: (g, p) => Generated.Add((g, p)));
        Assert(_fullRun.Requested == 1944 && _fullRun.Exported == 1908 && _fullRun.Skipped == 36 && _fullRun.Unprocessed == 0 && !_fullRun.Cancelled,
            "Expected 212 native species × 9 languages, four event-only species excluded.");
        Assert(File.ReadAllLines(Path.Combine(_fullRun.Folder, "report.csv")).Length == 1945, "CSV row count.");
        var unique = new HashSet<(string, ushort, int)>();
        foreach (string path in Directory.EnumerateFiles(_fullRun.Folder, "*.p*", SearchOption.AllDirectories))
        {
            var game = DexCatalog.Games.Single(g => Path.GetExtension(path) == "." + g.FileExtension);
            var pk = EntityFormat.GetFromBytes(File.ReadAllBytes(path), NativeGenerationAdapter.CreateBlank(game).Context)!;
            Assert(PokemonValidation.GetError(pk, game, pk.Species, pk.Language) is null, "Reopened legality " + path);
            Assert(unique.Add((game.Key, pk.Species, pk.Language)), "Duplicate file.");
        }
        Assert(unique.Count == 1908, "All files reopened.");
        Assert(SaveBoxImporter.Fingerprint(sentinel) == fingerprint && !sentinel.State.Edited, "Export touched save.");
        Console.WriteLine($"EXPORT {_fullRun.Exported} legal files, {_fullRun.Skipped} event exclusions.");
    }

    private static void RequiredSpecies()
    {
        foreach (int species in new[] { 810, 813, 816, 899, 906, 1019 })
        foreach (int language in new[] { 1, 3 })
        {
            var entry = Generated.Single(p => p.Pokemon.Species == species && p.Pokemon.Language == language);
            Assert(entry.Pokemon.OriginalTrainerName == "LANG" && entry.Pokemon.TID16 == 12345 && entry.Pokemon.SID16 == 54321, "Trainer.");
            PokemonValidation.SerializeAndValidate(entry.Pokemon, entry.Game);
        }
        Assert(!Generated.Any(p => p.Pokemon.Species is 893 or 1009 or 1010 or 1025), "Events should be excluded.");
    }

    private static void Preflights()
    {
        foreach (var game in DexCatalog.Games)
        {
            var save = Blank(game.Key);
            SaveBoxImporter.Preflight(save, game, Request(game.Key).PlannedCount);
        }
        var sv = Blank("sv");
        SaveBoxImporter.Preflight(sv, DexCatalog.Games[2], 960);
        Assert(SaveBoxImporter.RequiredBoxes(960) == 32, "Exact fit.");
        Reject(() => SaveBoxImporter.Preflight(sv, DexCatalog.Games[2], 1080), "1080");
        Reject(() => SaveBoxImporter.Preflight(sv, DexCatalog.Games[0], 712), "wrong save");
        foreach (var game in DexCatalog.Games)
        {
            var limited = Blank(game.Key, 0);
            SaveBoxImporter.Preflight(limited, game, limited.BoxCount * limited.BoxSlotCount);
            Assert(limited.BoxesUnlocked == 0 && !limited.State.Edited, "Preflight must ignore and preserve unlocked count.");
        }
        Reject(() => SaveBoxImporter.Preflight(sv, DexCatalog.Games[2], -1), "negative count");
        var swsh = (SAV8SWSH)Blank("swsh");
        swsh.TeamIndexes.TeamSlots[0] = 0;
        swsh.TeamIndexes.SaveBattleTeams();
        SaveBoxImporter.Preflight(swsh, DexCatalog.Games[0], 89);
        Reject(() => SaveBoxImporter.Preflight(swsh, DexCatalog.Games[0], 960), "protected slot reduces capacity");
        var first = Generated.First(p => p.Game.Key == "swsh" && p.Pokemon.Language == 1).Pokemon;
        SaveBoxImporter.Commit(swsh, Request("swsh", 1), [first], Path.Combine(Results, "protected-slot"), SaveBoxImporter.Fingerprint(swsh));
        var reopened = (SAV8SWSH)ReopenFixture(swsh.Clone().Write().ToArray(), "swsh");
        Assert(reopened.GetBoxSlotAtIndex(0).Species == 0 && reopened.GetBoxSlotAtIndex(1).Species == first.Species &&
            reopened.TeamIndexes.TeamSlots[0] == 0, "Skip protected empty slot without modifying team reservation.");
        swsh.TeamIndexes.ClearBattleTeams();
        swsh.SetBoxSlotAtIndex(Generated.First(p => p.Game.Key == "swsh").Pokemon.Clone(), 959, EntityImportSettings.None);
        SaveBoxImporter.Preflight(swsh, DexCatalog.Games[0], 89);
        Reject(() => SaveBoxImporter.Preflight(swsh, DexCatalog.Games[0], 960), "occupied slot reduces capacity");
    }

    private static void PartialImport()
    {
        foreach (var game in DexCatalog.Games)
        {
            var save = Blank(game.Key);
            var originals = ((ISCBlockArray)save).AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
            byte[] before = save.Clone().Write().ToArray();
            string fingerprint = SaveBoxImporter.Fingerprint(save);
            var two = Generated.Where(p => p.Game == game && p.Pokemon.Species == game.FirstSpecies && p.Pokemon.Language is 1 or 3).Select(p => p.Pokemon).ToArray();
            string backup = SaveBoxImporter.Commit(save, Request(game.Key, 1, 3), two, Path.Combine(Results, "partial-" + game.Key), fingerprint);
            Assert(File.ReadAllBytes(backup).SequenceEqual(before), "Backup must equal original.");
            Assert(save.BoxData.Count(p => p.Species != 0) == 2 && save.BoxesUnlocked == 32 && save.State.Edited, "Only generated inserted.");
            int changed = ((ISCBlockArray)save).AllBlocks.Count(b => !b.Data.SequenceEqual(originals[b.Key]));
            Assert(changed == 1, "Only box storage block may change, found " + changed);
            Assert(save.GetBoxSlotAtIndex(0).OriginalTrainerName == "LANG", "No trainer rewrite.");
            var restored = ReopenFixture(File.ReadAllBytes(backup), game.Key);
            Assert(restored is not null && restored.BoxData.All(p => p.Species == 0), "Backup reopens empty.");
        }
    }

    private static void ImportFailures()
    {
        var save = Blank("swsh");
        string hash = SaveBoxImporter.Fingerprint(save);
        var one = Generated.First(p => p.Game.Key == "swsh" && p.Pokemon.Language == 1).Pokemon;
        string folder = Path.Combine(Results, "rejected");
        Reject(() => SaveBoxImporter.Commit(save, Request("swsh", 1), [one, one], folder, hash), "duplicate");
        Reject(() => SaveBoxImporter.Commit(save, Request("swsh", 3), [one], folder, hash), "unselected language");
        var bad = one.Clone(); bad.IsNicknamed = true;
        Reject(() => SaveBoxImporter.Commit(save, Request("swsh", 1), [bad], folder, hash), "invalid entity");
        Assert(!Directory.Exists(folder), "Rejected import created backup.");
        save.OT = "EDIT";
        Reject(() => SaveBoxImporter.Commit(save, Request("swsh", 1), [one], folder, hash), "changed save");
        save.OT = "TEST";
        Assert(hash == SaveBoxImporter.Fingerprint(save), "Rejected import mutated original.");
        string file = Path.Combine(Results, "blocked-folder"); File.WriteAllText(file, "sentinel");
        Reject(() => SaveBoxImporter.Commit(save, Request("swsh", 1), [one], file, hash), "backup path failure");
        Assert(hash == SaveBoxImporter.Fingerprint(save) && !save.State.Edited, "Backup failure mutated save.");
    }

    private static void FullImports()
    {
        foreach (var game in DexCatalog.Games)
        {
            var request = Request(game.Key);
            var batch = Generated.Where(p => p.Game == game && request.LanguageIds.Contains(p.Pokemon.Language)).Select(p => p.Pokemon).ToArray();
            var save = Blank(game.Key, 1);
            SaveBoxImporter.Commit(save, request, batch, Path.Combine(Results, "full-" + game.Key), SaveBoxImporter.Fingerprint(save));
            byte[] data = save.Clone().Write().ToArray();
            File.WriteAllBytes(Path.Combine(Results, $"generated-test-{game.Key}.main"), data);
            var reopened = ReopenFixture(data, game.Key);
            Assert(reopened.BoxData.Count(p => p.Species != 0) == batch.Length, "Save reopen count.");
            Assert(reopened.BoxesUnlocked == 1, "Import must not change unlocked-box metadata.");
            Assert(reopened.GetBoxSlotAtIndex(30).Species != 0, "Must import past the unlocked-box count.");
            for (int i = 0; i < batch.Length; i++)
                Assert(PokemonValidation.GetError(reopened.GetBoxSlotAtIndex(i), game, batch[i].Species, batch[i].Language) is null, "Imported legality.");
            Console.WriteLine($"IMPORT {game.Key}: {batch.Length} legal Pokémon in disposable save.");
        }
    }

    private static void Cancellation()
    {
        using var cts = new CancellationTokenSource();
        int count = 0;
        var summary = GenerationEngine.Run(Request("swsh", 1), cancel: cts.Token,
            onGenerated: (_, _) => { if (++count == 5) cts.Cancel(); });
        Assert(summary.Cancelled && summary.Exported == 5 && summary.Unprocessed == 84 && summary.Skipped == 0, "Cancellation accounting.");
        Assert(File.ReadAllLines(Path.Combine(summary.Folder, "report.csv")).Length == 6, "Cancellation CSV.");
        using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
        summary = GenerationEngine.Run(Request("pla", 1), cancel: alreadyCancelled.Token);
        Assert(summary.Cancelled && summary.Exported == 0 && summary.Unprocessed == 7, "Cancel before starting.");
    }

    private static void SingleJob()
    {
        using var cts = new CancellationTokenSource();
        bool rejected = false;
        GenerationEngine.Run(Request("pla", 1), cancel: cts.Token, onGenerated: (_, _) =>
        {
            Reject(() => GenerationEngine.Run(Request("pla", 1)), "nested job");
            rejected = true; cts.Cancel();
        });
        Assert(rejected, "Job lock.");
        Assert(GenerationEngine.Run(Request("pla", 1)).Exported == 7, "Lock not released.");
    }

    private static void FailureAndRerun()
    {
        var req = Request("pla", 1) with { OutputDirectory = Path.Combine(Results, "failure") };
        Reject(() => GenerationEngine.Run(req, onGenerated: (_, _) => throw new IOException("Injected callback failure")), "callback failure");
        string failedRun = Directory.GetDirectories(req.OutputDirectory).Single();
        Assert(File.Exists(Path.Combine(failedRun, "summary.json")), "Failure summary.");
        using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(failedRun, "summary.json"))))
            Assert(document.RootElement.GetProperty("summary").GetProperty("Failure").GetString() == "Injected callback failure", "Failure cause recorded.");
        var first = GenerationEngine.Run(req);
        byte[] original = File.ReadAllBytes(Path.Combine(first.Folder, "report.csv"));
        var second = GenerationEngine.Run(req);
        Assert(first.Folder != second.Folder && original.SequenceEqual(File.ReadAllBytes(Path.Combine(first.Folder, "report.csv"))), "Rerun overwrote files.");
    }

    private static void HostUi()
    {
        string hostFolder = Path.Combine(Results, "host");
        string pluginsFolder = Path.Combine(hostFolder, "plugins");
        Directory.CreateDirectory(pluginsFolder);
        File.Copy(typeof(LanguageDexPlugin).Assembly.Location, Path.Combine(pluginsFolder, "LanguageDexGenerator.dll"));
        // Run actual PKHeX form and loader inside the test process, with isolated resources.
        PKHeX.WinForms.Main.Settings.LocalResources.SetLocalPath(hostFolder);
        StartupUtil.ReloadSettings(PKHeX.WinForms.Main.Settings);
        using var main = new Main();
        var plugins = new List<IPlugin>();
        var context = PluginLoader.LoadPlugins(pluginsFolder, plugins, false);
        Assert(plugins.Count == 1, "Plugin loader discovery.");
        var menu = (MenuStrip)typeof(Main).GetField("menuStrip1", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
        var provider = (ISaveFileProvider)typeof(Main).GetField("C_SAV", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
        plugins[0].Initialize(provider, menu);
        var tools = (ToolStripDropDownItem)menu.Items.Find("Menu_Tools", false).Single();
        var entry = (ToolStripMenuItem)tools.DropDownItems.Find("Menu_LanguageDexGen", false).Single();
        main.Show();
        Application.DoEvents();
        Capture(main, "pkhex-window.png");
        tools.ShowDropDown();
        using (var bitmap = new Bitmap(tools.DropDown.Width, tools.DropDown.Height))
        {
            tools.DropDown.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Results, "tools-menu.png"), ImageFormat.Png);
        }
        tools.HideDropDown();
        Exception? error = null;
        bool opened = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (_, _) =>
        {
            var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Name == "LanguageDexForm");
            if (form is null) return;
            timer.Stop();
            try
            {
                opened = true;
                var games = (CheckedListBox)form.Controls.Find("Games", true).Single();
                var languages = (CheckedListBox)form.Controls.Find("Languages", true).Single();
                Assert(games.CheckedItems.Count == 3 && languages.CheckedItems.Count == 8, "UI defaults.");
                Assert(!languages.GetItemChecked(5), "Spanish must default off.");
                Assert(((Button)form.Controls.Find("Export", true).Single()).Enabled, "Export requires no loaded save.");
                Capture(form, "plugin-dialog.png");
                foreach (string key in new[] { "swsh", "pla", "sv" })
                {
                    var testProvider = new Provider(Blank(key));
                    using var compatible = new ExportForm(testProvider);
                    compatible.Show(); Application.DoEvents(); compatible.Hide();
                }
            }
            catch (Exception ex) { error = ex; }
            finally { form.Close(); }
        };
        timer.Start();
        entry.PerformClick();
        Assert(opened, "Tools item did not open dialog.");
        main.Hide();
        GC.KeepAlive(context);
        if (error is not null) throw error;
    }

    private static void Capture(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(Results, name), ImageFormat.Png);
    }

    private sealed class Provider(SaveFile save) : ISaveFileProvider
    {
        public SaveFile SAV => save;
        public int CurrentBox => 0;
        public void ReloadSlots() { }
    }
}
