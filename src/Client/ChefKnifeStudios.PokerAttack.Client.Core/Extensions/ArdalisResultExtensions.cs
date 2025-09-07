using Ardalis.Result;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Extensions;

public static class ArdalisResultExtensions
{
    public static Result<T> LogErrors<T>(this Result<T>? result, ILogger logger, string context)
    {
        if (result is null)
        {
            var errMsg = "Result is null.";
            logger.LogError("{Context}: {Error}", context, errMsg);
            return Result.Error(errMsg);
        }

        if (!result.IsSuccess)
        {
            foreach (var error in result.Errors)
            {
                logger.LogError("{Context}: {Error}", context, error);
            }
        }

        return result;
    }
}
