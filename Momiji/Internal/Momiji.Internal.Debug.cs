using Microsoft.Extensions.Logging;
using Ole32 = Momiji.Interop.Ole32.NativeMethods;

namespace Momiji.Internal.Debug;

public class ThreadDebug
{
    public static void PrintObjectContext(
        ILoggerFactory loggerFactory
    )
    {
        var logger = loggerFactory.CreateLogger<ThreadDebug>();
        {
            var guid = typeof(Ole32.IComThreadingInfo).GUID;
            var result = Ole32.CoGetObjectContext(in guid, out var ppv);
            logger.LogInformation($"CoGetObjectContext {result}");

            if (ppv is Ole32.IComThreadingInfo comThreadingInfo)
            {
                comThreadingInfo.GetCurrentApartmentType(out var pAptType);
                comThreadingInfo.GetCurrentThreadType(out var pThreadType);
                comThreadingInfo.GetCurrentLogicalThreadId(out var pguidLogicalThreadId);
                logger.LogInformation($"IComThreadingInfo {pAptType} {pThreadType} {pguidLogicalThreadId}");
            }
        }

        {
            var guid = typeof(Ole32.IContext).GUID;
            var result = Ole32.CoGetObjectContext(in guid, out var ppv);
            logger.LogInformation($"CoGetObjectContext {result}");

            if (ppv is Ole32.IContext context)
            {
                var result2 = context.EnumContextProps(out var props);
                if (result2 == 0)
                {
                    if (props is Ole32.IEnumContextProps enumContextProps)
                    {
                        enumContextProps.Count(out var pcelt);
                        logger.LogInformation($"EnumContextProps Count {pcelt}");

                        enumContextProps.Next(0, out var pContextProperties, out var pceltFetched);
                        logger.LogInformation($"EnumContextProps Next {pContextProperties.policyId} {pContextProperties.pUnk} {pceltFetched}");

                    }
                }
            }
        }
    }
}