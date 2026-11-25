using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Pedidos 929956, 931479, 931502, 931561 y 931592 (25/11/26): pedidos «sin IVA» (cabecera NULL) que quedaban a medias, unas
    /// líneas al 21 % y otras a 0. LA REGLA: el IVA de las líneas vivas es el de la cabecera tal como se guarda; sin IVA, todo a 0.
    /// </summary>
    [TestClass]
    public class IvaLineasPedidoTests
    {
        private static LinPedidoVta Linea(int orden, string producto, short estado, decimal baseImponible, byte porcentajeIva,
            decimal porcentajeRe = 0, byte tipoLinea = 1)
        {
            decimal iva = baseImponible * porcentajeIva / 100;
            decimal re = baseImponible * porcentajeRe;
            return new LinPedidoVta
            {
                Nº_Orden = orden, Producto = producto, Estado = estado, TipoLinea = tipoLinea, IVA = "G21",
                Base_Imponible = baseImponible, PorcentajeIVA = porcentajeIva, PorcentajeRE = porcentajeRe,
                ImporteIVA = iva, ImporteRE = re, Total = baseImponible + iva + re
            };
        }

        private static string CodigoControlador()
        {
            string ruta = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\NestoAPI\Controllers\PedidosVentaController.cs"));
            return File.ReadAllText(ruta);
        }

        // ------------------------------------------------------------ (1) el PUT que pasa el pedido a sin IVA

        [TestMethod]
        public void PonerSinIva_LineaAl21ConRecargo_TodoA0YTotalLaBase()
        {
            LinPedidoVta linea = Linea(1, "41259", Constantes.EstadosLineaVenta.PENDIENTE, 28.01m, 21, 0.052m);

            ReglaIvaLineasPedido.PonerSinIva(linea);

            Assert.AreEqual((byte)0, linea.PorcentajeIVA, "931502: se quedaba en 21");
            Assert.AreEqual(0m, linea.PorcentajeRE);
            Assert.AreEqual(0m, linea.ImporteIVA);
            Assert.AreEqual(0m, linea.ImporteRE);
            Assert.AreEqual(28.01m, linea.Base_Imponible, "la base no cambia");
            Assert.AreEqual(28.01m, linea.Total);
            Assert.AreEqual("G21", linea.IVA, "el código de IVA del producto se queda");
        }

        [TestMethod]
        public void PutPedidoVenta_PasarASinIva_UsaLaReglaYNoSoloLosImportes()
        {
            string codigo = CodigoControlador();

            Assert.IsTrue(codigo.Contains("ReglaIvaLineasPedido.PonerSinIva(lineaPedido);"),
                "al quitar el IVA las líneas que ya tenía van a 0 % con la regla");
            Assert.IsFalse(codigo.Contains("lineaPedido.ImporteIVA = 0;"),
                "ya no se ponen a 0 solo los importes (dejaba PorcentajeIVA 21: pedido a medias)");
        }

        // ------------------------------------------------------------ (2) y (3) una sola verdad al guardar

        [TestMethod]
        public void NormalizarSiSinIva_931479_LasLineasVivasAl21PasanA0_LasServidasNo()
        {
            var lineas = new List<LinPedidoVta>
            {
                Linea(1, "40010", Constantes.EstadosLineaVenta.EN_CURSO, 100m, 21),
                Linea(2, "42650", Constantes.EstadosLineaVenta.EN_CURSO, 50m, 0),          // la del reparto de «solo en stock», ya a 0
                Linea(0, "45929", Constantes.EstadosLineaVenta.PENDIENTE, 30m, 21),        // la añadida por PUT que quedó al 21 %
                Linea(3, "62400000", Constantes.EstadosLineaVenta.PENDIENTE, 2m, 0, 0, 2), // comisión contra reembolso, a 0
                Linea(4, "40011", Constantes.EstadosLineaVenta.ALBARAN, 10m, 21)           // en albarán: no se toca
            };

            List<LinPedidoVta> normalizadas = ReglaIvaLineasPedido.NormalizarSiSinIva(null, lineas);

            CollectionAssert.AreEquivalent(new[] { "40010", "45929" }, normalizadas.Select(l => l.Producto).ToList());
            Assert.IsTrue(lineas.Where(ReglaIvaLineasPedido.EsViva).All(l => l.PorcentajeIVA == 0 && l.ImporteIVA == 0 && l.Total == l.Base_Imponible),
                "ninguna línea viva se queda con IVA");
            Assert.AreEqual((byte)21, lineas.Single(l => l.Producto == "40011").PorcentajeIVA, "lo servido no se toca");
            Assert.AreEqual(100m, lineas.Single(l => l.Producto == "40010").Total);
        }

        [TestMethod]
        public void NormalizarSiSinIva_CabeceraConIva_NoTocaNada()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, "40010", Constantes.EstadosLineaVenta.PENDIENTE, 100m, 21) };

            Assert.AreEqual(0, ReglaIvaLineasPedido.NormalizarSiSinIva("G21", lineas).Count);
            Assert.AreEqual((byte)21, lineas[0].PorcentajeIVA);
            Assert.AreEqual(121m, lineas[0].Total);
        }

        [TestMethod]
        public void NormalizarSiSinIva_SinIvaDeVerdad_TodoA0_NoHayNadaQueTocar()
        {
            var lineas = new List<LinPedidoVta>
            {
                Linea(1, "40010", Constantes.EstadosLineaVenta.PENDIENTE, 100m, 0),
                Linea(2, "40011", Constantes.EstadosLineaVenta.EN_CURSO, 10m, 0)
            };

            Assert.AreEqual(0, ReglaIvaLineasPedido.NormalizarSiSinIva("   ", lineas).Count, "los «sin IVA» legítimos no se tocan");
        }

        [TestMethod]
        public void AplicarIvaCabeceraAlGuardar_CabeceraSinIva_NormalizaLasLineasVivas()
        {
            var lineas = new List<LinPedidoVta>
            {
                Linea(0, "45929", Constantes.EstadosLineaVenta.PENDIENTE, 30m, 21),
                Linea(5, "40010", Constantes.EstadosLineaVenta.EN_CURSO, 100m, 21)
            };

            List<LinPedidoVta> normalizadas = PedidosVentaController.AplicarIvaCabeceraAlGuardar(null, lineas, "1", 931479, "PUT", "NUEVAVISION\\MARTA");

            Assert.AreEqual(2, normalizadas.Count);
            Assert.IsTrue(lineas.All(l => l.PorcentajeIVA == 0 && l.ImporteIVA == 0 && l.Total == l.Base_Imponible));
            StringAssert.Contains(ReglaIvaLineasPedido.MensajeNormalizadas("1", 931479, "PUT", "u", normalizadas), "[Pedido sin IVA");
        }

        [TestMethod]
        public void PostYPut_NormalizanElIvaDeLasLineasAntesDeGuardar()
        {
            string codigo = CodigoControlador();

            int post = codigo.IndexOf("AplicarIvaCabeceraAlGuardar(cabecera.IVA, lineasPedidoInsertar,", StringComparison.Ordinal);
            int put = codigo.IndexOf("AplicarIvaCabeceraAlGuardar(cabPedidoVta.IVA, lineasVivasTrasElPut.ToList(),", StringComparison.Ordinal);
            Assert.IsTrue(post > 0, "el POST normaliza con la cabecera que se guarda");
            Assert.IsTrue(put > 0, "el PUT normaliza con la cabecera que se guarda");
            Assert.IsTrue(codigo.IndexOf("await GuardarConChequeRegalo(planChequeRegalo, planCupon)", post, StringComparison.Ordinal) > post,
                "en el POST, antes de guardar");
            Assert.IsTrue(codigo.IndexOf("await GuardarConChequeRegalo(planChequeRegalo, planCupon)", put, StringComparison.Ordinal) > put,
                "en el PUT, antes de guardar");
            Assert.AreEqual(2, Regex.Matches(codigo, @"AplicarIvaCabeceraAlGuardar\(").Count - 1, "POST y PUT (más la definición)");
        }

        // ------------------------------------------------------------ cabecera con IVA y líneas a 0: solo aviso

        [TestMethod]
        public void MotivoIvaMixto_CabeceraConIva_MismoCodigoA0YAl21_LoDiceSinTocar()
        {
            var lineas = new List<LinPedidoVta>
            {
                Linea(1, "40010", Constantes.EstadosLineaVenta.PENDIENTE, 100m, 21),
                Linea(2, "42650", Constantes.EstadosLineaVenta.PENDIENTE, 50m, 0)
            };

            string motivo = ReglaIvaLineasPedido.MotivoIvaMixto("G21", lineas);

            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "2 [42650]");
            Assert.AreEqual((byte)0, lineas[1].PorcentajeIVA, "no se cambia nada");
            StringAssert.StartsWith(ReglaIvaLineasPedido.MensajeMixto("1", 1, "PUT", "u", "G21", motivo), "[Pedido con IVA mixto]");
        }

        [TestMethod]
        public void MotivoIvaMixto_TodoCoherenteOSinIva_Null()
        {
            var coherente = new List<LinPedidoVta>
            {
                Linea(1, "40010", Constantes.EstadosLineaVenta.PENDIENTE, 100m, 21),
                Linea(2, "40011", Constantes.EstadosLineaVenta.PENDIENTE, 50m, 21),
                Linea(3, "62400000", Constantes.EstadosLineaVenta.PENDIENTE, 2m, 0, 0, 2) // cuenta contable: no cuenta
            };

            Assert.IsNull(ReglaIvaLineasPedido.MotivoIvaMixto("G21", coherente));
            Assert.IsNull(ReglaIvaLineasPedido.MotivoIvaMixto(null, coherente), "sin IVA lo resuelve la normalización");
        }
    }
}
