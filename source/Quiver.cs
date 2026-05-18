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

        return base.GetOrCreateSlots(bagstack, parentinv, bagIndex, world);
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
                Variants inQuiverVariants = Variants.FromStack(quiverNotEmptySlot.Itemstack);

                if (inQuiverVariants.Get(metalVariantCode) != null)
                {
                    if (variants.Get(metalVariantCode) != inQuiverVariants.Get(metalVariantCode))
                    {
                        variants.Set(metalVariantCode, inQuiverVariants.Get(metalVariantCode));
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

                if (inQuiverVariants.Get(leatherVariantCode) != null)
                {
                    if (variants.Get(leatherVariantCode) != inQuiverVariants.Get(leatherVariantCode))
                    {
                        variants.Set(leatherVariantCode, inQuiverVariants.Get(leatherVariantCode));
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

                if (inQuiverVariants.Get(woodVariantCode) != null)
                {
                    if (variants.Get(woodVariantCode) != inQuiverVariants.Get(woodVariantCode))
                    {
                        variants.Set(woodVariantCode, inQuiverVariants.Get(woodVariantCode));
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
    }
}
