namespace NinOnlineAutoAssist.Live;

// Normalized UI categories, NOT a direct cast of Common.VillageType.
// Actual player affiliation codes 11..20 are Missing (Common.NpcKillAnnouncementRules.VillageBit).
public enum PlayerVillage { Unknown = 0, Leaf = 1, Sand = 2, Mist = 3, Renegade = 11 }

public static class PlayerVillages
{
    public static bool IsKnown(PlayerVillage village) => village is PlayerVillage.Leaf or PlayerVillage.Sand or PlayerVillage.Mist or PlayerVillage.Renegade;
    public static PlayerVillage FromCode(byte? code) => code switch
    {
        1 => PlayerVillage.Leaf, 2 => PlayerVillage.Sand, 3 => PlayerVillage.Mist,
        >= 11 and <= 20 => PlayerVillage.Renegade, _ => PlayerVillage.Unknown
    };
    public static string Label(PlayerVillage village) => village switch
    {
        PlayerVillage.Leaf => "Leaf", PlayerVillage.Sand => "Sand", PlayerVillage.Mist => "Mist",
        PlayerVillage.Renegade => "Renegado", _ => "Desconhecida"
    };
    public static int? ColorArgb(PlayerVillage village) => village switch
    {
        PlayerVillage.Leaf => unchecked((int)0xFF98C379),
        PlayerVillage.Sand => unchecked((int)0xFFE5C07B),
        PlayerVillage.Mist => unchecked((int)0xFF61AFEF),
        PlayerVillage.Renegade => unchecked((int)0xFFE06C75), _ => null
    };
}
