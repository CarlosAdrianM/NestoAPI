using System.Collections.Generic;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    /// <summary>
    /// NestoAPI#231: el mapeo terminal TPV → usuario se lee de la tabla TerminalesUsuariosTPV
    /// (vía IRepositorioTerminalesTPV), con fallback al diccionario por defecto. Y el terminal de
    /// Paloma ya es el nuevo.
    /// </summary>
    [TestClass]
    public class ContabilidadServiceTests
    {
        private static ContabilidadService ConMapa(Dictionary<string, string> mapa)
        {
            var repositorio = A.Fake<IRepositorioTerminalesTPV>();
            A.CallTo(() => repositorio.LeerMapa()).Returns(mapa);
            return new ContabilidadService(repositorio);
        }

        // ----- #296: validación previa a prdLiquidar (vía prdContabilizar) -----
        // El SP rechaza con RAISERROR ("Importes con mismo signo o importe 0...") enterrado en
        // ruido de transacciones. Replicamos sus validaciones de negocio en C# ANTES de ejecutar
        // el SP para dar un error claro y accionable (patrón #284).

        private static NestoAPI.Models.PreContabilidad LineaQueLiquida(string cuenta, decimal debe, decimal haber, int liquidado)
        {
            return new NestoAPI.Models.PreContabilidad
            {
                Nº_Cuenta = cuenta,
                Debe = debe,
                Haber = haber,
                Liquidado = liquidado
            };
        }

        private static NestoAPI.Models.ExtractoCliente Destino(int numOrden, string cliente, decimal importePdte)
        {
            return new NestoAPI.Models.ExtractoCliente
            {
                Nº_Orden = numOrden,
                Número = cliente,
                ImportePdte = importePdte
            };
        }

        [TestMethod]
        public void ErroresLiquidaciones_SignosOpuestos_NoHayErrores()
        {
            // Cargo de 100 liquida contra un pendiente de -100: caso válido.
            var lineas = new List<NestoAPI.Models.PreContabilidad> { LineaQueLiquida("12992", 0, 100, 555) };
            var destino = Destino(555, "12992", 100);

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => id == 555 ? destino : null);

            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores));
        }

        [TestMethod]
        public void ErroresLiquidaciones_MismoSigno_DaErrorClaroConClienteYMovimiento()
        {
            // Caso real de Reina: los dos importes con el mismo signo → el SP renegaba con ruido.
            var lineas = new List<NestoAPI.Models.PreContabilidad> { LineaQueLiquida("12992", 100, 0, 555) };
            var destino = Destino(555, "12992", 100);

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => destino);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0], "12992");
            StringAssert.Contains(errores[0], "555");
            StringAssert.Contains(errores[0], "signo");
        }

        [TestMethod]
        public void ErroresLiquidaciones_ImporteCero_DaError()
        {
            var lineas = new List<NestoAPI.Models.PreContabilidad> { LineaQueLiquida("12992", 100, 0, 555) };
            var destino = Destino(555, "12992", 0); // pendiente 0: ya está liquidado

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => destino);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0], "555");
        }

        [TestMethod]
        public void ErroresLiquidaciones_DestinoInexistente_DaError()
        {
            var lineas = new List<NestoAPI.Models.PreContabilidad> { LineaQueLiquida("12992", 100, 0, 999) };

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => null);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0], "999");
            StringAssert.Contains(errores[0], "no existe");
        }

        [TestMethod]
        public void ErroresLiquidaciones_ClientesDistintos_DaError()
        {
            var lineas = new List<NestoAPI.Models.PreContabilidad> { LineaQueLiquida("12992", 100, 0, 555) };
            var destino = Destino(555, "35615", -100);

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => destino);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0], "12992");
            StringAssert.Contains(errores[0], "35615");
        }

        [TestMethod]
        public void ErroresLiquidaciones_LineasSinLiquidar_SeIgnoran()
        {
            var lineas = new List<NestoAPI.Models.PreContabilidad>
            {
                new NestoAPI.Models.PreContabilidad { Nº_Cuenta = "12992", Debe = 100, Liquidado = null },
                new NestoAPI.Models.PreContabilidad { Nº_Cuenta = "12992", Debe = 100, Liquidado = 0 }
            };

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => null);

            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_ConMapaDeBD_DevuelveElUsuarioDeLaTabla()
        {
            var servicio = ConMapa(new Dictionary<string, string> { { "99999", "Nuevo Usuario" } });

            Assert.AreEqual("Nuevo Usuario", servicio.ObtenerUsuarioTerminal("99999"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_SinMapaDeBD_UsaDiccionarioPorDefecto()
        {
            var servicio = ConMapa(null);

            Assert.AreEqual("Victoria", servicio.ObtenerUsuarioTerminal("91900804275"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_MapaVacio_UsaDiccionarioPorDefecto()
        {
            var servicio = ConMapa(new Dictionary<string, string>());

            Assert.AreEqual("Victoria", servicio.ObtenerUsuarioTerminal("91900804275"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_Paloma_UsaElTerminalNuevo()
        {
            var servicio = ConMapa(null);

            Assert.AreEqual("Paloma", servicio.ObtenerUsuarioTerminal("91901505888"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_TerminalDesconocido_DevuelveVacio()
        {
            var servicio = ConMapa(null);

            Assert.AreEqual(string.Empty, servicio.ObtenerUsuarioTerminal("00000000000"));
        }
    }
}
