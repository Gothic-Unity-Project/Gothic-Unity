using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Domain.Config;
using Gothic.Core.Domain.SaveGames;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Vm;
using NUnit.Framework;
using ZenKit;
using ZenKit.Vobs;

namespace Gothic.Tests.PlayMode
{
    /// <summary>
    /// Regression tests for the ZenKit behaviour our save games rely on (see Docs/save-game.md).
    /// Tests which need world files are ignored without a configured Gothic installation (GameSettings.json).
    /// </summary>
    public class SaveGameZenKitTest
    {
        // Mounting all VDFs takes a while. Like ResourceCacheService, we mount them once per game version.
        private static readonly Dictionary<GameVersion, Vfs> _vfsCache = new();

        private readonly List<string> _tempFolders = new();

        /// <summary>
        /// ZenKit keeps the log callback of the last game start (BootstrapService.AwakeUnity). After the domain reload
        /// of a test run, it points to freed memory and the first ZenKit log message crashes Unity. Tests don't start
        /// the game, we therefore use ZenKit's own logger.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ZenKit.Logger.SetDefault(LogLevel.Error);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var folder in _tempFolders.Where(Directory.Exists))
                Directory.Delete(folder, true);

            _tempFolders.Clear();
        }

        [Test]
        public void ContentItemsPackAndUnpack()
        {
            var items = "ITFO_APPLE:5;ITMW_1H_SWORD_01,ITPO_HEALTH_01:2".UnpackItems();

            Assert.AreEqual(3, items.Count);
            Assert.AreEqual("ITMW_1H_SWORD_01", items[1].Name);
            Assert.AreEqual(1, items[1].Amount, "An item without amount counts once.");
            Assert.AreEqual("ITFO_APPLE:5,ITMW_1H_SWORD_01:1,ITPO_HEALTH_01:2", items.PackItems(','));
            Assert.AreEqual(0, ((string)null).UnpackItems().Count);
        }

        /// <summary>
        /// ZenKitCAPI's AiVars setter copies bytes instead of ints. If this test fails for the setter part, ZenKit is
        /// fixed and ZenKitNpcExtension.TrySetAiVars() can be removed.
        /// </summary>
        [Test]
        public void AiVarsAreWrittenCompletely()
        {
            var values = Enumerable.Range(1, 50).ToArray();

            var setterNpc = new Npc { AiVars = values };
            Assert.AreNotEqual(50, setterNpc.AiVars[49], "ZenKit's AiVars setter works now. Remove the workaround.");

            var npc = new Npc();
            Assert.IsTrue(npc.TrySetAiVars(values));
            CollectionAssert.AreEqual(values, npc.AiVars.Take(50));
        }

        /// <summary>
        /// A new Npc has PhysicsEnabled=true. G1 save games become unreadable then (ZenKit reads a rigid body which
        /// wasn't written). SaveGameNpcDomain.ClearPhysicsFlag() needs to be called before saving.
        /// </summary>
        [Test]
        public void G1WorldSaveRoundTrip()
        {
            var world = LoadWorld(GameVersion.Gothic1, "WORLD.ZEN");
            var item = (Item)Walk(world.RootObjects).First(i => i.Type == VirtualObjectType.oCItem);
            item.Amount = 7;

            var npc = new Npc { Name = "PC_HERO", NpcInstance = "PC_HERO", Level = 5, Xp = 1234, Player = true };
            npc.SetPacked((int)VmGothicEnums.InvCats.InvFood, "ITFOAPPLE:5;ITFOBEER:2");
            npc.AddItem(new Item
            {
                Name = "ITMW_1H_SWORD_01",
                Instance = "ITMW_1H_SWORD_01",
                Amount = 1,
                Flags = (int)VmGothicEnums.ItemFlags.ItemActive,
                PhysicsEnabled = false
            });
            Assert.IsTrue(npc.TrySetAiVars(Enumerable.Range(1, 50).ToArray()));

            world.RootObjects = new List<IVirtualObject> { npc, item };
            world.Npcs = new List<Npc>();
            new SaveGameNpcDomain().ClearPhysicsFlag(world.RootObjects);

            // Loading a broken G1 save allocates memory endlessly. Fail before.
            Assert.IsFalse(Walk(world.RootObjects).Any(i => i.PhysicsEnabled), "PhysicsEnabled needs to be cleared.");

            var loadedWorld = SaveAndLoad(GameVersion.Gothic1, world, "WORLD");

            var loadedItem = loadedWorld.RootObjects.OfType<IItem>().Single();
            var loadedNpc = loadedWorld.RootObjects.OfType<INpc>().Single();
            Assert.AreEqual(7, loadedItem.Amount);
            Assert.IsTrue(loadedNpc.Player);
            Assert.AreEqual(5, loadedNpc.Level);
            Assert.AreEqual(1234, loadedNpc.Xp);
            Assert.AreEqual(50, loadedNpc.AiVars[49]);
            Assert.AreEqual("ITFOAPPLE:5;ITFOBEER:2", loadedNpc.GetPacked((int)VmGothicEnums.InvCats.InvFood));
            Assert.AreEqual("ITMW_1H_SWORD_01", loadedNpc.Items.Single().Instance, "Equipment is stored as items.");
        }

        /// <summary>
        /// G2's packed inventory reader only keeps the first item of the first category. Everything comma separated
        /// inside category 0 survives (SaveGameNpcDomain.FlattenPackedInventoryForSave()).
        /// </summary>
        [Test]
        public void G2PackedInventoryRoundTrip()
        {
            var world = LoadWorld(GameVersion.Gothic2, "NEWWORLD.ZEN");
            var npc = new Npc { Name = "PC_HERO", NpcInstance = "PC_HERO", PhysicsEnabled = false };
            npc.SetPacked(0, "ITMW_1H_BAU_MACE:1,ITFO_APPLE:3,ITPO_HEALTH_01:2");
            world.RootObjects = new List<IVirtualObject> { npc };
            world.Npcs = new List<Npc>();

            var loadedWorld = SaveAndLoad(GameVersion.Gothic2, world, "NEWWORLD");

            var loadedNpc = loadedWorld.RootObjects.OfType<INpc>().Single();
            Assert.AreEqual(3, loadedNpc.GetPacked(0).UnpackItems().Count);
        }

        private World SaveAndLoad(GameVersion version, World world, string worldName)
        {
            var folder = Path.Combine(Path.GetTempPath(), $"GothicUnity-SaveGameTest-{Guid.NewGuid():N}");
            _tempFolders.Add(folder);

            var save = new SaveGame(version);
            save.Metadata.Title = "SaveGameZenKitTest";
            save.Metadata.World = worldName;
            save.Thumbnail = CreateThumbnail();
            Assert.IsTrue(save.Save(folder, world, worldName));

            var loaded = new SaveGame(version);
            Assert.IsTrue(loaded.Load(folder), "An empty THUMB.SAV would make Load() fail.");
            return loaded.LoadWorld(worldName);
        }

        private static Texture CreateThumbnail()
        {
            var builder = new TextureBuilder(4, 4);
            builder.AddMipmap(new byte[4 * 4 * 2], TextureFormat.R5G6B5);
            return builder.Build(TextureFormat.R5G6B5);
        }

        private static World LoadWorld(GameVersion version, string worldName)
        {
            string root;
            try
            {
                var config = JsonRootLoader.Load();
                root = version == GameVersion.Gothic1 ? config.Gothic1Path : config.Gothic2Path;
            }
            catch (Exception e)
            {
                Assert.Ignore($"No Gothic installation configured: {e.Message}");
                return null;
            }

            var dataFolder = Directory.Exists(root)
                ? Directory.GetDirectories(root).FirstOrDefault(i => Path.GetFileName(i).EqualsIgnoreCase("Data"))
                : null;
            if (dataFolder == null)
                Assert.Ignore($"No Gothic installation at >{root}<.");

            if (!_vfsCache.TryGetValue(version, out var vfs))
            {
                vfs = new Vfs();
                foreach (var vdf in Directory.GetFiles(dataFolder!).Where(i => i.EndsWithIgnoreCase(".vdf")))
                    vfs.MountDisk(vdf, VfsOverwriteBehavior.Older);
                _vfsCache[version] = vfs;
            }

            return new World(vfs, worldName, version);
        }

        private static IEnumerable<IVirtualObject> Walk(IEnumerable<IVirtualObject> vobs)
        {
            foreach (var vob in vobs)
            {
                yield return vob;
                foreach (var child in Walk(vob.Children))
                    yield return child;
            }
        }
    }
}
