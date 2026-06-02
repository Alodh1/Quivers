using AttributeRenderingLibrary;
using CombatOverhaul.Armor;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace QuiversAndSheaths;

public class QuiverBehavior : GearEquipableBag
{
    public QuiverBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        Stats = properties.AsObject<SheathStats>();
    }

    public override List<ItemSlotBagContent?> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv, int bagIndex, IWorldAccessor world)
    {
        if (parentinv is InventoryBasePlayer playerInventory && playerInventory.Player?.Entity != null && world.Api is ICoreServerAPI)
        {
            EntityPlayer player = playerInventory.Player.Entity;

            PlayerInventories[player.EntityId] = playerInventory;

            if (!ProcessedPlayers.Contains(player.EntityId))
            {
                playerInventory.SlotModified += slotIndex => OnSlotModified(playerInventory, player, slotIndex, bagIndex);
                ProcessedPlayers.Add(player.EntityId);
            }

            if (Stats.RefillMainHandWhenEmpty && !RefillTickListenerRegistered)
            {
                ((ICoreServerAPI)world.Api).Event.RegisterGameTickListener(CheckMainHandRefill, 100);
                RefillTickListenerRegistered = true;
            }
        }

        List<ItemSlotBagContent?> slots = base.GetOrCreateSlots(bagstack, parentinv, bagIndex, world);
        RefreshStoredSlotVariants(bagstack, slots);

        return slots;
    }

    protected readonly List<long> ProcessedPlayers = [];
    protected readonly Dictionary<long, InventoryBasePlayer> PlayerInventories = [];
    protected readonly Dictionary<long, bool> RefillMainHandWhenEmptyArmed = [];
    protected bool RefillTickListenerRegistered = false;
    protected SheathStats Stats = new();

    protected static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
    }

    protected virtual void CheckMainHandRefill(float dt)
    {
        if (!Stats.RefillMainHandWhenEmpty) return;

        foreach ((long entityId, InventoryBasePlayer backpackInventory) in PlayerInventories.ToArray())
        {
            EntityPlayer? player = backpackInventory.Player?.Entity;
            if (player == null)
            {
                PlayerInventories.Remove(entityId);
                RefillMainHandWhenEmptyArmed.Remove(entityId);
                continue;
            }

            ItemSlot? handSlot = player.RightHandItemSlot;
            if (handSlot == null) continue;

            if (!handSlot.Empty)
            {
                RefillMainHandWhenEmptyArmed[entityId] = CanThisQuiverRefillHand(backpackInventory, handSlot);
                continue;
            }

            if (!RefillMainHandWhenEmptyArmed.TryGetValue(entityId, out bool armed) || !armed) continue;

            if (TryRefillMainHandFromQuiver(backpackInventory, handSlot))
            {
                RefillMainHandWhenEmptyArmed[entityId] = false;
            }
            else
            {
                RefillMainHandWhenEmptyArmed[entityId] = false;
            }
        }
    }

    protected virtual bool CanThisQuiverRefillHand(InventoryBasePlayer backpackInventory, ItemSlot handSlot)
    {
        if (handSlot.Empty) return false;

        return backpackInventory
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => slot.SourceBag?.Item?.Id == collObj.Id)
            .Where(slot => slot.Config.SetVariants)
            .Any(slot => slot.CanHold(handSlot));
    }

    protected virtual bool TryRefillMainHandFromQuiver(InventoryBasePlayer backpackInventory, ItemSlot handSlot)
    {
        if (!handSlot.Empty) return false;

        ItemSlotBagContentWithWildcardMatch? sourceSlot = backpackInventory
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => slot.SourceBag?.Item?.Id == collObj.Id)
            .Where(slot => slot.Config.SetVariants)
            .Where(slot => !slot.Empty)
            .OrderBy(slot => slot.SlotIndex)
            .FirstOrDefault();

        if (sourceSlot == null) return false;

        ItemStack movedStack = sourceSlot.TakeOut(1);
        if (movedStack.StackSize <= 0) return false;

        handSlot.Itemstack = movedStack;
        sourceSlot.MarkDirty();
        handSlot.MarkDirty();
        return true;
    }

    protected virtual void RefreshStoredSlotVariants(ItemStack bagstack, IEnumerable<ItemSlotBagContent?> slots)
    {
        ItemSlotBagContentWithWildcardMatch[] bagSlots = slots
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => slot.Config.SetVariants)
            .Where(slot => slot.SourceBag?.Item?.Id == collObj.Id)
            .ToArray();

        foreach (string variantCode in bagSlots.Select(slot => slot.Config.SlotVariant).Distinct())
        {
            ItemSlotBagContentWithWildcardMatch quiverSlot = bagSlots.First(slot => slot.Config.SlotVariant == variantCode);
            ItemSlotBagContentWithWildcardMatch? quiverNotEmptySlot = bagSlots.FirstOrDefault(slot => !slot.Empty && slot.Config.SlotVariant == variantCode);

            Variants variants = Variants.FromStack(bagstack);
            string stateVariantCode = quiverSlot.Config.SlotStateVariant;

            SetVariant(variants, bagstack, stateVariantCode, quiverNotEmptySlot == null ? quiverSlot.Config.EmptyStateCode : quiverSlot.Config.FullStateCode);

            if (quiverNotEmptySlot == null)
            {
                continue;
            }

            ItemStack? storedStack = quiverNotEmptySlot.Itemstack;
            SheathableStats stats = storedStack?.Collectible?.Attributes?.AsObject<SheathableStats>() ?? new();

            SetVariant(variants, bagstack, variantCode, stats.InSheathVariantCode);

            if (!quiverSlot.Config.SetMaterialVariants || storedStack == null)
            {
                continue;
            }

            TrySetVariantFromStoredStack(variants, bagstack, quiverSlot.Config.SlotMetalVariant, stats.MetalVariantCode, storedStack, StoredVariantResolver.MetalVariantSources);
            TrySetVariantFromStoredStack(variants, bagstack, quiverSlot.Config.SlotLeatherVariant, stats.LeatherVariantCode, storedStack, StoredVariantResolver.LeatherVariantSources);
            TrySetVariantFromStoredStack(variants, bagstack, quiverSlot.Config.SlotWoodVariant, stats.WoodVariantCode, storedStack, StoredVariantResolver.WoodVariantSources);
        }
    }

    protected virtual void OnSlotModified(InventoryBasePlayer backpackInventory, EntityPlayer player, int slotIndex, int bagIndex)
    {
        InventoryBase? gearInventory = GetGearInventory(player);
        if (gearInventory == null) return;

        ItemSlot? sheathSlot = gearInventory
            .Where(slot => slot?.Itemstack?.Collectible?.Id == collObj.Id)
            .FirstOrDefault((ItemSlot?)null);
        if (sheathSlot?.Itemstack == null) return;

        IEnumerable<string> variantCodes = backpackInventory
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => slot.Config.SetVariants)
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

            Variants? variants;

            if (quiverNotEmptySlot == null)
            {
                variants = Variants.FromStack(sheathSlot.Itemstack);
                if (variants.Get(stateVariantCode) != quiverSlot.Config.EmptyStateCode)
                {
                    variants.Set(stateVariantCode, quiverSlot.Config.EmptyStateCode);
                    variants.ToStack(sheathSlot.Itemstack);
                    sheathSlot.MarkDirty();
                }
                continue;
            }

            variants = Variants.FromStack(sheathSlot.Itemstack);
            if (variants.Get(stateVariantCode) != quiverSlot.Config.FullStateCode)
            {
                variants.Set(stateVariantCode, quiverSlot.Config.FullStateCode);
                variants.ToStack(sheathSlot.Itemstack);
                sheathSlot.MarkDirty();
            }

            string metalVariantCode = quiverSlot.Config.SlotMetalVariant;
            string leatherVariantCode = quiverSlot.Config.SlotLeatherVariant;
            string woodVariantCode = quiverSlot.Config.SlotWoodVariant;

            SheathableStats stats = quiverNotEmptySlot.Itemstack?.Collectible?.Attributes?.AsObject<SheathableStats>() ?? new();

            variants = Variants.FromStack(sheathSlot.Itemstack);
            if (variants.Get(variantCode) != stats.InSheathVariantCode)
            {
                variants.Set(variantCode, stats.InSheathVariantCode);
                variants.ToStack(sheathSlot.Itemstack);
                sheathSlot.MarkDirty();
            }

            if (quiverSlot.Config.SetMaterialVariants && quiverNotEmptySlot.Itemstack != null)
            {
                TrySetVariantFromStoredStack(variants, sheathSlot, metalVariantCode, stats.MetalVariantCode, quiverNotEmptySlot.Itemstack, StoredVariantResolver.MetalVariantSources);
                TrySetVariantFromStoredStack(variants, sheathSlot, leatherVariantCode, stats.LeatherVariantCode, quiverNotEmptySlot.Itemstack, StoredVariantResolver.LeatherVariantSources);
                TrySetVariantFromStoredStack(variants, sheathSlot, woodVariantCode, stats.WoodVariantCode, quiverNotEmptySlot.Itemstack, StoredVariantResolver.WoodVariantSources);
            }
        }
    }

    protected virtual void TrySetVariantFromStoredStack(Variants variants, ItemSlot sheathSlot, string targetVariantCode, string defaultVariantValue, ItemStack? storedStack, params string[] sourceVariantCodes)
    {
        if (StoredVariantResolver.IsProtectedContainerVariantCode(targetVariantCode)) return;

        string variantValue = StoredVariantResolver.GetStoredMaterialVariant(storedStack, targetVariantCode, sourceVariantCodes) ?? defaultVariantValue;
        SetVariant(variants, sheathSlot, targetVariantCode, variantValue);
    }

    protected virtual void TrySetVariantFromStoredStack(Variants variants, ItemStack sheathStack, string targetVariantCode, string defaultVariantValue, ItemStack? storedStack, params string[] sourceVariantCodes)
    {
        if (StoredVariantResolver.IsProtectedContainerVariantCode(targetVariantCode)) return;

        string variantValue = StoredVariantResolver.GetStoredMaterialVariant(storedStack, targetVariantCode, sourceVariantCodes) ?? defaultVariantValue;
        SetVariant(variants, sheathStack, targetVariantCode, variantValue);
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
}
