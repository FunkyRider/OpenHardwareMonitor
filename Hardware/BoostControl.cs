using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OpenHardwareMonitor.Hardware {
  class BoostControl {
    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
    static extern UInt32 PowerWriteDCValueIndex(IntPtr RootPowerKey,
    [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
    [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
    [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid, int AcValueIndex);

    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
    static extern UInt32 PowerWriteACValueIndex(IntPtr RootPowerKey,
        [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
        [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
        [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
        int AcValueIndex);

    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
    static extern UInt32 PowerSetActiveScheme(IntPtr RootPowerKey,
        [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid);

    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
    static extern UInt32 PowerGetActiveScheme(IntPtr UserPowerKey, out IntPtr ActivePolicyGuid);

    static readonly Guid GUID_PROCESSOR_SETTINGS_SUBGROUP = new Guid("54533251-82be-4824-96c1-47b60b740d00");
    static readonly Guid GUID_BOOSTMODE = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
    static readonly Guid GUID_PROCESSOR_THROTTLE_MAXIMUM = new Guid("BC5038F7-23E0-4960-96DA-33ABAF5935EC");
    static readonly Guid GUID_PROCESSOR_THROTTLE_MINIMUM = new Guid("893DEE8E-2BEF-41E0-89C6-B55D0929964C");

    bool m_boostEnabled = false;
    bool m_desktopLocked = false;
    bool m_lockDeboost = false;

    public BoostControl(bool LockDeboost) {
      m_lockDeboost = LockDeboost;
      if (m_lockDeboost) {
        SystemEvents.SessionSwitch += new SessionSwitchEventHandler(OnSessionSwitch);
      }
    }

    private void SetProcessorBoostMode(int mode) {
      IntPtr pActiveSchemeGuid;
      var hr = PowerGetActiveScheme(IntPtr.Zero, out pActiveSchemeGuid);
      Guid activeSchemeGuid = (Guid)Marshal.PtrToStructure(pActiveSchemeGuid, typeof(Guid));

      hr = PowerWriteACValueIndex(
           IntPtr.Zero,
           activeSchemeGuid,
           GUID_PROCESSOR_SETTINGS_SUBGROUP,
           GUID_BOOSTMODE,
           mode);

      PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid); //This is necessary to apply the current scheme.
    }

    public void SetPerformanceLevel(int min, int max) {
      IntPtr pActiveSchemeGuid;
      var hr = PowerGetActiveScheme(IntPtr.Zero, out pActiveSchemeGuid);
      Guid activeSchemeGuid = (Guid)Marshal.PtrToStructure(pActiveSchemeGuid, typeof(Guid));

      hr = PowerWriteACValueIndex(
           IntPtr.Zero,
           activeSchemeGuid,
           GUID_PROCESSOR_SETTINGS_SUBGROUP,
           GUID_PROCESSOR_THROTTLE_MAXIMUM,
           max);

      hr = PowerWriteACValueIndex(
           IntPtr.Zero,
           activeSchemeGuid,
           GUID_PROCESSOR_SETTINGS_SUBGROUP,
           GUID_PROCESSOR_THROTTLE_MINIMUM,
           min);

      PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid); //This is necessary to apply the current scheme.
    }

    public void EnableBoost(bool enable) {
      if (enable != m_boostEnabled) {
        m_boostEnabled = enable;
        UpdateBoostState();
      }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) {
      if (e.Reason == SessionSwitchReason.SessionLock) {
        m_desktopLocked = true;
        UpdateBoostState();
        Console.WriteLine("Desktop Lock");
      }
      else if (e.Reason == SessionSwitchReason.SessionUnlock) {
        m_desktopLocked = false;
        UpdateBoostState();
        Console.WriteLine("Desktop Unlock");
      }
    }

    private void UpdateBoostState() {
      if (CanBoost()) {
        SetProcessorBoostMode(2);
      } else {
        SetProcessorBoostMode(0);
      }
    }

    public bool CanBoost()
    {
      return (m_boostEnabled && (!m_desktopLocked || !m_lockDeboost));
    }
  }
}
