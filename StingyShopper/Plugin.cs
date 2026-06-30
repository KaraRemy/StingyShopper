using System;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using StingyShopper.Services;
using StingyShopper.Windows;

namespace StingyShopper
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Stingy Shopper";

        private const string MainCommandName = "/pstingy";
        private const string AltCommandName = "/stingy";

        [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
        [PluginService] public static IDataManager DataManager { get; private set; } = null!;
        [PluginService] public static IFramework Framework { get; private set; } = null!;
        [PluginService] public static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] public static IClientState ClientState { get; private set; } = null!;
        [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;

        public Configuration Configuration { get; private set; }
        public WindowSystem WindowSystem { get; } = new("StingyShopper");
        public FileDialogManager FileDialogManager { get; } = new();

        public ItemLookupService ItemLookup { get; private set; }
        public UniversalisClient Universalis { get; private set; }
        public ShoppingListManager ListManager { get; private set; }
        public LifestreamIPC LifestreamIPC { get; private set; }
        public PurchaseTracker PurchaseTracker { get; private set; }

        public MainWindow MainWindow { get; private set; }
        public ConfigWindow ConfigWindow { get; private set; }

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            ItemLookup = new ItemLookupService(DataManager);
            Universalis = new UniversalisClient();
            ListManager = new ShoppingListManager(Configuration, ItemLookup, Universalis);
            LifestreamIPC = new LifestreamIPC(PluginInterface, PluginLog, ChatGui);
            PurchaseTracker = new PurchaseTracker(ChatGui, ListManager, Configuration, PluginLog);

            ConfigWindow = new ConfigWindow(Configuration);
            MainWindow = new MainWindow(Configuration, ItemLookup, ListManager, LifestreamIPC, ClientState, ObjectTable, FileDialogManager, () => ConfigWindow.Toggle());

            WindowSystem.AddWindow(MainWindow);
            WindowSystem.AddWindow(ConfigWindow);

            CommandManager.AddHandler(MainCommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Toggle StingyShopper main window. Use '/pstingy config' for settings."
            });

            CommandManager.AddHandler(AltCommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Toggle StingyShopper main window."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.Draw += DrawFileDialog;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        }

        public void Dispose()
        {
            WindowSystem.RemoveAllWindows();
            MainWindow.Dispose();
            ConfigWindow.Dispose();

            CommandManager.RemoveHandler(MainCommandName);
            CommandManager.RemoveHandler(AltCommandName);

            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.Draw -= DrawFileDialog;
            PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;

            PurchaseTracker.Dispose();
            Universalis.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            if (args.Trim().Equals("config", System.StringComparison.OrdinalIgnoreCase) ||
                args.Trim().Equals("settings", System.StringComparison.OrdinalIgnoreCase))
            {
                ConfigWindow.Toggle();
            }
            else
            {
                MainWindow.Toggle();
            }
        }

        private void DrawUI()
        {
            WindowSystem.Draw();
        }

        private void DrawFileDialog()
        {
            FileDialogManager.Draw();
        }

        private void DrawConfigUI()
        {
            ConfigWindow.Toggle();
        }

        private void ToggleMainUI()
        {
            MainWindow.Toggle();
        }
    }
}
