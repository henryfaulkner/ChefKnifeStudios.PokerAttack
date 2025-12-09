using System;
using System.Collections.Generic;
using System.Text;

namespace ChefKnifeStudios.PokerAttack.Shared;

public static class TimerHelper
{
    public static CancellationTokenSource SetInterval(Action action, int interval)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    action();
                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);

        return cts;
    }
}
