using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace RH.Functions
{
    public static class TokenRefreshTimerFunction
    {
        [FunctionName("TokenRefreshTimerFunction")]
        public static void Run([TimerTrigger("0 0 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            S3TokenRefresh.Run(log);
        }
    }
}
