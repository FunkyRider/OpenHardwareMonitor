/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2010-2014 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Timers;

namespace OpenHardwareMonitor.Hardware {

  internal delegate void ControlEventHandler(Control control);

  internal class Control : IControl {

    private readonly Identifier identifier;
    private readonly ISettings settings;
    private ISensor parentSensor;
    private ControlMode mode;
    private float softwareValue;
    private float minSoftwareValue;
    private float maxSoftwareValue;

    private float softwareCurveValue;
    private bool softwareCurveAttached;

    public Control(ISensor sensor, ISettings settings, float minSoftwareValue,
      float maxSoftwareValue) 
    {
      this.identifier = new Identifier(sensor.Identifier, "control");
      this.settings = settings;
      this.parentSensor = sensor;
      this.minSoftwareValue = minSoftwareValue;
      this.maxSoftwareValue = maxSoftwareValue;

      if (!float.TryParse(settings.GetValue(
          new Identifier(identifier, "value").ToString(), "0"),
        NumberStyles.Float, CultureInfo.InvariantCulture,
        out this.softwareValue)) 
      {
        this.softwareValue = 0;
      }
      int mode;
      if (!int.TryParse(settings.GetValue(
          new Identifier(identifier, "mode").ToString(),
          ((int)ControlMode.Undefined).ToString(CultureInfo.InvariantCulture)),
        NumberStyles.Integer, CultureInfo.InvariantCulture,
        out mode)) 
      {
        this.mode = ControlMode.Undefined;
      } else {
        this.mode = (ControlMode)mode;
      }
    }

    public Identifier Identifier {
      get {
        return identifier;
      }
    }

    public ControlMode ControlMode {
      get {
        if (mode == ControlMode.SoftwareCurve)
          if (!softwareCurveAttached)
            return ControlMode.Default;
          else
            return ControlMode.Software;
        else
          return mode;
      }
      private set {
        DetachSoftwareCurve();
        if (mode != value) {
          mode = value;
          if (ControlModeChanged != null)
            ControlModeChanged(this);
          this.settings.SetValue(new Identifier(identifier, "mode").ToString(),
            ((int)mode).ToString(CultureInfo.InvariantCulture));
        }
      }
    }
    public ControlMode ActualControlMode {
      get {
        if (mode == ControlMode.SoftwareCurve && !softwareCurveAttached)
          return ControlMode.Default;
        else
          return mode;
      }
    }

    public float SoftwareValue {
      get {
        if (mode == ControlMode.SoftwareCurve)
          return softwareCurveValue;
        else
          return softwareValue;
      }
      private set {
        if (softwareValue != value) {
          softwareValue = value;
          if (SoftwareControlValueChanged != null)
            SoftwareControlValueChanged(this);
          this.settings.SetValue(new Identifier(identifier,
            "value").ToString(),
            value.ToString(CultureInfo.InvariantCulture));
        }
      }
    }

    public void SetDefault() {
      ControlMode = ControlMode.Default;
    }

    // Set control to 100% for sec (Value range: 0 - 60)
    public void SetMaxSpeed(int sec) {
      if (softwareCurve != null) {
        softwareCurve.SetMaxSpeed(sec);
      }
    }

    public float MinSoftwareValue {
      get {
        return minSoftwareValue;
      }
    }

    public float MaxSoftwareValue {
      get {
        return maxSoftwareValue;
      }
    }

    public void SetSoftware(float value) {
      ControlMode = ControlMode.Software;
      SoftwareValue = value;
    }

    internal event ControlEventHandler ControlModeChanged;
    internal event ControlEventHandler SoftwareControlValueChanged;
    string sensorIdentifier;
    string loadSensorIdentifier;
    ISensor loadSensor;
    bool nonSoftwareCurve;
    public void SetSoftwareCurve(List<ISoftwareCurvePoint> points, ISensor sensor, ISensor loadSensor, IFanStopStartValues stopStart) {
      sensorIdentifier = null;
      nonSoftwareCurve = false;
      this.loadSensor = loadSensor;

      ControlMode = ControlMode.SoftwareCurve;
      var softwareCurve = new SoftwareCurve(points, sensor, loadSensor, stopStart);
      AttachSoftwareCurve(softwareCurve);

      this.settings.SetValue(new Identifier(identifier,
              "curveValue").ToString(),
              softwareCurve.ToString());

      Debug.WriteLine("softwareCurve.ToString(): " + softwareCurve.ToString());
    }

    public SoftwareCurve GetSoftwareCurve() {
      return softwareCurve;
    }


    public void NotifyHardwareAdded(List<IGroup> allhardware) {
      if (nonSoftwareCurve || softwareCurve != null)
        return;

      if (sensorIdentifier == null)
        if (!SoftwareCurve.TryParse(settings.GetValue(
            new Identifier(identifier, "curveValue").ToString(), ""),
            out sensorIdentifier,
            out loadSensorIdentifier)) {
          nonSoftwareCurve = true;
          return;
        }

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
      string sensorId = sensor.Identifier.ToString();
      //Debug.WriteLine("Sensor added: " + sensorId);
      if (sensorId == sensorIdentifier) {
        if (softwareCurve != null)
          return;
        var valueString = settings.GetValue(new Identifier(identifier, "curveValue").ToString(), "");
        List<ISoftwareCurvePoint> points;
        if (!SoftwareCurve.TryParse(valueString, out points)) {
          return;
        }

        IFanStopStartValues stopStart;
        if (!SoftwareCurve.TryParse(valueString, out stopStart)) {
          return;
        }

        this.softwareCurve = new SoftwareCurve(points, sensor, loadSensor, stopStart);
        if (loadSensor != null) {
          this.softwareCurve.SetLoadSensor(loadSensor);
        }

        int stepSpeed = 0;
        if (int.TryParse(loadSensorIdentifier, out stepSpeed)) {
          this.softwareCurve.setStepSpeed(stepSpeed);
        }

        Debug.WriteLine("hardware added software curve created");
        if (mode == ControlMode.SoftwareCurve)
          AttachSoftwareCurve(softwareCurve);
      } else if (sensorId == loadSensorIdentifier) {
        loadSensor = sensor;
        if (this.softwareCurve != null) {
          this.softwareCurve.SetLoadSensor(sensor);
        }
      }
    }

    public void NotifyHardwareRemoved(IHardware hardware) {
      if (softwareCurve == null)
        return;

      Debug.WriteLine("notify hardware removed");

      foreach (ISensor sensor in hardware.Sensors)
        if (sensor.Identifier.ToString() == sensorIdentifier) {
          NotifyClosing();
        }

      foreach (IHardware subHardware in hardware.SubHardware)
        NotifyHardwareRemoved(subHardware);
    }

    public void NotifyClosing() {
      if (softwareCurve == null)
        return;

      DetachSoftwareCurve();
      this.softwareCurve.Dispose();
      this.softwareCurve = null;
      if (ControlModeChanged != null)
        ControlModeChanged(this);
      Debug.WriteLine("closing");
    }

    private SoftwareCurve softwareCurve;

    private void AttachSoftwareCurve(SoftwareCurve newCurve) {
      if (this.softwareCurveAttached || this.softwareCurve != null)
        DetachSoftwareCurve();

      this.softwareCurve = newCurve;
      //this.softwareCurve.Sensor.Hardware.SensorRemoved += SensorRemoved;
      //this.parentSensor.Hardware.SensorRemoved += SensorRemoved;
      this.softwareCurve.SoftwareCurveValueChanged += this.HandleSoftwareCurveValueChange;
      this.softwareCurve.SoftwareCurveAbort += this.HandleSoftwareCurveAbort;
      this.softwareCurve.Start();
      this.softwareCurveAttached = true;
      Debug.WriteLine("attaching software curve - " + newCurve);
    }

    private void DetachSoftwareCurve() {
      if (!this.softwareCurveAttached || this.softwareCurve == null)
        return;

      this.softwareCurve.Stop();
      //this.softwareCurve.Sensor.Hardware.SensorRemoved -= SensorRemoved;
      //this.parentSensor.Hardware.SensorRemoved -= SensorRemoved;
      this.softwareCurve.SoftwareCurveValueChanged -= this.HandleSoftwareCurveValueChange;
      this.softwareCurve.SoftwareCurveAbort -= this.HandleSoftwareCurveAbort;
      this.softwareCurveAttached = false;
      Debug.WriteLine("detaching softwarecurve");
    }

    private void HandleSoftwareCurveValueChange(SoftwareCurve softwareCurve) {
      this.softwareCurveValue = softwareCurve.Value;
      //Debug.WriteLine("setting value from software curve: " + softwareCurve.Sensor.Name + " -> " + softwareCurve.Value);
      this.SoftwareControlValueChanged(this);
    }

    private void HandleSoftwareCurveAbort(SoftwareCurve softwareCurve) {
      DetachSoftwareCurve();
      if (ControlModeChanged != null)
        ControlModeChanged(this); // until softwarecurve is started again, get value of ControlMode is Default
      Debug.WriteLine("softwarecurve abort!");
    }
  }

  public class SoftwareCurve {
    internal delegate void SoftwareCurveValueHandler(SoftwareCurve softwareCurve);
    internal event SoftwareCurveValueHandler SoftwareCurveValueChanged;
    internal event SoftwareCurveValueHandler SoftwareCurveAbort;

    public readonly List<ISoftwareCurvePoint> Points;
    public readonly IFanStopStartValues StopStart;
    public readonly ISensor Sensor;
    public ISensor LoadSensor;
    private int maxSpeedCountDown;

    internal static bool TryParse(string settings, out List<ISoftwareCurvePoint> points) {
      points = new List<ISoftwareCurvePoint>();
      if (settings.Length < 1)
        return false;

      var splitPoints = settings.Split(';');
      if (splitPoints.Length < 2)
        return false;

      for (var i = 1; i < splitPoints.Length - 1; i++) {
        var splitPoint = splitPoints[i].Split(':');
        if (splitPoint.Length < 2)
          return false;

        float xpoint;
        if (!float.TryParse(splitPoint[0], NumberStyles.Float, CultureInfo.InvariantCulture, out xpoint))
          return false;

        float ypoint;
        if (!float.TryParse(splitPoint[1], NumberStyles.Float, CultureInfo.InvariantCulture, out ypoint))
          return false;

        points.Add(new SoftwareCurvePoint { SensorValue = xpoint, ControlValue = ypoint });
      }

      if (points.Count < 2)
        return false;

      return true;
    }

    internal static bool TryParse(string settings, out IFanStopStartValues stopStart) {
      stopStart = new FanStopStartValues();
      var splitPoints = settings.Split(';');
      if (splitPoints.Length < 1)
        return false;

      var splitPoint = splitPoints[0].Split('!');
      if (splitPoint.Length < 2)
        return false;

      float xpoint;
      if (!float.TryParse(splitPoint[0], NumberStyles.Float, CultureInfo.InvariantCulture, out xpoint))
        return false;

      float ypoint;
      if (!float.TryParse(splitPoint[1], NumberStyles.Float, CultureInfo.InvariantCulture, out ypoint))
        return false;

      stopStart.StopTemp = xpoint;
      stopStart.StartTemp = ypoint;
      return true;
    }

    internal static bool TryParse(string settings, out string sensorIdentifier, out string loadSensorIdentifier) {
      sensorIdentifier = null;
      loadSensorIdentifier = null;

      if (settings.Length < 1)
        return false;

      var split = settings.Split(';');
      if (split.Length < 1)
        return false;

      string[] sensors = split[split.Length - 1].Split(',');

      sensorIdentifier = sensors[0];
      loadSensorIdentifier = sensors.Length > 1 ? sensors[1] : null;

      return !string.IsNullOrEmpty(sensorIdentifier);
    }
    internal static ISensor FindSensor(IHardware hardware, string sensorIdentifier) {
      foreach (ISensor sensor in hardware.Sensors) {
        Debug.WriteLine(sensor.Identifier.ToString() + " " + sensorIdentifier);
        if (sensor.Identifier.ToString() == sensorIdentifier)
          return sensor;
      }
      foreach (IHardware subHardware in hardware.SubHardware)
        return FindSensor(subHardware, sensorIdentifier);

      return null;
    }

    internal SoftwareCurve(List<ISoftwareCurvePoint> points, ISensor sensor, ISensor loadSensor, IFanStopStartValues stopStart) {
      this.Points = points;
      this.Sensor = sensor;
      this.LoadSensor = loadSensor;
      this.StopStart = stopStart;
      if (this.Sensor.Identifier.ToString().IndexOf("virtual") > 0) {
        this.stepSpeed = 1;
      }
    }

    private float targetValue;
    private float stableValue;
    private int stableCount;
    private float previousSensorValue;
    private byte previousNoValue;
    // fanStatus: 0 - Stopped, 1 - Running, -1 - Indeterminate
    private int fanStatus;
    private bool started = false;
    private int stepSpeed = 0;

    internal float Value { get; private set; }
    internal void Start() {
      if (!started) {
        fanStatus = -1;
        stableValue = -1000.0f;
        stableCount = 0;
        previousNoValue = 0;
        previousSensorValue = -1000.0f;
        maxSpeedCountDown = 0;
        started = true;
        ControlTimer.Instance.addHandler(Tick);
      }
    }

    public void SetLoadSensor(ISensor sensor) {
      this.LoadSensor = sensor;
    }

    public void setStepSpeed(int speed) {
      this.stepSpeed = speed;
    }

    // Set control to 100% for sec (Value range: 0 - 60)
    public void SetMaxSpeed(int sec) {
      if (sec < 0) {
        sec = 0;
      } else if (sec > 60) {
        sec = 60;
      }
      maxSpeedCountDown = sec;
    }

    private void Tick(object s, ElapsedEventArgs e) {
      bool fanStateChanged = false;
      bool fanRampDown = false;

      if (maxSpeedCountDown > 0) {
        maxSpeedCountDown--;
        if (Value != 100) {
          Value = 100;
          fanStateChanged = true;
          SoftwareCurveValueChanged(this);
        }
        if (maxSpeedCountDown == 0) {
          fanRampDown = true;
        } else {
          return;
        }
      }

      float? sensorValue = null;
      if (Sensor != null) {
        if (stepSpeed > 0) {
          sensorValue = Sensor.Value;
        } else {
          sensorValue = Sensor.AverageValue;
        }
      }

      if (sensorValue.HasValue) {
        if (stableValue == sensorValue && stableCount < 10) {
          stableCount++;
        } else {
          stableValue = sensorValue.Value;
          stableCount = 0;
        }
        // Hysteresis of +/-1C to prevent fan speed fluctuating.
        // A stable value of 10 consecutive samples within hystersis also forces an update.
        if (previousSensorValue < sensorValue - 1 || previousSensorValue > sensorValue + 1 || (previousSensorValue != sensorValue && stableCount >= 10)) {
          previousSensorValue = sensorValue.Value;
          if (StopStart.StartTemp != 0 && StopStart.StopTemp != 0 && StopStart.StartTemp >= StopStart.StopTemp) {
            if (fanStatus != 1 && sensorValue > StopStart.StartTemp) {
              fanStatus = 1;
              fanStateChanged = true;
            } else if (fanStatus != 0 && sensorValue < StopStart.StopTemp) {
              fanStatus = 0;
              fanStateChanged = true;
            }
          } else {
            fanStatus = -1;
          }
          // As of writing this, a Control is controlled with percentages. Round away decimals
          targetValue = (fanStatus == 0) ? 0.0f : (float)Math.Round(Calculate(sensorValue.Value));
        }

        if (Value != targetValue || fanStateChanged) {
          if (fanRampDown) {
            Value = targetValue;
          } else if (LoadSensor != null && Value - targetValue > 1 && LoadSensor.Value > 20) {
            Value -= 0.1f;
          } else if (Value - targetValue > 20 && this.stepSpeed == 0) {
            Value -= 5;
          } else if (Value - targetValue > 10 && this.stepSpeed == 0) {
            Value -= 2;
          } else if (Value - targetValue > 1 && this.stepSpeed == 0) {
            Value -= 0.5f;
          } else {
            Value = targetValue;
          }
          SoftwareCurveValueChanged(this);
        }
      } else {
        previousNoValue++;

        if (previousNoValue > 3)
          SoftwareCurveAbort(this);
      }
    }
    private float Calculate(float SensorValue) {

      for (int i = 1; i < Points.Count; i++) {
        var Point1 = Points[i - 1];
        var Point2 = Points[i];

        if (SensorValue == Point1.SensorValue)
          return Point1.ControlValue;

        if (SensorValue > Point1.SensorValue && SensorValue < Point2.SensorValue) {
          var m = (Point2.ControlValue - Point1.ControlValue) / (Point2.SensorValue - Point1.SensorValue);
          var b = Point1.ControlValue - (m * Point1.SensorValue);

          return m * SensorValue + b;
        }
      }

      if (SensorValue <= Points[0].SensorValue)
        return Points[0].ControlValue;

      if (SensorValue >= Points[Points.Count - 1].SensorValue)
        return Points[Points.Count - 1].ControlValue;

      return -1;
    }
    internal void Stop() {
      if (started) {
        ControlTimer.Instance.removeHandler(Tick);
        started = false;
      }
    }
    internal void Dispose() {
      Stop();
    }
    public override string ToString() {
      StringBuilder builder = new StringBuilder();

      // Fan Stop/Start threshold temps
      builder.Append(StopStart.StopTemp.ToString(CultureInfo.InvariantCulture));
      builder.Append('!');
      builder.Append(StopStart.StartTemp.ToString(CultureInfo.InvariantCulture));
      builder.Append(';');

      // Fan control curve temp/pwm points
      foreach (var point in Points) {
        builder.Append(Math.Round(point.SensorValue, 1).ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(Math.Round(point.ControlValue, 1).ToString(CultureInfo.InvariantCulture));
        builder.Append(';');
      }

      // Sensor source
      builder.Append(Sensor.Identifier.ToString());
      if (LoadSensor != null) {
        builder.Append(',');
        builder.Append(LoadSensor.Identifier.ToString());
      } else if (stepSpeed > 0) {
        builder.Append(',');
        builder.Append(stepSpeed.ToString());
      }

      return builder.ToString();
    }

    internal class SoftwareCurvePoint : ISoftwareCurvePoint {
      public float SensorValue { get; set; }
      public float ControlValue { get; set; }
    }

    internal class FanStopStartValues : IFanStopStartValues {
      public float StopTemp { get; set; }
      public float StartTemp { get; set; }
    }
  }
}
