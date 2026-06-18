using ConfigLib;
using CombatOverhaul.Armor;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace QuiversAndSheaths;

public sealed class QuiversAndSheathsSettings
{
    public bool AllowStonesInSlingPouch { get; set; } = false;
}

public sealed class QuiversAndSheathsSystem : ModSystem
{
    public static QuiversAndSheathsSettings Settings { get; } = new();

    private ICoreClientAPI? _clientApi;
    private BackSlingStoredWeaponRenderer? _backSlingRenderer;
    private QsOverlayGizmoRenderer? _qsOverlayGizmoRenderer;

    public override void Start(ICoreAPI api)
    {
        api.RegisterCollectibleBehaviorClass("QuiversAndSheaths:ShapeTexturesFromAttributes", typeof(ShapeTexturesFromAttributes));
        api.RegisterCollectibleBehaviorClass("QuiversAndSheaths:ShapeReplacement", typeof(ShapeReplacement));
        api.RegisterCollectibleBehaviorClass("QuiversAndSheaths:Sheath", typeof(SheathBehavior));
        api.RegisterCollectibleBehaviorClass("QuiversAndSheaths:Quiver", typeof(QuiverBehavior));
        api.RegisterCollectibleBehaviorClass("QuiversAndSheaths:VariantFromSlot", typeof(VariantFromSlotBehavior));
        api.RegisterCollectibleBehaviorClass("QuiversAndSheaths:BackSlingRenderConfig", typeof(BackSlingRenderConfigBehavior));

        if (api.ModLoader.IsModEnabled("configlib"))
        {
            SubscribeToConfigChange(api);
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;
        _backSlingRenderer = new(api);
        _qsOverlayGizmoRenderer = new(api);
        api.Event.RegisterRenderer(_backSlingRenderer, EnumRenderStage.Opaque, "quiversandsheaths-back-sling-stored-weapon");
        BackSlingTransformTuningCommands.Register(api);
    }

    public override void Dispose()
    {
        if (_clientApi != null && _backSlingRenderer != null)
        {
            _clientApi.Event.UnregisterRenderer(_backSlingRenderer, EnumRenderStage.Opaque);
        }

        _backSlingRenderer?.Dispose();
        _qsOverlayGizmoRenderer?.Dispose();
        _backSlingRenderer = null;
        _qsOverlayGizmoRenderer = null;
        _clientApi = null;
    }

    private static void SubscribeToConfigChange(ICoreAPI api)
    {
        ConfigLibModSystem system = api.ModLoader.GetModSystem<ConfigLibModSystem>();

        system.SettingChanged += (domain, config, setting) =>
        {
            if (domain != "quiversandsheaths") return;

            setting.AssignSettingValue(Settings);
        };

        system.ConfigsLoaded += () =>
        {
            system.GetConfig("quiversandsheaths")?.AssignSettingsValues(Settings);
        };
    }
}
