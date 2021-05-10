using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OpenHardwareMonitor.Hardware.Virtual {
  class VirtualSensor : Hardware {
    private readonly List<VirtualSensorItem> sensors;

    public VirtualSensor(string name, int index, Identifier identifier, ISettings settings)
      : base(name, new Identifier("virtual", index.ToString(CultureInfo.InvariantCulture)), settings) {
      sensors = new List<VirtualSensorItem>();

      for (int i = 0; i < 16; i++) {
        string key = "/virtual/" + i.ToString() + "/source";
        if (settings.Contains(key)) {
          sensors.Add(new VirtualSensorItem("Virtual " + i.ToString(), i, SensorType.Temperature, this, settings));
        }
      }
    }

    public override HardwareType HardwareType {
      get { return HardwareType.Virtual; }
    }

    public override void Update() {
      foreach (VirtualSensorItem s in sensors) {
        s.update();
        ActivateSensor(s);
      }
    }

    private enum Combiner {
      sum,
      max
    }

    private class VirtualSensorItem : Sensor {
      private Combiner combiner;
      private List<ISensor> sources;
      private List<String> identifiers;
      public VirtualSensorItem(string name, int index, SensorType type, Hardware hardware, ISettings settings)
        : base(name, index, type, hardware, settings) {
        string key = "/virtual/" + index.ToString() + "/source";
        var value = settings.GetValue(key, "");
        var data = value.Split(';');
        combiner = (Combiner)Enum.Parse(typeof(Combiner), data[0]);
        sources = new List<ISensor>();
        identifiers = new List<String>();

        for (int i = 1; i < data.Length; i++) {
          identifiers.Add(data[i]);
          sources.Add(null);
        }
      }

      public void update() {
        float? value = 0;
        foreach (ISensor s in sources) {
          if (s == null) {
            return;
          }
          if (combiner == Combiner.sum) {
            value += s.Value;
          } else if (combiner == Combiner.max) {
            if (value < s.Value) {
              value = s.Value;
            }
          }
        }
        Value = value;
      }

      public override void NotifyHardwareAdded(List<IGroup> allhardware) {
        foreach (var group in allhardware)
          foreach (var hardware in group.Hardware)
            HardwareAdded(hardware);
      }

      private void HardwareAdded(IHardware hardware) {
        hardware.SensorAdded += SensorAdded;

        foreach (ISensor sensor in hardware.Sensors)
          SensorAdded(sensor);

        foreach (IHardware subHardware in hardware.SubHardware)
          HardwareAdded(subHardware);
      }

      private void SensorAdded(ISensor sensor) {
        for (int i = 0; i < identifiers.Count; i++) {
          if (sensor.Identifier.ToString() == identifiers[i]) {
            sources[i] = sensor;
          }
        }
      }
    }
  }
}
