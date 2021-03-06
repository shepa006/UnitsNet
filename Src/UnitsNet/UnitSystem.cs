﻿// Copyright © 2007 by Initial Force AS.  All rights reserved.
// https://github.com/InitialForce/SIUnits
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnitsNet.Attributes;
using UnitsNet.Templating;

namespace UnitsNet
{
    public class UnitSystem
    {
        private static readonly Dictionary<CultureInfo, UnitSystem> CultureToInstance;
        private static readonly List<Assembly> AssembliesList;
        private static readonly object ExternalAssembliesSync = new object();

        /// <summary>
        /// Per-unit-type dictionary of enum values by abbreviation. This is the inverse of <see cref="_unitTypeToUnitValueToAbbrevs"/>.
        /// </summary>
        private readonly Dictionary<Type, Dictionary<string, int>> _unitTypeToAbbrevToUnitValue;

        /// <summary>
        /// Per-unit-type dictionary of abbreviations by enum value. This is the inverse of <see cref="_unitTypeToAbbrevToUnitValue"/>.
        /// </summary>
        private readonly Dictionary<Type, Dictionary<int, List<string>>> _unitTypeToUnitValueToAbbrevs;

        /// <summary>
        ///     Create a SI system for parsing and generating strings of the specified culture.
        ///     If null is specified, the default English US culture will be used.
        /// </summary>
        /// <param name="cultureInfo"></param>
        /// <param name="assemblies">Optional external assemblies with unit enum types tagged with attributes of type <see cref="IUnitAttribute"/> and <see cref="I18nAttribute"/>.</param>
        public UnitSystem(CultureInfo cultureInfo, params Assembly[] assemblies)
        {
            if (cultureInfo == null)
                cultureInfo = new CultureInfo("en-US");

            if (assemblies == null || assemblies.Length == 0)
                assemblies = new[] {Assembly.GetExecutingAssembly()};
 
            Culture = cultureInfo;
            _unitTypeToUnitValueToAbbrevs = new Dictionary<Type, Dictionary<int, List<string>>>();
            _unitTypeToAbbrevToUnitValue = new Dictionary<Type, Dictionary<string, int>>();

            IEnumerable<Type> unitEnumTypes = TemplateUtils.GetUnitEnumTypes(assemblies);
            foreach (Type unitEnumType in unitEnumTypes)
            {
                Dictionary<int, I18nAttribute[]> attributesByValue =
                    TemplateUtils.GetI18nAttributesByUnitEnumValue(unitEnumType);

                foreach (KeyValuePair<int, I18nAttribute[]> pair in attributesByValue)
                {
                    // Fall back to US English if localization attribute is not found for this culture.
                    int unitEnumValue = pair.Key;
                    I18nAttribute[] i18NAttributes = pair.Value;
                    I18nAttribute attr =
                        i18NAttributes.FirstOrDefault(a => a.Culture.Equals(cultureInfo)) ??
                        i18NAttributes.FirstOrDefault(a => a.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase));

                    if (attr == null)
                        continue;

                    MapUnitToAbbreviation(unitEnumType, unitEnumValue, attr.Abbreviations);
                }
            } 
        }

        #region Static

        /// <summary>
        ///     The culture of which this unit system is based on. Either passed in to constructor or the default culture.
        /// </summary>
        public readonly CultureInfo Culture;


        static UnitSystem()
        {
            CultureToInstance = new Dictionary<CultureInfo, UnitSystem>();
            AssembliesList = new List<Assembly> {Assembly.GetExecutingAssembly()};
        }

        /// <summary>
        /// List of assemblies to search for units.
        /// </summary>
        public static ReadOnlyCollection<Assembly> Assemblies
        {
            get
            {
                lock (ExternalAssembliesSync)
                {
                    return new ReadOnlyCollection<Assembly>(AssembliesList);
                }
            }
        }

        /// <summary>
        /// Include third party units by adding an external assembly that contains unit enum types tagged with attributes
        /// of types <see cref="IUnitAttribute"/> and <see cref="I18nAttribute"/>.
        /// Must be done before calling <see cref="GetCached"/> the first time, as the instance is cached.
        /// </summary>
        /// <param name="assembly"></param>
        public static void AddAssembly(Assembly assembly)
        {
            lock (ExternalAssembliesSync)
            {
                if (!AssembliesList.Contains(assembly))
                    AssembliesList.Add(assembly);
            }
        }

        /// <summary>
        /// Remove an assembly to exclude units defined in that assembly.
        /// Must be done before calling <see cref="GetCached"/> the first time, as the instance is cached.
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns></returns>
        public static bool RemoveAssembly(Assembly assembly)
        {
            lock (ExternalAssembliesSync)
            {
                return AssembliesList.Remove(assembly);
            }
        }

        public static void ClearCache()
        {
            lock (ExternalAssembliesSync)
            {
                CultureToInstance.Clear();
            }
        }

        /// <summary>
        /// Get or create a unit system for parsing and presenting numbers, units and abbreviations.
        /// Creating can be a little expensive, so it will use a static cache.
        /// To always create, use the constructor.
        /// </summary>
        /// <param name="cultureInfo">Culture to use. If null then <see cref="CultureInfo.CurrentUICulture" /> will be used.</param>
        /// <param name="externalAssemblies">External assemblies to look for </param>
        /// <returns></returns>
        public static UnitSystem GetCached(CultureInfo cultureInfo = null, params Assembly[] externalAssemblies) 
        {
            if (cultureInfo == null)
                cultureInfo = CultureInfo.CurrentUICulture;

            var allAssemblies = new Assembly[0]
                .Concat(Assemblies)
                .Concat(externalAssemblies)
                .Distinct()
                .ToArray();

            if (!CultureToInstance.ContainsKey(cultureInfo))
                CultureToInstance[cultureInfo] = new UnitSystem(cultureInfo, allAssemblies);

            return CultureToInstance[cultureInfo];
        }

        #endregion

        #region Public methods

        public static TUnit Parse<TUnit>(string unitAbbreviation, CultureInfo culture)
            where TUnit : /*Enum constraint hack*/ struct, IComparable, IFormattable
        {
            return GetCached(culture).Parse<TUnit>(unitAbbreviation);
        }

        public TUnit Parse<TUnit>(string unitAbbreviation)
            where TUnit : /*Enum constraint hack*/ struct, IComparable, IFormattable
        {
            Type unitType = typeof (TUnit);
            Dictionary<string, int> abbrevToUnitValue;
            if (!_unitTypeToAbbrevToUnitValue.TryGetValue(unitType, out abbrevToUnitValue))
                throw new NotImplementedException(string.Format("No abbreviations defined for unit type [{0}] for culture [{1}].", unitType, Culture.EnglishName));

            int unitValue;
            TUnit result = abbrevToUnitValue.TryGetValue(unitAbbreviation, out unitValue)
                ? (TUnit) (object) unitValue
                : default(TUnit);

            return result;
        }

        public static string GetDefaultAbbreviation<TUnit>(TUnit unit, CultureInfo culture)
            where TUnit : /*Enum constraint hack*/ struct, IComparable, IFormattable
        {
            return GetCached(culture).GetDefaultAbbreviation(unit);
        }

        public string GetDefaultAbbreviation<TUnit>(TUnit unit)
            where TUnit : /*Enum constraint hack*/ struct, IComparable, IFormattable
        {
            return GetAllAbbreviations(unit).First();
        }

        #endregion

        #region Private helpers

        private void CreateUsEnglish()
        {
            CreateCultureInvariants();

            // Cooking units
            //MapUnitToAbbreviation(Unit.Tablespoon, "Tbsp", "Tbs", "T", "tb", "tbs", "tbsp", "tblsp", "tblspn", "Tbsp.", "Tbs.", "T.", "tb.", "tbs.", "tbsp.", "tblsp.", "tblspn.", "tablespoon", "Tablespoon");
            //MapUnitToAbbreviation(Unit.Teaspoon, "tsp","t", "ts", "tspn", "t.", "ts.", "tsp.", "tspn.", "teaspoon");
            //MapUnitToAbbreviation(Unit.Piece, "piece", "pieces", "pcs", "pcs.", "pc", "pc.", "pce", "pce.");
        }

        private void CreateNorwegianBokmaal()
        {
            CreateCultureInvariants();

            // Cooking units
            //MapUnitToAbbreviation(Unit.Tablespoon, "ss", "ss.", "SS", "SS.");
            //MapUnitToAbbreviation(Unit.Teaspoon, "ts", "ts.");
            //MapUnitToAbbreviation(Unit.Piece, "stk", "stk.", "x");
        }

        private void CreateRussian()
        {
            // Note: For units with multiple abbreviations, the first one is used in GetDefaultAbbreviation().
            //MapUnitToAbbreviation(Unit.Undefined, "(нет ед.изм.)");

            //// Length
            //MapUnitToAbbreviation(Unit.Kilometer, "км");
            //MapUnitToAbbreviation(Unit.Meter, "м");
            //MapUnitToAbbreviation(Unit.Decimeter, "дм");
            //MapUnitToAbbreviation(Unit.Centimeter, "см");
            //MapUnitToAbbreviation(Unit.Millimeter, "мм");
            //MapUnitToAbbreviation(Unit.Micrometer, "мкм");
            //MapUnitToAbbreviation(Unit.Nanometer, "нм");

            //// Length (imperial)
            //MapUnitToAbbreviation(Unit.Microinch, "микродюйм");
            //MapUnitToAbbreviation(Unit.Mil, "мил");
            //MapUnitToAbbreviation(Unit.Mile, "миля");
            //MapUnitToAbbreviation(Unit.Yard, "ярд");
            //MapUnitToAbbreviation(Unit.Foot, "фут");
            //MapUnitToAbbreviation(Unit.Inch, "дюйм", "\"");

            // Masses
            //MapUnitToAbbreviation(Unit.Megatonne, "Мт");
            //MapUnitToAbbreviation(Unit.Kilotonne, "кт");
            //MapUnitToAbbreviation(Unit.Tonne, "т");
            //MapUnitToAbbreviation(Unit.Kilogram, "кг");
            //MapUnitToAbbreviation(Unit.Hectogram, "гг");
            //MapUnitToAbbreviation(Unit.Decagram, "даг");
            //MapUnitToAbbreviation(Unit.Gram, "г");
            //MapUnitToAbbreviation(Unit.Decigram, "дг");
            //MapUnitToAbbreviation(Unit.Centigram, "сг");
            //MapUnitToAbbreviation(Unit.Milligram, "мг");
            //MapUnitToAbbreviation(Unit.Microgram, "мкг");
            //MapUnitToAbbreviation(Unit.Nanogram, "нг");

            //// Mass (imperial)
            //MapUnitToAbbreviation(Unit.ShortTon, "тонна малая");
            //MapUnitToAbbreviation(Unit.LongTon, "тонна большая");

            // Pressures
            //MapUnitToAbbreviation(Unit.Pascal, "Па");
            //MapUnitToAbbreviation(Unit.Kilopascal, "кПа");
            //MapUnitToAbbreviation(Unit.Megapascal, "МПа");
            //MapUnitToAbbreviation(Unit.KilogramForcePerSquareCentimeter, "кгс/см²");
            //MapUnitToAbbreviation(Unit.Psi, "psi");
            //MapUnitToAbbreviation(Unit.NewtonPerSquareCentimeter, "Н/см²");
            //MapUnitToAbbreviation(Unit.NewtonPerSquareMillimeter, "Н/мм²");
            //MapUnitToAbbreviation(Unit.NewtonPerSquareMeter, "Н/м²");
            //MapUnitToAbbreviation(Unit.Bar, "бар");
            //MapUnitToAbbreviation(Unit.TechnicalAtmosphere, "ат");
            //MapUnitToAbbreviation(Unit.Atmosphere, "атм");
            //MapUnitToAbbreviation(Unit.Torr, "торр");

            // Forces
            //MapUnitToAbbreviation(Unit.Kilonewton, "кН");
            //MapUnitToAbbreviation(Unit.Newton, "Н");
            //MapUnitToAbbreviation(Unit.KilogramForce, "кгс");
            //MapUnitToAbbreviation(Unit.Dyn, "дин");

            //// Force (imperial/other)
            //MapUnitToAbbreviation(Unit.KiloPond, "кгс");
            //MapUnitToAbbreviation(Unit.PoundForce, "фунт-сила");
            //MapUnitToAbbreviation(Unit.Poundal, "паундаль");

            // Area
            //MapUnitToAbbreviation(Unit.SquareKilometer, "км²");
            //MapUnitToAbbreviation(Unit.SquareMeter, "м²");
            //MapUnitToAbbreviation(Unit.SquareDecimeter, "дм²");
            //MapUnitToAbbreviation(Unit.SquareCentimeter, "см²");
            //MapUnitToAbbreviation(Unit.SquareMillimeter, "мм²");

            //// Area Imperial
            //MapUnitToAbbreviation(Unit.SquareMile, "миля²");
            //MapUnitToAbbreviation(Unit.SquareYard, "ярд²");
            //MapUnitToAbbreviation(Unit.SquareFoot, "фут²");
            //MapUnitToAbbreviation(Unit.SquareInch, "дюйм²");

            // Angle
            //MapUnitToAbbreviation(Unit.Degree, "°");
            //MapUnitToAbbreviation(Unit.Radian, "рад");
            //MapUnitToAbbreviation(Unit.Gradian, "g");

            // Volumes
            //MapUnitToAbbreviation(Unit.CubicKilometer, "км³");
            //MapUnitToAbbreviation(Unit.CubicMeter, "м³");
            //MapUnitToAbbreviation(Unit.CubicDecimeter, "дм³");
            //MapUnitToAbbreviation(Unit.CubicCentimeter, "см³");
            //MapUnitToAbbreviation(Unit.CubicMillimeter, "мм³");
            //MapUnitToAbbreviation(Unit.Hectoliter, "гл");
            //MapUnitToAbbreviation(Unit.Liter, "л");
            //MapUnitToAbbreviation(Unit.Deciliter, "дл");
            //MapUnitToAbbreviation(Unit.Centiliter, "сл");
            //MapUnitToAbbreviation(Unit.Milliliter, "мл");

            //// Volume US/Imperial
            //MapUnitToAbbreviation(Unit.CubicMile, "миля³");
            //MapUnitToAbbreviation(Unit.CubicYard, "ярд³");
            //MapUnitToAbbreviation(Unit.CubicFoot, "фут³");
            //MapUnitToAbbreviation(Unit.CubicInch, "дюйм³");
            //MapUnitToAbbreviation(Unit.UsGallon, "Американский галлон");
            //MapUnitToAbbreviation(Unit.UsOunce, "Американская унция");
            //MapUnitToAbbreviation(Unit.ImperialGallon, "Английский галлон");
            //MapUnitToAbbreviation(Unit.ImperialOunce, "Английская унция");

            // Torque
            //MapUnitToAbbreviation(Unit.Newtonmeter, "Н·м");

            // Generic / Other
            //MapUnitToAbbreviation(Unit.Piece, "штук");
            //MapUnitToAbbreviation(Unit.Percent, "%");

            // Electric potential
            //MapUnitToAbbreviation(Unit.Volt, "В");

            // Time
            //MapUnitToAbbreviation(Unit.Nanosecond, "нс");
            //MapUnitToAbbreviation(Unit.Microsecond, "мкс");
            //MapUnitToAbbreviation(Unit.Millisecond, "мс");
            //MapUnitToAbbreviation(Unit.Second, "с");
            //MapUnitToAbbreviation(Unit.Minute, "мин");
            //MapUnitToAbbreviation(Unit.Hour, "ч");
            //MapUnitToAbbreviation(Unit.Day, "д");
            //MapUnitToAbbreviation(Unit.Week, "мин");
            //MapUnitToAbbreviation(Unit.Month30Days, "месяц");
            //MapUnitToAbbreviation(Unit.Year365Days, "год");

            // Cooking units
            //MapUnitToAbbreviation(Unit.Tablespoon, "столовая ложка");
            //MapUnitToAbbreviation(Unit.Teaspoon, "чайная ложка");
                
            // Flow
            //MapUnitToAbbreviation(Unit.CubicMeterPerSecond, "м³/с");
            //MapUnitToAbbreviation(Unit.CubicMeterPerHour, "м³/ч");

            // RotationalSpeed
            //MapUnitToAbbreviation(Unit.RevolutionPerSecond, "об/с");
            //MapUnitToAbbreviation(Unit.RevolutionPerMinute, "об/мин");

            // Temperature
            //MapUnitToAbbreviation(Unit.Kelvin, "K");
            //MapUnitToAbbreviation(Unit.DegreeCelsius, "°C");
            //MapUnitToAbbreviation(Unit.DegreeDelisle, "°De");
            //MapUnitToAbbreviation(Unit.DegreeFahrenheit, "°F");
            //MapUnitToAbbreviation(Unit.DegreeNewton, "°N");
            //MapUnitToAbbreviation(Unit.DegreeRankine, "°R");
            //MapUnitToAbbreviation(Unit.DegreeReaumur, "°Ré");
            //MapUnitToAbbreviation(Unit.DegreeRoemer, "°Rø");
        }

        private void CreateCultureInvariants()
        {
            // For correct abbreviations, see: http://lamar.colostate.edu/~hillger/correct.htm
            // Note: For units with multiple abbreviations, the first one is used in GetDefaultAbbreviation().
            //MapUnitToAbbreviation(Unit.Undefined, "(no unit)");

            //// Length
            //MapUnitToAbbreviation(Unit.Kilometer, "km");
            //MapUnitToAbbreviation(Unit.Meter, "m");
            //MapUnitToAbbreviation(Unit.Decimeter, "dm");
            //MapUnitToAbbreviation(Unit.Centimeter, "cm");
            //MapUnitToAbbreviation(Unit.Millimeter, "mm");
            //MapUnitToAbbreviation(Unit.Micrometer, "μm");
            //MapUnitToAbbreviation(Unit.Nanometer, "nm");

            //// Length (imperial)
            //MapUnitToAbbreviation(Unit.Microinch, "μin");
            //MapUnitToAbbreviation(Unit.Mil, "mil");
            //MapUnitToAbbreviation(Unit.Mile, "mi");
            //MapUnitToAbbreviation(Unit.Yard, "yd");
            //MapUnitToAbbreviation(Unit.Foot, "ft");
            //MapUnitToAbbreviation(Unit.Inch, "in");

            // Masses
            //MapUnitToAbbreviation(Unit.Megatonne, "Mt");
            //MapUnitToAbbreviation(Unit.Kilotonne, "kt");
            //MapUnitToAbbreviation(Unit.Tonne, "t");
            //MapUnitToAbbreviation(Unit.Kilogram, "kg");
            //MapUnitToAbbreviation(Unit.Hectogram, "hg");
            //MapUnitToAbbreviation(Unit.Decagram, "dag");
            //MapUnitToAbbreviation(Unit.Gram, "g");
            //MapUnitToAbbreviation(Unit.Decigram, "dg");
            //MapUnitToAbbreviation(Unit.Centigram, "cg");
            //MapUnitToAbbreviation(Unit.Milligram, "mg");
            //MapUnitToAbbreviation(Unit.Microgram, "µg");
            //MapUnitToAbbreviation(Unit.Nanogram, "ng");

            // Mass (imperial)
            //MapUnitToAbbreviation(Unit.ShortTon, "short tn");
            //MapUnitToAbbreviation(Unit.Pound, "lb");
            //MapUnitToAbbreviation(Unit.LongTon, "long tn");

            // Pressures
            //MapUnitToAbbreviation(Unit.Pascal, "Pa");
            //MapUnitToAbbreviation(Unit.Kilopascal, "kPa");
            //MapUnitToAbbreviation(Unit.Megapascal, "MPa");
            //MapUnitToAbbreviation(Unit.KilogramForcePerSquareCentimeter, "kgf/cm²");
            //MapUnitToAbbreviation(Unit.Psi, "psi");
            //MapUnitToAbbreviation(Unit.NewtonPerSquareCentimeter, "N/cm²");
            //MapUnitToAbbreviation(Unit.NewtonPerSquareMillimeter, "N/mm²");
            //MapUnitToAbbreviation(Unit.NewtonPerSquareMeter, "N/m²");
            //MapUnitToAbbreviation(Unit.Bar, "bar");
            //MapUnitToAbbreviation(Unit.TechnicalAtmosphere, "at");
            //MapUnitToAbbreviation(Unit.Atmosphere, "atm");
            //MapUnitToAbbreviation(Unit.Torr, "Torr");

            // Forces
            //MapUnitToAbbreviation(Unit.Kilonewton, "kN");
            //MapUnitToAbbreviation(Unit.Newton, "N");
            //MapUnitToAbbreviation(Unit.KilogramForce, "kgf");
            //MapUnitToAbbreviation(Unit.Dyn, "dyn");

            //// Force (imperial/other)
            //MapUnitToAbbreviation(Unit.KiloPond, "kp");
            //MapUnitToAbbreviation(Unit.PoundForce, "lbf");
            //MapUnitToAbbreviation(Unit.Poundal, "pdl");

            // Area
            //MapUnitToAbbreviation(Unit.SquareKilometer, "km²");
            //MapUnitToAbbreviation(Unit.SquareMeter, "m²");
            //MapUnitToAbbreviation(Unit.SquareDecimeter, "dm²");
            //MapUnitToAbbreviation(Unit.SquareCentimeter, "cm²");
            //MapUnitToAbbreviation(Unit.SquareMillimeter, "mm²");

            //// Area Imperial
            //MapUnitToAbbreviation(Unit.SquareMile, "mi²");
            //MapUnitToAbbreviation(Unit.SquareYard, "yd²");
            //MapUnitToAbbreviation(Unit.SquareFoot, "ft²");
            //MapUnitToAbbreviation(Unit.SquareInch, "in²");

            // Angle
            //MapUnitToAbbreviation(Unit.Degree, "°");
            //MapUnitToAbbreviation(Unit.Radian, "rad");
            //MapUnitToAbbreviation(Unit.Gradian, "g");

            //// Volumes
            //MapUnitToAbbreviation(Unit.CubicKilometer, "km³");
            //MapUnitToAbbreviation(Unit.CubicMeter, "m³");
            //MapUnitToAbbreviation(Unit.CubicDecimeter, "dm³");
            //MapUnitToAbbreviation(Unit.CubicCentimeter, "cm³");
            //MapUnitToAbbreviation(Unit.CubicMillimeter, "mm³");
            //MapUnitToAbbreviation(Unit.Hectoliter, "hl");
            //MapUnitToAbbreviation(Unit.Liter, "l");
            //MapUnitToAbbreviation(Unit.Deciliter, "dl");
            //MapUnitToAbbreviation(Unit.Centiliter, "cl");
            //MapUnitToAbbreviation(Unit.Milliliter, "ml");

            //// Volume US/Imperial
            //MapUnitToAbbreviation(Unit.CubicMile, "mi³");
            //MapUnitToAbbreviation(Unit.CubicYard, "yd³");
            //MapUnitToAbbreviation(Unit.CubicFoot, "ft³");
            //MapUnitToAbbreviation(Unit.CubicInch, "in³");
            //MapUnitToAbbreviation(Unit.UsGallon, "gal (U.S.)");
            //MapUnitToAbbreviation(Unit.UsOunce, "oz (U.S.)");
            //MapUnitToAbbreviation(Unit.ImperialGallon, "gal (imp.)");
            //MapUnitToAbbreviation(Unit.ImperialOunce, "oz (imp.)");

            //// Torque
            //MapUnitToAbbreviation(Unit.Newtonmeter, "Nm");

            //// Generic / Other
            //MapUnitToAbbreviation(Unit.Piece, "piece");
            //MapUnitToAbbreviation(Unit.Percent, "%");

            //// Electric potential
            //MapUnitToAbbreviation(Unit.Volt, "V");

            //// Times
            //MapUnitToAbbreviation(Unit.Nanosecond, "ns");
            //MapUnitToAbbreviation(Unit.Microsecond, "μs");
            //MapUnitToAbbreviation(Unit.Millisecond, "ms");
            //MapUnitToAbbreviation(Unit.Second, "s");
            //MapUnitToAbbreviation(Unit.Minute, "min");
            //MapUnitToAbbreviation(Unit.Hour, "h");
            //MapUnitToAbbreviation(Unit.Day, "d");
            //MapUnitToAbbreviation(Unit.Week, "week");
            //MapUnitToAbbreviation(Unit.Month30Days, "month");
            //MapUnitToAbbreviation(Unit.Year365Days, "year");
            
            //// Cooking units
            //MapUnitToAbbreviation(Unit.Tablespoon, "Tbsp", "Tbs", "T", "tb", "tbs", "tbsp", "tblsp", "tblspn", "Tbsp.", "Tbs.", "T.", "tb.", "tbs.", "tbsp.", "tblsp.", "tblspn.");
            //MapUnitToAbbreviation(Unit.Teaspoon, "tsp","t", "ts", "tspn", "t.", "ts.", "tsp.", "tspn.");

            // Flow
            //MapUnitToAbbreviation(Unit.CubicMeterPerSecond, "m³/s");
            //MapUnitToAbbreviation(Unit.CubicMeterPerHour, "m³/h");

            //// RotationalSpeed
            //MapUnitToAbbreviation(Unit.RevolutionPerSecond, "r/s");
            //MapUnitToAbbreviation(Unit.RevolutionPerMinute, "r/min");

            //// Speed
            //MapUnitToAbbreviation(Unit.FootPerSecond, "ft/s");
            //MapUnitToAbbreviation(Unit.KilometerPerHour, "km/h");
            //MapUnitToAbbreviation(Unit.Knot, "kt", "kn", "knot", "knots");
            //MapUnitToAbbreviation(Unit.MeterPerSecond, "m/s");
            //MapUnitToAbbreviation(Unit.MilePerHour, "mph");

            //// Temperature
            //MapUnitToAbbreviation(Unit.Kelvin, "K");
            //MapUnitToAbbreviation(Unit.DegreeCelsius, "°C");
            //MapUnitToAbbreviation(Unit.DegreeDelisle, "°De");
            //MapUnitToAbbreviation(Unit.DegreeFahrenheit, "°F");
            //MapUnitToAbbreviation(Unit.DegreeNewton, "°N");
            //MapUnitToAbbreviation(Unit.DegreeRankine, "°R");
            //MapUnitToAbbreviation(Unit.DegreeReaumur, "°Ré");
            //MapUnitToAbbreviation(Unit.DegreeRoemer, "°Rø");
        }

        private void MapUnitToAbbreviation<TUnit>(TUnit unit, params string[] abbreviations)
            where TUnit : /*Enum constraint hack*/ struct, IComparable, IFormattable
        {
            // Assuming TUnit is an enum, this conversion is safe. Not possible to cleanly enforce this today.
            // Src: http://stackoverflow.com/questions/908543/how-to-convert-from-system-enum-to-base-integer
            // http://stackoverflow.com/questions/79126/create-generic-method-constraining-t-to-an-enum
            int unitValue = Convert.ToInt32(unit);
            Type unitType = typeof (TUnit);
            MapUnitToAbbreviation(unitType, unitValue, abbreviations);
        }

        private void MapUnitToAbbreviation(Type unitType, int unitValue, params string[] abbreviations)
        {
            if (abbreviations == null)
                throw new ArgumentNullException("abbreviations");

            Dictionary<int, List<string>> unitValueToAbbrev;
            if (!_unitTypeToUnitValueToAbbrevs.TryGetValue(unitType, out unitValueToAbbrev))
            {
                unitValueToAbbrev = _unitTypeToUnitValueToAbbrevs[unitType] = new Dictionary<int, List<string>>();
            }

            List<string> existingAbbreviations;
            if (!unitValueToAbbrev.TryGetValue(unitValue, out existingAbbreviations))
            {
                existingAbbreviations = unitValueToAbbrev[unitValue] = new List<string>();
            }

            // Update any existing abbreviations so that the latest abbreviations 
            // take precedence in GetDefaultAbbreviation().
            unitValueToAbbrev[unitValue] = abbreviations.Concat(existingAbbreviations).Distinct().ToList();
            foreach (string abbreviation in abbreviations)
            {
                Dictionary<string, int> abbrevToUnitValue;
                if (!_unitTypeToAbbrevToUnitValue.TryGetValue(unitType, out abbrevToUnitValue))
                {
                    abbrevToUnitValue = _unitTypeToAbbrevToUnitValue[unitType] =
                        new Dictionary<string, int>();
                }

                if (!abbrevToUnitValue.ContainsKey(abbreviation))
                    abbrevToUnitValue[abbreviation] = unitValue;
            }
        }

        #endregion

        public bool TryParse<TUnit>(string unitAbbreviation, out TUnit unit)
            where TUnit : /*Enum constraint hack*/ struct, IComparable, IFormattable
        {
            Type unitType = typeof (TUnit);

            Dictionary<string, int> abbrevToUnitValue;
            if (!_unitTypeToAbbrevToUnitValue.TryGetValue(unitType, out abbrevToUnitValue))
                throw new NotImplementedException(string.Format("No abbreviations defined for unit type [{0}] for culture [{1}].", unitType, Culture.EnglishName));

            int unitValue;
            if (!abbrevToUnitValue.TryGetValue(unitAbbreviation, out unitValue))
            {
                unit = default(TUnit);
                return false;
            }

            unit = (TUnit) (object) unitValue;
            return true;
        }

        public string[] GetAllAbbreviations<TUnit>(TUnit unit)
            where TUnit : /*Enum constraint hack*/ struct, IComparable, IFormattable
        {
            Type unitType = typeof (TUnit);
            int unitValue = Convert.ToInt32(unit);

            Dictionary<int, List<string>> unitValueToAbbrevs;
            if (!_unitTypeToUnitValueToAbbrevs.TryGetValue(unitType, out unitValueToAbbrevs))
                throw new NotImplementedException(string.Format("No abbreviations defined for unit type [{0}] for culture [{1}].", unitType, Culture.EnglishName));

            List<string> abbrevs;
            if (!unitValueToAbbrevs.TryGetValue(unitValue, out abbrevs))
                throw new NotImplementedException(string.Format("No abbreviations defined for unit type [{0}.{1}] for culture [{2}].", unitType, unitValue, Culture.EnglishName));

            // Return the first (most commonly used) abbreviation for this unit)
            return abbrevs.ToArray();
        }
    }
}