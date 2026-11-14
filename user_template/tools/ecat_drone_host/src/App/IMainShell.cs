/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * IMainShell.cs - interface the tabs use to reach the application core.
 */

using EcatDroneHost.Protocol;

namespace EcatDroneHost.App
{
    /// <summary>Application services shared by all tabs.</summary>
    public interface IMainShell
    {
        /// <summary>The connected device client (never null; check IsConnected).</summary>
        BoardClient Client { get; }

        /// <summary>Shared protocol / operation log.</summary>
        LogBuffer Log { get; }

        /// <summary>The AM32 bridge session object (never null).</summary>
        Am32Bridge Am32Bridge { get; }

        bool IsConnected { get; }

        /// <summary>Last parameter list read from the device (may be empty).</summary>
        ParamDesc[] Parameters { get; }

        /// <summary>Motor pole pairs (PARAM_DSHOT_POLE_PAIRS, default 7).</summary>
        int PolePairs { get; }

        /// <summary>Find a cached device parameter descriptor (null when not loaded).</summary>
        ParamDesc FindParameter(int id);

        void LogInfo(string category, string text);
        void LogWarning(string category, string text);
        void LogError(string category, string text);

        /// <summary>Re-read the parameter table from the device (background).</summary>
        void RequestParameterReload();

        /// <summary>Show a short message in the status strip.</summary>
        void ShowStatus(string text, bool isError);
    }
}
