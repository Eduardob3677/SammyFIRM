using System;

namespace SamFirm.Utils
{
    /// <summary>
    /// Centralized logging utility with consistent formatting.
    /// Matches the bash logging system format for consistency across the project.
    /// </summary>
    internal static class Logger
    {
        private const int LogWidth = 80;

        /// <summary>
        /// Gets the current timestamp in HH:mm:ss format.
        /// </summary>
        private static string GetTimestamp() => DateTime.Now.ToString("HH:mm:ss");

        /// <summary>
        /// Prints a divider line with specified character.
        /// </summary>
        private static void PrintDivider(char c = '-')
        {
            Console.WriteLine(new string(c, LogWidth));
        }

        /// <summary>
        /// Logs an informational message.
        /// Format: [HH:MM:SS] [INFO] message
        /// </summary>
        public static void Info(string message)
        {
            Console.WriteLine($"[{GetTimestamp()}] [INFO] {message}");
        }

        /// <summary>
        /// Logs a warning message.
        /// Format: [HH:MM:SS] [WARN] message
        /// </summary>
        public static void Warn(string message)
        {
            Console.WriteLine($"[{GetTimestamp()}] [WARN] {message}");
        }

        /// <summary>
        /// Logs an error message.
        /// Format: [HH:MM:SS] [ERROR] message
        /// </summary>
        public static void Error(string message)
        {
            Console.WriteLine($"[{GetTimestamp()}] [ERROR] {message}");
        }

        /// <summary>
        /// Logs a debug message.
        /// Format: [HH:MM:SS] [DEBUG] message
        /// </summary>
        public static void Debug(string message)
        {
            Console.WriteLine($"[{GetTimestamp()}] [DEBUG] {message}");
        }

        /// <summary>
        /// Logs a success/done message.
        /// Format: [DONE] message
        /// </summary>
        public static void Done(string message)
        {
            Console.WriteLine($"[DONE] {message}");
        }

        /// <summary>
        /// Logs a success/done message with duration.
        /// Format: [DONE] message (duration)
        /// </summary>
        public static void Done(string message, string duration)
        {
            Console.WriteLine($"[DONE] {message} ({duration})");
        }

        /// <summary>
        /// Logs a begin message for a process.
        /// Format: ->message
        /// </summary>
        public static void Begin(string message)
        {
            Console.WriteLine($"->{message}");
            Console.WriteLine();
        }

        /// <summary>
        /// Logs a running/processing message.
        /// Format: [HH:MM:SS] [*] message...
        /// </summary>
        public static void Running(string message)
        {
            Console.WriteLine($"[{GetTimestamp()}] [*] {message}...");
        }

        /// <summary>
        /// Logs raw output without formatting (for multi-line data display).
        /// </summary>
        public static void Raw(string message)
        {
            Console.WriteLine(message);
        }

        /// <summary>
        /// Logs an exception with error level.
        /// </summary>
        public static void Exception(string context, Exception ex)
        {
            Error($"{context}: {ex.Message}");
        }

        /// <summary>
        /// Logs an exception with detailed information.
        /// </summary>
        public static void ExceptionDetail(string context, Exception ex)
        {
            Error($"{context}: {ex.Message}");
            Debug($"Exception details: {ex}");
        }

        /// <summary>
        /// Logs a fatal error and displays process failed message.
        /// </summary>
        public static void ErrorExit(string message, int code = 1)
        {
            Console.WriteLine();
            Console.WriteLine("!!! PROCESS FAILED !!!");
            PrintDivider('=');
            Console.WriteLine($">> {message}");
            Console.WriteLine($"Exiting with code: {code}");
        }

        /// <summary>
        /// Logs a dialog box with title and optional description.
        /// </summary>
        public static void Dialog(string title, string description = null)
        {
            Console.WriteLine();
            PrintDivider('-');
            Console.WriteLine($"| {title}");
            if (!string.IsNullOrEmpty(description))
            {
                PrintDivider('-');
                Console.WriteLine($"| {description}");
            }
            PrintDivider('-');
            Console.WriteLine();
        }
    }
}
