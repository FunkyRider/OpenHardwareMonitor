using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenHardwareMonitor.Hardware.Virtual {
  class VirtualGroup : IGroup {
    private readonly List<VirtualSensor> hardware = new List<VirtualSensor>();
    private readonly StringBuilder report = new StringBuilder();

    public VirtualGroup(ISettings settings) {
      for (int i = 0; i < 16; i ++) {
        if (settings.Contains("/virtual/" + i.ToString() + "/source")) {
          hardware.Add(new VirtualSensor("Virtual Sensors", i, new Identifier("virtual"), settings));
          break;
        }
      }
    }

    public IHardware[] Hardware {
      get {
        return hardware.ToArray();
      }
    }

    public string GetReport() {
      return report.ToString();
    }

    public void Close() {
    }
  }
}
