﻿/*
 
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
    bool nonSoftwareCurve;
    public void SetSoftwareCurve(List<ISoftwareCurvePoint> points, ISensor sensor, IFanStopStartValues stopStart) {
      sensorIdentifier = null;
      nonSoftwareCurve = false;

      ControlMode = ControlMode.SoftwareCurve;
      var softwareCurve = new SoftwareCurve(points, sensor, stopStart);
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
            out sensorIdentifier)) {
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
      if (softwareCurve != null)
        return;

      if (sensor.Identifier.ToString() == sensorIdentifier) {
        var valueString = settings.GetValue(new Identifier(identifier, "curveValue").ToString(), "");
        List<ISoftwareCurvePoint> points;
        if (!SoftwareCurve.TryParse(valueString, out points)) {
          return;
        }

        IFanStopStartValues stopStart;
        if (!SoftwareCurve.TryParse(valueString, out stopStart)) {
          return;
        }

        this.softwareCurve = new SoftwareCurve(points, sensor, stopStart);
        Debug.WriteLine("hardware added software curve created");
        if (mode == ControlMode.SoftwareCurve)
          AttachSoftwareCurve(softwareCurve);
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
      Debug.WriteLine("attaching softwarecurve");
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
      Debug.WriteLine("setting value from software curve: " + softwareCurve.Value);
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

    internal static bool TryParse(string settings, out string sensorIdentifier) {
      sensorIdentifier = null;

      if (settings.Length < 1)
        return false;

      var split = settings.Split(';');
      if (split.Length < 1)
        return false;

      sensorIdentifier = split[split.Length - 1];

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

    internal SoftwareCurve(List<ISoftwareCurvePoint> points, ISensor sensor, IFanStopStartValues stopStart) {
      this.Points = points;
      this.Sensor = sensor;
      this.StopStart = stopStart;
    }

    private Timer timer;
    private float targetValue;
    private float stableValue;
    private int stableCount;
    private float previousSensorValue;
    private byte previousNoValue;
    // fanStatus: 0 - Stopped, 1 - Running, -1 - Indeterminate
    private int fanStatus;
    internal float Value { get; private set; }
    internal void Start() {
      if (timer == null)
        timer = new Timer();
      else if (timer.Enabled)
        return;

      fanStatus = -1;
      stableValue = -1000.0f;
      stableCount = 0;
      previousNoValue = 0;
      previousSensorValue = -1000.0f;
      timer.Elapsed += Tick;
      timer.Interval = 1000;
      timer.Start();
    }
    private void Tick(object s, ElapsedEventArgs e) {
      if (Sensor != null && Sensor.AverageValue.HasValue) {
        float sensorValue = Sensor.AverageValue.Value;
        bool fanStateChanged = false;

        if (stableValue == sensorValue && stableCount < 10) {
          stableCount++;
        } else {
          stableValue = sensorValue;
          stableCount = 0;
        }
        // Hysteresis of +/-1C to prevent fan speed fluctuating.
        // A stable value of 10 consecutive samples within hystersis also forces an update.
        if (previousSensorValue < sensorValue - 1 || previousSensorValue > sensorValue + 1 || (previousSensorValue != sensorValue && stableCount >= 10)) {
          previousSensorValue = sensorValue;
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
          targetValue = (fanStatus == 0) ? 0.0f : (float)Math.Round(Calculate(sensorValue));
        }

        if (Value != targetValue || fanStateChanged) {
          if (Value - targetValue > 50) {
            Value -= 5;
          } else if (Value - targetValue > 10) {
            Value -= 2;
          } else if (Value - targetValue > 1) {
            Value--;
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
      if (timer != null) {
        timer.Stop();
        timer.Elapsed -= Tick;
      }
    }
    internal void Dispose() {
      Stop();
      if (timer != null)
        timer.Dispose();
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
        builder.Append(point.SensorValue.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(point.ControlValue.ToString(CultureInfo.InvariantCulture));
        builder.Append(';');
      }

      // Sensor source
      builder.Append(Sensor.Identifier.ToString());

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
