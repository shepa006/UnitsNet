﻿using System;
using UnitsNet.Attributes;

namespace UnitsNet.Units
{
    public enum AngleUnit
    {
        [I18n("en-US", "(undefined)")]
        [I18n("ru-RU", "(нет ед.изм.)")]
        [I18n("nb-NO", "(ingen)")]
        Undefined = 0,
        
        // Metric

        [I18n("en-US", "rad")]
        [I18n("ru-RU", "рад")]
        [Angle(180 / Math.PI)]
        Radian,

        [I18n("en-US", "°")]
        [I18n("ru-RU", "°")]
        [Angle(1)]
        Degree, // Base unit

        [I18n("en-US", "g")]
        [I18n("ru-RU", "g")]
        [Angle(0.9)]
        Gradian,
    }
}