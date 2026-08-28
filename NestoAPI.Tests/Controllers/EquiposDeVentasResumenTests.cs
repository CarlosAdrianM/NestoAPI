using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// El resumen de rapports repartía los correos con dos cosas escritas a mano en Constantes:
    /// el jefe de ventas ("ASH") y su dirección de correo. Ahora los equipos salen de EquiposVenta
    /// y el correo, de la ficha del jefe, así que con dos jefes cada uno recibe el resumen de SU
    /// equipo sin tocar código.
    /// </summary>
    [TestClass]
    public class EquiposDeVentasResumenTests
    {
        private const string DIRECCION = "direccion@nuevavision.es";

        private static SeguimientosClientesController.JefeYVendedor Fila(string jefe, string vendedor)
            => new SeguimientosClientesController.JefeYVendedor { Jefe = jefe, Vendedor = vendedor };

        [TestMethod]
        public void ConstruirEquipos_UnSoloJefe_RecibeElResumenDeSuEquipoEnSuCorreo()
        {
            // El caso de hoy: ASH con su equipo. Antes esto salía de la constante.
            var filas = new[] { Fila("ASH", "JE"), Fila("ASH", "DV"), Fila("ASH", "JGP") };
            var correos = new Dictionary<string, string> { ["ASH"] = "albertosancho@nuevavision.es" };

            var equipos = SeguimientosClientesController.ConstruirEquipos(filas, correos, DIRECCION);

            Assert.AreEqual(1, equipos.Count);
            Assert.AreEqual("albertosancho@nuevavision.es", equipos[0].Correo);
            CollectionAssert.AreEqual(new[] { "DV", "JE", "JGP" }, equipos[0].Vendedores);
        }

        [TestMethod]
        public void ConstruirEquipos_DosJefes_CadaUnoConSuEquipoYSuCorreo()
        {
            // Lo que se busca con el cambio: que el día que haya dos jefes funcione solo.
            var filas = new[]
            {
                Fila("ASH", "JE"), Fila("ASH", "DV"),
                Fila("MPP", "AAA"), Fila("MPP", "BBB")
            };
            var correos = new Dictionary<string, string>
            {
                ["ASH"] = "alberto@nuevavision.es",
                ["MPP"] = "maria@nuevavision.es"
            };

            var equipos = SeguimientosClientesController.ConstruirEquipos(filas, correos, DIRECCION);

            Assert.AreEqual(2, equipos.Count);
            var ash = equipos.Single(e => e.Jefe == "ASH");
            var mpp = equipos.Single(e => e.Jefe == "MPP");
            Assert.AreEqual("alberto@nuevavision.es", ash.Correo);
            CollectionAssert.AreEqual(new[] { "DV", "JE" }, ash.Vendedores);
            Assert.AreEqual("maria@nuevavision.es", mpp.Correo);
            CollectionAssert.AreEqual(new[] { "AAA", "BBB" }, mpp.Vendedores);
        }

        [TestMethod]
        public void ConstruirEquipos_JefeSinCorreoEnSuFicha_ElResumenVaADireccionYSeMarca()
        {
            // No puede pasar que un equipo se quede sin informe porque falte un dato en una ficha:
            // va a dirección y queda marcado para que el llamante lo registre en ELMAH.
            var filas = new[] { Fila("ASH", "JE") };
            var correos = new Dictionary<string, string> { ["ASH"] = "   " };

            var equipos = SeguimientosClientesController.ConstruirEquipos(filas, correos, DIRECCION);

            Assert.AreEqual(DIRECCION, equipos[0].Correo);
            Assert.IsTrue(equipos[0].SinCorreoPropio);
        }

        [TestMethod]
        public void ConstruirEquipos_JefeQueNoEstaEnVendedores_TampocoSeQuedaSinInforme()
        {
            var equipos = SeguimientosClientesController.ConstruirEquipos(
                new[] { Fila("XXX", "JE") }, new Dictionary<string, string>(), DIRECCION);

            Assert.AreEqual(DIRECCION, equipos[0].Correo);
            Assert.IsTrue(equipos[0].SinCorreoPropio);
        }

        [TestMethod]
        public void ConstruirEquipos_NoLeAfectaElRellenoDeLosChar()
        {
            // Vendedor y Superior son char en la base de datos: llegan con espacios detrás.
            var filas = new[] { Fila("ASH  ", "JE  "), Fila("ash", "DV ") };
            var correos = new Dictionary<string, string> { ["ASH"] = "alberto@nuevavision.es" };

            var equipos = SeguimientosClientesController.ConstruirEquipos(filas, correos, DIRECCION);

            Assert.AreEqual(1, equipos.Count, "ASH y 'ash  ' son el mismo jefe");
            Assert.AreEqual("alberto@nuevavision.es", equipos[0].Correo);
            CollectionAssert.AreEqual(new[] { "DV", "JE" }, equipos[0].Vendedores);
        }

        [TestMethod]
        public void ConstruirEquipos_SinEquipos_NoRevienta()
        {
            var equipos = SeguimientosClientesController.ConstruirEquipos(
                new SeguimientosClientesController.JefeYVendedor[0],
                new Dictionary<string, string>(), DIRECCION);

            Assert.AreEqual(0, equipos.Count);
        }
    }
}
