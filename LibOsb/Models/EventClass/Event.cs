﻿using System;
using LibOsb.Enums;

namespace LibOsb.Models.EventClass
{
    /// <summary>
    /// Parent class of all actions. Should not be instantiated directly.
    /// </summary>
    public abstract class Event
    {
        public double StartTime { get; internal set; }
        public double EndTime { get; internal set; }
        public EasingType Easing { get; protected set; }
        public string Type { get; protected set; }
        public abstract string ScriptParams { get; }
        // 扩展属性
        public abstract bool IsStatic { get; }

        public override string ToString()
        {
            return string.Join(",", Type, (int)System.Enum.Parse(typeof(EasingType), Easing.ToString()),
                Math.Round(StartTime), StartTime.Equals(EndTime) ? "" : ((int)Math.Round(EndTime)).ToString(),
                ScriptParams);
        }

        internal void _AdjustTime(int time)
        {
            StartTime += time;
            EndTime += time;
        }
    }
}