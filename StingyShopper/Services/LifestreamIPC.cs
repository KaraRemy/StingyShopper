using System;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace StingyShopper.Services
{
    public class LifestreamIPC
    {
        private readonly IDalamudPluginInterface pluginInterface;
        private readonly IPluginLog pluginLog;
        private readonly IChatGui chatGui;

        private readonly ICallGateSubscriber<bool>? isBusySubscriber;
        private readonly ICallGateSubscriber<string, bool>? changeWorldSubscriber;
        private readonly ICallGateSubscriber<string, object>? executeCommandSubscriber;

        public LifestreamIPC(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog, IChatGui chatGui)
        {
            this.pluginInterface = pluginInterface;
            this.pluginLog = pluginLog;
            this.chatGui = chatGui;

            try
            {
                this.isBusySubscriber = this.pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
                this.changeWorldSubscriber = this.pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.ChangeWorld");
                this.executeCommandSubscriber = this.pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
            }
            catch (Exception ex)
            {
                this.pluginLog.Warning($"[StingyShopper] Failed to initialize Lifestream IPC subscribers: {ex.Message}");
            }
        }

        public bool IsLifestreamInstalled()
        {
            try
            {
                var installedPluginsProp = this.pluginInterface.GetType().GetProperty("InstalledPlugins", BindingFlags.Public | BindingFlags.Instance);
                if (installedPluginsProp == null) return false;

                var installedPlugins = installedPluginsProp.GetValue(this.pluginInterface) as System.Collections.IEnumerable;
                if (installedPlugins == null) return false;

                foreach (var plugin in installedPlugins)
                {
                    var nameProp = plugin.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                                   ?? plugin.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var name = nameProp?.GetValue(plugin) as string;

                    var isLoadedProp = plugin.GetType().GetProperty("IsLoaded", BindingFlags.Public | BindingFlags.Instance)
                                       ?? plugin.GetType().GetProperty("IsLoaded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var isLoaded = isLoadedProp?.GetValue(plugin) as bool? ?? false;

                    if (string.Equals(name, "Lifestream", StringComparison.OrdinalIgnoreCase) && isLoaded)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                this.pluginLog.Error(ex, "[StingyShopper] Error checking if Lifestream is installed");
            }
            return false;
        }

        public bool IsLifestreamBusy()
        {
            if (!IsLifestreamInstalled()) return false;

            try
            {
                return this.isBusySubscriber?.InvokeFunc() ?? false;
            }
            catch
            {
                return false;
            }
        }

        public bool ChangeWorld(string worldName)
        {
            if (string.IsNullOrWhiteSpace(worldName)) return false;

            if (!IsLifestreamInstalled())
            {
                this.chatGui.PrintError("[StingyShopper] Lifestream plugin is not installed or not loaded. Teleport failed.");
                return false;
            }

            // Prefer executeCommandSubscriber to chain world travel and market board navigation (e.g. "Odin mb")
            try
            {
                if (this.executeCommandSubscriber != null)
                {
                    this.executeCommandSubscriber.InvokeAction($"{worldName} mb");
                    return true;
                }
            }
            catch (Exception ex)
            {
                this.pluginLog.Warning($"[StingyShopper] Error calling Lifestream.ExecuteCommand for chained travel: {ex.Message}");
            }

            // Fallback to basic world change if ExecuteCommand subscriber is unavailable
            try
            {
                if (this.changeWorldSubscriber != null)
                {
                    return this.changeWorldSubscriber.InvokeFunc(worldName);
                }
            }
            catch (Exception ex)
            {
                this.pluginLog.Error($"[StingyShopper] Error calling Lifestream.ChangeWorld fallback: {ex.Message}");
            }

            return false;
        }

        public bool TeleportToMarketBoard()
        {
            if (!IsLifestreamInstalled()) return false;

            try
            {
                if (this.executeCommandSubscriber != null)
                {
                    this.executeCommandSubscriber.InvokeAction("mb");
                    return true;
                }
            }
            catch (Exception ex)
            {
                this.pluginLog.Error($"[StingyShopper] Error calling Lifestream.ExecuteCommand for marketboard: {ex.Message}");
            }
            return false;
        }
    }
}
