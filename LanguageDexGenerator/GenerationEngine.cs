using System.Globalization;
using System.Text;
using System.Text.Json;
using PKHeX.Core;

namespace LanguageDexGenerator;

public static class GenerationEngine
{
    private static readonly SemaphoreSlim Job = new(1, 1);

    public static GenerationSummary Run(GenerationRequest request,
        IProgress<(int Current, int Total, string Label)>? progress = null,
        CancellationToken cancel = default,
        Action<GameProfile, PKM>? onGenerated = null, ITrainerInfo? handlingTrainer = null)
    {
        request.Validate();
        request = request with { GameKeys = [.. request.GameKeys], LanguageIds = [.. request.LanguageIds] };
        var trainer = handlingTrainer is null ? null : new SimpleTrainerInfo(handlingTrainer);
        if (!Job.Wait(0)) throw new InvalidOperationException("Ya hay una generación en curso.");
        try { return RunCore(request, progress, cancel, onGenerated, trainer); }
        finally { Job.Release(); }
    }

    private static GenerationSummary RunCore(GenerationRequest request,
        IProgress<(int Current, int Total, string Label)>? progress,
        CancellationToken cancel, Action<GameProfile, PKM>? onGenerated, ITrainerInfo? handlingTrainer)
    {
        var games = DexCatalog.Games.Where(g => request.GameKeys.Contains(g.Key)).ToArray();
        var languages = LegacyLanguages.All.Where(l => request.LanguageIds.Contains(l.Id)).ToArray();
        int total = request.PlannedCount;
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string output = Path.Combine(Path.GetFullPath(request.OutputDirectory), $"LanguageDex_{stamp}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        var report = new List<GenerationLine>(total);
        int current = 0, exported = 0;
        bool cancelled = false;
        string? failure = null;
        GenerationSummary summary;
        try
        {
            foreach (var game in games)
            foreach (var lang in languages)
            foreach (int species in game.Species)
            {
                cancel.ThrowIfCancellationRequested();
                string label = $"{game.Label} · #{species:0000} · {lang.Label}";
                var (pk, reason) = NativeGenerationAdapter.TryGenerate(game, species, lang.Id,
                    request.OriginalTrainer, request.TID16, request.SID16, request.ExcludeMysteryGifts, cancel,
                    handlingTrainer: handlingTrainer);
                if (pk is null)
                    report.Add(new(game.Key, species, lang.Id, lang.Code, "SKIPPED", "", "", reason));
                else
                {
                    byte[] bytes = PokemonValidation.SerializeAndValidate(pk, game);
                    string subfolder = Path.Combine(output, game.Key, lang.Code);
                    Directory.CreateDirectory(subfolder);
                    string fullpath = Path.Combine(subfolder, $"{species:0000}_{lang.Code}_{pk.Version}.{game.FileExtension}");
                    string temporary = fullpath + ".tmp";
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        file.Write(bytes);
                    File.Move(temporary, fullpath, overwrite: false);
                    exported++;
                    report.Add(new(game.Key, species, lang.Id, lang.Code, "EXPORTED", pk.Version.ToString(),
                        Path.GetRelativePath(output, fullpath), ""));
                    onGenerated?.Invoke(game, pk.Clone());
                }
                progress?.Report((++current, total, label));
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { cancelled = true; }
        catch (Exception ex)
        {
            failure = ex.Message;
            throw new IOException($"Falló la generación. Informe en {output}. {ex.Message}", ex);
        }
        finally
        {
            summary = new(total, exported, report.Count(r => r.Status == "SKIPPED"), output, cancelled, total - report.Count, failure);
            WriteCsv(Path.Combine(output, "report.csv"), report);
            WriteNew(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
            {
                summary, request, GeneratedAt = DateTimeOffset.Now,
                CoreVersion = typeof(PKM).Assembly.GetName().Version?.ToString(),
                Generator = "PKHeX.Core native encounters (no ALM, no events)",
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return summary;
    }

    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        writer.Write(text);
    }

    private static void WriteCsv(string path, IEnumerable<GenerationLine> rows)
    {
        static string Q(string? raw) => "\"" + (raw ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var file = new StreamWriter(stream, new UTF8Encoding(true));
        file.WriteLine("game,species,language_id,language,status,origin,file,reason");
        foreach (var r in rows)
            file.WriteLine(string.Join(",", [Q(r.Game), r.Species.ToString(CultureInfo.InvariantCulture),
                r.Language.ToString(CultureInfo.InvariantCulture), Q(r.LanguageCode), Q(r.Status), Q(r.Origin), Q(r.File), Q(r.Reason)]));
    }
}
