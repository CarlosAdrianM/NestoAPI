using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Novedades
{
    /// <summary>28/10/26: las versiones menores salen junto a las de su versión mayor en el popup de arranque.</summary>
    [TestClass]
    public class ReglasVersionesNovedadesTests
    {
        private static readonly List<(string version, string titulo)> Novedades = new List<(string, string)>
        {
            ("1.10.50.0", "retener"), ("1.10.51.0", "P.O."), ("1.10.51.0", "fin de mes"), ("1.10.51.1", "picking primera hora"), ("sin version", "rara")
        };

        private static List<string> Titulos(string vista)
            => ReglasVersionesNovedades.DesdeVersionVista(Novedades, Version.Parse(vista), n => n.version).Select(n => n.titulo).ToList();

        [TestMethod]
        public void VersionMenorNueva_SacaTambienTodasLasDeSuVersionMayor()
        {
            CollectionAssert.AreEquivalent(new[] { "P.O.", "fin de mes", "picking primera hora", "rara" }, Titulos("1.10.51.0"));
        }

        [TestMethod]
        public void VersionMayorNueva_SacaSoloLasPosteriores()
        {
            CollectionAssert.AreEquivalent(new[] { "P.O.", "fin de mes", "picking primera hora", "rara" }, Titulos("1.10.50.0"));
            CollectionAssert.AreEquivalent(new[] { "retener", "P.O.", "fin de mes", "picking primera hora", "rara" }, Titulos("1.10.49.0"));
        }

        [TestMethod]
        public void NadaNuevo_SoloLasDeVersionNoParseable()
        {
            CollectionAssert.AreEquivalent(new[] { "rara" }, Titulos("1.10.51.1"));
            CollectionAssert.AreEquivalent(new[] { "rara" }, Titulos("1.10.52.0"));
        }

        [TestMethod]
        public void VersionMayor_TresPrimerasCifras()
        {
            Assert.AreEqual(new Version(1, 10, 51), ReglasVersionesNovedades.VersionMayor("1.10.51.1"));
            Assert.AreEqual(new Version(2, 20, 0), ReglasVersionesNovedades.VersionMayor("2.20"));
            Assert.IsNull(ReglasVersionesNovedades.VersionMayor("x"));
        }
    }
}
