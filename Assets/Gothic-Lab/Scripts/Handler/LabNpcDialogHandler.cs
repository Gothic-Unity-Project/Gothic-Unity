using System.Collections;
using GUZ.Core.Adapters.Adnimations;
using GUZ.Core.Adapters.Npc;
using GUZ.Core.Animations;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Proxy;
using GUZ.Core.Models.Container;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Npc;
using GUZ.Lab.Mocks;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Lab.Handler
{
    public class LabNpcDialogHandler : AbstractLabHandler
    {
        public GameObject NpcSlotGo;

        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly NpcService _npcService;


        private string _bloodwynInstanceId = "GRD_233_Bloodwyn";
        private NpcInstance _bloodwynInstance;


        public override void Bootstrap()
        {
            BootstrapBloodwyn();

            StartCoroutine(IdleAnimations());
        }

        /// <summary>
        /// Some random animations to test blending etc.
        /// </summary>
        private IEnumerator IdleAnimations()
        {
            yield break;
            
            var npcRoot = NpcSlotGo.transform.GetChild(0).GetChild(0).gameObject;
            var animSystem = npcRoot.GetComponent<AnimationSystem>();
            var animHeadHandler = npcRoot.GetComponent<NpcHeadAnimationHandler>();

            // For UseItemToState animations
            var props = npcRoot.GetComponentInParent<NpcLoader>().Npc.GetUserData().Props;
            var beerSymbol = GameStateService.GothicVm.GetSymbolByName("ItFoBeer");
            props.CurrentItem = beerSymbol!.Index;

            yield return new WaitForSeconds(1f);

            while (true)
            {
                {
                    animSystem.PlayAnimation("T_BENCH_Stand_2_S0");
                    yield return new WaitForSeconds(3f);
                    animSystem.PlayAnimation("T_BENCH_S0_2_S1");
                    yield return new WaitForSeconds(3f);
                }
                continue;
                
                // Sit down - Moves forward and hovers over ground like a magician on his carpet.
                {
                    // Humans Militia have no CollisionVolumeScale change (CVS).
                    props.MdsNameOverlay = null;
                    animSystem.PlayAnimation("T_STAND_2_SIT");
                    yield return new WaitForSeconds(5f);
                    animSystem.PlayAnimation("T_SIT_2_STAND");
                    yield return new WaitForSeconds(5f);
                }
                continue;

                // Leg shake with FPS:10
                {
                    animSystem.PlayAnimation("t_BoringKick");
                    yield return new WaitForSeconds(1f);
                }
                continue;

                // Rotate, then idle
                {
                    animSystem.PlayAnimation("T_RUNTURNL");
                    yield return new WaitForSeconds(2f);
                    animSystem.StopAnimation("T_RUNTURNL");
                    animSystem.PlayAnimation("S_WALK");
                    yield return new WaitForSeconds(1f);
                    animSystem.StopAnimation("S_WALK");
                    continue;
                }

                animSystem.PlayAnimation("t_Potion_Stand_2_S0");
                yield return new WaitForSeconds(5f);
                animSystem.PlayAnimation("t_Potion_Random_3");
                yield return new WaitForSeconds(5f);
                animSystem.PlayAnimation("t_Potion_S0_2_Stand");
                yield return new WaitForSeconds(5f);
                continue;


                animSystem.PlayAnimation("T_DIALOGGESTURE_01");
                yield return new WaitForSeconds(2f);
                animSystem.StopAnimation("T_DIALOGGESTURE_01");
                animSystem.PlayAnimation("T_DIALOGGESTURE_02");
                yield return new WaitForSeconds(2f);

                continue;

                animSystem.PlayAnimation("S_WALK");
                Debug.Log("idle");
                yield return new WaitForSeconds(2f);

                // Test Layer2-blendout automation
                animSystem.PlayAnimation("T_DIALOGGESTURE_01");
                yield return new WaitForSeconds(3f);

                // Test start-stop
                animSystem.PlayAnimation("T_DIALOGGESTURE_08");
                yield return new WaitForSeconds(3f);
                animSystem.StopAnimation("T_DIALOGGESTURE_08");
                yield return new WaitForSeconds(1f);

                // Change idle state (layer 1)
                animSystem.PlayAnimation("S_RUN");
                yield return new WaitForSeconds(2f);
                // animHeadHandler.StartLookAt(Camera.main!.transform);
                continue;

                animSystem.PlayAnimation("T_DIALOGGESTURE_08");
                Debug.Log("8");
                yield return new WaitForSeconds(8f);
            }
        }

        private void BootstrapBloodwyn()
        {
            var newNpc = new GameObject();
            var loaderComp = newNpc.AddComponent<NpcLoader>();

            newNpc.SetParent(NpcSlotGo);

            var npcSymbol = GameStateService.GothicVm.GetSymbolByName(_bloodwynInstanceId)!;
            _bloodwynInstance = GameStateService.GothicVm.AllocInstance<NpcInstance>(npcSymbol);

            var npcData = new NpcContainer
            {
                Instance = _bloodwynInstance,
                Props = new(),
                Vob = new NpcProxy(npcSymbol.Index)
            };

            _bloodwynInstance.UserData = npcData;
            loaderComp.Npc = _bloodwynInstance;
            _multiTypeCacheService.NpcCache.Add(npcData);

            newNpc.name = _bloodwynInstance.GetName(NpcNameSlot.Slot0);
            GameStateService.GothicVm.GlobalSelf = _bloodwynInstance;

            GameStateService.GothicVm.InitInstance(_bloodwynInstance);

            // We need to initialize the NPC at this frame to set the positions of child GOs now.
            _npcService.InitNpc(newNpc, true);
            newNpc.transform.SetLocalPositionAndRotation(default, default);
            newNpc.transform.GetChild(0).SetLocalPositionAndRotation(default, default);

            // Otherwise NPC will start its daily routine.
            Destroy(newNpc.GetComponentInChildren<AiHandler>());
            newNpc.transform.GetChild(0).gameObject.AddComponent<LabAiHandler>();

            newNpc.name = "Dialog NPC";
        }
    }
}
