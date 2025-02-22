using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.RTWorkQueue;
using Momiji.Core.Threading;
using RTWorkQ = Momiji.Interop.RTWorkQ.NativeMethods;

namespace Momiji.Internal.Log;

internal static partial class LogDefine
{
    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} ({file}:{line} {member})"
    )]
    internal static partial void LogWithLine(this ILogger logger, LogLevel logLevel, string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} [{value}]({file}:{line} {member})"
    )]
    internal static partial void LogWithLine(this ILogger logger, LogLevel logLevel, string message, object value,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} Id:[{key}] ({file}:{line} {member})"
    )]
    internal static partial void LogCacheKey(this ILogger logger, LogLevel logLevel, string message, object key,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} Id:[{key}] [{value}] ({file}:{line} {member})"
    )]
    internal static partial void LogCacheKey(this ILogger logger, LogLevel logLevel, string message, object key, object value,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} apartmentType:[{type}] ({file}:{line} {member})"
    )]
    internal static partial void LogApartmentType(this ILogger logger, LogLevel logLevel, string message, ApartmentType type,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} apartmentType:[{type}] / current:[{current}] ({file}:{line} {member})"
    )]
    internal static partial void LogApartmentType(this ILogger logger, LogLevel logLevel, string message, ApartmentType type, ApartmentType current,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} workQueueId:[{workQueueId}]({file}:{line} {member})"
    )]
    internal static partial void LogRTWorkQueueId(this ILogger logger, LogLevel logLevel, string message, RTWorkQ.WorkQueueId workQueueId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} usageClass:{usageClass} priority:{priority} taskId:{taskId:X}({file}:{line} {member})"
    )]
    internal static partial void LogWithMMCSS(this ILogger logger, LogLevel logLevel, string message, string usageClass, IRTWorkQueue.TaskPriority priority, int taskId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} usageClass:{usageClass} priority:{priority} taskId:{taskId:X} workQueueId:{workQueueId}({file}:{line} {member})"
    )]
    internal static partial void LogWithMMCSS(this ILogger logger, LogLevel logLevel, string message, string usageClass, IRTWorkQueue.TaskPriority priority, int taskId, RTWorkQ.WorkQueueId workQueueId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} usageClass:{usageClass}({file}:{line} {member})"
    )]
    internal static partial void LogWithMMCSS(this ILogger logger, LogLevel logLevel, string message, string usageClass,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} type:{type}({file}:{line} {member})"
    )]
    internal static partial void LogWithWorkQueueType(this ILogger logger, LogLevel logLevel, string message, IRTWorkQueue.WorkQueueType type,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} type:{type} queueId:{workQueueId}({file}:{line} {member})"
    )]
    internal static partial void LogWithWorkQueueType(this ILogger logger, LogLevel logLevel, string message, IRTWorkQueue.WorkQueueType type, RTWorkQ.WorkQueueId workQueueId,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} Id:[{id}] apartmentType:[{type}] ({file}:{line} {member})"
    )]
    internal static partial void LogRTWorkQueueAsyncResultPoolValue(this ILogger logger, LogLevel logLevel, string message, uint id, ApartmentType type,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} Id:[{id}] apartmentType:[{type}] ({file}:{line} {member})"
    )]
    internal static partial void LogRTWorkQueueAsyncResultPoolValue(this ILogger logger, LogLevel logLevel, Exception exception, string message, uint id, ApartmentType type,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} Id:[{id}] apartmentType:[{type}] [{value1}] ({file}:{line} {member})"
    )]
    internal static partial void LogRTWorkQueueAsyncResultPoolValue(this ILogger logger, LogLevel logLevel, string message, uint id, ApartmentType type, object value1,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} Id:[{id}] apartmentType:[{type}] [{value1}] ({file}:{line} {member})"
    )]
    internal static partial void LogRTWorkQueueAsyncResultPoolValue(this ILogger logger, LogLevel logLevel, Exception exception, string message, uint id, ApartmentType type, object value1,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );

    [Conditional("DEBUG")]
    [LoggerMessage(
        Message = "{message} Id:[{id}] apartmentType:[{type}] [{value1}] [{value2}] ({file}:{line} {member})"
    )]
    internal static partial void LogRTWorkQueueAsyncResultPoolValue(this ILogger logger, LogLevel logLevel, string message, uint id, ApartmentType type, object value1, object value2,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0,
        [CallerMemberName] string member = ""
        );



}
