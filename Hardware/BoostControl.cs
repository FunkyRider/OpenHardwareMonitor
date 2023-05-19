using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

    private bool m_boostEnabled = false;
    private bool m_desktopLocked = false;
    private bool m_lockDeboost = false;
    private Process m_proc;
    private int m_boostMode = 2;
    private int m_procId = 0;
    private int m_affinity = 0;
    private DateTime lastTime;
    private TimeSpan lastTotalProcessorTime;
    private DateTime curTime;
    private TimeSpan curTotalProcessorTime;

    public BoostControl(bool LockDeboost) {
      m_lockDeboost = LockDeboost;
      if (m_lockDeboost) {
        SystemEvents.SessionSwitch += new SessionSwitchEventHandler(OnSessionSwitch);
      }
    }

    public bool HasLockDeboost() {
      return m_lockDeboost;
    }

    public void SetBoostMode(int mode) {
      m_boostMode = mode;
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
        SetProcessorBoostMode(m_boostMode);
      } else {
        SetProcessorBoostMode(0);
      }
    }

    public bool CanBoost()
    {
      return (m_boostEnabled && (!m_desktopLocked || !m_lockDeboost));
    }


    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static Process getForegroundProcess() {
      uint processID = 0;
      IntPtr hWnd = GetForegroundWindow(); // Get foreground window handle
      if (hWnd == null) {
        return null;
      }
      uint threadID = GetWindowThreadProcessId(hWnd, out processID); // Get PID from window handle
      if (processID == 0) {
        return null;
      }
      Process fgProc = Process.GetProcessById(Convert.ToInt32(processID)); // Get it as a C# obj.
      // NOTE: In some rare cases ProcessID will be NULL. Handle this how you want. 
      return fgProc;
    }

    public void UpdateProcessAffinity(int mode) {
      Process proc = getForegroundProcess();
      if (proc == null) {
        return;
      }
      try {
        if (m_procId != proc.Id) {
          if (m_proc != null && !m_proc.HasExited) {
            // Reset last process mask
            foreach (ProcessThread t in proc.Threads) {
              if (t.ThreadState == ThreadState.Running) {
                t.ProcessorAffinity = (IntPtr)0xFFFFFF;
              }
            }
            Debug.WriteLine("Restore affinity: " + m_proc.ProcessName);
          }
          m_proc = proc;
          m_procId = proc.Id;
          m_affinity = 0x7FFFFFFF;
          lastTime = DateTime.Now;
          if (proc.HasExited) {
            return;
          }
          lastTotalProcessorTime = proc.TotalProcessorTime;
          return;
        }
        int AffinityMask = 0;
        curTime = DateTime.Now;
        if (proc.HasExited) {
          return;
        }
        curTotalProcessorTime = proc.TotalProcessorTime;

        double CPUUsage = (curTotalProcessorTime.TotalMilliseconds - lastTotalProcessorTime.TotalMilliseconds) / curTime.Subtract(lastTime).TotalMilliseconds / Convert.ToDouble(Environment.ProcessorCount) * 100;

        lastTime = curTime;
        lastTotalProcessorTime = curTotalProcessorTime;

        if (CPUUsage <= 24.8) {
          // One CCX, SMT
          AffinityMask = (mode == 1) ? 0xFFF : 0xFFF000;
        } else {
          // Two CCXs, SMT
          AffinityMask = 0xFFFFFF;
        }
        if (m_affinity != AffinityMask) {
          m_affinity = AffinityMask;
          if (!proc.HasExited) {
            foreach (ProcessThread t in proc.Threads) {
              if (t.ThreadState == ThreadState.Running) {
                t.ProcessorAffinity = (IntPtr)AffinityMask;
              }
            }
          }
          Debug.WriteLine("Process time: " + CPUUsage + ", affinity: " + m_affinity.ToString("X") + ", name: " + proc.ProcessName);
        }
      } catch (System.InvalidOperationException) {

      } catch (Win32Exception) {

      }
    }
  }
}
