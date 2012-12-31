using System.Collections.Generic;

namespace Questor.Modules.Caching
{
    public class TargetingCache
    {
        public static EntityCache CurrentDronesTarget { get; set; }

        public static EntityCache CurrentWeaponsTarget { get; set; }

        public static int CurrentTargetShieldPct { get; set; }

        public static int CurrentTargetArmorPct { get; set; }

        public static int CurrentTargetStructurePct { get; set; }

        public static double CurrentTargetID { get; set; }

        public static IEnumerable<EntityCache> EntitiesWarpDisruptingMe { get; set; }

        public static string EntitiesWarpDisruptingMeText { get; set; }

        public static IEnumerable<EntityCache> EntitiesJammingMe { get; set; }

        public static string EntitiesJammingMeText { get; set; }

        public static IEnumerable<EntityCache> EntitiesWebbingMe { get; set; }

        public static string EntitiesWebbingMeText { get; set; }

        public static IEnumerable<EntityCache> EntitiesNeutralizingMe { get; set; }

        public static string EntitiesNeutralizingMeText { get; set; }

        public static IEnumerable<EntityCache> EntitiesTrackingDisruptingMe { get; set; }

        public static string EntitiesTrackingDisruptingMeText { get; set; }

        public static IEnumerable<EntityCache> EntitiesDampeningMe { get; set; }

        public static string EntitiesDampeningMeText { get; set; }

        public static IEnumerable<EntityCache> EntitiesTargetPatingingMe { get; set; }

        public static string EntitiesTargetPaintingMeText { get; set; }

        //public TargetingCache()
        //{
        //}
    }
}