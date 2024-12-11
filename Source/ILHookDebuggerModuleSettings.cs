using YamlDotNet.Serialization;

namespace Celeste.Mod.ILHookDebugger;

public class ILHookDebuggerModuleSettings : EverestModuleSettings
{
    [YamlIgnore]
    private bool autoRefresh = false;
    private bool hookMonoModInternal = false;
    private bool unloadWhenDetached = false;

    [SettingIgnore]
    [SettingName("ILHookDebugger_Settings_AutoRefresh")]
    [SettingSubHeader("ILHookDebugger_Settings_AutoRefresh_Help")]
    public bool AutoRefresh
    {
        get => autoRefresh; 
        set
        {
            if(HookMonoModInternal)
            {
                return;
            }
            autoRefresh = value;
            ILHookDebuggerModule.Instance.AutoRefresh(value);
        }
    }
    [SettingIgnore]
    [SettingName("ILHookDebugger_Settings_HookMonoModInternal")]
    public bool HookMonoModInternal
    {
        get => hookMonoModInternal; 
        set
        {
            hookMonoModInternal = value;
            ILHookDebuggerModule.Instance.HookMonoModInternal(value);
            if(value)
            {
                AutoRefresh = false;
            }
        }
    }
    [SettingName("ILHookDebugger_Settings_UnloadWhenDetached")]
    public bool UnloadWhenDetached
    {
        get => unloadWhenDetached; set
        {
            unloadWhenDetached = value;
            ILHookDebuggerModule.Instance.UnloadWhenDetached(value);
        }
    }
}