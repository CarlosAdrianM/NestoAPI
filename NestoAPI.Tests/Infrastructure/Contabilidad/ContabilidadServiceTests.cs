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

        // ----- Bug 22/07/26 (crear remesa #332): prdCopiarCliente con empresa con padding -----
        // El RemesasViewModel manda la empresa como char(3) con relleno ("1  "); la comparación
        // exacta con "1" la daba por DISTINTA y ejecutaba prdCopiarCliente copiando el cliente
        // de la empresa 1 sobre sí mismo → PK duplicada en dbo.CCC y remesa abortada.

        [TestMethod]
        public void EsEmpresaDistintaDeLaPorDefecto_EmpresaPorDefectoConYSinPadding_EsFalse()
        {
            Assert.IsFalse(ContabilidadService.EsEmpresaDistintaDeLaPorDefecto("1"));
            Assert.IsFalse(ContabilidadService.EsEmpresaDistintaDeLaPorDefecto("1  "), "El char(3) llega con padding");
            Assert.IsFalse(ContabilidadService.EsEmpresaDistintaDeLaPorDefecto(null));
            Assert.IsFalse(ContabilidadService.EsEmpresaDistintaDeLaPorDefecto("  "));
        }

        [TestMethod]
        public void EsEmpresaDistintaDeLaPorDefecto_OtraEmpresaConYSinPadding_EsTrue()
        {
            Assert.IsTrue(ContabilidadService.EsEmpresaDistintaDeLaPorDefecto("3"));
            Assert.IsTrue(ContabilidadService.EsEmpresaDistintaDeLaPorDefecto("3  "));
        }

        // ----- #296: validación previa a prdLiquidar (vía prdContabilizar) -----
        // El SP rechaza con RAISERROR ("Importes con mismo signo o importe 0...") enterrado en
        // ruido de transacciones. Replicamos sus validaciones de negocio en C# ANTES de ejecutar
        // el SP para dar un error claro y accionable (patrón #284).

        private static NestoAPI.Models.PreContabilidad LineaQueLiquida(string cuenta, decimal debe, decimal haber, int liquidado,
            string tipoCuenta = NestoAPI.Models.Constantes.Contabilidad.TiposCuenta.CLIENTE)
        {
            return new NestoAPI.Models.PreContabilidad
            {
                Nº_Cuenta = cuenta,
                TipoCuenta = tipoCuenta,
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
        public void ErroresLiquidaciones_LineaDeProveedor_SeIgnora()
        {
            // #311 (caso real 16/07/26, Carlos con Pilar y Reina contabilizando a la vez): los
            // pagos a proveedor liquidan contra ExtractoProveedor, cuyo espacio de ids no tiene
            // nada que ver con ExtractosCliente. Validarlos contra clientes daba falsos positivos
            // ("el movimiento 378612 es del cliente 7363") y bloqueaba la conciliación de Bancos.
            var lineas = new List<NestoAPI.Models.PreContabilidad>
            {
                LineaQueLiquida("433", 0, 38.62m, 378612, NestoAPI.Models.Constantes.Contabilidad.TiposCuenta.PROVEEDOR)
            };
            var destinoDeOtroCliente = Destino(378612, "7363", -100);

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => destinoDeOtroCliente);

            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores));
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

        // ----- NestoAPI#343: validación previa de campos necesarios (centro de coste) -----
        // Réplica exacta de prdComprobarCamposNecesarios: cuentas de gasto 620-639 (también en
        // la contrapartida) con centro de coste/delegación/departamento a NULL abortan el SP
        // con RAISERROR enterrado en ruido de transacciones (caso real 922749, traspaso espejo).

        private static NestoAPI.Models.PreContabilidad LineaContable(string cuenta, string centroCoste = "CA",
            string delegacion = "ALG", string departamento = "ADM", string contrapartida = null, int orden = 1)
        {
            return new NestoAPI.Models.PreContabilidad
            {
                Nº_Orden = orden,
                Nº_Cuenta = cuenta,
                Contrapartida = contrapartida,
                CentroCoste = centroCoste,
                Delegación = delegacion,
                Departamento = departamento,
                Concepto = "Gasto de prueba",
                TipoCuenta = NestoAPI.Models.Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE
            };
        }

        [TestMethod]
        public void ErroresCamposNecesarios_GastoSinCentroCoste_Avisa()
        {
            var lineas = new List<NestoAPI.Models.PreContabilidad>
            {
                LineaContable("62400003", centroCoste: null, orden: 77)
            };

            var errores = ContabilidadService.ErroresCamposNecesariosDiario(lineas);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0], "77");
            StringAssert.Contains(errores[0], "62400003");
            StringAssert.Contains(errores[0], "centro de coste");
        }

        [TestMethod]
        public void ErroresCamposNecesarios_ContrapartidaDeGastoSinDepartamento_Avisa()
        {
            var lineas = new List<NestoAPI.Models.PreContabilidad>
            {
                LineaContable("57200013", departamento: null, contrapartida: "63100001")
            };

            var errores = ContabilidadService.ErroresCamposNecesariosDiario(lineas);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0], "contrapartida");
            StringAssert.Contains(errores[0], "63100001");
        }

        [TestMethod]
        public void ErroresCamposNecesarios_GastoConTodo_CuentaNoGasto_YBlancoNoNulo_NoAvisan()
        {
            var lineas = new List<NestoAPI.Models.PreContabilidad>
            {
                LineaContable("62400003"),
                // 60x/61x/64x quedan FUERA de la fórmula de CamposNecesarios (620-639)
                LineaContable("60000001", centroCoste: null),
                LineaContable("61000001", centroCoste: null),
                LineaContable("64000001", centroCoste: null),
                LineaContable("70000000", centroCoste: null),
                // El SP comprueba IS NULL: un blanco pasa — cuadre exacto, sin tolerancias
                LineaContable("62400003", centroCoste: "  ")
            };

            var errores = ContabilidadService.ErroresCamposNecesariosDiario(lineas);

            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void EsCuentaDeGastoConCentroCoste_SoloDel620Al639()
        {
            Assert.IsTrue(ContabilidadService.EsCuentaDeGastoConCentroCoste("62000000"));
            Assert.IsTrue(ContabilidadService.EsCuentaDeGastoConCentroCoste("63999999"));
            Assert.IsFalse(ContabilidadService.EsCuentaDeGastoConCentroCoste("60000000"));
            Assert.IsFalse(ContabilidadService.EsCuentaDeGastoConCentroCoste("61000000"));
            Assert.IsFalse(ContabilidadService.EsCuentaDeGastoConCentroCoste("64000000"));
            Assert.IsFalse(ContabilidadService.EsCuentaDeGastoConCentroCoste(null));
            Assert.IsFalse(ContabilidadService.EsCuentaDeGastoConCentroCoste("  "));
        }
    }
}
