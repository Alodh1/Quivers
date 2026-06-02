using AttributeRenderingLibrary;
using Vintagestory.API.Common;

namespace QuiversAndSheaths;

internal static class StoredVariantResolver
{
    public static readonly string[] MetalVariantSources = ["metal", "material"];
    public static readonly string[] LeatherVariantSources = ["leather", "color"];
    public static readonly string[] WoodVariantSources = ["wood"];

    public static bool IsProtectedContainerVariantCode(string? variantCode)
    {
        if (string.IsNullOrWhiteSpace(variantCode)) return true;

        return variantCode.Equals("metal", StringComparison.Ordinal)
            || variantCode.Equals("material", StringComparison.Ordinal)
            || variantCode.Equals("leather", StringComparison.Ordinal)
            || variantCode.Equals("color", StringComparison.Ordinal)
            || variantCode.Equals("wood", StringComparison.Ordinal);
    }

    public static string? GetStoredMaterialVariant(ItemStack? storedStack, string targetVariantCode, params string[] sourceVariantCodes)
    {
        return GetStoredVariant(storedStack, requireSimpleValue: true, targetVariantCode, sourceVariantCodes);
    }

    public static string? GetStoredTextureVariant(ItemStack? storedStack, string targetVariantCode, params string[] sourceVariantCodes)
    {
        return GetStoredVariant(storedStack, requireSimpleValue: false, targetVariantCode, sourceVariantCodes);
    }

    private static string? GetStoredVariant(ItemStack? storedStack, bool requireSimpleValue, string targetVariantCode, params string[] sourceVariantCodes)
    {
        if (storedStack == null) return null;

        Variants variants = Variants.FromStack(storedStack);
        foreach (string key in new[] { targetVariantCode }.Concat(sourceVariantCodes))
        {
            string? value = variants.Get(key);
            if (CanUseValue(value, requireSimpleValue)) return value;

            value = storedStack.Attributes.GetString(key);
            if (CanUseValue(value, requireSimpleValue)) return value;

            if (storedStack.Collectible?.Variant?.TryGetValue(key, out value) == true && CanUseValue(value, requireSimpleValue))
            {
                return value;
            }
        }

        return null;
    }

    private static bool CanUseValue(string? value, bool requireSimpleValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return !requireSimpleValue || IsSimpleVariantValue(value);
    }

    private static bool IsSimpleVariantValue(string value)
    {
        return !value.Contains('/', StringComparison.Ordinal)
            && !value.Contains('\\', StringComparison.Ordinal)
            && !value.Contains(':', StringComparison.Ordinal)
            && !value.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }
}
