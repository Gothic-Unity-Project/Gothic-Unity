using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Domain.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Vm
{
    public class VmService
    {
        [Inject] private readonly GameStateService _gameStateService;
        
        public DaedalusVm Vm => _gameStateService.GothicVm;
        
        //
        // Daedalus
        //
        public int AIVMMRealId => _gameStateService.GothicVm.GetSymbolByName("AIV_MM_REAL_ID")!.GetInt(0);
        public int AIVInvincibleKey => _gameStateService.GothicVm.GetSymbolByName("AIV_INVINCIBLE")!.GetInt(0);
        public int AIVItemStatusKey => _gameStateService.GothicVm.GetSymbolByName("AIV_ITEMSTATUS")!.GetInt(0);
        public int AIVItemFreqKey => _gameStateService.GothicVm.GetSymbolByName("AIV_ITEMFREQ")!.GetInt(0);
        public int TAITNone => _gameStateService.GothicVm.GetSymbolByName("TA_IT_NONE")!.GetInt(0);

        // Fight
        public int NpcVoiceVariationMax => _gameStateService.GothicVm.GetSymbolByName("NPC_VOICE_VARIATION_MAX")!.GetInt(0);

        // Lock picking
        public string DoorLockSoundName => "DOOR_LOCK.WAV";
        public string PickLockFailureSoundName => _gameStateService.GothicVm.GetSymbolByName("_STR_SOUND_PICKLOCK_FAILURE").GetString(0);
        public string PickLockBrokenSoundName => _gameStateService.GothicVm.GetSymbolByName("_STR_SOUND_PICKLOCK_BROKEN").GetString(0);
        public string PickLockSuccessSoundName => _gameStateService.GothicVm.GetSymbolByName("_STR_SOUND_PICKLOCK_SUCCESS").GetString(0);
        public string PickLockUnlockSoundName => _gameStateService.GothicVm.GetSymbolByName("_STR_SOUND_PICKLOCK_UNLOCK").GetString(0);
        // public string DoorUnlockSoundName => "DOOR_UNLOCK.WAV"; // _STR_*_UNLOCK value above couldn't be found/isn't used in G1, therefore we use this as fallback.

        public string[] MobSit => _gameStateService.GothicVm.GetSymbolByName("MOB_SIT").GetString(0).Split(',');
        public string[] MobLie => _gameStateService.GothicVm.GetSymbolByName("MOB_LIE").GetString(0).Split(',');
        public string[] MobClimb => _gameStateService.GothicVm.GetSymbolByName("MOB_CLIMB").GetString(0).Split(',');
        public string[] MobNotInterruptable => _gameStateService.GothicVm.GetSymbolByName("MOB_NOTINTERRUPTABLE").GetString(0).Split(',');

        // TODO - Gothic2 has a different amount of talents
        public int TalentsMax => _gameStateService.GothicVm.GetSymbolByName("NPC_TALENT_MAX").GetInt(0);
        public List<string> TalentTitles
        {
            get
            {
                var talents = _gameStateService.GothicVm.GetSymbolByName("TXT_TALENTS");;
                return Enumerable.Range(0, TalentsMax).Select(i => talents.GetString((ushort)i)).ToList();
            }
        }

        public List<string> TalentSkills
        {
            get
            {
                var talentSkills = _gameStateService.GothicVm.GetSymbolByName("TXT_TALENTS_SKILLS");;
                var talentCount = TalentsMax;
                return Enumerable.Range(0, talentCount).Select(i => talentSkills.GetString((ushort)i)).ToList();
            }
        }

        public string GetGuildName(int guildId)
        {
            var guilds = _gameStateService.GothicVm.GetSymbolByName("TXT_GUILDS");
            var max = _gameStateService.GothicVm.GetSymbolByName("GIL_MAX")?.GetInt(0);
            if (guilds == null || guildId < 0 || guildId >= max)
                return string.Empty;
            return guilds.GetString((ushort)guildId);
        }

        public int InvCatMax => _gameStateService.GothicVm.GetSymbolByName("INV_CAT_MAX").GetInt(0);
        public List<string> InventoryCategories
        {
            get
            {
                var invCats = _gameStateService.GothicVm.GetSymbolByName("TXT_INV_CAT")!;
                var invCatCount = InvCatMax;
                return Enumerable.Range(0, invCatCount).Select(i => invCats.GetString((ushort)i)).ToList();
            }
        }

        
        //
        // Menu
        // 
        
        public readonly string[] DisabledGothicMenuSettings =
        {
            // Whole submenus
            "MENUITEM_OPT_VIDEO", "MENUITEM_OPT_CONTROLS",
            // Parts of main SETTINGS menu
            "MENUITEM_PERF", "MENUITEM_PERF_CHOICE",
            // Parts of GAME menu
            "MENUITEM_GAME_ANIMATE_WINDOWS", "MENUITEM_GAME_ANIMATE_WINDOWS_CHOICE",
            "MENUITEM_GAME_LOOKAROUND_INVERSE", "MENUITEM_GAME_LOOKAROUND_INVERSE_CHOICE",
            "MENUITEM_M", "MENUITEM_M_CHOICE",
            "MENUITEM_MSENSITIVITY", "MENUITEM_MSENSITIVITY_SLIDER",
            "MENUITEM_GAME_BLOOD", "MENUITEM_GAME_BLOOD_CHOICE",
            // Parts of GRAPHICS menu
            "MENUITEM_GRA_TEXQUAL", "MENUITEM_GRA_TEXQUAL_SLIDER",
            "MENUITEM_GRA_MODEL_DETAIL", "MENUITEM_GRA_MODEL_DETAIL_SLIDER",
            "MENUITEM_GRA_SKY_EFFECTS", "MENUITEM_GRA_SKY_EFFECTS_CHOICE",
            // Parts of AUDIO menu
            "MENUITEM_AUDIO_SFXVOL", "MENUITEM_AUDIO_SFXVOL_SLIDER",
            "MENUITEM_AUDIO_PROVIDER", "MENUITEM_AUDIO_PROVIDER_CHOICE",
            "MENUITEM_AUDIO_SPEEKER", "MENUITEM_AUDIO_SPEEKER_CHOICE",
            "MENUITEM_AUDIO_REVERB", "MENUITEM_AUDIO_REVERB_CHOICE",
            "MENUITEM_AUDIO_SAMPLERATE", "MENUITEM_AUDIO_SAMPLERATE_CHOICE"
        };
        
        public int MaxUserStrings => _gameStateService.MenuVm.GetSymbolInt("MAX_USERSTRINGS");
        public int MaxItems => _gameStateService.MenuVm.GetSymbolInt("MAX_ITEMS");
        public int MaxEvent => _gameStateService.MenuVm.GetSymbolInt("MAX_EVENTS");
        public int MaxSelActions => _gameStateService.MenuVm.GetSymbolInt("MAX_SEL_ACTIONS");
        public int MaxUserVars => _gameStateService.MenuVm.GetSymbolInt("MAX_USERVARS");
        public string BackPic => _gameStateService.MenuVm.GetSymbolString("MENU_BACK_PIC");
        public int MenuStartY => _gameStateService.MenuVm.GetSymbolInt("MENU_START_Y");
        public int MenuDY => _gameStateService.MenuVm.GetSymbolInt("MENU_DY");

        
        //
        // Fight
        //
        public int FightAiMoveMax => _gameStateService.FightVm.GetSymbolByName("MAX_MOVE")!.GetInt(0);

        //
        // Hero calls
        //

        /// <summary>
        /// Calls a Daedalus function with self = hero (like the engine does for mob states). Restores the old self.
        /// Returns false if the symbol doesn't exist or the script failed (error is logged).
        /// </summary>
        public bool CallAsHero(string functionName, LogCat cat)
        {
            var vm = _gameStateService.GothicVm;
            var symbol = vm.GetSymbolByName(functionName);
            if (symbol == null)
                return false;

            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                vm.Call(symbol.Index);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError($"[VmService] '{functionName}' failed: {e.Message}", cat);
                return false;
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
        }

        /// <summary>
        /// Same as CallAsHero(), but for functions with a return value. Returns false only if the symbol doesn't exist.
        /// A script error is logged and leaves result at default.
        /// </summary>
        public bool CallAsHero<T>(string functionName, LogCat cat, out T result)
        {
            result = default;
            var vm = _gameStateService.GothicVm;
            var symbol = vm.GetSymbolByName(functionName);
            if (symbol == null)
                return false;

            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                result = vm.Call<T>(symbol.Index);
            }
            catch (Exception e)
            {
                Logger.LogError($"[VmService] '{functionName}' failed: {e.Message}", cat);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }

            return true;
        }

        //
        // NPC calls
        //

        /// <summary>
        /// Calls a Daedalus function (a state function, ZS_Dead, ...) with the globals the engine sets for an NPC: self,
        /// and other/victim if given. Restores them afterwards. Returns the int result (0 without one or on a script
        /// error, which is logged - an exception mustn't escape into the VM's own call).
        /// </summary>
        public int CallAsNpc(int symbolIndex, NpcInstance self, NpcInstance other, NpcInstance victim, LogCat cat)
        {
            var vm = _gameStateService.GothicVm;
            var symbol = vm.GetSymbolByIndex(symbolIndex);
            if (symbol == null)
                return 0;

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            var oldVictim = vm.GlobalVictim;
            vm.GlobalSelf = self;
            if (other != null)
                vm.GlobalOther = other;
            if (victim != null)
                vm.GlobalVictim = victim;

            try
            {
                if (symbol.ReturnType == DaedalusDataType.Int)
                    return vm.Call<int>(symbolIndex);

                vm.Call(symbolIndex);
                return 0;
            }
            catch (Exception e)
            {
                Logger.LogError($"[VmService] {symbol.Name} of {self.GetName(NpcNameSlot.Slot0)} failed: {e.Message}",
                    cat);
                return 0;
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
                vm.GlobalVictim = oldVictim;
            }
        }
    }
}
