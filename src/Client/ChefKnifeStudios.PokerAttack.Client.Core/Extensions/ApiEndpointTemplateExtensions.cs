using System.Text.RegularExpressions;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Extensions;

public static partial class ApiEndpointTemplateExtensions
{
    [GeneratedRegex(@"\{([^{}:]+)(?::[^{}]+)?\}", RegexOptions.Compiled)]
    private static partial Regex AspNetRouteRegex();

    /// <summary>
    /// Formats a route template by replacing named parameter placeholders with provided values in sequential order.
    /// Handles ASP.NET Core style route templates with optional type constraints (e.g., "{paramName:int}").
    /// </summary>
    /// <param name="routeTemplate">The route template containing named parameters in curly braces.</param>
    /// <param name="values">The values to substitute for the parameters in the order they appear in the template.</param>
    /// <returns>A formatted route with parameter placeholders replaced by the provided values.</returns>
    /// <example>
    /// <code>
    /// // Route template: "/payment-methods/{paymentMethodId:int}"
    /// var url = ApiEndpoints.PaymentMethods.DeletePaymentMethod.FormatRoute(123);
    /// // Result: "/payment-methods/123"
    /// 
    /// // Route template with multiple parameters: "/users/{userId:int}/orders/{orderId}"
    /// var orderUrl = ApiEndpoints.Users.GetOrder.FormatRoute(42, "ORD-789");
    /// // Result: "/users/42/orders/ORD-789"
    /// </code>
    /// </example>
    public static string FormatRoute(this string routeTemplate, params object[] values)
    {
        if (string.IsNullOrEmpty(routeTemplate))
            return routeTemplate;

        int valueIndex = 0;
        var result = AspNetRouteRegex().Replace(
            routeTemplate,
            match =>
            {
                if (valueIndex < values.Length)
                    return values[valueIndex++]?.ToString() ?? string.Empty;
                return match.Value;
            });

        return result;
    }
}
