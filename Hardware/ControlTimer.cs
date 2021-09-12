using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace OpenHardwareMonitor.Hardware {
  class ControlTimer {
    private static ControlTimer instance = null;
    private static readonly object padlock = new object();
    private static readonly object handlerlock = new object();
    private Timer timer;
    private int handlerCount = 0;

    ControlTimer() {
      timer = new Timer();
      timer.Interval = 1000;
    }

    public void addHandler(ElapsedEventHandler handler) {
      lock (handlerlock) {
        timer.Elapsed += handler;
        handlerCount++;
        if (!timer.Enabled) {
          timer.Start();
        }
      }
    }

    public void removeHandler(ElapsedEventHandler handler) {
      lock (handlerlock) {
        timer.Elapsed -= handler;
        handlerCount--;
        if (handlerCount == 0) {
          timer.Stop();
        }
      }
    }

    public static ControlTimer Instance {
      get {
        if (instance == null) {
          lock (padlock) {
            if (instance == null) {
              instance = new ControlTimer();
            }
          }
        }
        return instance;
      }
    }
  }
}
