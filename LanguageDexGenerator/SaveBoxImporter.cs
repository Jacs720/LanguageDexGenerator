using System.Security.Cryptography;
using PKHeX.Core;

namespace LanguageDexGenerator;

public static class SaveBoxImporter
{
    public static int RequiredBoxes(int pokemonCount, int slotsPerBox = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pokemonCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(slotsPerBox, 1);
        return checked((pokemonCount + slotsPerBox - 1) / slotsPerBox);
    }

    public static string Fingerprint(SaveFile save) => Convert.ToHexString(SHA256.HashData(save.Clone().Write().Span));

    public static void Preflight(SaveFile save, GameProfile profile, int plannedCount)
        => PlanSlots(save, profile, plannedCount);

    private static int[] PlanSlots(SaveFile save, GameProfile profile, int plannedCount)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (!DexCatalog.Games.Contains(profile) || !profile.AllowedOrigins.Contains(save.Version) ||
            save.PKMType != profile.ExpectedType || save is not (SAV8SWSH or SAV8LA or SAV9SV))
            throw new InvalidOperationException($"La partida {save.Version} no corresponde a {profile.Label}.");
        if (plannedCount <= 0 || save.BoxCount <= 0 || save.BoxSlotCount <= 0)
            throw new ArgumentException("La partida o el número de Pokémon no son válidos.");
        int slots = checked(save.BoxCount * save.BoxSlotCount);
        if (plannedCount > slots)
            throw new InvalidOperationException($"Se solicitan {plannedCount} Pokémon y caben {slots}. Desmarca idiomas.");
        var available = new List<int>(plannedCount);
        for (int i = 0; i < slots; i++)
        {
            if (save.GetBoxSlotAtIndex(i).Species == 0 && !save.IsBoxSlotOverwriteProtected(i))
                available.Add(i);
            if (available.Count == plannedCount) return available.ToArray();
        }
        throw new InvalidOperationException($"Se requieren {plannedCount} espacios vacíos y solo hay {available.Count} disponibles en las cajas, sin contar espacios protegidos. Libera espacio o desmarca idiomas.");
    }

    public static string Commit(SaveFile save, GenerationRequest request, IReadOnlyList<PKM> generated,
        string runFolder, string expectedFingerprint)
    {
        request.Validate();
        if (request.GameKeys.Count != 1) throw new ArgumentException("Selecciona un único juego para importar.");
        var profile = DexCatalog.Games.Single(g => g.Key == request.GameKeys[0]);
        var slots = PlanSlots(save, profile, request.PlannedCount);
        if (generated.Count == 0 || generated.Count > request.PlannedCount)
            throw new InvalidOperationException("La cantidad generada no corresponde a la solicitud.");
        if (Fingerprint(save) != expectedFingerprint)
            throw new InvalidOperationException("La partida cambió después de iniciar la generación. No se importó nada.");

        var frozen = generated.Select(pk => pk.Clone()).ToArray();
        var seen = new HashSet<(ushort, int)>();
        foreach (var pk in frozen)
        {
            string? error = PokemonValidation.GetError(pk, profile, pk.Species, pk.Language);
            if (error is not null || !request.LanguageIds.Contains(pk.Language))
                throw new InvalidOperationException($"Resultado #{pk.Species} no válido para importar: {error ?? "Idioma no seleccionado."}");
            if (!seen.Add((pk.Species, pk.Language))) throw new InvalidOperationException("Hay combinaciones duplicadas.");
        }

        // All insertions and their legality checks happen on an isolated clone first.
        var original = save.Clone();
        var candidate = save.Clone();
        for (int i = 0; i < frozen.Length; i++)
        {
            int slot = slots[i];
            candidate.SetBoxSlotAtIndex(frozen[i], slot, EntityImportSettings.None);
            string? error = PokemonValidation.GetError(candidate.GetBoxSlotAtIndex(slot), profile, frozen[i].Species, frozen[i].Language);
            if (error is not null) throw new InvalidOperationException("La validación de la caja falló: " + error);
        }
        Directory.CreateDirectory(runFolder);
        string backup = Path.Combine(runFolder, $"original_save_{Guid.NewGuid():N}.main");
        using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(original.Write().Span);
            stream.Flush(true);
        }
        if (Fingerprint(save) != expectedFingerprint)
            throw new InvalidOperationException("La partida cambió antes de confirmar la escritura. No se importó nada.");
        bool editedBefore = save.State.Edited;
        try { save.CopyChangesFrom(candidate); }
        catch
        {
            save.CopyChangesFrom(original);
            save.State.Edited = editedBefore;
            throw;
        }
        return backup;
    }
}
