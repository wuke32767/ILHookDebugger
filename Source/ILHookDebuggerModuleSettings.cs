using YamlDotNet.Serialization;

namespace Celeste.Mod.ILHookDebugger;

public class ILHookDebuggerModuleSettings : EverestModuleSettings
{
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
    public bool MappingUtilsIntegration
    {
        get => ILHookDebuggerModule.MappingUtilsIntegration;
        set
        {
            ILHookDebuggerModule.MappingUtilsIntegration.Value = value;
        }
    }
    public void CreateMappingUtilsIntegrationEntry(TextMenu menu, bool _)
    {
        if (ILHookDebuggerModule.CheckMappingUtils.Value)
        {
            var unit = new TextMenu.OnOff(Dialog.Clean("ILHookDebugger_Settings_MappingUtilsIntegration"), MappingUtilsIntegration);
            unit.OnValueChange += val =>
            {
                MappingUtilsIntegration = val;
            };
            menu.Add(unit);
            unit.AddDescription(menu, Dialog.Clean("ILHookDebugger_Settings_MappingUtilsIntegration_Help"));
        }
    }
}