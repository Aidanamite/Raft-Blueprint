using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using Steamworks;
using HarmonyLib;
using UnityEngine.UI;
using System.Reflection;
using System.Reflection.Emit;
using RaftModLoader;
using HMLLibrary;
using Object = UnityEngine.Object;

public class RaftBlueprint : Mod
{
    //
    public static RaftBlueprint self;
    Harmony harmony;
    static string _f = Path.Combine(SaveAndLoad.AppPath, "raftBlueprints");
    static string prefix = "[Raft Blueprint]: ";
    public static string storage_folder
    {
        get
        {
            if (!Directory.Exists(_f))
                Directory.CreateDirectory(_f);
            return _f;
        }
    }
    public static int newAnchors;
    public void Start()
    {
        self = this;
        newAnchors = 0;
        harmony = new Harmony("com.aidanamite.RaftBlueprint");
        harmony.PatchAll();
        Debug.Log(prefix + "Mod has been loaded!");
    }

    public void OnModUnload()
    {
        harmony.UnpatchAll();
        if (ComponentManager<Raft>.Value != null)
            ComponentManager<Raft>.Value.RemoveAnchor(newAnchors);
        Debug.Log(prefix + "Mod has been unloaded!");
    }
    
    public static void SaveRaft(string raftName)
    {
        if (RAPI.GetLocalPlayer() == null)
        {
            Debug.LogError(prefix + "This can only be done in a world");
            return;
        }
        string storeFile = Path.Combine(storage_folder, raftName);
        List<RGD> RGD_Store = new List<RGD>();
        foreach (MonoBehaviour_ID monoBehaviour_ID in FindObjectsOfType<MonoBehaviour_ID>())
            if (monoBehaviour_ID != null && monoBehaviour_ID.transform.ParentedToRaft() && !(monoBehaviour_ID is Network_Player))
            {
                RGD rgd = monoBehaviour_ID.Serialize_Save();
                if (rgd != null)
                    RGD_Store.Add(rgd);
                else
                {
                    List<RGD> list = monoBehaviour_ID.Serialize_SaveMultiple();
                    if (list.ContainsItems())
                        RGD_Store.AddRange(list);
                }
            }

        BinaryFormatter binaryFormatter = new BinaryFormatter();
        FileStream fileStream = FileManager.CreateFile(storeFile);
        Network_Player player = RAPI.GetLocalPlayer();
        if (fileStream != null)
        {
            Vector3 savePos;
            if (player.transform.ParentedToRaft())
                savePos = ComponentManager<Raft>.Value.transform.InverseTransformPoint(player.transform.position);
            else
                savePos = ComponentManager<Raft>.Value.transform.InverseTransformPoint(FindObjectOfType<RaftBounds>().walkableBlocks[0].transform.position) + Vector3.up;
            fileStream.Write(BitConverter.GetBytes(savePos.x), 0, 4);
            fileStream.Write(BitConverter.GetBytes(savePos.y), 0, 4);
            fileStream.Write(BitConverter.GetBytes(savePos.z), 0, 4);
            foreach (RGD gameObject in RGD_Store)
            {
                long sPos = fileStream.Position;
                fileStream.Position += 8;
                binaryFormatter.Serialize(fileStream, gameObject);
                long ePos = fileStream.Position;
                fileStream.Position = sPos;
                fileStream.Write(BitConverter.GetBytes(ePos - sPos - 8), 0, 8);
                fileStream.Position = ePos;
            }
            fileStream.Close();
            if (ExtraSettingsAPI_Loaded && ComponentManager<Settings>.Value.IsOpen)
                self.reloadBlueprintList();
        }
    }
    public static bool tryLoadRaft(string raftName, bool destroyOld)
    {
        if (File.Exists(Path.Combine(storage_folder, raftName)))
        {
            return LoadRaft(raftName, destroyOld);
        }
        Debug.LogError(prefix + "Raft to load was not found");
        return false;
    }
    public static bool LoadRaft(string raftName, bool destroyOld)
    {
        if (RAPI.GetLocalPlayer() == null)
        {
            Debug.LogError(prefix + "This can only be done in a world");
            return false;
        }
        if (ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
        {
            Debug.LogError(prefix + "Cannot load a raft while other players are in the world");
            return false;
        }
        string storeFile = Path.Combine(storage_folder, raftName);
        List<RGD> RGD_Store = new List<RGD>();
        FileStream fileStream = FileManager.OpenFile(storeFile, FileMode.Open);
        var binForm = new BinaryFormatter();
        Vector3 playerPos = fileStream.ReadVector3();
        List<GameObject> blocksToDestroy = new List<GameObject>();
        List<GameObject> entitiesToDestroy = new List<GameObject>();
        bool movePlayer = RAPI.GetLocalPlayer().PersonController.transform.ParentedToRaft();
        if (destroyOld)
            foreach (GameObject monoBehaviour_ID in FindObjectsOfType<GameObject>())
                if (monoBehaviour_ID != null && monoBehaviour_ID.transform.ParentedToRaft())
                {
                    if (monoBehaviour_ID.GetComponent<Block>() != null)
                        blocksToDestroy.Add(monoBehaviour_ID);
                    else if (monoBehaviour_ID.GetComponent<Network_Entity>() != null && monoBehaviour_ID.GetComponent<Network_Player>() == null && monoBehaviour_ID.GetComponent<AI_NetworkBehavior_Shark>() == null && monoBehaviour_ID.GetComponent<Seagull>() == null)
                        entitiesToDestroy.Add(monoBehaviour_ID);
                }
        while (fileStream.Position < fileStream.Length)
        {
            int data = (int)BitConverter.ToInt64(fileStream.ReadBytes(8), 0);
            MemoryStream memStream = new MemoryStream();
            memStream.Write(fileStream.ReadBytes(data), 0, data);
            memStream.Seek(0, SeekOrigin.Begin);
            RGD_Store.Add((RGD)binForm.Deserialize(memStream));
        }
        fileStream.Close();
        fileStream.Dispose();
        restoreObjects(RGD_Store,!destroyOld);
        RGD_Store.Clear();
        foreach (GameObject gO in blocksToDestroy)
            if (gO != null)
                DestroyBlock(gO.GetComponent<Block>());
        foreach (Cropplot cropplot in FindObjectsOfType<Cropplot>())
            Debug.Log(cropplot.plantationSlots);
        foreach (GameObject gO in entitiesToDestroy)
            Destroy(gO);
        if (movePlayer)
            RAPI.GetLocalPlayer().PersonController.transform.position = ComponentManager<Raft>.Value.transform.TransformPoint(playerPos);
        Transform rot = ComponentManager<Raft>.Value.transform.Find("RotatePivot");
        rot.localPosition = new Vector3(0, 0, 0.75f);
        rot.Find("LockedPivot").localPosition = -rot.localPosition;
        return true;
    }

    public static void DestroyBlock(Block block)
    {
        block.gameObject.SetActive(false);
        FindObjectOfType<RaftBounds>().RemovedWalkableBlocks(new List<Block> { block });
        if (block.networkedBehaviour != null)
            NetworkUpdateManager.RemoveBehaviour(block.networkedBehaviour);
        DestroyImmediate(block.gameObject);
        Traverse.Create<BlockCreator>().Field<List<Block>>("placedBlocks").Value.Remove(block);
    }

    [ConsoleCommand(name: "saveRaft", docs: "Syntax: 'saveRaft <name>' saves the current raft under the specified name")]
    public static string MyCommand(string[] args)
    {
        if (args.Length < 1)
            return prefix + "Not enough arguments";
        for (int i = 1; i < args.Length; i++)
            args[0] += args[i];
        SaveRaft(args[0]);
        return prefix + "Raft saved";
    }

    [ConsoleCommand(name: "loadRaft", docs: "Syntax: 'loadRaft <name>' tries to load the raft stored under the specified name")]
    public static string MyCommand2(string[] args)
    {
        if (args.Length < 1)
            return prefix + "Not enough arguments";
        for (int i = 1; i < args.Length; i++)
            args[0] += args[i];
        if (tryLoadRaft(args[0], true))
            return prefix + "Raft loaded";
        return "";
    }

    [ConsoleCommand(name: "loadRaft2", docs: "Syntax: 'loadRaft2 <name>' tries to load the raft stored under the specified name (does not remove the current raft)")]
    public static string MyCommand5(string[] args)
    {
        if (args.Length < 1)
            return prefix + "Not enough arguments";
        for (int i = 1; i < args.Length; i++)
            args[0] += args[i];
        if (tryLoadRaft(args[0], false))
            return prefix + "Raft loaded";
        return "";
    }

    [ConsoleCommand(name: "rotateRaft", docs: "Syntax: 'rotateRaft <steps>' rotates the raft clockwise by the number of steps (1 = 90°, 2 = 180°, 3 = 270°)")]
    public static string MyCommand3(string[] args)
    {
        if (ComponentManager<Raft>.Value == null)
            return "No raft found";
        if (ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
            return "Cannot rotate raft while other players are in the world";
        if (args.Length < 1)
            return prefix + "Not enough arguments";
        if (args.Length > 1)
            return prefix + "Too many arguments";
        int rot;
        if (!int.TryParse(args[0], out rot))
            return "Could not parse " + args[0] + " as a whole number";
        ComponentManager<Raft>.Value.Rotate(rot);
        return "Raft has been rotated by " + (90 * rot) + "°";
    }

    [ConsoleCommand(name: "moveRaft", docs: "Syntax: 'moveRaft <\u200bx> <\u200bz>' offsets the raft's blocks by the amounts specified")]
    public static string MyCommand4(string[] args)
    {
        if (ComponentManager<Raft>.Value == null)
            return "No raft found";
        if (ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
            return "Cannot move raft while other players are in the world";
        if (args.Length < 2)
            return prefix + "Not enough arguments";
        if (args.Length > 2)
            return prefix + "Too many arguments";
        int x;
        if (!int.TryParse(args[0], out x))
            return "Could not parse " + args[0] + " as a whole number";
        int y;
        if (!int.TryParse(args[1], out y))
            return "Could not parse " + args[1] + " as a whole number";
        ComponentManager<Raft>.Value.Offset(x, y);
        return "Raft has been moved by (" + x + ", " + y + ") blocks";
    }

    [ConsoleCommand(name: "centerRaft", docs: "Syntax: 'centerRaft [volume|mass]' attempts to recenter the raft based on either the raft's volume or the mass")]
    public static string MyCommand6(string[] args)
    {
        if (ComponentManager<Raft>.Value == null)
            return "No raft found";
        if (ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
            return "Cannot move raft while other players are in the world";
        if (args.Length > 1)
            return prefix + "Too many arguments";
        int x;
        int y;
        if (args.Length == 0 || args[0].ToLowerInvariant() == "volume")
        {
            var bounds = (0, 0, 0, 0);
            foreach (var b in BlockCreator.GetPlacedBlocks())
            {
                if (!b || !b.IsWalkable())
                    continue;
                var p = (Mathf.RoundToInt(b.transform.localPosition.x / BlockCreator.BlockSize + BlockCreator.HalfBlockSize), Mathf.RoundToInt(b.transform.localPosition.z / BlockCreator.BlockSize - BlockCreator.HalfBlockSize));
                if (bounds.Item1 > p.Item1)
                    bounds.Item1 = p.Item1;
                if (bounds.Item3 < p.Item1)
                    bounds.Item3 = p.Item1;
                if (bounds.Item2 > p.Item2)
                    bounds.Item2 = p.Item2;
                if (bounds.Item4 < p.Item2)
                    bounds.Item4 = p.Item2;
            }
            x = (bounds.Item1 + bounds.Item3) / -2;
            y = (bounds.Item2 + bounds.Item4) / -2;
        }
        else if (args[0].ToLowerInvariant() == "mass")
        {
            var mass = (0, 0, 0);
            foreach (var b in BlockCreator.GetPlacedBlocks())
            {
                if (!b || !b.IsWalkable())
                    continue;
                mass.Item1 += Mathf.RoundToInt(b.transform.localPosition.x / BlockCreator.BlockSize + BlockCreator.HalfBlockSize);
                mass.Item2 += Mathf.RoundToInt(b.transform.localPosition.z / BlockCreator.BlockSize - BlockCreator.HalfBlockSize);
                mass.Item3 += 1;
            }
            if (mass.Item3 != 0)
            {
                x = Mathf.RoundToInt((float)mass.Item1 / -mass.Item3);
                y = Mathf.RoundToInt((float)mass.Item2 / -mass.Item3);
            }else
            {
                x = 0;
                y = 0;
            }
        }
        else
            return args[0].ToLowerInvariant() + " is not a valid center formula name";
        ComponentManager<Raft>.Value.Offset(x, y);
        return "Raft has been moved by (" + x + ", " + y + ") blocks";
    }

    public static void restoreObjects(List<RGD> objects, bool disallowOverlap) {
        SaveAndLoad saveAndLoad = ComponentManager<SaveAndLoad>.Value;
		Network_Player network_Player = ComponentManager<Raft_Network>.Value.GetLocalPlayer();
		Network_Host_Entities value6 = ComponentManager<Network_Host_Entities>.Value;
		ObjectManager value7 = ComponentManager<ObjectManager>.Value;
			foreach (var rgd in objects)
			{
				switch (rgd.Type)
				{
					case RGDType.Block:
					case RGDType.Block_CookingPot:
					case RGDType.Block_FuelTank:
					case RGDType.Block_ZiplineBase:
					case RGDType.Block_Foundation:
					case RGDType.Block_Firework:
					case RGDType.Block_Electric_Purifier:
					case RGDType.Block_EngineControls:
					case RGDType.Block_Fence:
					case RGDType.Block_Interactable:
					case RGDType.Block_TicTacToe:
					case RGDType.Block_ShortcutPlank:
						{
							RGD_Block rgd_Block = rgd as RGD_Block;
							if (rgd_Block != null)
								saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Block);
							break;
						}
					case RGDType.Block_Door:
						{
							RGD_Door rgd_Door = rgd as RGD_Door;
							Block block = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Door);
							if (block != null)
								rgd_Door.Restore(block.GetComponent<Door>());
							break;
						}
					case RGDType.Block_Cropplot:
						{
							RGD_Cropplot rgd_Cropplot = rgd as RGD_Cropplot;
							Block block2 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Cropplot);
							if (block2 != null)
								rgd_Cropplot.RestoreCropplot(block2.GetComponent<Cropplot>(), network_Player.PlantManager);
							break;
						}
					case RGDType.Block_CookingStand:
						{
							RGD_CookingStand rgd_CookingStand = rgd as RGD_CookingStand;
							Block block3 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_CookingStand);
							if (block3 != null)
								rgd_CookingStand.RestoreStand(block3.GetComponent<Block_CookingStand>());
							break;
						}
					case RGDType.Block_Storage:
						{
							RGD_Storage rgd_Storage = rgd as RGD_Storage;
							Block block4 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Storage);
							if (block4 != null)
								rgd_Storage.RestoreInventory(block4.GetComponent<Storage_Small>().GetInventoryReference());
							break;
						}
					case RGDType.Block_AnchorStationary:
						{
							RGD_AnchorStationary rgd_AnchorStationary = rgd as RGD_AnchorStationary;
							Block block5 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_AnchorStationary);
							if (block5 != null)
								rgd_AnchorStationary.RestoreAnchor(block5.GetComponent<Anchor_Stationary>());
							break;
						}
					case RGDType.Block_AnchorThrowable:
						{
							RGD_AnchorThrowable rgd_AnchorThrowable = rgd as RGD_AnchorThrowable;
							Block block6 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_AnchorThrowable);
							if (block6 != null)
								rgd_AnchorThrowable.RestoreAnchorThrowable(block6, block6.GetComponent<Anchor_Throwable_Stand>());
							break;
						}
					case RGDType.Block_ItemCollector:
						{
							RGD_ItemCollector rgd_ItemCollector = rgd as RGD_ItemCollector;
							Block block7 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_ItemCollector);
							if (block7 != null)
								rgd_ItemCollector.RestoreItemCollector(block7.GetComponentInChildren<ItemCollector>());
							break;
						}
					case RGDType.Block_Scarecrow:
						{
							RGD_Scarecrow rgd_Scarecrow = rgd as RGD_Scarecrow;
							Block block8 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Scarecrow);
							if (block8 != null)
								rgd_Scarecrow.RestoreScarecrow(block8.GetComponent<Scarecrow>());
							break;
						}
					case RGDType.Block_Brick:
						{
							RGD_Brick rgd_Brick = rgd as RGD_Brick;
							Block block9 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Brick);
							if (block9 != null)
								rgd_Brick.RestoreBrick(block9 as Brick_Wet);
							break;
						}
					case RGDType.Block_BirdsNest:
						{
							RGD_Birdsnest rgd_Birdsnest = rgd as RGD_Birdsnest;
							Block block10 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Birdsnest);
							if (block10 != null)
								rgd_Birdsnest.RestoreBirdsNest(block10.GetComponent<BirdsNest>());
							break;
						}
					case RGDType.Block_ResearchTable:
						{
							RGD_ResearchTable rgd_ResearchTable = rgd as RGD_ResearchTable;
							Block block11 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_ResearchTable);
							if (block11 != null)
								rgd_ResearchTable.RestoreResearchTable(block11.GetComponent<ResearchTable>());
							break;
						}
					case RGDType.Block_Sail:
						{
							RGD_Sail rgd_Sail = rgd as RGD_Sail;
							Block block12 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Sail);
							if (block12 != null)
								rgd_Sail.RestoreSail(block12.GetComponent<Sail>());
							break;
						}
					case RGDType.ID_TextWriterObject:
						{
							RGD_TextWriterObject rgd_TextWriterObject = rgd as RGD_TextWriterObject;
							if (rgd_TextWriterObject != null)
								saveAndLoad.StartCoroutine(Traverse.Create(saveAndLoad).Method("LoadTextWriterLate", new object[] { rgd_TextWriterObject, 0.6f }).GetValue<IEnumerator>());
							break;
						}
					case RGDType.Block_Reciever:
						{
							RGD_Reciever rgd_Reciever = rgd as RGD_Reciever;
							saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Reciever)?.GetComponent<Reciever>()?.Restore(rgd_Reciever);
							break;
						}
					case RGDType.AI_NetworkBehaviour:
						{
							RGD_AI_NetworkBehaviour rgd_AI_NetworkBehaviour = rgd as RGD_AI_NetworkBehaviour;
							if (rgd_AI_NetworkBehaviour != null)
								value6.rgdAINetworkBehaviour.Add(rgd_AI_NetworkBehaviour);
							break;
						}
					case RGDType.ID_TrophyHolder:
						{
							RGDTrophyHolder rgdtrophyHolder = rgd as RGDTrophyHolder;
							Block block15 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgdtrophyHolder);
							TrophyHolder componentInChildren = block15?.GetComponentInChildren<TrophyHolder>();
							if (componentInChildren != null)
								rgdtrophyHolder.RestoreTrophyHolder(componentInChildren);
							break;
						}
					case RGDType.ID_ChickenEggs:
						{
							RGD_Chicken_Eggs rgd_Chicken_Eggs = rgd as RGD_Chicken_Eggs;
							if (rgd_Chicken_Eggs != null && rgd_Chicken_Eggs.rgdEggs.ContainsItems())
								foreach (RGD_Egg rgd_Egg in rgd_Chicken_Eggs.rgdEggs)
									if (rgd_Egg != null)
										value7.Object_Chicken.LayEgg(rgd_Egg.localRaftPosition.Value, rgd_Egg.eggObjectIndex);
							break;
						}
					case RGDType.Block_Sprinkler:
						{
							RGD_Block_Sprinkler rgd_Block_Sprinkler = rgd as RGD_Block_Sprinkler;
							Block block16 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Block_Sprinkler);
							if (block16 != null)
							{
								Sprinkler componentInChildren2 = block16.GetComponentInChildren<Sprinkler>();
								if (componentInChildren2 != null)
									rgd_Block_Sprinkler.rgdSprinkler.RestoreSprinkler(componentInChildren2);
							}
							break;
						}
					case RGDType.Block_Radio:
						{
							RGD_Block_Radio rgd_Block_Radio = rgd as RGD_Block_Radio;
							saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Block_Radio)?.GetComponentInChildren<RadioPlayer>()?.RestoreRadio(rgd_Block_Radio.rgdRadio);
							break;
						}
					case RGDType.Block_SteeringWheel:
						{
							RGD_SteeringWheel rgd_SteeringWheel = rgd as RGD_SteeringWheel;
							Block block18 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_SteeringWheel);
							block18?.GetComponentInChildren<SteeringWheel>()?.RestoreWheel(rgd_SteeringWheel);
							break;
						}
					case RGDType.Block_Motor_Wheel:
						{
							RGD_MotorWheel rgd_MotorWheel = rgd as RGD_MotorWheel;
							saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_MotorWheel)?.GetComponentInChildren<MotorWheel>()?.RestoreMotor(rgd_MotorWheel);
							break;
						}
					case RGDType.Block_BioFuelCreator:
						{
							RGD_Block_BiofuelExtractor rgd_Block_BiofuelExtractor = rgd as RGD_Block_BiofuelExtractor;
							saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Block_BiofuelExtractor)?.GetComponent<Placeable_BiofuelExtractor>()?.RestoreExtractor(rgd_Block_BiofuelExtractor.rgdBioFuelCreator);
							break;
						}
					case RGDType.Block_BeeHive:
						{
							RGD_Block_BeeHive rgd_Block_BeeHive = rgd as RGD_Block_BeeHive;
							Block block21 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Block_BeeHive);
							if (rgd_Block_BeeHive != null && block21 != null)
							{
								BeeHive componentInChildren6 = block21.GetComponentInChildren<BeeHive>();
								if (componentInChildren6 != null)
									rgd_Block_BeeHive.RestoreBeehive(componentInChildren6);
							}
							break;
						}
					case RGDType.Block_BatteryCharger:
						{
							RGD_Block_BatteryCharger rgd_Block_BatteryCharger = rgd as RGD_Block_BatteryCharger;
							Block block22 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Block_BatteryCharger);
							if (block22 != null)
							{
								BatteryCharger componentInChildren7 = block22.GetComponentInChildren<BatteryCharger>();
								if (componentInChildren7 != null)
									rgd_Block_BatteryCharger.rgdBatteryCharger.RestoreBatteryCharger(componentInChildren7);
							}
							break;
						}
					case RGDType.Block_WindTurbine:
						{
							RGD_Block_WindTurbine rgd_Block_WindTurbine = rgd as RGD_Block_WindTurbine;
							Block block23 = saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Block_WindTurbine);
							if (block23 != null)
							{
								WindTurbine componentInChildren8 = block23.GetComponentInChildren<WindTurbine>();
								if (componentInChildren8 != null)
									rgd_Block_WindTurbine.rgdWindTurbine.Restore(componentInChildren8);
							}
							break;
						}
					case RGDType.Block_Recycler:
						{
							RGD_Block_Recycler rgd_Block_Recycler = rgd as RGD_Block_Recycler;
							saveAndLoad.RestoreBlock(network_Player.BlockCreator, rgd_Block_Recycler)?.GetComponent<Placeable_RecyclerExtractor>()?.RestoreExtractor(rgd_Block_Recycler.rgdRecycler);
							break;
						}
				}
			}
		SaveAndLoad.LoadRGDAINetworkBehaviours?.Invoke();
		ComponentManager<RaftBounds>.Value?.Initialize();
		ComponentManager<RaftCollisionManager>.Value?.Initialize();
        ReloadRaftCollisions();
	}
    public void reloadBlueprintList()
    {
        ExtraSettingsAPI_ResetComboboxContent("Blueprints");
        string[] files = Directory.GetFiles(storage_folder);
        List<string> content = new List<string>();
        content.Add(ExtraSettingsAPI_GetComboboxContent("Blueprints")[0]);
        foreach (string file in files)
            content.Add(Path.GetFileName(file));
        ExtraSettingsAPI_SetComboboxContent("Blueprints", content.ToArray());
    }

    public void ExtraSettingsAPI_SettingsOpen()
    {
        reloadBlueprintList();
    }
    public void ExtraSettingsAPI_ButtonPress(string name)
    {
        if (name == "load" && ExtraSettingsAPI_GetComboboxSelectedIndex("Blueprints") > 0)
            tryLoadRaft(ExtraSettingsAPI_GetComboboxSelectedItem("Blueprints"), true);
        if (name == "load2" && ExtraSettingsAPI_GetComboboxSelectedIndex("Blueprints") > 0)
            tryLoadRaft(ExtraSettingsAPI_GetComboboxSelectedItem("Blueprints"), false);
        if (name == "save")
        {
            string tmp = ExtraSettingsAPI_GetInputValue("saveName");
            if (tmp == "")
                tmp = SaveAndLoad.CurrentGameFileName;
            SaveRaft(tmp);
            ExtraSettingsAPI_SetComboboxSelectedItem("Blueprints", tmp);
        }
        if (name == "browse")
            System.Diagnostics.Process.Start(storage_folder);
        if (name == "movex")
            ComponentManager<Raft>.Value.Offset(int.Parse(ExtraSettingsAPI_GetInputValue("offset")), 0);
        if (name == "movez")
            ComponentManager<Raft>.Value.Offset(0, int.Parse(ExtraSettingsAPI_GetInputValue("offset")));
        if (name == "rotate")
            ComponentManager<Raft>.Value.Rotate(int.Parse(ExtraSettingsAPI_GetInputValue("offset")));
        if (name == "center")
            MyCommand6(new[] { ExtraSettingsAPI_GetComboboxSelectedItem("formula") });
    }

    public static void ReloadRaftCollisions()
    {
        var c = ComponentManager<BlockCollisionConsolidator>.Value;
        c.Invoke("OnDisable", 0);
        c.Invoke("OnEnable", 0);
    }


    static bool ExtraSettingsAPI_Loaded = false;
    public static int ExtraSettingsAPI_GetComboboxSelectedIndex(string SettingName) => -1;
    public static string ExtraSettingsAPI_GetComboboxSelectedItem(string SettingName) => "";
    public static string[] ExtraSettingsAPI_GetComboboxContent(string SettingName) => new string[0];
    public static void ExtraSettingsAPI_SetComboboxSelectedItem(string SettingName, string value) { }
    public static void ExtraSettingsAPI_SetComboboxContent(string SettingName, string[] value) { }
    public static void ExtraSettingsAPI_ResetComboboxContent(string SettingName) { }
    public static string ExtraSettingsAPI_GetInputValue(string SettingName) => "";
    public static float ExtraSettingsAPI_GetSliderValue(string SettingName) => 0;
}

static class ExtentionMethods
{
    public static byte[] ReadBytes(this FileStream stream, int count)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
            bytes[i] = (byte)stream.ReadByte();
        return bytes;
    }
    public static Block RestoreBlock(this SaveAndLoad saveAndLoad,BlockCreator blockCreator, RGD_Block block)
    {
        return Traverse.Create(saveAndLoad).Method("RestoreBlock", new object[] { blockCreator, block }).GetValue<Block>();
    }
    public static Vector3 ReadVector3(this FileStream stream)
    {
        return new Vector3(BitConverter.ToSingle(stream.ReadBytes(4), 0), BitConverter.ToSingle(stream.ReadBytes(4), 0), BitConverter.ToSingle(stream.ReadBytes(4), 0));
    }

    public static void Rotate(this Raft raft, int rotation)
    {
        rotation %= 4;
        foreach (Transform child in raft.transform.Find("RotatePivot").Find("LockedPivot"))
        {
            child.RotateAround(child.parent.position, child.parent.up, 90 * rotation);
            if (child.GetComponent<Block>() != null && !child.GetComponent<Block>().isRotateable && new[] { DPS.Ceiling, DPS.Default, DPS.FloorAndCeiling, DPS.Floor, DPS.Pipe }.Contains(child.GetComponent<Block>().dpsType) && !child.GetComponent<Block>().forceRotationToQuadDirection)
                child.Rotate(Vector3.up, -90 * rotation);
        }
        foreach (BitmaskTile tile in Object.FindObjectsOfType<BitmaskTile>())
            tile.Refresh(false);
        RaftBlueprint.ReloadRaftCollisions();
    }

    public static void Offset(this Raft raft, int x, int z)
    {
        foreach (Transform child in raft.transform.Find("RotatePivot").Find("LockedPivot"))
            child.localPosition += new Vector3(BlockCreator.BlockSize * x, 0, BlockCreator.BlockSize * z);
        RaftBlueprint.ReloadRaftCollisions();
    }
}

[HarmonyPatch(typeof(Cropplot_Grass), "PlantSeed")]
public class Patch_PlantGrass
{
    static bool Prefix(Cropplot_Grass __instance)
    {
        if (__instance != null && __instance.IsFull)
            return false;
        return true;
    }
}