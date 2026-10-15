using PKHeX.Core;

namespace LanguageDexGenerator;

/// <summary>Only nine legacy game languages. Spanish LATAM (11) is deliberately excluded.</summary>
public static class LegacyLanguages
{
    public static readonly (int Id, string Code, string Label)[] All =
    [
        (1, "ja", "Japonés"), (2, "en", "Inglés"), (3, "fr", "Francés"),
        (4, "it", "Italiano"), (5, "de", "Alemán"), (7, "es", "Español (España)"),
        (8, "ko", "Coreano"), (9, "zh-Hans", "Chino simplificado"),
        (10, "zh-Hant", "Chino tradicional"),
    ];
    public static bool IsAllowed(int id) => All.Any(x => x.Id == id);
    public static string Code(int id) => All.Single(x => x.Id == id).Code;
}

public sealed record GameProfile(
    string Key, string Label, int FirstSpecies, int LastSpecies,
    GameVersion[] AllowedOrigins, Type ExpectedType, string FileExtension)
{
    public IEnumerable<int> Species => Enumerable.Range(FirstSpecies, LastSpecies - FirstSpecies + 1);
    public int SpeciesCount => LastSpecies - FirstSpecies + 1;
}

public static class DexCatalog
{
    // National-Dex IDs; no BDSP, no ZA; forms of earlier species are outside current scope.
    public static readonly GameProfile[] Games =
    [
        new("swsh", "Sword / Shield", 810, 898, [GameVersion.SW, GameVersion.SH], typeof(PK8), "pk8"),
        new("pla", "Legends: Arceus", 899, 905, [GameVersion.PLA], typeof(PA8), "pa8"),
        new("sv", "Scarlet / Violet", 906, 1025, [GameVersion.SL, GameVersion.VL], typeof(PK9), "pk9"),
    ];
}

public sealed record GenerationRequest
{
    public List<string> GameKeys { get; init; } = ["swsh", "pla", "sv"];
    public List<int> LanguageIds { get; init; } = [1, 2, 3, 4, 5, 8, 9, 10];
    public string OriginalTrainer { get; init; } = "LANG";
    public ushort TID16 { get; init; } = 12345;
    public ushort SID16 { get; init; } = 54321;
    public bool ExcludeMysteryGifts { get; init; } = true;
    public string OutputDirectory { get; init; } = "";
    public int PlannedCount => DexCatalog.Games.Where(g => GameKeys.Contains(g.Key)).Sum(g => g.SpeciesCount) * LanguageIds.Count;

    public void Validate()
    {
        if (GameKeys.Count == 0 || GameKeys.Distinct().Count() != GameKeys.Count ||
            GameKeys.Any(x => !DexCatalog.Games.Any(g => g.Key == x)))
            throw new ArgumentException("Selecciona juegos válidos sin repetir.");
        if (LanguageIds.Count == 0 || LanguageIds.Distinct().Count() != LanguageIds.Count ||
            LanguageIds.Any(x => !LegacyLanguages.IsAllowed(x)))
            throw new ArgumentException("Selecciona idiomas tradicionales (sin ID 11). ");
        if (string.IsNullOrWhiteSpace(OriginalTrainer) || OriginalTrainer.Length > 6)
            throw new ArgumentException("OT debe tener de 1 a 6 caracteres para el perfil universal.");
        if (OriginalTrainer.Any(c => c > 127 || char.IsControl(c)))
            throw new ArgumentException("Para este MVP usa OT ASCII; evita incompatibilidades entre idiomas.");
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            throw new ArgumentException("Indica una carpeta de exportación.");
        if (!ExcludeMysteryGifts)
            throw new ArgumentException("Los eventos están excluidos en esta versión.");
    }
}

public sealed record GenerationLine(
    string Game, int Species, int Language, string LanguageCode, string Status,
    string Origin, string File, string Reason);

public sealed record GenerationSummary(int Requested, int Exported, int Skipped, string Folder,
    bool Cancelled = false, int Unprocessed = 0, string? Failure = null);
