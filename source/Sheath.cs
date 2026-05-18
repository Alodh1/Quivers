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

        return base.GetOrCreateSlots(bagstack, parentinv, bagIndex, world);
    }

    protected readonly List<long> ProcessedPlayers = [];
    protected SheathStats Stats = new();

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
            if (variants.Get(variantCode) != Stats.FullStateCode)
            {
                variants.Set(variantCode, Stats.FullStateCode);
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

            Variants weaponVariants = Variants.FromStack(slotAtIndex.Itemstack);
            
            if (weaponVariants.Get(metalVariantCode) != null)
            {
                if (variants.Get(metalVariantCode) != weaponVariants.Get(metalVariantCode))
                {
                    variants.Set(metalVariantCode, weaponVariants.Get(metalVariantCode));
                    variants.ToStack(sheathSlot.Itemstack);
                    sheathSlot.MarkDirty();
                }
            }
            else
            {
                if (variants.Get(metalVariantCode) != stats.MetalVariantCode)
                {
                    variants.Set(metalVariantCode, stats.MetalVariantCode);
                    variants.ToStack(sheathSlot.Itemstack);
                    sheathSlot.MarkDirty();
                }
            }

            if (weaponVariants.Get(leatherVariantCode) != null)
            {
                if (variants.Get(leatherVariantCode) != weaponVariants.Get(leatherVariantCode))
                {
                    variants.Set(leatherVariantCode, weaponVariants.Get(leatherVariantCode));
                    variants.ToStack(sheathSlot.Itemstack);
                    sheathSlot.MarkDirty();
                }
            }
            else
            {
                if (variants.Get(leatherVariantCode) != stats.LeatherVariantCode)
                {
                    variants.Set(leatherVariantCode, stats.LeatherVariantCode);
                    variants.ToStack(sheathSlot.Itemstack);
                    sheathSlot.MarkDirty();
                }
            }

            if (weaponVariants.Get(woodVariantCode) != null)
            {
                if (variants.Get(woodVariantCode) != weaponVariants.Get(woodVariantCode))
                {
                    variants.Set(woodVariantCode, weaponVariants.Get(woodVariantCode));
                    variants.ToStack(sheathSlot.Itemstack);
                    sheathSlot.MarkDirty();
                }
            }
            else
            {
                if (variants.Get(woodVariantCode) != stats.WoodVariantCode)
                {
                    variants.Set(woodVariantCode, stats.WoodVariantCode);
                    variants.ToStack(sheathSlot.Itemstack);
                    sheathSlot.MarkDirty();
                }
            }
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

            Variants inQuiverVariants = quiverSlot.Itemstack?.Attributes != null ? Variants.FromStack(quiverSlot.Itemstack) : new();

            TrySetVariant(variants, sheathSlot, leatherVariantCode, stats.LeatherVariantCode, inQuiverVariants);
            TrySetVariant(variants, sheathSlot, metalVariantCode, stats.MetalVariantCode, inQuiverVariants);
            TrySetVariant(variants, sheathSlot, woodVariantCode, stats.WoodVariantCode, inQuiverVariants);
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

    protected virtual void SetVariant(Variants variants, ItemSlot slot, string variantCode, string variantValue)
    {
        if (variants.Get(variantCode) != variantValue)
        {
            variants.Set(variantCode, variantValue);
            variants.ToStack(slot.Itemstack);
            slot.MarkDirty();
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
