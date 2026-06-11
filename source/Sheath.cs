using AttributeRenderingLibrary;
using CombatOverhaul;
using CombatOverhaul.Armor;
using CombatOverhaul.Utils;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System.Reflection;

namespace QuiversAndSheaths;

public class SheathStats
{
    public string RightHandVariant { get; set; } = "right_slot";
    public string LeftHandVariant { get; set; } = "left_slot";
    public string RightHandStateVariant { get; set; } = "right_slot_state";
    public string LeftHandStateVariant { get; set; } = "left_slot_state";
    public string EmptyStateCode { get; set; } = "empty";
    public string FullStateCode { get; set; } = "full";
    public Dictionary<string, string> StateOverrideByStoredItem { get; set; } = [];
    public string RightWeaponMetalVariant { get; set; } = "right_metal";
    public string RightWeaponLeatherVariant { get; set; } = "right_leather";
    public string RightWeaponWoodVariant { get; set; } = "right_wood";
    public string LeftWeaponMetalVariant { get; set; } = "left_metal";
    public string LeftWeaponLeatherVariant { get; set; } = "left_leather";
    public string LeftWeaponWoodVariant { get; set; } = "left_wood";
    public bool DualDaggerSheath { get; set; } = false;
    public bool RefillMainHandWhenEmpty { get; set; } = false;
}

public class SheathableStats
{
    public string InSheathVariantCode { get; set; } = "default";
    public string MetalVariantCode { get; set; } = "copper";
    public string LeatherVariantCode { get; set; } = "plain";
    public string WoodVariantCode { get; set; } = "oak";
}

public class SheathBehavior : ToolBag
{
    private static readonly string[] SlingStoneWildcards = ["stone-*", "*:stone-*"];

    public SheathBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        Stats = properties.AsObject<SheathStats>();
    }

    public override List<ItemSlotBagContent?> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv, int bagIndex, IWorldAccessor world)
    {
        Debug.WriteLine($"GetOrCreateSlots: {bagIndex}");

        if (parentinv is InventoryBasePlayer playerInventory && playerInventory.Player?.Entity != null && world.Api is ICoreServerAPI)
        {
            EntityPlayer player = playerInventory.Player.Entity;

            if (!ProcessedPlayers.Contains(player.EntityId))
            {
                playerInventory.SlotModified += slotIndex => OnSlotModified(playerInventory, player, slotIndex);
                ProcessedPlayers.Add(player.EntityId);
            }
        }

        List<ItemSlotBagContent?> slots = base.GetOrCreateSlots(bagstack, parentinv, bagIndex, world);
        ApplySlingPouchStoneSetting(slots);
        RefreshStoredToolSlotVariants(bagstack, slots);
        RefreshStoredStorageSlotVariants(bagstack, slots);

        return slots;
    }

    protected readonly List<long> ProcessedPlayers = [];
    protected SheathStats Stats = new();

    private void RefreshStoredToolSlotVariants(ItemStack bagstack, IEnumerable<ItemSlotBagContent?> slots)
    {
        foreach (ItemSlotBagContentWithWildcardMatch slot in slots.OfType<ItemSlotBagContentWithWildcardMatch>())
        {
            if (!slot.Config.HandleHotkey || slot.SourceBag?.Item?.Id != collObj.Id)
            {
                continue;
            }

            bool mainHand = slot.MainHand;
            string stateVariantCode = mainHand ? Stats.RightHandStateVariant : Stats.LeftHandStateVariant;
            Variants variants = Variants.FromStack(bagstack);

            if (slot.Empty)
            {
                SetVariant(variants, bagstack, stateVariantCode, Stats.EmptyStateCode);
                continue;
            }

            SetVariant(variants, bagstack, stateVariantCode, GetStateCodeForStoredItem(slot.Itemstack));

            if (slot.Itemstack?.Collectible?.Attributes == null)
            {
                continue;
            }

            SheathableStats stats = slot.Itemstack.Collectible.Attributes.AsObject<SheathableStats>();
            string variantCode = mainHand ? Stats.RightHandVariant : Stats.LeftHandVariant;
            string metalVariantCode = mainHand ? Stats.RightWeaponMetalVariant : Stats.LeftWeaponMetalVariant;
            string leatherVariantCode = mainHand ? Stats.RightWeaponLeatherVariant : Stats.LeftWeaponLeatherVariant;
            string woodVariantCode = mainHand ? Stats.RightWeaponWoodVariant : Stats.LeftWeaponWoodVariant;

            SetVariant(variants, bagstack, variantCode, stats.InSheathVariantCode);
            TrySetVariantFromStoredStack(variants, bagstack, metalVariantCode, stats.MetalVariantCode, slot.Itemstack, StoredVariantResolver.MetalVariantSources);
            TrySetVariantFromStoredStack(variants, bagstack, leatherVariantCode, stats.LeatherVariantCode, slot.Itemstack, StoredVariantResolver.LeatherVariantSources);
            TrySetVariantFromStoredStack(variants, bagstack, woodVariantCode, stats.WoodVariantCode, slot.Itemstack, StoredVariantResolver.WoodVariantSources);
            CopyStoredShieldTextureVariants(variants, bagstack, slot.Itemstack, mainHand);
        }
    }

    private void RefreshStoredStorageSlotVariants(ItemStack bagstack, IEnumerable<ItemSlotBagContent?> slots)
    {
        ItemSlotBagContentWithWildcardMatch[] bagSlots = slots
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => !slot.Config.HandleHotkey)
            .Where(slot => slot.Config.SetVariants && slot.SourceBag?.Item?.Id == collObj.Id)
            .ToArray();

        foreach (string variantCode in bagSlots.Select(slot => slot.Config.SlotVariant).Distinct())
        {
            ItemSlotBagContentWithWildcardMatch quiverSlot = bagSlots.First(slot => slot.Config.SlotVariant == variantCode);
            ItemSlotBagContentWithWildcardMatch? quiverNotEmptySlot = bagSlots.FirstOrDefault(slot => !slot.Empty && slot.Config.SlotVariant == variantCode);

            Variants variants = Variants.FromStack(bagstack);
            string stateVariantCode = quiverSlot.Config.SlotStateVariant;

            SetVariant(variants, bagstack, stateVariantCode, quiverNotEmptySlot == null ? quiverSlot.Config.EmptyStateCode : quiverSlot.Config.FullStateCode);

            string metalVariantCode = quiverSlot.Config.SlotMetalVariant;
            string leatherVariantCode = quiverSlot.Config.SlotLeatherVariant;
            string woodVariantCode = quiverSlot.Config.SlotWoodVariant;

            ItemStack? storedStack = quiverNotEmptySlot?.Itemstack;
            SheathableStats stats = storedStack?.Collectible?.Attributes?.AsObject<SheathableStats>() ?? new();

            if (storedStack?.Collectible?.Attributes != null || variants.Get(variantCode) == null)
            {
                SetVariant(variants, bagstack, variantCode, stats.InSheathVariantCode);
            }

            TrySetVariantFromStoredStack(variants, bagstack, leatherVariantCode, stats.LeatherVariantCode, storedStack, StoredVariantResolver.LeatherVariantSources);
            TrySetVariantFromStoredStack(variants, bagstack, metalVariantCode, stats.MetalVariantCode, storedStack, StoredVariantResolver.MetalVariantSources);
            TrySetVariantFromStoredStack(variants, bagstack, woodVariantCode, stats.WoodVariantCode, storedStack, StoredVariantResolver.WoodVariantSources);
            CopyStoredShieldTextureVariants(variants, bagstack, storedStack, mainHand: false);
        }
    }

    private void ApplySlingPouchStoneSetting(List<ItemSlotBagContent?> slots)
    {
        if (collObj.Code?.Path != "beltbag-back-pouch-sling") return;

        bool allowStones = QuiversAndSheathsSystem.Settings.AllowStonesInSlingPouch;

        foreach (ItemSlotBagContentWithWildcardMatch slot in slots.OfType<ItemSlotBagContentWithWildcardMatch>())
        {
            if (slot.Config.HandleHotkey) continue;
            if (slot.Config.BackpackCategoryCode != "ammunition") continue;

            string[] withoutStones = slot.Config.CanHoldWildcards
                .Where(wildcard => !SlingStoneWildcards.Contains(wildcard))
                .ToArray();

            slot.Config.CanHoldWildcards = allowStones
                ? withoutStones.Concat(SlingStoneWildcards).Distinct().ToArray()
                : withoutStones;
        }
    }

    protected static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
    }
    protected virtual int GetBagIndex(InventoryBasePlayer backpackInventory, EntityPlayer player, int slotIndex)
    {
        try
        {
            ItemSlot slot = backpackInventory[slotIndex];
            if (slot is ItemSlotBagContentWithWildcardMatch mainSlot)
            {
                return mainSlot.BagIndex;
            }

            if (slot is ItemSlotTakeOutOnly takeOutSlot)
            {
                return takeOutSlot.BagIndex;
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(player.Api, this, $"Error on getting bag index: {exception}");
        }

        return 0;
    }

    protected virtual void OnSlotModified(InventoryBasePlayer backpackInventory, EntityPlayer player, int slotIndex)
    {
        int bagIndex = GetBagIndex(backpackInventory, player, slotIndex);

        OnSlotModifiedOtherSlots(backpackInventory, player, slotIndex, bagIndex);

        InventoryBase? gearInventory = GetGearInventory(player);
        if (gearInventory == null) return;

        ItemSlot? sheathSlot = gearInventory[bagIndex];

        if (sheathSlot?.Itemstack == null) return;

        ItemSlot slotAtIndex = backpackInventory[slotIndex];
        if (!TryReadSlotBagData(slotAtIndex, out bool handleHotkey, out bool mainHand, out string? slotVariant, out Item? sourceBagItem) || !handleHotkey)
        {
            return;
        }

        if (slotAtIndex.Empty)
        {
            string variantCode = mainHand ? Stats.RightHandStateVariant : Stats.LeftHandStateVariant;
            Variants variants = Variants.FromStack(sheathSlot.Itemstack);
            if (variants.Get(variantCode) != Stats.EmptyStateCode)
            {
                variants.Set(variantCode, Stats.EmptyStateCode);
                variants.ToStack(sheathSlot.Itemstack);
                sheathSlot.MarkDirty();
            }
            return;
        }
        else
        {
            string variantCode = mainHand ? Stats.RightHandStateVariant : Stats.LeftHandStateVariant;
            Variants variants = Variants.FromStack(sheathSlot.Itemstack);
            string stateCode = GetStateCodeForStoredItem(slotAtIndex.Itemstack);
            if (variants.Get(variantCode) != stateCode)
            {
                variants.Set(variantCode, stateCode);
                variants.ToStack(sheathSlot.Itemstack);
                sheathSlot.MarkDirty();
            }
        }

        if (slotAtIndex.Itemstack?.Collectible?.Attributes != null)
        {
            SheathableStats stats = slotAtIndex.Itemstack.Collectible.Attributes.AsObject<SheathableStats>();
            
            string variantCode = mainHand ? Stats.RightHandVariant : Stats.LeftHandVariant;
            string metalVariantCode = mainHand ? Stats.RightWeaponMetalVariant : Stats.LeftWeaponMetalVariant;
            string leatherVariantCode = mainHand ? Stats.RightWeaponLeatherVariant : Stats.LeftWeaponLeatherVariant;
            string woodVariantCode = mainHand ? Stats.RightWeaponWoodVariant : Stats.LeftWeaponWoodVariant;
            
            Variants variants = Variants.FromStack(sheathSlot.Itemstack);
            if (variants.Get(variantCode) != stats.InSheathVariantCode)
            {
                variants.Set(variantCode, stats.InSheathVariantCode);
                variants.ToStack(sheathSlot.Itemstack);
                sheathSlot.MarkDirty();
            }

            TrySetVariantFromStoredStack(variants, sheathSlot, metalVariantCode, stats.MetalVariantCode, slotAtIndex.Itemstack, StoredVariantResolver.MetalVariantSources);
            TrySetVariantFromStoredStack(variants, sheathSlot, leatherVariantCode, stats.LeatherVariantCode, slotAtIndex.Itemstack, StoredVariantResolver.LeatherVariantSources);
            TrySetVariantFromStoredStack(variants, sheathSlot, woodVariantCode, stats.WoodVariantCode, slotAtIndex.Itemstack, StoredVariantResolver.WoodVariantSources);
            CopyStoredShieldTextureVariants(variants, sheathSlot, slotAtIndex.Itemstack, mainHand);
        }
    }

    protected virtual void OnSlotModifiedOtherSlots(InventoryBasePlayer backpackInventory, EntityPlayer player, int slotIndex, int bagIndex)
    {
        InventoryBase? gearInventory = GetGearInventory(player);
        if (gearInventory == null) return;

        ItemSlot? sheathSlot = gearInventory
            .Where(slot => slot?.Itemstack?.Collectible?.Id == collObj.Id)
            .FirstOrDefault((ItemSlot?)null);
        if (sheathSlot?.Itemstack == null) return;

        IEnumerable<string> variantCodes = backpackInventory
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => !slot.Config.HandleHotkey)
            .Where(slot => slot.Config.SetVariants && slot.SourceBag?.Item?.Id == collObj.Id)
            .Select(slot => slot.Config.SlotVariant)
            .Distinct();

        foreach (string variantCode in variantCodes)
        {
            ItemSlotBagContentWithWildcardMatch quiverSlot = backpackInventory
                .OfType<ItemSlotBagContentWithWildcardMatch>()
                .First(slot => slot.Config.SlotVariant == variantCode);

            ItemSlotBagContentWithWildcardMatch? quiverNotEmptySlot = backpackInventory
                .OfType<ItemSlotBagContentWithWildcardMatch>()
                .Where(slot => !slot.Empty && slot.Config.SlotVariant == variantCode)
                .FirstOrDefault((ItemSlotBagContentWithWildcardMatch?)null);

            string stateVariantCode = quiverSlot.Config.SlotStateVariant;

            bool hasAttributes = sheathSlot.Itemstack?.Collectible?.Attributes != null;

            Variants variants = hasAttributes ? Variants.FromStack(sheathSlot.Itemstack) : new();

            if (quiverNotEmptySlot == null)
            {
                SetVariant(variants, sheathSlot, stateVariantCode, quiverSlot.Config.EmptyStateCode);
            }
            else
            {
                SetVariant(variants, sheathSlot, stateVariantCode, quiverSlot.Config.FullStateCode);
            }

            string metalVariantCode = quiverSlot.Config.SlotMetalVariant;
            string leatherVariantCode = quiverSlot.Config.SlotLeatherVariant;
            string woodVariantCode = quiverSlot.Config.SlotWoodVariant;

            SheathableStats stats = quiverNotEmptySlot?.Itemstack?.Collectible?.Attributes?.AsObject<SheathableStats>() ?? new();

            hasAttributes = quiverNotEmptySlot?.Itemstack?.Collectible?.Attributes?.AsObject<SheathableStats>() != null;

            if (hasAttributes || variants.Get(variantCode) == null)
            {
                SetVariant(variants, sheathSlot, variantCode, stats.InSheathVariantCode);
            }

            ItemStack? storedStack = quiverNotEmptySlot?.Itemstack;

            TrySetVariantFromStoredStack(variants, sheathSlot, leatherVariantCode, stats.LeatherVariantCode, storedStack, StoredVariantResolver.LeatherVariantSources);
            TrySetVariantFromStoredStack(variants, sheathSlot, metalVariantCode, stats.MetalVariantCode, storedStack, StoredVariantResolver.MetalVariantSources);
            TrySetVariantFromStoredStack(variants, sheathSlot, woodVariantCode, stats.WoodVariantCode, storedStack, StoredVariantResolver.WoodVariantSources);
            CopyStoredShieldTextureVariants(variants, sheathSlot, storedStack, mainHand: false);
        }
    }

    protected virtual void TrySetVariant(Variants variants, ItemSlot slot, string variantCode, string defaultVariantValue, Variants variantValueHolder)
    {
        if (variantValueHolder.Get(variantCode) != null)
        {
            SetVariant(variants, slot, variantCode, variantValueHolder);
        }
        else
        {
            SetVariant(variants, slot, variantCode, defaultVariantValue);
        }
    }

    private string GetStateCodeForStoredItem(ItemStack? storedStack)
    {
        string code = storedStack?.Collectible?.Code?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(code))
        {
            foreach ((string pattern, string stateCode) in Stats.StateOverrideByStoredItem)
            {
                if (!string.IsNullOrWhiteSpace(stateCode) && MatchesWildcard(pattern, code))
                {
                    return stateCode;
                }
            }
        }

        return Stats.FullStateCode;
    }

    private static bool MatchesWildcard(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (pattern == "*") return true;

        string[] parts = pattern.Split('*');
        int index = 0;

        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            string part = parts[partIndex];
            if (part.Length == 0) continue;

            int found = value.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            if (partIndex == 0 && !pattern.StartsWith('*') && found != 0) return false;

            index = found + part.Length;
        }

        return pattern.EndsWith('*') || index == value.Length;
    }

    protected virtual void SetVariant(Variants variants, ItemSlot slot, string variantCode, string variantValue)
    {
        if (variants.Get(variantCode) != variantValue)
        {
            variants.Set(variantCode, variantValue);
            variants.ToStack(slot.Itemstack);
            slot.MarkDirty();
        }
    }

    protected virtual void SetVariant(Variants variants, ItemStack stack, string variantCode, string variantValue)
    {
        if (variants.Get(variantCode) != variantValue)
        {
            variants.Set(variantCode, variantValue);
            variants.ToStack(stack);
        }
    }

    protected virtual void SetVariant(Variants variants, ItemSlot slot, string variantCode, Variants variantValueHolder)
    {
        if (variants.Get(variantCode) != variantValueHolder.Get(variantCode))
        {
            variants.Set(variantCode, variantValueHolder.Get(variantCode));
            variants.ToStack(slot.Itemstack);
            slot.MarkDirty();
        }
    }

    private void TrySetVariantFromStoredStack(Variants variants, ItemSlot sheathSlot, string targetVariantCode, string defaultVariantValue, ItemStack? storedStack, params string[] sourceVariantCodes)
    {
        if (StoredVariantResolver.IsProtectedContainerVariantCode(targetVariantCode)) return;

        string variantValue = StoredVariantResolver.GetStoredMaterialVariant(storedStack, targetVariantCode, sourceVariantCodes) ?? defaultVariantValue;
        SetVariant(variants, sheathSlot, targetVariantCode, variantValue);
    }

    private void TrySetVariantFromStoredStack(Variants variants, ItemStack sheathStack, string targetVariantCode, string defaultVariantValue, ItemStack? storedStack, params string[] sourceVariantCodes)
    {
        if (StoredVariantResolver.IsProtectedContainerVariantCode(targetVariantCode)) return;

        string variantValue = StoredVariantResolver.GetStoredMaterialVariant(storedStack, targetVariantCode, sourceVariantCodes) ?? defaultVariantValue;
        SetVariant(variants, sheathStack, targetVariantCode, variantValue);
    }

    private static string? GetStoredStackVariant(ItemStack? storedStack, string targetVariantCode, params string[] sourceVariantCodes)
    {
        return StoredVariantResolver.GetStoredTextureVariant(storedStack, targetVariantCode, sourceVariantCodes);
    }

    private void CopyStoredShieldTextureVariants(Variants variants, ItemSlot sheathSlot, ItemStack? storedStack, bool mainHand)
    {
        CopyStoredShieldTextureVariants((key, value) => SetVariant(variants, sheathSlot, key, value), storedStack, mainHand);
    }

    private void CopyStoredShieldTextureVariants(Variants variants, ItemStack sheathStack, ItemStack? storedStack, bool mainHand)
    {
        CopyStoredShieldTextureVariants((key, value) => SetVariant(variants, sheathStack, key, value), storedStack, mainHand);
    }

    private void CopyStoredShieldTextureVariants(Action<string, string> setVariant, ItemStack? storedStack, bool mainHand)
    {
        if (storedStack?.Collectible == null || !IsShield(storedStack)) return;

        string prefix = mainHand ? "right" : "left";

        CopyTextureAttributeVariant(setVariant, storedStack, prefix, "cloth", "cloth1", "color");
        CopyTextureAttributeVariant(setVariant, storedStack, prefix, "cloth1", "cloth1", "color");
        CopyTextureAttributeVariant(setVariant, storedStack, prefix, "cloth2", "cloth2", "color");
        CopyTextureAttributeVariant(setVariant, storedStack, prefix, "cloth3", "cloth3", "cloth2", "cloth1", "color");

        CopyVanillaRoundShieldTextureVariants(setVariant, storedStack, prefix);
    }

    private static bool IsShield(ItemStack stack)
    {
        return stack.Collectible.Code?.Path.Contains("shield", StringComparison.OrdinalIgnoreCase) == true;
    }

    private void CopyTextureAttributeVariant(Action<string, string> setVariant, ItemStack storedStack, string prefix, string targetTextureCode, params string[] sourceKeys)
    {
        string? texturePath = GetStoredStackVariant(storedStack, $"{prefix}_{targetTextureCode}", sourceKeys);
        if (string.IsNullOrEmpty(texturePath)) return;

        if (sourceKeys.Contains("color") && !texturePath.Contains('/') && !texturePath.Contains(':'))
        {
            texturePath = $"game:block/cloth/linen/{texturePath}";
        }

        setVariant($"{prefix}_{targetTextureCode}", NormalizeTexturePath(texturePath));
    }

    private void CopyVanillaRoundShieldTextureVariants(Action<string, string> setVariant, ItemStack storedStack, string prefix)
    {
        string? construction = GetStoredStackVariant(storedStack, "construction");
        if (construction is not ("woodmetal" or "woodmetalleather" or "metal")) return;

        string metal = GetStoredStackVariant(storedStack, "metal", "material") ?? "iron";
        string wood = GetStoredStackVariant(storedStack, "wood") ?? "generic";
        string color = GetStoredStackVariant(storedStack, "color", "leather") ?? "plain";
        string deco = GetStoredStackVariant(storedStack, "deco") ?? "none";

        switch (construction)
        {
            case "woodmetal":
                string woodTexture = wood == "generic" ? "game:item/tool/shield/wood" : $"game:block/wood/debarked/{wood}";
                SetTexturePathVariant(setVariant, prefix, "front", woodTexture);
                SetTexturePathVariant(setVariant, prefix, "back", woodTexture);
                SetTexturePathVariant(setVariant, prefix, "handle", woodTexture);
                SetTexturePathVariant(setVariant, prefix, "rim", $"game:block/metal/sheet/{metal}1");
                break;
            case "woodmetalleather":
                SetTexturePathVariant(setVariant, prefix, "front", $"game:item/tool/shield/{(deco == "ornate" ? "ornate" : "leather")}/{color}");
                SetTexturePathVariant(setVariant, prefix, "rim", $"game:block/metal/sheet/{metal}1");
                break;
            case "metal":
                SetTexturePathVariant(setVariant, prefix, "front", deco == "ornate" ? $"game:item/tool/shield/ornate/{color}" : $"game:block/metal/plate/{metal}");
                SetTexturePathVariant(setVariant, prefix, "back", $"game:block/metal/plate/{metal}");
                SetTexturePathVariant(setVariant, prefix, "handle", $"game:block/metal/sheet/{metal}1");
                SetTexturePathVariant(setVariant, prefix, "rim", $"game:block/metal/sheet/{metal}1");
                break;
        }
    }

    private void SetTexturePathVariant(Action<string, string> setVariant, string prefix, string textureCode, string texturePath)
    {
        setVariant($"{prefix}_{textureCode}", NormalizeTexturePath(texturePath));
    }

    private static string NormalizeTexturePath(string texturePath)
    {
        texturePath = texturePath.Trim().Replace('\\', '/');
        if (texturePath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
        {
            texturePath = texturePath["textures/".Length..];
        }
        if (texturePath.StartsWith("game:", StringComparison.OrdinalIgnoreCase))
        {
            texturePath = texturePath["game:".Length..];
        }
        if (texturePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            texturePath = texturePath[..^".png".Length];
        }

        return texturePath;
    }


    protected override bool OnHotkeyPressed(KeyCombination keyCombination)
    {
        if (!Stats.DualDaggerSheath)
        {
            return base.OnHotkeyPressed(keyCombination);
        }

        var inventory = GetBackpackInventory();
        bool handled = false;

        if (inventory != null && Api != null && ClientApi != null && HotkeyCooldownUntilMs < Api.World.ElapsedMilliseconds)
        {
            string toolBagId = collObj.Code.ToString();
            ToolBagSystemClient? system = ClientApi.ModLoader?.GetModSystem<CombatOverhaulSystem>()?.ClientToolBagSystem;

            if (system != null)
            {
                List<ItemSlotBagContentWithWildcardMatch> slots = inventory
                    .OfType<ItemSlotBagContentWithWildcardMatch>()
                    .Where(slot => slot.Config.HandleHotkey)
                    .Where(slot => slot.ToolBagId == toolBagId)
                    .ToList();

                IGrouping<int, ItemSlotBagContentWithWildcardMatch>? selectedGroup = slots
                    .GroupBy(slot => slot.ToolBagIndex)
                    .FirstOrDefault(group => group.Any(IsDualDaggerSlotActionable));

                if (selectedGroup != null)
                {
                    ItemSlotBagContentWithWildcardMatch? mainSlot = selectedGroup
                        .Where(slot => slot.MainHand)
                        .OrderBy(slot => slot.SlotIndex)
                        .FirstOrDefault();

                    ItemSlotBagContentWithWildcardMatch? offhandSlot = selectedGroup
                        .Where(slot => !slot.MainHand)
                        .OrderBy(slot => slot.SlotIndex)
                        .FirstOrDefault();

                    if (mainSlot != null && IsDualDaggerSlotActionable(mainSlot))
                    {
                        system.Send(mainSlot.ToolBagId, mainSlot.ToolBagIndex, mainSlot.MainHand, mainSlot.SlotIndex);
                        handled = true;
                    }

                    if (offhandSlot != null && IsDualDaggerSlotActionable(offhandSlot))
                    {
                        system.Send(offhandSlot.ToolBagId, offhandSlot.ToolBagIndex, offhandSlot.MainHand, offhandSlot.SlotIndex);
                        handled = true;
                    }

                    if (!handled)
                    {
                        foreach (ItemSlotBagContentWithWildcardMatch slot in selectedGroup.OrderBy(slot => slot.MainHand ? 0 : 1))
                        {
                            system.Send(slot.ToolBagId, slot.ToolBagIndex, slot.MainHand, slot.SlotIndex);
                            handled = true;
                        }
                    }
                }

                if (handled)
                {
                    HotkeyCooldownUntilMs = Api.World.ElapsedMilliseconds + HotkeyCooldown;
                }
            }
        }

        if (handled)
        {
            return true;
        }

        return PreviousHotkeyHandler?.Invoke(keyCombination) ?? false;
    }

    private bool IsDualDaggerSlotActionable(ItemSlotBagContentWithWildcardMatch slot)
    {
        ItemSlot? handSlot = slot.MainHand ? ClientApi?.World?.Player?.Entity?.RightHandItemSlot : ClientApi?.World?.Player?.Entity?.LeftHandItemSlot;
        bool handHasItem = handSlot?.Itemstack != null;
        bool slotHasItem = slot.Itemstack != null;

        if (handHasItem && handSlot != null && slot.CanHold(handSlot))
        {
            return true;
        }

        return !handHasItem && slotHasItem;
    }

    private static bool TryReadSlotBagData(ItemSlot slot, out bool handleHotkey, out bool mainHand, out string? slotVariant, out Item? sourceBagItem)
    {
        handleHotkey = false;
        mainHand = false;
        slotVariant = null;
        sourceBagItem = null;

        if (slot is ItemSlotBagContentWithWildcardMatch typed)
        {
            handleHotkey = typed.Config.HandleHotkey;
            mainHand = typed.MainHand;
            slotVariant = typed.Config.SlotVariant;
            sourceBagItem = typed.SourceBag?.Item;
            return true;
        }

        object? cfg = slot.GetType().GetProperty("Config", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot);
        object? sourceBag = slot.GetType().GetProperty("SourceBag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot);
        if (cfg == null)
        {
            return false;
        }

        handleHotkey = (bool?)cfg.GetType().GetProperty("HandleHotkey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(cfg) ?? false;
        mainHand = (bool?)slot.GetType().GetProperty("MainHand", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot) ?? false;
        slotVariant = (string?)cfg.GetType().GetProperty("SlotVariant", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(cfg);
        sourceBagItem = sourceBag?.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(sourceBag) as Item;

        return true;
    }
}
