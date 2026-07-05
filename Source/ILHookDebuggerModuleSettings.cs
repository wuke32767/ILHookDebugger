using YamlDotNet.Serialization;

namespace Celeste.Mod.ILHookDebugger;

public class ILHookDebuggerModuleSettings : EverestModuleSettings
{
    private bool mappingUtilsIntegration = true;

    [DefaultButtonBinding(0, 0)]
    public ButtonBinding PanelKey { get; set; } = null!;

    [SettingName("ILHookDebugger_Settings_PrettifyMonoMod")]
    [SettingSubText("ILHookDebugger_Settings_PrettifyMonoMod_Help")]
    public bool PrettifyMonoMod
    {
        get => ILHookDebuggerModule.PrettifyMonoMod;
        set
        {
            ILHookDebuggerModule.PrettifyMonoMod.Value = value;
        }
    }

    [SettingName("ILHookDebugger_Settings_BreakOnce")]
    [SettingSubText("ILHookDebugger_Settings_BreakOnce_Help")]
    public bool BreakOnce
    {
        get => ILHookDebuggerModule.BreakOnce;
        set
        {
            ILHookDebuggerModule.BreakOnce.Value = value;
        }
    }

    [SettingName("ILHookDebugger_Settings_IEnumeratorPatch")]
    [SettingSubText("ILHookDebugger_Settings_IEnumeratorPatch_Help")]
    public bool IEnumeratorPatch
    {
        get => ILHookDebuggerModule.IEnumeratorPatch;
        set
        {
            ILHookDebuggerModule.IEnumeratorPatch.Value = value;
        }
    }

    [SettingName("ILHookDebugger_Settings_UnloadWhenDetached")]
    [SettingSubText("ILHookDebugger_Settings_UnloadWhenDetached_Help")]
    public bool UnloadWhenDetached
    {
        get => ILHookDebuggerModule.UnloadWhenDetached;
        set
        {
            ILHookDebuggerModule.UnloadWhenDetached.Value = value;
        }
    }
    [SettingName("ILHookDebugger_Settings_IDE")]
    [SettingSubText("ILHookDebugger_Settings_IDE_Help")]
    public Compatibility IDE
    {
        get => ILHookDebuggerModule.IDE;
        set
        {
            ILHookDebuggerModule.IDE.Value = value;
        }
    }
    [SettingIgnore]
    public bool ShowHelp { get; set; } = true;
    public bool UseConvertor
    {
        get => ILHookDebuggerModule.TextConvertor;
        set
        {
            ILHookDebuggerModule.TextConvertor.Value = value;
        }
    }

    [SettingName("ILHookDebugger_Settings_DecompileResolver")]
    [SettingSubText("ILHookDebugger_Settings_DecompileResolver_Help")]
    [SettingIgnore]
    public bool UseDecompileResolver { get; set; } = true;

    [SettingName("ILHookDebugger_Settings_DecompileHackFix")]
    [SettingSubText("ILHookDebugger_Settings_DecompileHackFix_Help")]
    public bool DecompilerHackFix1
    {
        get;
        set
        {
            if (value)
            {
                ILHookDebuggerModule.TryFix();
            }
            else
            {
                ILHookDebuggerModule.Unfix();
            }
            field = value;
        }
    }

    [SettingName("ILHookDebugger_Settings_Decompiler_ServerMode")]
    [SettingSubText("ILHookDebugger_Settings_Help_Decompiler_ServerMode")]
    public bool ServerMode
    {
        get;
        set
        {
            if (value)
            {
                ILHookDebuggerModule.Service();
            }
            else
            {
                ILHookDebuggerModule.CtrlC();
            }
            field = value;
        }
    }

    [SettingNeedsRelaunch]
    public int Port { get; set; } = 4446;

    [SettingIgnore]
    public bool OpenInEditor { get; set; }

    [SettingIgnore]
    public bool UseTextEditor { get; set; }

    [SettingName("ILHookDebugger_Settings_ConsoleColor")]
    [SettingSubText("ILHookDebugger_Settings_ConsoleColor_Help")]
    public bool ColorfulConsole { get; set; } = true;

    public bool MappingUtilsIntegration2
    {
        get => mappingUtilsIntegration;
        set
        {
            mappingUtilsIntegration = value;
            ILHookDebuggerModule.Instance.DoMappingUtils();
        }
    }

    public string? ServerInfo { get; set; }

    public void CreateIDEEntry(TextMenu menu, bool _)
    {
        var c = System.Enum.GetNames<Compatibility>();
        int max = c.Length - 1;
        if (!ILHookDebuggerModule.CheckRunner.Value.Item1)
        {
            max--;
        }
        var i = new TextMenu.Slider(Dialog.Clean("ILHookDebugger_Settings_IDE"), i => c[i], 0, max, (int)IDE);
        i.OnValueChange += v =>
        {
            IDE = (Compatibility)v;
        };
        menu.Add(i);
        i.AddDescription(menu, Dialog.Clean("ILHookDebugger_Settings_IDE_Help"));
    }
    public void CreateServerInfoEntry(TextMenu menu, bool _)
    {
    }
    public void CreatePortEntry(TextMenu menu, bool _)
    {
        var i = new TextMenu.Slider(Dialog.Clean("ILHookDebugger_Settings_Decompiler_ServerModePort"), _ => Port.ToString(), Port, Port);
        menu.Add(i);
        i.AddDescription(menu, Dialog.Clean("ILHookDebugger_Settings_Help_Decompiler_ServerModePort"));
        i.AddDescription(menu, Dialog.Clean("ILHookDebugger_Settings_Help_Decompiler_ServerModePort_NoEdit"));
    }
    public void CreateBreakOnceEntry(TextMenu menu, bool _)
    {
        if (ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.CanNotModifyValues))
        {
            var unit = new TextMenu.Option<string>(Dialog.Clean("ILHookDebugger_Settings_BreakOnce"));
            menu.Add(unit);
            unit.Add(Dialog.Clean("options_on"), "", true);
            unit.AddDescription(menu, Dialog.Clean("ILHookDebugger_Settings_BreakOnce_Help_Disabled"));
        }
        else
        {
            var unit = new TextMenu.OnOff(Dialog.Clean("ILHookDebugger_Settings_BreakOnce"), BreakOnce);
            unit.OnValueChange += val =>
            {
                BreakOnce = val;
            };
            menu.Add(unit);
            unit.AddDescription(menu, Dialog.Clean("ILHookDebugger_Settings_BreakOnce_Help"));
        }
    }
    public void CreateUseConvertorEntry(TextMenu menu, bool _)
    {
        if (ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.NormalizeName))
        {
            var unit = new TextMenu.OnOff(Dialog.Clean("ILHookDebugger_Settings_Convertor"), UseConvertor);
            unit.OnValueChange += val =>
            {
                UseConvertor = val;
            };
            menu.Add(unit);
            unit.AddDescription(menu, Dialog.Clean("ILHookDebugger_Settings_Convertor_Help"));
        }
    }
    public void CreateMappingUtilsIntegration2Entry(TextMenu menu, bool _)
    {
        if (MappingUtils.MappingUtilsTabs.IsImported)
        {
            var unit = new TextMenu.OnOff(Dialog.Clean("ILHookDebugger_Settings_MappingUtilsIntegration"), MappingUtilsIntegration2);
            unit.OnValueChange += val =>
            {
                MappingUtilsIntegration2 = val;
            };
            menu.Add(unit);
            unit.AddDescription(menu, Dialog.Clean("ILHookDebugger_Settings_MappingUtilsIntegration_Help"));
        }
    }
}