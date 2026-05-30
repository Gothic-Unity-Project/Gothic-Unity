using System.Collections;
using System.Linq;
using GUZ.Core.Adapters.Npc;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Proxy;
using GUZ.Core.Models.Container;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Npc;
using HurricaneVR.Framework.Core.Utils;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Lab.Handler
{
    public class LabFightHandler : AbstractLabHandler
    {
        [SerializeField] private TMP_Dropdown _monsterSelector;
        [SerializeField] private GameObject _spawnPoint;

        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly NpcService _npcService;

        public override void Bootstrap()
        {
            var allNames = GameStateService.GothicVm.GetInstanceSymbols("C_Npc").Select(i => i.Name).ToList();
            _monsterSelector.options = allNames.Select(i => new TMP_Dropdown.OptionData(i)).ToList();
        }

        public void SpawnMonsterClick(int amount)
        {
            StartCoroutine(SpawnMonster(amount));
        }

        public void SpawnMoleratClick(int amount)
        {
            StartCoroutine(SpawnMonster(amount, "Molerat"));
        }

        public void SpawnGoblinClick(int amount)
        {
            // GreenGobboClub - more strafe movement
            // BlackGobboMace - more attack combos
            StartCoroutine(SpawnMonster(amount, "GreenGobboClub"));
        }

        public void SpawnZombieClick(int amount)
        {
            StartCoroutine(SpawnMonster(amount, "ZOMBIE"));
        }
        
        public void MonsterDestroyClick()
        {
            for (var i = 0; i < _spawnPoint.transform.childCount; i++)
            {
                Destroy(_spawnPoint.transform.GetChild(i).gameObject);
            }
        }

        private IEnumerator SpawnMonster(int amount, string instanceName = null)
        {
            if (instanceName.IsNullOrWhiteSpace())
                instanceName = _monsterSelector.options[_monsterSelector.value].text;
            
            while (amount-- > 0)
            {
                var monsterGo = CreateMonserLoader(instanceName);
                _npcService.InitNpc(monsterGo, true);
                yield return null;
            }
        }
        
        private GameObject CreateMonserLoader(string instanceName)
        {
            var instance = GameStateService.GothicVm.AllocInstance<NpcInstance>(instanceName);

            var monsterLoadingGo = new GameObject(instanceName);
            monsterLoadingGo.SetParent(_spawnPoint);
            var loaderGoComp = monsterLoadingGo.AddComponent<NpcLoader>();
            loaderGoComp.Npc = instance;

            var npcData = new NpcContainer
            {
                Instance = instance,
                Vob =  new NpcProxy(instance.Index),
                Props = new()
            };
            
            instance.UserData = npcData;

            _multiTypeCacheService.NpcCache.Add(npcData);
            GameStateService.GothicVm.InitInstance(instance);
            npcData.Vob.CopyFromInstanceData(instance); // e.g., copy Instance.FightTactics -> Vob.FightTactics
            
            // Only NPCs have an ID && Monsters have no voice.
            if (instance.Id != 0 || instance.Voice != 0)
                return null;
            else
                return monsterLoadingGo;
        }
    }
}
