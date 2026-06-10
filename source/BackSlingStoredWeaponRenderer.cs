using CombatOverhaul;
using CombatOverhaul.Animations;
using CombatOverhaul.Armor;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace QuiversAndSheaths;

public sealed class BackSlingRenderConfigBehavior : CollectibleBehavior
{
    public BackSlingStoredWeaponRenderConfig Config { get; private set; } = new();

    public BackSlingRenderConfigBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);
        Config = properties.AsObject<BackSlingStoredWeaponRenderConfig>() ?? new();
    }
}

public sealed class BackSlingStoredWeaponRenderConfig
{
    public int SlotIndex { get; set; } = 0;
    public string AttachmentPart { get; set; } = "UpperTorso";
    public string RenderTarget { get; set; } = "HandTp";
    public bool ApplyStoredItemTranslation { get; set; } = false;
    public bool ApplyStoredItemRotation { get; set; } = false;
    public bool ApplyStoredItemScale { get; set; } = true;
    public Dictionary<string, BackSlingStoredWeaponItemTransform> TransformByStoredItem { get; set; } = [];
    public ModelTransform Transform { get; set; } = new()
    {
        Translation = new Vec3f(12f, 4.2f, 1.05f),
        Rotation = new Vec3f(0f, -86f, 125f),
        Origin = new Vec3f(0f, 0f, 0f),
        Scale = 1f
    };
}

public sealed class BackSlingStoredWeaponItemTransform
{
    public bool? ApplyStoredItemTranslation { get; set; }
    public bool? ApplyStoredItemRotation { get; set; }
    public bool? ApplyStoredItemScale { get; set; }
    public ModelTransform Transform { get; set; } = new()
    {
        Translation = new Vec3f(0f, 0f, 0f),
        Rotation = new Vec3f(0f, 0f, 0f),
        Origin = new Vec3f(0f, 0f, 0f),
        Scale = 1f
    };
}

public sealed class BackSlingStoredWeaponRenderer : IRenderer, IDisposable
{
    private const string BackSlingCode = "shoulderbag-back-sling-polearms";

    private readonly ICoreClientAPI _api;
    private readonly DummySlot _dummySlot = new();
    private readonly HashSet<string> _warnedRenderFailures = [];
    private bool _disposed;

    public BackSlingStoredWeaponRenderer(ICoreClientAPI api)
    {
        _api = api;
    }

    public double RenderOrder => 0.55;
    public int RenderRange => 9999;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_disposed || stage != EnumRenderStage.Opaque) return;

        foreach (Entity entity in _api.World.LoadedEntities.Values)
        {
            if (entity is not EntityPlayer player || !player.Alive) continue;
            if (IsLocalFirstPerson(player)) continue;

            ItemSlot? slingSlot = FindBackSlingSlot(player);
            ItemStack? slingStack = slingSlot?.Itemstack;
            if (!IsBackSling(slingStack)) continue;

            BackSlingStoredWeaponRenderConfig config = slingStack!.Collectible
                .GetBehavior<BackSlingRenderConfigBehavior>()?.Config ?? new();

            ItemStack? storedStack = GetStoredStack(slingStack, config);
            if (storedStack == null) continue;

            RenderStoredStack(player, slingStack, storedStack, config, deltaTime);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private ItemSlot? FindBackSlingSlot(EntityPlayer player)
    {
        InventoryBase? gearInventory = player.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
        if (gearInventory == null) return null;

        for (int index = 0; index < gearInventory.Count; index++)
        {
            ItemSlot slot = gearInventory[index];
            if (slot is not GearSlot gearSlot || gearSlot.SlotType != "backgear") continue;
            if (IsBackSling(slot.Itemstack)) return slot;
        }

        return null;
    }

    private static bool IsBackSling(ItemStack? stack)
    {
        AssetLocation? code = stack?.Collectible?.Code;
        return code?.Domain == "quiversandsheaths" && code.Path == BackSlingCode;
    }

    private ItemStack? GetStoredStack(ItemStack slingStack, BackSlingStoredWeaponRenderConfig config)
    {
        ITreeAttribute? backpackTree = slingStack.Attributes.GetTreeAttribute("backpack");
        ITreeAttribute? slotsTree = backpackTree?.GetTreeAttribute("slots");
        if (slotsTree == null) return null;

        string preferredSlotKey = $"slot-{Math.Max(0, config.SlotIndex)}";
        if (TryResolveStoredStack(slotsTree[preferredSlotKey]?.GetValue() as ItemStack, out ItemStack? preferred))
        {
            return preferred;
        }

        foreach ((_, IAttribute attribute) in slotsTree.SortedCopy())
        {
            if (TryResolveStoredStack(attribute?.GetValue() as ItemStack, out ItemStack? storedStack))
            {
                return storedStack;
            }
        }

        return null;
    }

    private bool TryResolveStoredStack(ItemStack? stack, out ItemStack? resolved)
    {
        resolved = null;
        if (stack == null || stack.StackSize <= 0) return false;

        stack.ResolveBlockOrItem(_api.World);
        if (stack.Collectible == null) return false;

        resolved = stack;
        return true;
    }

    private void RenderStoredStack(EntityPlayer player, ItemStack slingStack, ItemStack storedStack, BackSlingStoredWeaponRenderConfig config, float dt)
    {
        if (!TryBuildBackSlingModelMatrix(player, config, out Matrixf modelMatrix)) return;

        EnumItemRenderTarget target = ParseRenderTarget(config.RenderTarget);
        _dummySlot.Itemstack = storedStack;
        ItemRenderInfo renderInfo = _api.Render.GetItemStackRenderInfo(_dummySlot, target, dt);
        if (renderInfo.ModelRef == null)
        {
            WarnOnce(storedStack, "no model ref");
            return;
        }

        BackSlingStoredWeaponItemTransform? itemTransform = GetItemTransform(config, storedStack);
        ApplyModelTransform(modelMatrix, config.Transform);
        if (itemTransform != null)
        {
            ApplyModelTransform(modelMatrix, itemTransform.Transform);
        }

        ApplyStoredItemTransform(modelMatrix, renderInfo.Transform, config, itemTransform);

        BlockPos lightPos = player.Pos.AsBlockPos;
        Vec4f light = _api.World.BlockAccessor.GetLightRGBs(lightPos.X, lightPos.Y, lightPos.Z);

        if (TryRenderAnimatable(player, storedStack, renderInfo, modelMatrix, light, target, dt)) return;

        RenderStaticStack(storedStack, renderInfo, modelMatrix, light);
    }

    private static BackSlingStoredWeaponItemTransform? GetItemTransform(BackSlingStoredWeaponRenderConfig config, ItemStack storedStack)
    {
        if (config.TransformByStoredItem == null || config.TransformByStoredItem.Count == 0) return null;

        string code = storedStack.Collectible?.Code?.ToString() ?? string.Empty;
        BackSlingStoredWeaponItemTransform? bestTransform = null;
        int bestSpecificity = -1;

        foreach ((string pattern, BackSlingStoredWeaponItemTransform transform) in config.TransformByStoredItem)
        {
            if (!MatchesWildcard(pattern, code)) continue;

            int specificity = pattern.Count(character => character != '*');
            if (specificity <= bestSpecificity) continue;

            bestTransform = transform;
            bestSpecificity = specificity;
        }

        return bestTransform;
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

    private bool TryBuildBackSlingModelMatrix(EntityPlayer player, BackSlingStoredWeaponRenderConfig config, out Matrixf modelMatrix)
    {
        modelMatrix = new Matrixf();
        if (player.Properties?.Client?.Renderer is EntityShapeRenderer renderer)
        {
            modelMatrix.Set(renderer.ModelMat);
        }
        else
        {
            BuildPlayerModelMatrix(modelMatrix, player);
        }

        if (player.AnimManager?.Animator is not AnimatorBase animator ||
            !TryFindPose(animator.RootPoses, config.AttachmentPart, out ElementPose? torsoPose) ||
            torsoPose?.AnimModelMatrix == null)
        {
            return false;
        }

        modelMatrix.Mul(torsoPose.AnimModelMatrix);
        return true;
    }

    private bool IsLocalFirstPerson(EntityPlayer player)
    {
        IClientPlayer? localPlayer = _api.World.Player;
        return localPlayer?.Entity?.EntityId == player.EntityId && localPlayer.CameraMode == EnumCameraMode.FirstPerson;
    }

    private static void BuildPlayerModelMatrix(Matrixf matrix, EntityPlayer playerEntity)
    {
        matrix.Identity();
        Vec3d camera = playerEntity.CameraPos;
        matrix.Translate(playerEntity.Pos.X - camera.X, playerEntity.Pos.InternalY - camera.Y, playerEntity.Pos.Z - camera.Z);

        float rotX = playerEntity.Properties.Client.Shape?.rotateX ?? 0;
        float rotY = playerEntity.Properties.Client.Shape?.rotateY ?? 0;
        float rotZ = playerEntity.Properties.Client.Shape?.rotateZ ?? 0;

        matrix.Translate(0, playerEntity.SelectionBox.Y2 / 2f, 0);
        matrix.RotateX(playerEntity.Pos.Roll + rotX * GameMath.DEG2RAD);
        matrix.RotateY(playerEntity.BodyYaw + (90f + rotY) * GameMath.DEG2RAD);
        matrix.RotateZ(playerEntity.WalkPitch + rotZ * GameMath.DEG2RAD);
        matrix.Translate(0, -playerEntity.SelectionBox.Y2 / 2f, 0);

        float size = playerEntity.Properties.Client.Size;
        matrix.Scale(size, size, size);
        matrix.Translate(-0.5f, 0, -0.5f);
    }

    private static bool TryFindPose(IEnumerable<ElementPose>? poses, string elementName, out ElementPose? result)
    {
        result = null;
        if (poses == null) return false;

        foreach (ElementPose pose in poses)
        {
            if (string.Equals(pose.ForElement?.Name, elementName, StringComparison.OrdinalIgnoreCase))
            {
                result = pose;
                return true;
            }

            if (TryFindPose(pose.ChildElementPoses, elementName, out result)) return true;
        }

        return false;
    }

    private static EnumItemRenderTarget ParseRenderTarget(string? renderTarget)
    {
        return Enum.TryParse(renderTarget, ignoreCase: true, out EnumItemRenderTarget target)
            ? target
            : EnumItemRenderTarget.HandTp;
    }

    private static void ApplyModelTransform(Matrixf matrix, ModelTransform transform)
    {
        FastVec3f scale = transform.ScaleXYZ;
        matrix
            .Translate(transform.Translation.X / 16f, transform.Translation.Y / 16f, transform.Translation.Z / 16f)
            .Translate(transform.Origin.X / 16f, transform.Origin.Y / 16f, transform.Origin.Z / 16f)
            .RotateX(transform.Rotation.X * GameMath.DEG2RAD)
            .RotateY(transform.Rotation.Y * GameMath.DEG2RAD)
            .RotateZ(transform.Rotation.Z * GameMath.DEG2RAD)
            .Scale(scale.X, scale.Y, scale.Z)
            .Translate(-transform.Origin.X / 16f, -transform.Origin.Y / 16f, -transform.Origin.Z / 16f);
    }

    private static void ApplyStoredItemTransform(
        Matrixf matrix,
        ModelTransform transform,
        BackSlingStoredWeaponRenderConfig config,
        BackSlingStoredWeaponItemTransform? itemTransform)
    {
        bool applyTranslation = itemTransform?.ApplyStoredItemTranslation ?? config.ApplyStoredItemTranslation;
        bool applyRotation = itemTransform?.ApplyStoredItemRotation ?? config.ApplyStoredItemRotation;
        bool applyScale = itemTransform?.ApplyStoredItemScale ?? config.ApplyStoredItemScale;
        FastVec3f scale = transform.ScaleXYZ;

        if (applyTranslation)
        {
            matrix.Translate(transform.Translation.X / 16f, transform.Translation.Y / 16f, transform.Translation.Z / 16f);
        }

        matrix.Translate(transform.Origin.X / 16f, transform.Origin.Y / 16f, transform.Origin.Z / 16f);

        if (applyRotation)
        {
            matrix
                .RotateX(transform.Rotation.X * GameMath.DEG2RAD)
                .RotateY(transform.Rotation.Y * GameMath.DEG2RAD)
                .RotateZ(transform.Rotation.Z * GameMath.DEG2RAD);
        }

        if (applyScale)
        {
            matrix.Scale(scale.X, scale.Y, scale.Z);
        }

        matrix.Translate(-transform.Origin.X / 16f, -transform.Origin.Y / 16f, -transform.Origin.Z / 16f);
    }

    private bool TryRenderAnimatable(EntityPlayer player, ItemStack storedStack, ItemRenderInfo renderInfo, Matrixf modelMatrix, Vec4f light, EnumItemRenderTarget target, float dt)
    {
        if (storedStack.Item?.GetCollectibleBehavior(typeof(Animatable), true) is not Animatable animatable) return false;

        animatable.BeforeRender(_api, storedStack, player, target, dt);
        AnimatableShape? shape = animatable.CurrentAnimatableShape;
        CombatOverhaulAnimationsSystem? animationSystem = _api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>();
        IShaderProgram? shader = animationSystem?.AnimatedItemShaderProgram;

        if (shape == null || shader == null) return false;

        try
        {
            shape.Render(shader, renderInfo, _api.Render, storedStack, light, modelMatrix, player, dt);
            return !AnimatableShape.HadRenderErrorLastCall;
        }
        catch (Exception exception)
        {
            WarnOnce(storedStack, exception.Message);
            return false;
        }
    }

    private void RenderStaticStack(ItemStack storedStack, ItemRenderInfo renderInfo, Matrixf modelMatrix, Vec4f light)
    {
        IRenderAPI render = _api.Render;
        IShaderProgram? previous = render.CurrentActiveShader;
        IShaderProgram? shader = render.GetEngineShader(EnumShaderProgram.Standard);
        if (shader == null) return;

        try
        {
            previous?.Stop();
            shader.Use();
            shader.Uniform("dontWarpVertices", 0);
            shader.Uniform("addRenderFlags", 0);
            shader.Uniform("normalShaded", renderInfo.NormalShaded ? 1 : 0);
            shader.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
            shader.Uniform("alphaTest", renderInfo.AlphaTest);
            shader.Uniform("damageEffect", renderInfo.DamageEffect);
            shader.Uniform("overlayOpacity", renderInfo.OverlayOpacity);
            shader.Uniform("rgbaAmbientIn", render.AmbientColor);
            shader.Uniform("rgbaLightIn", light);
            shader.Uniform("rgbaGlowIn", new Vec4f(1f, 1f, 1f, 0f));
            shader.Uniform("rgbaFogIn", render.FogColor);
            shader.Uniform("fogMinIn", render.FogMin);
            shader.Uniform("fogDensityIn", render.FogDensity);
            shader.Uniform("extraGlow", 0);
            shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
            shader.UniformMatrix("viewMatrix", render.CameraMatrixOriginf);
            shader.UniformMatrix("modelMatrix", modelMatrix.Values);

            if (renderInfo.CullFaces) render.GlEnableCullFace();
            else render.GlDisableCullFace();

            render.RenderMultiTextureMesh(renderInfo.ModelRef, "tex", 0);
            render.GlEnableCullFace();
        }
        catch (Exception exception)
        {
            WarnOnce(storedStack, exception.Message);
        }
        finally
        {
            shader.Stop();
            previous?.Use();
        }
    }

    private void WarnOnce(ItemStack stack, string reason)
    {
        string code = stack.Collectible?.Code?.ToString() ?? "unknown";
        string key = $"{code}:{reason}";
        if (!_warnedRenderFailures.Add(key)) return;

        _api.Logger.Warning("[QuiversAndSheaths] Could not render back sling stored weapon {0}: {1}", code, reason);
    }
}
