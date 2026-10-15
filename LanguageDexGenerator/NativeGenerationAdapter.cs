using System.Diagnostics;
using PKHeX.Core;

namespace LanguageDexGenerator;

/// <summary>Uses only PKHeX's native encounter database; no ALM global settings or transfers.</summary>
public static class NativeGenerationAdapter
{
    public static (PKM? Pokemon, string Reason) TryGenerate(
        GameProfile game, int species, int language, string ot, ushort tid, ushort sid,
        bool excludeMysteryGifts = true, CancellationToken cancel = default,
        TimeSpan? timeLimit = null, ITrainerInfo? handlingTrainer = null)
    {
        if (!DexCatalog.Games.Contains(game) || !game.Species.Contains(species) || !LegacyLanguages.IsAllowed(language))
            throw new ArgumentException("Especie, juego o idioma fuera del catálogo.");
        if (!excludeMysteryGifts)
            throw new ArgumentException("Esta versión solo permite encuentros nativos sin Mystery Gifts.");

        // Event-only species have no native, non-distribution encounter in these games.
        if (species is 893 or 1025)
            return (null, "EVENT_ONLY: requiere distribución por evento; no hay encuentro nativo sin evento.");
        if (species is 1009 or 1010)
            return (null, "EVENT_RAID_EXCLUDED: solo disponible mediante teraincursión de distribución; eventos excluidos.");

        var watch = Stopwatch.StartNew();
        var limit = timeLimit ?? TimeSpan.FromSeconds(8);
        var errors = new List<string>();
        int attempts = 0;
        foreach (var origin in game.AllowedOrigins)
        {
            var trainer = new SimpleTrainerInfo(origin) { Language = language, OT = ot, TID16 = tid, SID16 = sid };
            var target = CreateBlank(game);
            target.Species = (ushort)species;
            target.CurrentLevel = 100;
            target.Gender = target.PersonalInfo.RandomGender();
            target.Version = origin;
            var evoOrigin = new EvolutionOrigin((ushort)species, origin.Context, origin.Generation, 1, 100, OriginOptions.EncounterTemplate);
            var chain = EvolutionChain.GetOriginChain(target, evoOrigin);
            var generator = EncounterGenerator.GetGenerator(origin, origin.Generation);

            // Favor direct encounters before considering a legal evolution from a pre-evolution.
            // Do not read or change EncounterMovesetGenerator.PriorityList (shared with other plugins).
            // Enumerate categories independently. The pinned Core's SV iterator can revisit
            // static encounters when Static and Slot flags are combined.
            var groups = new[] { EncounterTypeGroup.Slot, EncounterTypeGroup.Static, EncounterTypeGroup.Egg, EncounterTypeGroup.Trade };
            var candidates = groups.SelectMany(group => generator.GetPossible(target, chain, origin, group))
                .Where(e => e is not MysteryGift and not ITeraRaid9 and not EncounterOutbreak9
                    and not EncounterStatic8ND and not EncounterStatic8NC && e.Context == target.Context)
                .OrderBy(e => e.Species == species ? 0 : 1)
                .ThenBy(e => e is EncounterSlot9 ? 0 : 1)
                .ThenBy(e => e is IEncounterEgg ? 1 : 0).ToArray();

            foreach (var encounter in candidates)
            {
                for (int retry = 0; retry < 4; retry++)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (watch.Elapsed > limit)
                        return (null, $"TIMEOUT: {attempts} intentos. " + string.Join(" | ", errors.Distinct().Take(3)));
                    attempts++;
                    try
                    {
                        var pk = encounter.ConvertToPKM(trainer, EncounterCriteria.Unrestricted with { Shiny = Shiny.Never });
                        if (pk.IsShiny || pk.Language != language || !game.AllowedOrigins.Contains(pk.Version))
                            continue; // Retry the encounter, never patch PID, language or origin.
                        if (pk.IsEgg) pk.ForceHatchPKM(trainer);
                        // Use Core's normal trade/handler logic for the loaded save. Keep the
                        // requested original trainer, language and native encounter intact.
                        // Never change PKHeX's shared legality settings to suppress this check.
                        if (handlingTrainer is not null && pk is IHandlerUpdate handler)
                            handler.UpdateHandler(handlingTrainer);
                        if (pk.Species != species || pk.Form != 0)
                        {
                            // The origin chain was supplied by PKHeX; this represents in-game evolution.
                            // Original encounter data, PID, EC, language and trainer remain untouched.
                            pk.Species = (ushort)species;
                            pk.Form = 0;
                            pk.CurrentLevel = 100;
                            pk.RefreshAbility(System.Numerics.BitOperations.TrailingZeroCount((uint)pk.AbilityNumber));
                            pk.ClearNickname();
                            var evolving = new LegalityAnalysis(pk);
                            pk.SetSuggestedFormArgument(pk.Species, pk.Form, pk.Context, evolving.Info.EvoChainsAllGens, encounter.Species);
                            pk.SetMoveset();
                            if (pk.Species == (ushort)Species.Hydrapple && pk is ITechRecord record)
                            {
                                // Dipplin must know Dragon Cheer to evolve. Its TM record survives evolution.
                                pk.SetMoves([(ushort)Move.DragonCheer]);
                                record.SetRecordFlags([(ushort)Move.DragonCheer], evolving.Info.EvoChainsAllGens.Get(pk.Context));
                            }
                            pk.ResetPartyStats();
                        }
                        pk.RefreshChecksum();
                        string? error = PokemonValidation.GetError(pk, game, species, language);
                        if (error is null) return (pk, "");
                        errors.Add($"{origin}/{encounter.GetType().Name}: {error}");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { errors.Add($"{origin}/{encounter.GetType().Name}: {ex.Message}"); }
                }
            }
        }
        return (null, $"NO_VALID_NATIVE_ENCOUNTER: {attempts} intentos. " + string.Join(" | ", errors.Distinct().Take(4)));
    }

    public static PKM CreateBlank(GameProfile game) => game.Key switch
    {
        "swsh" => new PK8(), "pla" => new PA8(), "sv" => new PK9(),
        _ => throw new ArgumentException("Juego no admitido."),
    };
}
