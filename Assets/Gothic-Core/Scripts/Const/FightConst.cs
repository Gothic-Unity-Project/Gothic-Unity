using GUZ.Core.Services;

namespace GUZ.Core.Const
{
    public static class FightConst
    {
        // TODO - G1 only. G2 has different attack actions!
        public static class AttackActions
        {
            // Enemy is doing stuff - (e.g., hero (enemy) is doing something to a Lurker)
            public static string EnemyPrehit = "FA_ENEMY_PREHIT_{0}"; // Enemy attacks me
            public static string EnemyStormPrehit = "FA_ENEMY_STORMPREHIT_{0}"; // Enemy makes a storm attack
            public static string EnemyTurnToHitFocus = "FA_ENEMY_TURNTOHIT_FOCUS_{0}"; // Enemy turns to hit (?). Only visible for Orcs and OrcUndead. But not sure if it's being used in G1.

            // W
            public static string MyWCombo = "FA_MY_W_COMBO_{0}"; // I'm in the combo window
            public static string MyWRunto = "FA_MY_W_RUNTO_{0}"; // I run towards the opponent
            public static string MyWStrafe = "FA_MY_W_STRAFE_{0}"; // I take a hit
            public static string MyWFocus = "FA_MY_W_FOCUS_{0}"; // I have opponent in focus (can hit)
            public static string MyWNoFocus = "FA_MY_W_NOFOCUS_{0}"; // I don't have opponent in focus
            
            // G - Goto range. weaponRange < G-range < weaponRange*3
            // From Daedalus: Gehen-Reichweite (3 * W). Puffer für Fernkämpfer in dem sie zur NK-Waffe wechseln sollten
            public static string MyGCombo = "FA_MY_G_COMBO_{0}"; // I'm in the combo window
            public static string MyGRunto = "FA_MY_G_RUNTO_{0}"; // I run towards the opponent
            public static string MyGStrafe = "FA_MY_G_STRAFE_{0}"; // I take a hit
            public static string MyGFocus = "FA_MY_G_FOCUS_{0}"; // I have opponent in focus (can hit)
            
            // FK - ranged attack
            // Ranged attack distance (FK > weaponRange *3)
            // From Daedalus: FK - Fernkampf-Reichweite (30m)
            public static string MyGFkNoFocus = "FA_MY_G_FK_NOFOCUS_{0}"; // I have opponent NOT in focus (also applies to G-distance!)
            public static string MyFkFocus = "FA_MY_FK_FOCUS_{0}"; // I have opponent in focus

            // FK Far - ranged attack far (difference to FK: Ranged weapon equipped)
            // Only:
            // 1. Ranged attack distance (FK far > weaponRange *3 < 30m) and
            // 2. NPC/Monster is in ranged attack mode (bow etc.)
            public static string MyFkFocusFar = "FA_MY_FK_FOCUS_FAR_{0}"; // I have opponent in focus
            public static string MyFkNoFocusFar = "FA_MY_FK_NOFOCUS_FAR_{0}"; // I have opponent NOT in focus
        }
    }
}
