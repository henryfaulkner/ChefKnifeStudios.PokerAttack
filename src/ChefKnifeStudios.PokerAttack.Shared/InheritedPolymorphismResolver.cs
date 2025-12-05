using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ChefKnifeStudios.PokerAttack.Shared;

public class InheritedPolymorphismResolver : DefaultJsonTypeInfoResolver
{
    /*
     * ensure that JSON polymorphic serialization of derived types include
     * type discriminator when returning single instance as well
     *
     * fix for known bug:
     * https://github.com/dotnet/aspnetcore/issues/45548
     * 
     * reference:
     * https://github.com/dotnet/runtime/issues/77532#issuecomment-1300541631
     */
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);

        // Only handles class hierarchies -- interface hierarchies left out intentionally here
        if (!type.IsSealed && typeInfo.PolymorphismOptions is null && type.BaseType != null)
        {
            // recursively resolve metadata for the base type and extract any derived type declarations that overlap with the current type
            var basePolymorphismOptions = GetTypeInfo(type.BaseType, options).PolymorphismOptions;
            if (basePolymorphismOptions != null)
            {
                foreach (var derivedType in basePolymorphismOptions.DerivedTypes)
                {
                    if (type.IsAssignableFrom(derivedType.DerivedType))
                    {
                        typeInfo.PolymorphismOptions ??= new JsonPolymorphismOptions
                        {
                            IgnoreUnrecognizedTypeDiscriminators = basePolymorphismOptions.IgnoreUnrecognizedTypeDiscriminators,
                            TypeDiscriminatorPropertyName = basePolymorphismOptions.TypeDiscriminatorPropertyName,
                            UnknownDerivedTypeHandling = basePolymorphismOptions.UnknownDerivedTypeHandling,
                        };

                        typeInfo.PolymorphismOptions.DerivedTypes.Add(derivedType);
                    }
                }
            }
        }

        return typeInfo;
    }
}
