/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2012 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using OpenHardwareMonitor.Collections;

namespace OpenHardwareMonitor.Hardware {

  internal class Sensor : ISensor {

    private readonly string defaultName;
    private string name;
    private readonly int index;
    private readonly bool defaultHidden;
    private readonly SensorType sensorType;
    private readonly Hardware hardware;
    private readonly ReadOnlyArray<IParameter> parameters;
    private float?[] previousValues = new float?[5];
    private float? currentValue;
    private float? minValue;
    private float? maxValue;
    private readonly RingCollection<SensorValue> 
      values = new RingCollection<SensorValue>();
    private readonly ISettings settings;
    private IControl control;
    
    private float sum;
    private int count;
   
    public Sensor(string name, int index, SensorType sensorType,
      Hardware hardware, ISettings settings) : 
      this(name, index, sensorType, hardware, null, settings) { }

    public Sensor(string name, int index, SensorType sensorType,
      Hardware hardware, ParameterDescription[] parameterDescriptions, 
      ISettings settings) :
      this(name, index, false, sensorType, hardware,
        parameterDescriptions, settings) { }

    public Sensor(string name, int index, bool defaultHidden, 
      SensorType sensorType, Hardware hardware, 
      ParameterDescription[] parameterDescriptions, ISettings settings) 
    {           
      this.index = index;
      this.defaultHidden = defaultHidden;
      this.sensorType = sensorType;
      this.hardware = hardware;
      Parameter[] parameters = new Parameter[parameterDescriptions == null ?
        0 : parameterDescriptions.Length];
      for (int i = 0; i < parameters.Length; i++ ) 
        parameters[i] = new Parameter(parameterDescriptions[i], this, settings);
      this.parameters = parameters;

      this.settings = settings;
      this.defaultName = name; 
      this.name = settings.GetValue(
        new Identifier(Identifier, "name").ToString(), name);

      GetSensorValuesFromSettings();      

      hardware.Closing += delegate(IHardware h) {
        SetSensorValuesToSettings();
        if (control != null) {
          control.NotifyClosing();
        }
      };
    }

    private void SetSensorValuesToSettings() {/*
      using (MemoryStream m = new MemoryStream()) {
        using (GZipStream c = new GZipStream(m, CompressionMode.Compress))
        using (BufferedStream b = new BufferedStream(c, 65536))
        using (BinaryWriter writer = new BinaryWriter(b)) {
          long t = 0;
          foreach (SensorValue sensorValue in values) {
            long v = sensorValue.Time.ToBinary();
            writer.Write(v - t);
            t = v;
            writer.Write(sensorValue.Value);
          }
          writer.Flush();
        }
        settings.SetValue(new Identifier(Identifier, "values").ToString(),
          Convert.ToBase64String(m.ToArray()));
      }*/
      settings.SetValue(new Identifier(Identifier, "values").ToString(), "");
    }

    private void GetSensorValuesFromSettings() {
      string name = new Identifier(Identifier, "values").ToString();
      string s = settings.GetValue(name, null);

      try {
        byte[] array = Convert.FromBase64String(s);
        s = null;
        DateTime now = DateTime.UtcNow;
        using (MemoryStream m = new MemoryStream(array))
        using (GZipStream c = new GZipStream(m, CompressionMode.Decompress))
        using (BinaryReader reader = new BinaryReader(c)) {
          try {
            long t = 0;
            while (true) {
              t += reader.ReadInt64();
              DateTime time = DateTime.FromBinary(t);
              if (time > now)
                break;
              float value = reader.ReadSingle();
              AppendValue(value, time);
            }
          } catch (EndOfStreamException) { }
        }
      } catch { }
      if (values.Count > 0)
        AppendValue(float.NaN, DateTime.UtcNow);

      // remove the value string from the settings to reduce memory usage
      settings.Remove(name);
    }

    private void AppendValue(float value, DateTime time) {
      if (values.Count >= 2 && values.Last.Value == value && 
        values[values.Count - 2].Value == value) {
        values.Last = new SensorValue(value, time);
        return;
      } 

      values.Append(new SensorValue(value, time));
    }

    public IHardware Hardware {
      get { return hardware; }
    }

    public SensorType SensorType {
      get { return sensorType; }
    }

    public Identifier Identifier {
      get {
        return new Identifier(hardware.Identifier,
          sensorType.ToString().ToLowerInvariant(),
          index.ToString(CultureInfo.InvariantCulture));
      }
    }

    public string Name {
      get { 
        return name; 
      }
      set {
        if (!string.IsNullOrEmpty(value)) 
          name = value;          
        else 
          name = defaultName;
        settings.SetValue(new Identifier(Identifier, "name").ToString(), name);
      }
    }

    public int Index {
      get { return index; }
    }

    public bool IsDefaultHidden {
      get { return defaultHidden; }
    }

    public IReadOnlyArray<IParameter> Parameters {
      get { return parameters; }
    }

    public float? SmoothValue {
      get {
        int count = (this.currentValue != null) ? 1 : 0;
        float velocity = 0;
        float? total = this.currentValue;

        for (var i = 0; i < this.previousValues.Length; i ++) {
          if (this.previousValues[i] == null) {
            break;
          }
          float v = this.previousValues[i].Value;
          if (total == null) {
            total = v;
            count = 1;
          } else {
            if (i == 0) {
              velocity += (this.currentValue.Value - v);
            } else {
              velocity += (this.previousValues[i - 1].Value - v);
            }
            total += v;
            count++;
          }
        }

        // Average window depends on velocity. Faster change results in smaller average window so response is better.
        if (count > 0) {
          velocity /= count;
        }

        double raw = Math.Round(this.currentValue.Value / 10.0f) * 10;
        double smoothed = (count > 0) ? (Math.Round(total.Value / count / 10.0f) * 10) : 0;
        return (float?)((Math.Abs(velocity) > 10) ? raw : smoothed);
      }
    }

    public float? Value {
      get { 
        return currentValue; 
      }
      set {
        DateTime now = DateTime.UtcNow;
        while (values.Count > 0 && (now - values.First.Time).TotalDays > 1)
          values.Remove();

        if (value.HasValue) {
          sum += value.Value;
          count++;
          if (count == 4) {
            AppendValue(sum / count, now);
            sum = 0;
            count = 0;
          }
        }

        if (this.sensorType == SensorType.Fan || this.sensorType == SensorType.Temperature) {
          this.addPreviousValue(this.currentValue);
        }
        this.currentValue = value;
        if (minValue > value || !minValue.HasValue)
          minValue = value;
        if (maxValue < value || !maxValue.HasValue)
          maxValue = value;
      }
    }

    public float? AverageValue {
      get {
        int count = (this.currentValue != null) ? 1 : 0;
        float? total = this.currentValue;
        for (var i = 0; i < this.previousValues.Length; i++) {
          if (this.previousValues[i] == null) {
            break;
          }
          float v = this.previousValues[i].Value;
          if (total == null) {
            total = v;
            count = 1;
          } else {
            total += v;
            count++;
          }
        }
        return total / count;
      }
    }

    private void addPreviousValue(float? value) {
      for (int i = this.previousValues.Length - 1; i > 0; i--) {
        this.previousValues[i] = this.previousValues[i - 1];
      }
      this.previousValues[0] = value;
    }

    public float? Min { get { return minValue; } }
    public float? Max { get { return maxValue; } }

    public void ResetMin() {
      minValue = null;
    }

    public void ResetMax() {
      maxValue = null;
    }

    public IEnumerable<SensorValue> Values {
      get { return values; }
    }    

    public void Accept(IVisitor visitor) {
      if (visitor == null)
        throw new ArgumentNullException("visitor");
      visitor.VisitSensor(this);
    }

    public void Traverse(IVisitor visitor) {
      foreach (IParameter parameter in parameters)
        parameter.Accept(visitor);
    }

    public IControl Control {
      get {
        return control;
      }
      internal set {
        this.control = value;
      }
    }
  }
}
