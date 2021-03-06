﻿using UnitsNet.Attributes;

namespace UnitsNet.Units
{
    public enum RotationalSpeedUnit
    {
        [I18n("en-US", "(undefined)")]
        [I18n("ru-RU", "(нет ед.изм.)")]
        [I18n("nb-NO", "(ingen)")]
        Undefined = 0, 

        [I18n("en-US", "r/s")]
        [I18n("ru-RU", "об/с")]
        [RotationalSpeed(1, "RevolutionsPerSecond")]
        RevolutionPerSecond,

        [I18n("en-US", "rpm", "r/min")]
        [I18n("ru-RU", "об/мин")]
        [RotationalSpeed(1.0 / 60, "RevolutionsPerMinute")]
        RevolutionPerMinute,
    }
}