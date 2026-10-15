using PKHeX.Core;

namespace LanguageDexGenerator;

public static class PokemonValidation
{
    public static string? GetError(PKM pk, GameProfile game, int species, int language)
    {
        if (!DexCatalog.Games.Contains(game) || species < game.FirstSpecies || species > game.LastSpecies ||
            !LegacyLanguages.IsAllowed(language) || pk.Species != species || pk.Language != language ||
            pk.GetType() != game.ExpectedType || !game.AllowedOrigins.Contains(pk.Version) ||
            pk.IsEgg || pk.IsShiny || pk.Form != 0 || pk.IsNicknamed)
            return "Especie, idioma, origen, formato o estado inesperados.";
        if (pk is IHomeTrack { Tracker: not 0 }) return "Se requiere un Pokémon nativo sin historial de HOME.";
        if (pk.Nickname != SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, pk.Format))
            return "El nombre no coincide con el nombre natural en el idioma seleccionado.";
        var la = new LegalityAnalysis(pk);
        if (la.EncounterOriginal is MysteryGift) return "Mystery Gift excluido.";
        if (la.EncounterOriginal.Context != pk.Context) return "El encuentro no pertenece al juego nativo.";
        return la.Valid ? null : la.Report();
    }

    public static byte[] SerializeAndValidate(PKM pk, GameProfile game)
    {
        string? error = GetError(pk, game, pk.Species, pk.Language);
        if (error is not null) throw new InvalidOperationException(error);
        var bytes = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(bytes);
        var reopened = EntityFormat.GetFromBytes(bytes, pk.Context)
            ?? throw new InvalidOperationException("No se puede volver a abrir el archivo generado.");
        error = GetError(reopened, game, pk.Species, pk.Language);
        if (error is not null) throw new InvalidOperationException("Error al reabrir: " + error);
        return bytes;
    }
}
