﻿using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using Random_Features.Libs;
using SharpDX;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using Input = ExileCore.Input;
using nuVector2 = System.Numerics.Vector2;

namespace PickIt
{
    public class PickIt : BaseSettingsPlugin<PickItSettings>
    {
        private const string PickitRuleDirectory = "Pickit Rules";
        private readonly List<Entity> _entities = new List<Entity>();
        private readonly Stopwatch _pickUpTimer = Stopwatch.StartNew();
        private readonly Stopwatch DebugTimer = Stopwatch.StartNew();
        private readonly WaitTime toPick = new WaitTime(1);
        private readonly WaitTime wait1ms = new WaitTime(1);
        private readonly WaitTime wait2ms = new WaitTime(2);
        private readonly WaitTime wait3ms = new WaitTime(3);
        private readonly WaitTime waitForNextTry = new WaitTime(1);
        private Vector2 _clickWindowOffset;
        private HashSet<string> _magicRules;
        private HashSet<string> _normalRules;
        private HashSet<string> _rareRules;
        private HashSet<string> _uniqueRules;
        private HashSet<string> _ignoreRules;
        private Dictionary<string, int> _weightsRules = new Dictionary<string, int>();
        private WaitTime _workCoroutine;
        public DateTime buildDate;
        private uint coroutineCounter;
        private bool FullWork = true;
        public string MagicRuleFile;
        private WaitTime mainWorkCoroutine = new WaitTime(5);
        public string NormalRuleFile;
        private Coroutine pickItCoroutine;
        public string RareRuleFile;
        private WaitTime tryToPick = new WaitTime(7);
        public string UniqueRuleFile;
        private WaitTime waitPlayerMove = new WaitTime(10);
        private List<string> _customItems = new List<string>();
        public int[,] inventorySlots { get; set; } = new int[0,0];
        public ServerInventory InventoryItems { get; set; }
        public static PickIt Controller { get; set; }


        public FRSetManagerPublishInformation FullRareSetManagerData = new FRSetManagerPublishInformation();

        public PickIt()
        {
            Name = "Pickit";
        }

        public string PluginVersion { get; set; }
        private List<string> PickitFiles { get; set; }

        public override bool Initialise()
        {
            Controller = this;
            pickItCoroutine = new Coroutine(MainWorkCoroutine(), this, "Pick It");
            Core.ParallelRunner.Run(pickItCoroutine);
            pickItCoroutine.Pause();
            DebugTimer.Reset();
            Settings.MouseSpeed.OnValueChanged += (sender, f) => { Mouse.speedMouse = Settings.MouseSpeed.Value; };
            _workCoroutine = new WaitTime(Settings.ExtraDelay);
            Settings.ExtraDelay.OnValueChanged += (sender, i) => _workCoroutine = new WaitTime(i);
            LoadRuleFiles();
            //LoadCustomItems();
            return true;
        }

        // bad idea to add hard coded pickups.
        private void LoadCustomItems()
        {
            _customItems.Add("Treasure Key");
            _customItems.Add("Silver Key");
            _customItems.Add("Golden Key");
            _customItems.Add("Flashpowder Keg");
            _customItems.Add("Divine Life Flask");
            _customItems.Add("Quicksilver Flask");
            _customItems.Add("Stone of Passage");
        }

        private IEnumerator MainWorkCoroutine()
        {
            while (true)
            {
                yield return FindItemToPick();

                coroutineCounter++;
                pickItCoroutine.UpdateTicks(coroutineCounter);
                yield return _workCoroutine;
            }
        }

        public override void DrawSettings()
        {
            Settings.ShowInventoryView.Value = ImGuiExtension.Checkbox("Show Inventory Slots", Settings.ShowInventoryView.Value);
            Settings.MoveInventoryView.Value = ImGuiExtension.Checkbox("Moveable Inventory Slots", Settings.MoveInventoryView.Value);

            Settings.PickUpKey = ImGuiExtension.HotkeySelector("Pickup Key: " + Settings.PickUpKey.Value.ToString(), Settings.PickUpKey);
            Settings.LeftClickToggleNode.Value = ImGuiExtension.Checkbox("Mouse Button: " + (Settings.LeftClickToggleNode ? "Left" : "Right"), Settings.LeftClickToggleNode);
            Settings.LeftClickToggleNode.Value = ImGuiExtension.Checkbox("Return Mouse To Position Before Click", Settings.ReturnMouseToBeforeClickPosition);
            Settings.GroundChests.Value = ImGuiExtension.Checkbox("Click Chests If No Items Around", Settings.GroundChests);
            Settings.PickupRange.Value = ImGuiExtension.IntSlider("Pickup Radius", Settings.PickupRange);
            Settings.ChestRange.Value = ImGuiExtension.IntSlider("Chest Radius", Settings.ChestRange);
            Settings.ExtraDelay.Value = ImGuiExtension.IntSlider("Extra Click Delay", Settings.ExtraDelay);
            Settings.MouseSpeed.Value = ImGuiExtension.FloatSlider("Mouse speed", Settings.MouseSpeed);
            Settings.TimeBeforeNewClick.Value = ImGuiExtension.IntSlider("Time wait for new click", Settings.TimeBeforeNewClick);
            //Settings.OverrideItemPickup.Value = ImGuiExtension.Checkbox("Item Pickup Override", Settings.OverrideItemPickup); ImGui.SameLine();
            //ImGuiExtension.ToolTip("Override item.CanPickup\n\rDO NOT enable this unless you know what you're doing!");
            Settings.LazyLooting.Value = ImGuiExtension.Checkbox("Use Lazy Looting", Settings.LazyLooting);
            Settings.LazyLootingPauseKey.Value = ImGuiExtension.HotkeySelector("Pause lazy looting for 2 sec: " + Settings.LazyLootingPauseKey.Value, Settings.LazyLootingPauseKey);
            
            var tempRef = false;
            if (ImGui.CollapsingHeader("Pickit Rules", ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.Button("Reload All Files")) LoadRuleFiles();
                Settings.NormalRuleFile = ImGuiExtension.ComboBox("Normal Rules", Settings.NormalRuleFile, PickitFiles, out tempRef);
                if (tempRef) _normalRules = LoadPickit(Settings.NormalRuleFile);
                Settings.MagicRuleFile = ImGuiExtension.ComboBox("Magic Rules", Settings.MagicRuleFile, PickitFiles, out tempRef);
                if (tempRef) _magicRules = LoadPickit(Settings.MagicRuleFile);
                Settings.RareRuleFile = ImGuiExtension.ComboBox("Rare Rules", Settings.RareRuleFile, PickitFiles, out tempRef);
                if (tempRef) _rareRules = LoadPickit(Settings.RareRuleFile);
                Settings.UniqueRuleFile = ImGuiExtension.ComboBox("Unique Rules", Settings.UniqueRuleFile, PickitFiles, out tempRef);
                if (tempRef) _uniqueRules = LoadPickit(Settings.UniqueRuleFile);
                Settings.WeightRuleFile = ImGuiExtension.ComboBox("Weight Rules", Settings.WeightRuleFile, PickitFiles, out tempRef);
                if (tempRef) _weightsRules = LoadWeights(Settings.WeightRuleFile);
                Settings.IgnoreRuleFile = ImGuiExtension.ComboBox("Ignore Rules", Settings.IgnoreRuleFile, PickitFiles, out tempRef);
                if (tempRef) _ignoreRules = LoadPickit(Settings.IgnoreRuleFile);
            }

            if (ImGui.CollapsingHeader("Item Logic", ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.TreeNode("Influence Types"))
                {
                    Settings.ShaperItems.Value = ImGuiExtension.Checkbox("Shaper Items", Settings.ShaperItems);
                    Settings.ElderItems.Value = ImGuiExtension.Checkbox("Elder Items", Settings.ElderItems);
                    Settings.HunterItems.Value = ImGuiExtension.Checkbox("Hunter Items", Settings.HunterItems);
                    Settings.CrusaderItems.Value = ImGuiExtension.Checkbox("Crusader Items", Settings.CrusaderItems);
                    Settings.WarlordItems.Value = ImGuiExtension.Checkbox("Warlord Items", Settings.WarlordItems);
                    Settings.RedeemerItems.Value = ImGuiExtension.Checkbox("Redeemer Items", Settings.RedeemerItems);
                    Settings.FracturedItems.Value = ImGuiExtension.Checkbox("Fractured Items", Settings.FracturedItems);
                    Settings.VeiledItems.Value = ImGuiExtension.Checkbox("Veiled Items", Settings.VeiledItems);
                    ImGui.Spacing();
                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("Links/Sockets/RGB"))
                {
                    Settings.RGB.Value = ImGuiExtension.Checkbox("RGB Items", Settings.RGB);
                    Settings.RGBWidth.Value = ImGuiExtension.IntSlider("Maximum Width##RGBWidth", Settings.RGBWidth);
                    Settings.RGBHeight.Value = ImGuiExtension.IntSlider("Maximum Height##RGBHeight", Settings.RGBHeight);
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.Spacing();
                    Settings.TotalSockets.Value = ImGuiExtension.IntSlider("##Sockets", Settings.TotalSockets);
                    ImGui.SameLine();
                    Settings.Sockets.Value = ImGuiExtension.Checkbox("Sockets", Settings.Sockets);
                    Settings.LargestLink.Value = ImGuiExtension.IntSlider("##Links", Settings.LargestLink);
                    ImGui.SameLine();
                    Settings.Links.Value = ImGuiExtension.Checkbox("Links", Settings.Links);
                    ImGui.Separator();
                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("Overrides"))
                {
                    Settings.UseWeight.Value = ImGuiExtension.Checkbox("Use Weight", Settings.UseWeight);
                    Settings.IgnoreScrollOfWisdom.Value = ImGuiExtension.Checkbox("Ignore Scroll Of Wisdom", Settings.IgnoreScrollOfWisdom);
                    Settings.PickUpEverything.Value = ImGuiExtension.Checkbox("Pickup Everything", Settings.PickUpEverything);
                    Settings.AllDivs.Value = ImGuiExtension.Checkbox("All Divination Cards", Settings.AllDivs);
                    Settings.AllCurrency.Value = ImGuiExtension.Checkbox("All Currency", Settings.AllCurrency);
                    Settings.AllUniques.Value = ImGuiExtension.Checkbox("All Uniques", Settings.AllUniques);
                    Settings.QuestItems.Value = ImGuiExtension.Checkbox("Quest Items", Settings.QuestItems);
                    Settings.Maps.Value = ImGuiExtension.Checkbox("##Maps", Settings.Maps);
                    ImGui.SameLine();
                    if (ImGui.TreeNode("Maps"))
                    {
                        Settings.MapTier.Value = ImGuiExtension.IntSlider("Lowest Tier", Settings.MapTier);
                        Settings.UniqueMap.Value = ImGuiExtension.Checkbox("All Unique Maps", Settings.UniqueMap);
                        Settings.MapFragments.Value = ImGuiExtension.Checkbox("Fragments", Settings.MapFragments);
                        ImGui.Spacing();
                        ImGui.TreePop();
                    }

                    Settings.GemQuality.Value = ImGuiExtension.IntSlider("##Gems", "Lowest Quality", Settings.GemQuality);
                    ImGui.SameLine();
                    Settings.Gems.Value = ImGuiExtension.Checkbox("Gems", Settings.Gems);

                    Settings.FlasksQuality.Value = ImGuiExtension.IntSlider("##Flasks", "Lowest Quality", Settings.FlasksQuality);
                    ImGui.SameLine();
                    Settings.Flasks.Value = ImGuiExtension.Checkbox("Flasks", Settings.Flasks);
                    ImGui.Separator();
                    ImGui.TreePop();
                }
                Settings.HeistItems.Value = ImGuiExtension.Checkbox("Heist Items", Settings.HeistItems);

                Settings.Rares.Value = ImGuiExtension.Checkbox("##Rares", Settings.Rares);
                ImGui.SameLine();
                if (ImGui.TreeNode("Rares##asd"))
                {
                    Settings.RareJewels.Value = ImGuiExtension.Checkbox("Jewels", Settings.RareJewels);
                    Settings.RareRingsilvl.Value = ImGuiExtension.IntSlider("##RareRings", "Lowest iLvl", Settings.RareRingsilvl);
                    ImGui.SameLine();
                    Settings.RareRings.Value = ImGuiExtension.Checkbox("Rings", Settings.RareRings);
                    Settings.RareAmuletsilvl.Value = ImGuiExtension.IntSlider("##RareAmulets", "Lowest iLvl", Settings.RareAmuletsilvl);
                    ImGui.SameLine();
                    Settings.RareAmulets.Value = ImGuiExtension.Checkbox("Amulets", Settings.RareAmulets);
                    Settings.RareBeltsilvl.Value = ImGuiExtension.IntSlider("##RareBelts", "Lowest iLvl", Settings.RareBeltsilvl);
                    ImGui.SameLine();
                    Settings.RareBelts.Value = ImGuiExtension.Checkbox("Belts", Settings.RareBelts);
                    Settings.RareGlovesilvl.Value = ImGuiExtension.IntSlider("##RareGloves", "Lowest iLvl", Settings.RareGlovesilvl);
                    ImGui.SameLine();
                    Settings.RareGloves.Value = ImGuiExtension.Checkbox("Gloves", Settings.RareGloves);
                    Settings.RareBootsilvl.Value = ImGuiExtension.IntSlider("##RareBoots", "Lowest iLvl", Settings.RareBootsilvl);
                    ImGui.SameLine();
                    Settings.RareBoots.Value = ImGuiExtension.Checkbox("Boots", Settings.RareBoots);
                    Settings.RareHelmetsilvl.Value = ImGuiExtension.IntSlider("##RareHelmets", "Lowest iLvl", Settings.RareHelmetsilvl);
                    ImGui.SameLine();
                    Settings.RareHelmets.Value = ImGuiExtension.Checkbox("Helmets", Settings.RareHelmets);
                    Settings.RareArmourilvl.Value = ImGuiExtension.IntSlider("##RareArmours", "Lowest iLvl", Settings.RareArmourilvl);
                    ImGui.SameLine();
                    Settings.RareArmour.Value = ImGuiExtension.Checkbox("Armours", Settings.RareArmour);
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.Spacing();
                    Settings.RareShieldilvl.Value = ImGuiExtension.IntSlider("##Shields", "Lowest iLvl", Settings.RareShieldilvl);
                    ImGui.SameLine();
                    Settings.RareShield.Value = ImGuiExtension.Checkbox("Shields", Settings.RareShield);
                    Settings.RareShieldWidth.Value = ImGuiExtension.IntSlider("Maximum Width##RareShieldWidth", Settings.RareShieldWidth);
                    Settings.RareShieldHeight.Value = ImGuiExtension.IntSlider("Maximum Height##RareShieldHeight", Settings.RareShieldHeight);
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.Spacing();
                    Settings.RareWeaponilvl.Value = ImGuiExtension.IntSlider("##RareWeapons", "Lowest iLvl", Settings.RareWeaponilvl);
                    ImGui.SameLine();
                    Settings.RareWeapon.Value = ImGuiExtension.Checkbox("Weapons", Settings.RareWeapon);
                    Settings.RareWeaponWidth.Value = ImGuiExtension.IntSlider("Maximum Width##RareWeaponWidth", Settings.RareWeaponWidth);
                    Settings.RareWeaponHeight.Value = ImGuiExtension.IntSlider("Maximum Height##RareWeaponHeight", Settings.RareWeaponHeight);
                    if (ImGui.TreeNode("Full Rare Set Manager Integration##FRSMI"))
                    {
                        ImGui.BulletText("You must use github.com/DetectiveSquirrel/FullRareSetManager in order to utilize this section\nThis will determine what items are still needed to be picked up\nfor the chaos recipe, it uses FRSM's count to check this.'");
                        ImGui.Spacing();
                        Settings.FullRareSetManagerOverride.Value = ImGuiExtension.Checkbox("Override Rare Pickup with Full Rare Set Managers' needed pieces", Settings.FullRareSetManagerOverride);

                        Settings.FullRareSetManagerOverrideAllowIdentifiedItems.Value = ImGuiExtension.Checkbox("Pickup Identified items?", Settings.FullRareSetManagerOverrideAllowIdentifiedItems);
                        ImGui.Spacing();
                        ImGui.Spacing();
                        ImGui.BulletText("Set the number you wish to pickup for Full Rare Set Manager overrides\nDefault: -1\n-1 will disable these overrides");
                        ImGui.Spacing();
                        Settings.FullRareSetManagerPickupOverrides.Weapons = ImGuiExtension.IntSlider("Max Weapons(s)##FRSMOverrides1", Settings.FullRareSetManagerPickupOverrides.Weapons, -1, 100);
                        Settings.FullRareSetManagerPickupOverrides.Helmets = ImGuiExtension.IntSlider("Max Helmets##FRSMOverrides2", Settings.FullRareSetManagerPickupOverrides.Helmets, -1, 100);
                        Settings.FullRareSetManagerPickupOverrides.BodyArmors = ImGuiExtension.IntSlider("Max Body Armors##FRSMOverrides3", Settings.FullRareSetManagerPickupOverrides.BodyArmors, -1, 100);
                        Settings.FullRareSetManagerPickupOverrides.Gloves = ImGuiExtension.IntSlider("Max Gloves##FRSMOverrides4", Settings.FullRareSetManagerPickupOverrides.Gloves, -1, 100);
                        Settings.FullRareSetManagerPickupOverrides.Boots = ImGuiExtension.IntSlider("Max Boots##FRSMOverrides5", Settings.FullRareSetManagerPickupOverrides.Boots, -1, 100);
                        Settings.FullRareSetManagerPickupOverrides.Belts = ImGuiExtension.IntSlider("Max Belts##FRSMOverrides6", Settings.FullRareSetManagerPickupOverrides.Belts, -1, 100);
                        Settings.FullRareSetManagerPickupOverrides.Amulets = ImGuiExtension.IntSlider("Max Amulets##FRSMOverrides7", Settings.FullRareSetManagerPickupOverrides.Amulets, -1, 100);
                        Settings.FullRareSetManagerPickupOverrides.Rings = ImGuiExtension.IntSlider("Max Ring Sets##FRSMOverrides8", Settings.FullRareSetManagerPickupOverrides.Rings, -1, 100);
                        ImGui.Spacing();
                        ImGui.Spacing();
                        ImGui.BulletText("Set the ilvl Min/Max you wish to pickup for Full Rare Set Manager overrides\nIt is up to you how to use these two features\nit does not change how FullRareSetManager counts its sets.\nDefault: -1\n-1 will disable these overrides");
                        ImGui.Spacing();
                        Settings.FullRareSetManagerPickupOverrides.MinItemLevel = ImGuiExtension.IntSlider("Minimum Item Level##FRSMOverrides9", Settings.FullRareSetManagerPickupOverrides.MinItemLevel, -1, 100);
                        Settings.FullRareSetManagerPickupOverrides.MaxItemLevel = ImGuiExtension.IntSlider("Max Item Level##FRSMOverrides10", Settings.FullRareSetManagerPickupOverrides.MaxItemLevel, -1, 100);
                        ImGui.TreePop();
                    }
                    ImGui.TreePop();

                }
            }
        }

        private DateTime DisableLazyLootingTill { get; set; }

        public override Job Tick()
        {
            if (GameController.Game.IngameState.ServerData.PlayerInventories.Count == 0)
                return null;

            InventoryItems = GameController.Game.IngameState.ServerData.PlayerInventories[0].Inventory;
            inventorySlots = Misc.GetContainer2DArray(InventoryItems);
            DrawIgnoredCellsSettings();
            if (Input.GetKeyState(Settings.LazyLootingPauseKey)) DisableLazyLootingTill = DateTime.Now.AddSeconds(2);
            if (Input.GetKeyState(Keys.Escape)) pickItCoroutine.Pause();

            if (Input.GetKeyState(Settings.PickUpKey.Value) ||
                CanLazyLoot())
            {
                DebugTimer.Restart();

                if (pickItCoroutine.IsDone)
                {
                    var firstOrDefault = Core.ParallelRunner.Coroutines.FirstOrDefault(x => x.OwnerName == nameof(PickIt));

                    if (firstOrDefault != null)
                        pickItCoroutine = firstOrDefault;
                }

                pickItCoroutine.Resume();
                FullWork = false;
            }
            else
            {
                if (FullWork)
                {
                    pickItCoroutine.Pause();
                    DebugTimer.Reset();
                }
            }

            if (DebugTimer.ElapsedMilliseconds > 300)
            {
                FullWork = true;
                //LogMessage("Error pick it stop after time limit 300 ms", 1);
                DebugTimer.Reset();
            }
            //Graphics.DrawText($@"PICKIT :: Debug Tick Timer ({DebugTimer.ElapsedMilliseconds}ms)", new Vector2(100, 100), FontAlign.Left);
            //DebugTimer.Reset();

            return null;
        }



        //TODO: Make function pretty

        private void DrawIgnoredCellsSettings()
        {
            if (!Settings.ShowInventoryView.Value)
                return;

            var _opened = true;

            var MoveableFlag = ImGuiWindowFlags.NoScrollbar |
                               ImGuiWindowFlags.NoTitleBar |
                               ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;

            var NonMoveableFlag = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground |
                                  ImGuiWindowFlags.NoTitleBar |
                                  ImGuiWindowFlags.NoInputs |
                                  ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;

            ImGui.SetNextWindowPos(Settings.InventorySlotsVector2, ImGuiCond.Always, nuVector2.Zero);

            if (ImGui.Begin($"{Name}", ref _opened,
                Settings.MoveInventoryView.Value ? MoveableFlag : NonMoveableFlag))
            {
                var _numb = 1;
                for (var i = 0; i < 5; i++)
                for (var j = 0; j < 12; j++)
                {
                    var toggled = Convert.ToBoolean(inventorySlots[i, j]);
                    if (ImGui.Checkbox($"##{_numb}IgnoredCells", ref toggled)) inventorySlots[i, j] ^= 1;

                    if ((_numb - 1) % 12 < 11) ImGui.SameLine();

                    _numb += 1;
                }

                if (Settings.MoveInventoryView.Value)
                    Settings.InventorySlotsVector2 = ImGui.GetWindowPos();

                ImGui.End();
            }
        }

        public bool InCustomList(HashSet<string> checkList, CustomItem itemEntity, ItemRarity rarity)
        {
            if (checkList.Contains(itemEntity.BaseName) && !_ignoreRules.Contains(itemEntity.BaseName) && itemEntity.Rarity == rarity)
                return true;
            if (checkList.Contains(itemEntity.ClassName) && !_ignoreRules.Contains(itemEntity.ClassName) && itemEntity.Rarity == rarity)
                return true;
            return false;
        }

        public bool OverrideChecks(CustomItem item)
        {
            try
            {
                if (_ignoreRules.Contains(item.BaseName) || _ignoreRules.Contains(item.ClassName))
                    return false;

                #region Currency

                if (Settings.AllCurrency && item.ClassName.EndsWith("Currency"))
                {
                    return !item.Path.Equals("Metadata/Items/Currency/CurrencyIdentification", StringComparison.Ordinal) ||
                           !Settings.IgnoreScrollOfWisdom;
                }

                #endregion

                #region Shaper & Elder

                if (Settings.ElderItems)
                {
                    if (item.IsElder)
                        return true;
                }

                if (Settings.ShaperItems)
                {
                    if (item.IsShaper)
                        return true;
                }

                if (Settings.FracturedItems)
                {
                    if (item.IsFractured)
                        return true;
                }

                #endregion


                if (Settings.HeistItems)
                {
                    if (item.IsHeist)
                        return true;
                }

                #region Influenced

                if (Settings.HunterItems)
                {
                    if (item.IsHunter)
                        return true;
                }

                if (Settings.RedeemerItems)
                {
                    if (item.IsRedeemer)
                        return true;
                }

                if (Settings.CrusaderItems)
                {
                    if (item.IsCrusader)
                        return true;
                }

                if (Settings.WarlordItems)
                {
                    if (item.IsWarlord)
                        return true;
                }

                if (Settings.VeiledItems)
                {
                    if (item.IsVeiled)
                        return true;
                }

                #endregion

                #region Rare Overrides

                if (Settings.Rares && item.Rarity == ItemRarity.Rare)
                {
                    var setData = FullRareSetManagerData;
                    var maxSetWanted = setData.WantedSets;
                    var maxPickupOverides = Settings.FullRareSetManagerPickupOverrides;

                    if (Settings.FullRareSetManagerOverride.Value &&
                        (maxPickupOverides.MinItemLevel > -1 ? item.ItemLevel >= maxPickupOverides.MinItemLevel : item.ItemLevel >= 60) &&
                        (maxPickupOverides.MaxItemLevel > -1 ? item.ItemLevel <= maxPickupOverides.MaxItemLevel : item.ItemLevel <= 74))
                    { 

                        if (item.IsIdentified && !Settings.FullRareSetManagerOverrideAllowIdentifiedItems.Value)
                            return false;

                        if (Settings.RareRings && item.ClassName == "Ring" && setData.GatheredRings < (maxPickupOverides.Rings > -1 ? maxPickupOverides.Rings : maxSetWanted)) return true;
                        if (Settings.RareAmulets && item.ClassName == "Amulet" && setData.GatheredAmulets < (maxPickupOverides.Amulets > -1 ? maxPickupOverides.Amulets : maxSetWanted)) return true;
                        if (Settings.RareBelts && item.ClassName == "Belt" && setData.GatheredBelts < (maxPickupOverides.Belts > -1 ? maxPickupOverides.Belts : maxSetWanted)) return true;
                        if (Settings.RareGloves && item.ClassName == "Gloves" && setData.GatheredGloves < (maxPickupOverides.Gloves > -1 ? maxPickupOverides.Gloves : maxSetWanted)) return true;
                        if (Settings.RareBoots && item.ClassName == "Boots" && setData.GatheredBoots < (maxPickupOverides.Boots > -1 ? maxPickupOverides.Boots : maxSetWanted)) return true;
                        if (Settings.RareHelmets && item.ClassName == "Helmet" && setData.GatheredHelmets < (maxPickupOverides.Helmets > -1 ? maxPickupOverides.Helmets : maxSetWanted)) return true;
                        if (Settings.RareArmour && item.ClassName == "Body Armour" && setData.GatheredBodyArmors < (maxPickupOverides.BodyArmors > -1 ? maxPickupOverides.BodyArmors : maxSetWanted)) return true;
                        if (Settings.RareWeapon && item.IsWeapon && setData.GatheredWeapons < (maxPickupOverides.Weapons > -1 ? maxPickupOverides.Weapons : maxSetWanted))
                            if (item.Width <= Settings.RareWeaponWidth && item.Height <= Settings.RareWeaponHeight) return true;

                    }
                    else
                    {
                        if (Settings.RareRings && item.ClassName == "Ring" && item.ItemLevel >= Settings.RareRingsilvl) return true; 
                        if (Settings.RareAmulets && item.ClassName == "Amulet" && item.ItemLevel >= Settings.RareAmuletsilvl) return true;
                        if (Settings.RareBelts && item.ClassName == "Belt" && item.ItemLevel >= Settings.RareBeltsilvl) return true;
                        if (Settings.RareGloves && item.ClassName == "Gloves" && item.ItemLevel >= Settings.RareGlovesilvl) return true;
                        if (Settings.RareBoots && item.ClassName == "Boots" && item.ItemLevel >= Settings.RareBootsilvl) return true;
                        if (Settings.RareHelmets && item.ClassName == "Helmet" && item.ItemLevel >= Settings.RareHelmetsilvl) return true;
                        if (Settings.RareArmour && item.ClassName == "Body Armour" && item.ItemLevel >= Settings.RareArmourilvl) return true;

                        if (Settings.RareWeapon && item.IsWeapon && item.ItemLevel >= Settings.RareWeaponilvl)
                            if (item.Width <= Settings.RareWeaponWidth && item.Height <= Settings.RareWeaponHeight) return true;
                    }

                    if (Settings.RareShield && item.ClassName == "Shield" && item.ItemLevel >= Settings.RareShieldilvl)
                        if (item.Width <= Settings.RareShieldWidth && item.Height <= Settings.RareShieldHeight)
                            return true;

                    if (Settings.RareJewels && (item.ClassName == "Jewel" || item.ClassName == "AbyssJewel")) return true;
                }

                #endregion

                #region Sockets/Links/RGB

                if (Settings.Sockets && item.Sockets >= Settings.TotalSockets.Value) return true;
                if (Settings.Links && item.LargestLink >= Settings.LargestLink) return true;
                if (Settings.RGB && item.IsRGB && item.Width <= Settings.RGBWidth && item.Height <= Settings.RGBHeight) return true;

                #endregion

                #region Divination Cards

                if (Settings.AllDivs && item.ClassName == "DivinationCard") return true;

                #endregion

                #region Maps

                if (Settings.Maps && item.MapTier >= Settings.MapTier.Value) return true;
                if (Settings.Maps && Settings.UniqueMap && item.MapTier >= 1 && item.Rarity == ItemRarity.Unique) return true;
                if (Settings.Maps && Settings.MapFragments && item.ClassName == "MapFragment") return true;

                #endregion

                #region Quest Items

                if (Settings.QuestItems && item.ClassName == "QuestItem") return true;

                #endregion

                #region Qualiity Rules

                if (Settings.Gems && item.Quality >= Settings.GemQuality.Value && item.ClassName.Contains("Skill Gem")) return true;
                if (Settings.Flasks && item.Quality >= Settings.FlasksQuality.Value && item.ClassName.Contains("Flask")) return true;

                #endregion

                #region Uniques

                if (Settings.AllUniques && item.Rarity == ItemRarity.Unique) return true;

                #endregion

                #region Custom Rules
                if (item.BaseName.Contains("Watchstone"))
                    return true;
                if (item.BaseName.Contains("Incubator"))
                    return true;
                if (item.BaseName.Contains(" Seed"))
                    return true;
                if (item.BaseName.Contains(" Grain"))
                    return true;
                if (item.BaseName.Contains(" Bulb"))
                    return true;
                #endregion
            }
            catch (Exception e)
            {
                LogError($"{nameof(OverrideChecks)} error: {e}");
            }

            return false;
        }

        public bool DoWePickThis(CustomItem itemEntity)
        {
            if (!itemEntity.IsValid)
                return false;

            var pickItemUp = false;


            #region Force Pickup All

            if (Settings.PickUpEverything)
            {
                return true;
            }

            #endregion

            #region Rarity Rule Switch

                switch (itemEntity.Rarity)
                {
                    case ItemRarity.Normal:
                        if (_normalRules != null)
                        {
                            if (InCustomList(_normalRules, itemEntity, itemEntity.Rarity))
                                pickItemUp = true;
                        }

                        break;
                    case ItemRarity.Magic:
                        if (_magicRules != null)
                        {
                            if (InCustomList(_magicRules, itemEntity, itemEntity.Rarity))
                                pickItemUp = true;
                        }

                        break;
                    case ItemRarity.Rare:
                        if (_rareRules != null)
                        {
                            if (InCustomList(_rareRules, itemEntity, itemEntity.Rarity))
                                pickItemUp = true;
                        }

                        break;
                    case ItemRarity.Unique:
                        if (_uniqueRules != null)
                        {
                            if (InCustomList(_uniqueRules, itemEntity, itemEntity.Rarity))
                                pickItemUp = true;
                        }

                        break;
                }

            #endregion

            #region Override Rules

            if (OverrideChecks(itemEntity)) pickItemUp = true;

            #endregion

            #region Metamorph

            if (itemEntity.IsMetaItem)
            {
                pickItemUp = true;
            }

            #endregion

            return pickItemUp;
        }
        public override void ReceiveEvent(string eventId, object args)
        {
            if (!Settings.Enable.Value) return;

            if (eventId == "frsm_display_data")
            {

                var argSerialised = JsonConvert.SerializeObject(args);
                FullRareSetManagerData = JsonConvert.DeserializeObject<FRSetManagerPublishInformation>(argSerialised);
            }
        }

        private IEnumerator FindItemToPick()
        {
            if (!GameController.Window.IsForeground()) yield break;
            var window = GameController.Window.GetWindowRectangleTimeCache;
            var playerPos = GameController.Player.GridPos;

            List<CustomItem> currentLabels;
            var morphPath = "Metadata/MiscellaneousObjects/Metamorphosis/MetamorphosisMonsterMarker";

            if (Settings.UseWeight)
            {
                currentLabels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabelsVisible
                    .Where(x => x.Address != 0 &&
                                x.ItemOnGround?.Path != null && x.ItemOnGround.HasComponent<WorldItem>() &&
                                x.IsVisible && x.Label.GetClientRectCache.Center.PointInRectangle(window) &&
                                x.CanPickUp && (x.MaxTimeForPickUp.TotalSeconds <= 0) || x.ItemOnGround?.Path == morphPath)
                    .Select(x => new CustomItem(x, GameController.Files,
                        x.ItemOnGround.DistancePlayer, _weightsRules, x.ItemOnGround?.Path == morphPath))
                    .OrderByDescending(x => x.Weight).ThenBy(x => x.Distance).ToList();
            }
            else
            {
                currentLabels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabelsVisible
                    .Where(x => x.Address != 0 &&
                                x.ItemOnGround?.Path != null && x.ItemOnGround.HasComponent<WorldItem>() &&
                                x.IsVisible && x.Label.GetClientRectCache.Center.PointInRectangle(window) &&
                                x.CanPickUp && (x.MaxTimeForPickUp.TotalSeconds <= 0) || x.ItemOnGround?.Path == morphPath)
                    .Select(x => new CustomItem(x, GameController.Files,
                        x.ItemOnGround.DistancePlayer, _weightsRules, x.ItemOnGround?.Path == morphPath))
                    .OrderBy(x => x.Distance).ToList();
            }

            GameController.Debug["PickIt"] = currentLabels;
            var rectangleOfGameWindow = GameController.Window.GetWindowRectangleTimeCache;
            rectangleOfGameWindow.Inflate(-36, -36);
            var pickUpThisItem = currentLabels.FirstOrDefault(x =>
                DoWePickThis(x) && x.Distance < Settings.PickupRange && x.GroundItem != null &&
                rectangleOfGameWindow.Intersects(new RectangleF(x.LabelOnGround.Label.GetClientRectCache.Center.X,
                    x.LabelOnGround.Label.GetClientRectCache.Center.Y, 3, 3)) && Misc.CanFitInventory(x));
            
            if (Input.GetKeyState(Settings.PickUpKey.Value) ||
                CanLazyLoot() && ShouldLazyLoot(pickUpThisItem))
            {
                yield return TryToPickV2(pickUpThisItem);
                FullWork = true;
            }
        }
        
        /// <summary>
        /// LazyLoot item independent checks
        /// </summary>
        /// <returns></returns>
        private bool CanLazyLoot()
        {
            if (!Settings.LazyLooting) return false;
            if (Settings.LazyLooting && !Input.GetKeyState(Settings.LazyLootingPauseKey)) return true;
            // if (DisableLazyLootingTill > DateTime.Now) return false;
            
            return true;
        }
        
        /// <summary>
        /// LazyLoot item dependent checks
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private bool ShouldLazyLoot(CustomItem item)
        {
            var itemPos = item.LabelOnGround.ItemOnGround.Pos;
            var playerPos = GameController.Player.Pos;
            if (Math.Abs(itemPos.Z - playerPos.Z) > 50) return false;
            var dx = itemPos.X - playerPos.X;
            var dy = itemPos.Y - playerPos.Y;
            if (dx * dx + dy * dy > 275 * 275) return false;

            if (item.IsElder || item.IsFractured || item.IsShaper ||
                item.IsHunter || item.IsCrusader || item.IsRedeemer || item.IsWarlord || item.IsHeist)
                return true;
            
            if (item.Rarity == ItemRarity.Rare && item.Width * item.Height > 1) return false;
            
            return true;
        }

        private IEnumerator TryToPickV2(CustomItem pickItItem)
        {
            if (!pickItItem.IsValid)
            {
                FullWork = true;
                //LogMessage("PickItem is not valid.", 5, Color.Red);
                yield break;
            }

            var centerOfItemLabel = pickItItem.LabelOnGround.Label.GetClientRectCache.Center;
            var rectangleOfGameWindow = GameController.Window.GetWindowRectangleTimeCache;

            var oldMousePosition = Mouse.GetCursorPositionVector();
            _clickWindowOffset = rectangleOfGameWindow.TopLeft;
            rectangleOfGameWindow.Inflate(-36, -36);
            centerOfItemLabel.X += rectangleOfGameWindow.Left;
            centerOfItemLabel.Y += rectangleOfGameWindow.Top;
            if (!rectangleOfGameWindow.Intersects(new RectangleF(centerOfItemLabel.X, centerOfItemLabel.Y, 3, 3)))
            {
                FullWork = true;
                //LogMessage($"Label outside game window. Label: {centerOfItemLabel} Window: {rectangleOfGameWindow}", 5, Color.Red);
                yield break;
            }

            var tryCount = 0;

            while (tryCount < 3)
            {
                var completeItemLabel = pickItItem.LabelOnGround?.Label;

                if (completeItemLabel == null)
                {
                    if (tryCount > 0)
                    {
                        //LogMessage("Probably item already picked.", 3);
                        yield break;
                    }

                    //LogError("Label for item not found.", 5);
                    yield break;
                }

                //while (GameController.Player.GetComponent<Actor>().isMoving)
                //{
                //    yield return waitPlayerMove;
                //}
                var clientRect = completeItemLabel.GetClientRect();

                var clientRectCenter = clientRect.Center;

                var vector2 = clientRectCenter + _clickWindowOffset;

                if (!rectangleOfGameWindow.Intersects(new RectangleF(vector2.X, vector2.Y, 3, 3)))
                {
                    FullWork = true;
                    //LogMessage($"x,y outside game window. Label: {centerOfItemLabel} Window: {rectangleOfGameWindow}", 5, Color.Red);
                    yield break;
                }

                Mouse.MoveCursorToPosition(vector2);
                yield return wait2ms;

                if (pickItItem.IsTargeted())
                    yield return Mouse.LeftClick();

                yield return toPick;
                tryCount++;
            }

            tryCount = 0;

            while (GameController.Game.IngameState.IngameUi.ItemsOnGroundLabelsVisible.FirstOrDefault(
                       x => x.Address == pickItItem.LabelOnGround.Address) != null && tryCount < 6)
            {
                tryCount++;
                //yield return waitForNextTry;
            }

            //yield return waitForNextTry;

            //   Mouse.MoveCursorToPosition(oldMousePosition);
        }

        #region (Re)Loading Rules

        private void LoadRuleFiles()
        {
            var PickitConfigFileDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Compiled", nameof(PickIt),
                PickitRuleDirectory);

            if (!Directory.Exists(PickitConfigFileDirectory))
            {
                Directory.CreateDirectory(PickitConfigFileDirectory);
                return;
            }

            var dirInfo = new DirectoryInfo(PickitConfigFileDirectory);

            PickitFiles = dirInfo.GetFiles("*.txt").Select(x => Path.GetFileNameWithoutExtension(x.Name)).ToList();
            _normalRules = LoadPickit(Settings.NormalRuleFile);
            _magicRules = LoadPickit(Settings.MagicRuleFile);
            _rareRules = LoadPickit(Settings.RareRuleFile);
            _uniqueRules = LoadPickit(Settings.UniqueRuleFile);
            _weightsRules = LoadWeights(Settings.WeightRuleFile);
            _ignoreRules = LoadPickit(Settings.IgnoreRuleFile);
        }

        public HashSet<string> LoadPickit(string fileName)
        {
            var hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (fileName == string.Empty)
            {
                return hashSet;
            }

            var pickitFile = $@"{DirectoryFullName}\{PickitRuleDirectory}\{fileName}.txt";

            if (!File.Exists(pickitFile))
            {
                return hashSet;
            }

            var lines = File.ReadAllLines(pickitFile);

            foreach (var x in lines.Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("#")))
            {
                hashSet.Add(x.Trim());
            }

            LogMessage($"PICKIT :: (Re)Loaded {fileName}", 5, Color.GreenYellow);
            return hashSet;
        }

        public Dictionary<string, int> LoadWeights(string fileName)
        {
            var result = new Dictionary<string, int>();
            var filePath = $@"{DirectoryFullName}\{PickitRuleDirectory}\{fileName}.txt";
            if (!File.Exists(filePath)) return result;

            var lines = File.ReadAllLines(filePath);

            foreach (var x in lines.Where(x => !string.IsNullOrEmpty(x) && !x.StartsWith("#") && x.IndexOf('=') > 0))
            {
                try
                {
                    var s = x.Split('=');
                    if (s.Length == 2) result[s[0].Trim()] = int.Parse(s[1]);
                }
                catch (Exception)
                {
                    DebugWindow.LogError($"{nameof(PickIt)} => Error when parse weight.");
                }
            }

            LogMessage($"PICKIT :: (Re)Loaded {fileName}", 5, Color.Cyan);
            return result;
        }

        public override void OnPluginDestroyForHotReload()
        {
            pickItCoroutine.Done(true);
        }

        #endregion

        #region Adding / Removing Entities

        public override void EntityAdded(Entity Entity)
        {
        }

        public override void EntityRemoved(Entity Entity)
        {
        }

        #endregion
    }
}
