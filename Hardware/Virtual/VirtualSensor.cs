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
      max,
      min,
      avg
    }

    private class BiasScale {
      public float bias;
      public float scale;
    }

    private class VirtualSensorItem : Sensor {
      private Combiner combiner;
      private List<ISensor> sources;
      private List<String> identifiers;
      private List<BiasScale> biasScales;
      private float? average;

      public VirtualSensorItem(string name, int index, SensorType type, Hardware hardware, ISettings settings)
        : base(name, index, type, hardware, settings) {
        string key = "/virtual/" + index.ToString() + "/source";
        var value = settings.GetValue(key, "");
        var data = value.Split(';');
        combiner = (Combiner)Enum.Parse(typeof(Combiner), data[0]);
        sources = new List<ISensor>();
        identifiers = new List<String>();
        biasScales = new List<BiasScale>();
        average = new float?();

        for (int i = 1; i < data.Length; i++) {
          var id = data[i].Split(',');
          BiasScale bs = new BiasScale();
          bs.bias = (id.Length > 1) ? float.Parse(id[1]) : 0;
          bs.scale = (id.Length > 2) ? float.Parse(id[2]) : 1;
          identifiers.Add(id[0]);
          sources.Add(null);
          biasScales.Add(bs);
        }
      }

      public void update() {
        float? value = 0;
        if (combiner == Combiner.avg) {
          ISensor s = sources[0];
          BiasScale bs = biasScales[0];
          if (!average.HasValue) {
            average = s.Value;
          }
          if (average.HasValue) {
            if (bs.bias == 0) {
              bs.bias = 1.0f;
            }
            float weight = 1.0f / bs.bias;
            average = average * (1.0f - weight) + s.Value * weight;
            value = average;
          }
        } else {
          for (int i = 0; i < sources.Count; i++) {
            ISensor s = sources[i];
            BiasScale bs = biasScales[i];
            if (s == null) {
              return;
            }
            float? bsValue = (s.Value + bs.bias) * bs.scale;
            if (bsValue < 0) {
              bsValue = 0;
            }
            if (combiner == Combiner.sum) {
              value += bsValue;
            } else if (combiner == Combiner.max) {
              if (value < bsValue) {
                value = bsValue;
              }
            } else if (combiner == Combiner.min) {
              if (value > bsValue) {
                value = bsValue;
              }
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
