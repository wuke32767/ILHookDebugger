using YamlDotNet.Serialization;

namespace Celeste.Mod.ILHookDebugger;

public class ILHookDebuggerModuleSettings : EverestModuleSettings
{
    [SettingIgnore]
    [SettingName("ILHookDebugger_Settings_AutoRefresh")]
    [SettingSubText("ILHookDebugger_Settings_AutoRefresh_Help")]
    public bool AutoRefresh
    {
        get => ILHookDebuggerModule.AutoRefresh;
        set
        {
            if (HookMonoModInternal)
            {
                value = false;
            }
            ILHookDebuggerModule.AutoRefresh.Value = value;
        }
    }
    [SettingName("ILHookDebugger_Settings_HookMonoModInternal")]
    [SettingSubText("ILHookDebugger_Settings_HookMonoModInternal_Help")]
    public bool HookMonoModInternal
    {
        get => ILHookDebuggerModule.HookMonoModInternal;
        set
        {
            ILHookDebuggerModule.HookMonoModInternal.Value = value;
            if (value)
            {
                AutoRefresh = false;
            }
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
}